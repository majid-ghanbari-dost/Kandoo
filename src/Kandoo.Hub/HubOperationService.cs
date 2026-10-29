using System.Text.Json;
using Kandoo.Core;

namespace Kandoo.Hub;

public sealed class HubOperationService
{
    private readonly object _gate = new();
    private readonly IHubStore _store;
    private readonly IHubAccessStore _access;
    private readonly IClock _clock;
    private readonly DraftLockManager _locks;

    public HubOperationService(IHubStore store, IHubAccessStore access, IClock clock)
    {
        _store = store;
        _access = access;
        _clock = clock;
        _locks = new DraftLockManager(clock);
        _locks.ReplaceForPersistence(store.GetDraftLocks());
    }

    public HubOperationResult Submit(Guid routeStoreId, HubSession session, HubOperationRequest transport)
    {
        lock (_gate)
        {
            var validation = ValidateEnvelope(routeStoreId, session, transport);
            if (validation is not null)
            {
                return validation;
            }

            var request = new OperationRequest(
                transport.StoreId!.Value,
                transport.DeviceId!.Value,
                transport.OperationId!,
                transport.OperationType!,
                transport.BaseVersion,
                transport.Payload.Clone());

            PrepareTimeout(request);
            OperationResult result;
            try
            {
                result = _store.Execute(request, transaction => ExecuteDomain(request, transaction));
            }
            catch
            {
                ReloadLocks();
                throw;
            }

            if (result.Status == OperationExecutionStatus.OperationIdConflict)
            {
                return new HubOperationResult(409, new HubOperationReply(
                    "OPERATION_ID_CONFLICT", "REJECTED", null, request.RequestHash, false, Empty()));
            }

            var stored = JsonSerializer.Deserialize<StoredOperationResponse>(result.Response!, HubJson.Options)!;
            return new HubOperationResult(
                HttpStatus(stored.Status),
                new HubOperationReply(
                    stored.Status,
                    stored.ResultStatus,
                    result.OperationSeq,
                    request.RequestHash,
                    result.IsReplay,
                    stored.Response));
        }
    }

    public DraftLock? GetLock(Guid storeId, string phone)
    {
        lock (_gate)
        {
            var current = _locks.Get(storeId, phone);
            PersistTimeout(current);
            return current;
        }
    }

    public bool Revoke(Guid storeId, Guid deviceId, Guid ownerUserId)
    {
        lock (_gate)
        {
            _locks.Disconnect(deviceId);
            var revoked = _access.RevokeDevice(storeId, deviceId, ownerUserId, _clock.UtcNow);
            ReloadLocks();
            return revoked;
        }
    }

    public int Disconnect(Guid storeId, Guid deviceId)
    {
        lock (_gate)
        {
            _locks.Disconnect(deviceId);
            var released = _access.DisconnectDevice(storeId, deviceId, _clock.UtcNow);
            ReloadLocks();
            return released;
        }
    }

    private string ExecuteDomain(OperationRequest request, IHubTransaction transaction)
    {
        try
        {
            return request.OperationType switch
            {
                "product.update" => UpdateProduct(request, transaction),
                "invoice.draft_create" => CreateDraft(request, transaction),
                "invoice.item_add" => AddItem(request, transaction),
                "invoice.issue" => Issue(request, transaction),
                "invoice.cancel" => Cancel(request, transaction),
                _ => Rejected("OPERATION_TYPE_UNSUPPORTED")
            };
        }
        catch (DraftLockException exception)
        {
            return Rejected(exception.Code);
        }
        catch (DomainException exception)
        {
            return Rejected(exception.Code);
        }
        catch (OverflowException)
        {
            return Rejected("VALIDATION_ERROR");
        }
    }

    private string UpdateProduct(OperationRequest request, IHubTransaction transaction)
    {
        var productId = RequiredString(request.Payload, "product_id");
        var current = transaction.GetProduct(request.StoreId, productId);
        if (current is null)
        {
            return Rejected("PRODUCT_NOT_FOUND");
        }

        if (request.BaseVersion is null || request.BaseVersion != current.Version)
        {
            return Rejected("VERSION_CONFLICT", new
            {
                current_version = current.Version,
                product = ProductProjection(current)
            });
        }

        var price = current.UnitPriceRials;
        if (request.Payload.TryGetProperty("unit_price_rials", out var priceElement))
        {
            if (priceElement.ValueKind != JsonValueKind.Number || !priceElement.TryGetInt64(out price) || price < 0)
            {
                throw new DomainException("VALIDATION_ERROR");
            }
        }

        var specification = current.Specification;
        if (request.Payload.TryGetProperty("specification", out var specificationElement))
        {
            specification = specificationElement.ValueKind == JsonValueKind.Null
                ? null
                : specificationElement.GetString() ?? throw new DomainException("VALIDATION_ERROR");
        }

        var updated = current with
        {
            UnitPriceRials = price,
            Specification = specification,
            Version = checked(current.Version + 1)
        };
        transaction.SaveProduct(updated);
        return Accepted(new { product = ProductProjection(updated), version = updated.Version });
    }

    private string CreateDraft(OperationRequest request, IHubTransaction transaction)
    {
        var phone = PhoneNormalizer.Normalize(RequiredString(request.Payload, "customer_phone"));
        var invoiceId = Guid.NewGuid();
        var invoice = new Invoice(invoiceId, request.StoreId, request.OperationId, request.DeviceId, _clock.UtcNow, null);
        // Acquire before any write: a conflict must not leave a second draft in the rejected transaction.
        var draftLock = _locks.Acquire(
            request.StoreId, phone, invoiceId, request.DeviceId, Guid.NewGuid(), 300);
        transaction.SaveInvoice(invoice);
        transaction.SaveDraftLock(draftLock);
        return Accepted(new
        {
            invoice = InvoiceProjection(invoice),
            @lock = LockProjection(draftLock),
            next_display_number = checked(transaction.GetDisplayNumberHighWatermark(request.StoreId) + 1)
        });
    }

    private string AddItem(OperationRequest request, IHubTransaction transaction)
    {
        var invoiceId = RequiredGuid(request.Payload, "invoice_id");
        var invoice = transaction.GetInvoice(request.StoreId, invoiceId)
            ?? throw new DomainException("INVOICE_NOT_FOUND");
        var draftLock = RequireOwnedLock(request, invoiceId);
        var description = RequiredString(request.Payload, "description");
        var quantity = RequiredInt64(request.Payload, "quantity_millis");
        var unitPrice = RequiredInt64(request.Payload, "unit_price_rials");
        var lineTotal = RequiredInt64(request.Payload, "line_total_rials");
        if (quantity <= 0 || unitPrice < 0 || lineTotal < 0)
        {
            throw new DomainException("VALIDATION_ERROR");
        }

        var expectedTotal = checked(quantity * unitPrice);
        if (expectedTotal % 1000 != 0 || lineTotal != expectedTotal / 1000)
        {
            throw new DomainException("VALIDATION_ERROR");
        }

        string? productId = null;
        if (request.Payload.TryGetProperty("product_id", out _))
        {
            productId = RequiredString(request.Payload, "product_id");
            var product = transaction.GetProduct(request.StoreId, productId);
            if (product is null || !product.IsActive)
            {
                throw new DomainException("PRODUCT_NOT_FOUND");
            }
        }

        invoice.AddItem(new InvoiceItem(
            Guid.NewGuid(), request.StoreId, invoice.Items.Count + 1, description,
            quantity, unitPrice, lineTotal, productId));
        if (!_locks.AcceptChange(request.StoreId, draftLock.CustomerPhone, request.DeviceId, DraftChangeKind.Item))
        {
            throw new DomainException("LOCK_NOT_OWNED");
        }

        transaction.SaveInvoice(invoice);
        transaction.SaveDraftLock(draftLock);
        return Accepted(new { invoice = InvoiceProjection(invoice), @lock = LockProjection(draftLock) });
    }

    private string Issue(OperationRequest request, IHubTransaction transaction)
    {
        var invoiceId = RequiredGuid(request.Payload, "invoice_id");
        var invoice = transaction.GetInvoice(request.StoreId, invoiceId)
            ?? throw new DomainException("INVOICE_NOT_FOUND");
        var draftLock = RequireOwnedLock(request, invoiceId);
        try
        {
            invoice.EnsureIssuable();
        }
        catch (InvalidOperationException)
        {
            throw new DomainException("INVOICE_NOT_ISSUABLE");
        }

        var next = checked(transaction.GetDisplayNumberHighWatermark(request.StoreId) + 1);
        var snapshot = HubJson.Element(new { invoice_id = invoiceId, display_number = next });
        var payload = CanonicalJson.Serialize(snapshot);
        var digest = CanonicalJson.Sha256Hex(snapshot);
        invoice.Issue(next, _clock.UtcNow, payload, digest);
        if (!_locks.Release(request.StoreId, draftLock.CustomerPhone, request.DeviceId, LockReleaseReason.Issue))
        {
            throw new DomainException("LOCK_NOT_OWNED");
        }

        transaction.SaveInvoice(invoice);
        transaction.SaveDraftLock(draftLock);
        transaction.SetDisplayNumberHighWatermark(request.StoreId, next);
        return Accepted(new
        {
            invoice = InvoiceProjection(invoice),
            @lock = LockProjection(draftLock),
            next_display_number = checked(next + 1)
        });
    }

    private string Cancel(OperationRequest request, IHubTransaction transaction)
    {
        var invoiceId = RequiredGuid(request.Payload, "invoice_id");
        var invoice = transaction.GetInvoice(request.StoreId, invoiceId)
            ?? throw new DomainException("INVOICE_NOT_FOUND");
        var draftLock = RequireOwnedLock(request, invoiceId);
        invoice.Retire(_clock.UtcNow, RetirementReason.Cancel);
        if (!_locks.Release(request.StoreId, draftLock.CustomerPhone, request.DeviceId, LockReleaseReason.Cancel))
        {
            throw new DomainException("LOCK_NOT_OWNED");
        }

        transaction.SaveInvoice(invoice);
        transaction.SaveDraftLock(draftLock);
        return Accepted(new { invoice = InvoiceProjection(invoice), @lock = LockProjection(draftLock) });
    }

    private DraftLock RequireOwnedLock(OperationRequest request, Guid invoiceId)
    {
        // PrepareTimeout runs before the SQLite transaction. Do not open a nested
        // store transaction here; an expired lock is simply not owned.
        var current = _locks.GetByInvoice(request.StoreId, invoiceId);
        if (current is null || current.State != DraftLockState.Active || current.DeviceId != request.DeviceId)
        {
            throw new DomainException("LOCK_NOT_OWNED");
        }

        return current;
    }

    private void PrepareTimeout(OperationRequest request)
    {
        if (request.OperationType == "invoice.draft_create" &&
            request.Payload.TryGetProperty("customer_phone", out var phone) &&
            phone.ValueKind == JsonValueKind.String)
        {
            PersistTimeout(_locks.Get(request.StoreId, phone.GetString()!));
        }
        else if (request.OperationType.StartsWith("invoice.", StringComparison.Ordinal) &&
                 request.Payload.TryGetProperty("invoice_id", out var invoice) &&
                 invoice.ValueKind == JsonValueKind.String &&
                 Guid.TryParse(invoice.GetString(), out var invoiceId))
        {
            PersistTimeout(_locks.GetByInvoice(request.StoreId, invoiceId));
        }
    }

    private void PersistTimeout(DraftLock? current)
    {
        if (current?.State == DraftLockState.Released && current.ReleaseReason == LockReleaseReason.Timeout)
        {
            _store.CompleteTimeout(current);
        }
    }

    private void ReloadLocks() => _locks.ReplaceForPersistence(_store.GetDraftLocks());

    private static HubOperationResult? ValidateEnvelope(
        Guid routeStoreId,
        HubSession session,
        HubOperationRequest request)
    {
        string? code = null;
        if (request.ProtocolVersion != "0" || request.SessionId != session.SessionId ||
            request.StoreId != routeStoreId || request.StoreId != session.StoreId ||
            request.DeviceId != session.DeviceId || string.IsNullOrWhiteSpace(request.OperationId) ||
            string.IsNullOrWhiteSpace(request.OperationType) || request.Payload.ValueKind != JsonValueKind.Object)
        {
            code = "VALIDATION_ERROR";
        }

        OperationRequest? domain = null;
        if (code is null)
        {
            domain = new OperationRequest(
                request.StoreId!.Value, request.DeviceId!.Value, request.OperationId!,
                request.OperationType!, request.BaseVersion, request.Payload);
            if (request.RequestHash is null ||
                !string.Equals(request.RequestHash, domain.RequestHash, StringComparison.Ordinal))
            {
                code = "REQUEST_HASH_MISMATCH";
            }
        }

        return code is null
            ? null
            : new HubOperationResult(400, new HubOperationReply(
                code, "REJECTED", null, request.RequestHash, false, Empty()));
    }

    private static string Accepted(object response) => Stored("SUCCEEDED", "ACCEPTED", response);
    private static string Rejected(string status) => Stored(status, "REJECTED", new { });
    private static string Rejected(string status, object response) => Stored(status, "REJECTED", response);

    private static string Stored(string status, string resultStatus, object response) =>
        JsonSerializer.Serialize(new StoredOperationResponse(status, resultStatus, HubJson.Element(response)), HubJson.Options);

    private static int HttpStatus(string status) => status switch
    {
        "SUCCEEDED" => 200,
        "PRODUCT_NOT_FOUND" or "INVOICE_NOT_FOUND" => 404,
        "VERSION_CONFLICT" or "DRAFT_LOCK_CONFLICT" or "DRAFT_ALREADY_ACTIVE" or
            "LOCK_NOT_OWNED" or "INVOICE_NOT_ISSUABLE" => 409,
        _ => 400
    };

    private static object ProductProjection(Product product) => new
    {
        product_id = product.ProductId,
        unit_price_rials = product.UnitPriceRials,
        version = product.Version,
        specification = product.Specification
    };

    private static object InvoiceProjection(Invoice invoice) => new
    {
        invoice_id = invoice.InvoiceId,
        external_invoice_number = invoice.ExternalInvoiceNumber,
        display_number = invoice.DisplayNumber,
        status = invoice.Status.ToString().ToUpperInvariant(),
        retired = invoice.RetiredAt is not null,
        retirement_reason = invoice.RetirementReason?.ToString().ToUpperInvariant(),
        issued_at = invoice.IssuedAt,
        issued_payload = IssuedPayloadProjection(invoice),
        issued_content_sha256 = invoice.IssuedContentSha256,
        items = invoice.Items.Select(item => new
        {
            invoice_item_id = item.InvoiceItemId,
            line_number = item.LineNumber,
            description = item.Description,
            quantity_millis = item.QuantityMillis,
            unit_price_rials = item.UnitPriceRials,
            line_total_rials = item.LineTotalRials,
            product_id = item.ProductId
        }).ToArray()
    };

    private static JsonElement? IssuedPayloadProjection(Invoice invoice)
    {
        if (invoice.IssuedPayload is null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(invoice.IssuedPayload);
        return document.RootElement.Clone();
    }

    private static object LockProjection(DraftLock draftLock) => new
    {
        lock_id = draftLock.LockId,
        store_id = draftLock.StoreId,
        customer_phone = draftLock.CustomerPhone,
        invoice_id = draftLock.InvoiceId,
        device_id = draftLock.DeviceId,
        acquired_at = draftLock.AcquiredAt,
        last_activity_at = draftLock.LastActivityAt,
        timeout_seconds = draftLock.TimeoutSeconds,
        expires_at = draftLock.ExpiresAt,
        state = draftLock.State.ToString().ToUpperInvariant(),
        release_reason = draftLock.ReleaseReason?.ToString().ToUpperInvariant()
    };

    private static string RequiredString(JsonElement payload, string name)
    {
        if (!payload.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new DomainException("VALIDATION_ERROR");
        }

        return value.GetString()!;
    }

    private static Guid RequiredGuid(JsonElement payload, string name) =>
        Guid.TryParse(RequiredString(payload, name), out var value)
            ? value
            : throw new DomainException("VALIDATION_ERROR");

    private static long RequiredInt64(JsonElement payload, string name)
    {
        if (!payload.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt64(out var number))
        {
            throw new DomainException("VALIDATION_ERROR");
        }

        return number;
    }

    private static JsonElement Empty() => HubJson.Element(new { });

    private sealed class DomainException(string code) : Exception
    {
        public string Code { get; } = code;
    }
}
