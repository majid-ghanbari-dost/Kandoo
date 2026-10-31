using System.Globalization;
using System.Text.Json;
using Kandoo.Core;
using Microsoft.Data.Sqlite;

namespace Kandoo.Hub;

internal sealed class SqliteHubTransaction : IHubTransaction
{
    private readonly SqliteConnection _connection;
    private readonly SqliteTransaction _transaction;
    private bool _completed;

    internal SqliteHubTransaction(SqliteConnection connection)
    {
        _connection = connection;
        _transaction = connection.BeginTransaction();
    }

    public Product? GetProduct(Guid storeId, string productId)
    {
        using var command = Command("""
            SELECT store_id, product_id, name, normalized_name, similarity_key,
                   unit_price_rials, version, is_active, specification, product_tag_number,
                   category, color, size, material, brand, barcode, purchase_price_rials
            FROM products WHERE store_id = $store AND product_id = $product;
            """);
        Add(command, "$store", Uuid(storeId));
        Add(command, "$product", productId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadProduct(reader) : null;
    }

    public IReadOnlyList<Product> GetProducts()
    {
        using var command = Command("""
            SELECT store_id, product_id, name, normalized_name, similarity_key,
                   unit_price_rials, version, is_active, specification, product_tag_number,
                   category, color, size, material, brand, barcode, purchase_price_rials
            FROM products ORDER BY store_id, product_id;
            """);
        using var reader = command.ExecuteReader();
        var products = new List<Product>();
        while (reader.Read())
        {
            products.Add(ReadProduct(reader));
        }

        return products;
    }

    public void SaveProduct(Product product)
    {
        using var command = Command("""
            INSERT INTO products
                (store_id, product_id, name, normalized_name, similarity_key,
                 unit_price_rials, version, is_active, specification, product_tag_number,
                 category, color, size, material, brand, barcode, purchase_price_rials)
            VALUES ($store, $product, $name, $normalized, $similarity, $price, $version, $active, $specification,
                    $tag, $category, $color, $size, $material, $brand, $barcode, $purchasePrice)
            ON CONFLICT(store_id, product_id) DO UPDATE SET
                name = excluded.name, normalized_name = excluded.normalized_name,
                similarity_key = excluded.similarity_key, unit_price_rials = excluded.unit_price_rials,
                version = excluded.version, is_active = excluded.is_active, specification = excluded.specification,
                category = excluded.category, color = excluded.color, size = excluded.size,
                material = excluded.material, brand = excluded.brand, barcode = excluded.barcode,
                purchase_price_rials = excluded.purchase_price_rials;
            """);
        Add(command, "$store", Uuid(product.StoreId));
        Add(command, "$product", product.ProductId);
        Add(command, "$name", product.Name);
        Add(command, "$normalized", product.NormalizedName);
        Add(command, "$similarity", product.SimilarityKey);
        Add(command, "$price", product.UnitPriceRials);
        Add(command, "$version", product.Version);
        Add(command, "$active", product.IsActive ? 1 : 0);
        Add(command, "$specification", product.Specification);
        Add(command, "$tag", product.ProductTagNumber);
        Add(command, "$category", product.Category);
        Add(command, "$color", product.Color);
        Add(command, "$size", product.Size);
        Add(command, "$material", product.Material);
        Add(command, "$brand", product.Brand);
        Add(command, "$barcode", product.Barcode);
        Add(command, "$purchasePrice", product.PurchasePriceRials);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<ProductOperatorField> GetProductOperatorFields(Guid storeId, string productId)
    {
        using var command = Command("""
            SELECT field_key, field_value FROM product_operator_fields
            WHERE store_id = $store AND product_id = $product ORDER BY position;
            """);
        Add(command, "$store", Uuid(storeId));
        Add(command, "$product", productId);
        using var reader = command.ExecuteReader();
        var fields = new List<ProductOperatorField>();
        while (reader.Read())
        {
            fields.Add(new ProductOperatorField(reader.GetString(0), reader.GetString(1)));
        }

        return fields;
    }

    public void SaveProductOperatorFields(Guid storeId, string productId, IReadOnlyList<ProductOperatorField> fields)
    {
        var normalized = ProductRegistrationInput.ValidateOperatorFields(fields);
        using (var delete = Command("""
            DELETE FROM product_operator_fields WHERE store_id = $store AND product_id = $product;
            """))
        {
            Add(delete, "$store", Uuid(storeId));
            Add(delete, "$product", productId);
            delete.ExecuteNonQuery();
        }

        for (var position = 0; position < normalized.Count; position++)
        {
            using var insert = Command("""
                INSERT INTO product_operator_fields (store_id, product_id, position, field_key, field_value)
                VALUES ($store, $product, $position, $key, $value);
                """);
            Add(insert, "$store", Uuid(storeId));
            Add(insert, "$product", productId);
            Add(insert, "$position", position);
            Add(insert, "$key", normalized[position].Key);
            Add(insert, "$value", normalized[position].Value);
            insert.ExecuteNonQuery();
        }
    }

    public void AppendInventory(InventoryEvent inventoryEvent)
    {
        if (inventoryEvent.QuantityDeltaMillis == 0)
        {
            throw new ArgumentException("Inventory deltas must be non-zero.", nameof(inventoryEvent));
        }

        using var command = Command("""
            INSERT INTO inventory_events
                (inventory_event_id, store_id, product_id, quantity_delta_millis,
                 event_type, operation_id, device_id, reverses_event_id, reason)
            VALUES ($id, $store, $product, $delta, $type, $operation, $device, $reverses, $reason);
            """);
        Add(command, "$id", Uuid(inventoryEvent.InventoryEventId));
        Add(command, "$store", Uuid(inventoryEvent.StoreId));
        Add(command, "$product", inventoryEvent.ProductId);
        Add(command, "$delta", inventoryEvent.QuantityDeltaMillis);
        Add(command, "$type", (int)inventoryEvent.EventType);
        Add(command, "$operation", inventoryEvent.OperationId);
        Add(command, "$device", Uuid(inventoryEvent.DeviceId));
        Add(command, "$reverses", inventoryEvent.ReversesEventId is Guid id ? Uuid(id) : null);
        Add(command, "$reason", inventoryEvent.Reason);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<InventoryEvent> GetInventoryEvents()
    {
        using var command = Command("""
            SELECT inventory_event_id, store_id, product_id, quantity_delta_millis,
                   event_type, operation_id, device_id, reverses_event_id, reason
            FROM inventory_events ORDER BY event_order;
            """);
        using var reader = command.ExecuteReader();
        var events = new List<InventoryEvent>();
        while (reader.Read())
        {
            events.Add(new InventoryEvent(
                Guid.Parse(reader.GetString(0)),
                Guid.Parse(reader.GetString(1)),
                reader.GetString(2),
                reader.GetInt64(3),
                (InventoryEventType)reader.GetInt32(4),
                reader.GetString(5),
                Guid.Parse(reader.GetString(6)),
                reader.IsDBNull(7) ? null : Guid.Parse(reader.GetString(7)),
                reader.IsDBNull(8) ? null : reader.GetString(8)));
        }

        return events;
    }

    public Invoice? GetInvoice(Guid storeId, Guid invoiceId)
    {
        InvoiceRow? row = null;
        using (var command = Command("""
            SELECT store_id, invoice_id, created_operation_id, created_by_device_id,
                   created_at, external_invoice_number, display_number, status,
                   retired_at, retirement_reason, issued_at, issued_payload,
                   issued_content_sha256, customer_id
            FROM invoices WHERE store_id = $store AND invoice_id = $invoice;
            """))
        {
            Add(command, "$store", Uuid(storeId));
            Add(command, "$invoice", Uuid(invoiceId));
            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                row = ReadInvoiceRow(reader);
            }
        }

        return row is null ? null : HydrateInvoice(row);
    }

    public IReadOnlyList<Invoice> GetInvoices()
    {
        var ids = new List<(Guid StoreId, Guid InvoiceId)>();
        using (var command = Command("SELECT store_id, invoice_id FROM invoices ORDER BY store_id, invoice_id;"))
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                ids.Add((Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1))));
            }
        }

        var invoices = new List<Invoice>(ids.Count);
        foreach (var (storeId, invoiceId) in ids)
        {
            invoices.Add(GetInvoice(storeId, invoiceId)!);
        }

        return invoices;
    }

    public void SaveInvoice(Invoice invoice)
    {
        using (var guard = Command("SELECT status, retired_at FROM invoices WHERE store_id = $store AND invoice_id = $invoice;"))
        {
            Add(guard, "$store", Uuid(invoice.StoreId));
            Add(guard, "$invoice", Uuid(invoice.InvoiceId));
            using var reader = guard.ExecuteReader();
            if (reader.Read())
            {
                var oldStatus = (InvoiceStatus)reader.GetInt32(0);
                var oldRetired = !reader.IsDBNull(1);
                if (oldStatus is InvoiceStatus.Issued or InvoiceStatus.Revoked or InvoiceStatus.Superseded || oldRetired)
                {
                    throw new InvalidOperationException("ISSUED_INVOICE_IMMUTABLE");
                }
            }
        }

        using (var command = Command("""
            INSERT INTO invoices
                (store_id, invoice_id, created_operation_id, created_by_device_id,
                 created_at, external_invoice_number, display_number, status,
                 retired_at, retirement_reason, issued_at, issued_payload,
                 issued_content_sha256, customer_id)
            VALUES ($store, $invoice, $operation, $device, $created, $external,
                    $display, $status, $retired, $retirement, $issued, $payload, $digest, $customer)
            ON CONFLICT(store_id, invoice_id) DO UPDATE SET
                created_operation_id = excluded.created_operation_id,
                created_by_device_id = excluded.created_by_device_id,
                created_at = excluded.created_at, external_invoice_number = excluded.external_invoice_number,
                display_number = excluded.display_number, status = excluded.status,
                retired_at = excluded.retired_at, retirement_reason = excluded.retirement_reason,
                issued_at = excluded.issued_at, issued_payload = excluded.issued_payload,
                issued_content_sha256 = excluded.issued_content_sha256, customer_id = excluded.customer_id;
            """))
        {
            Add(command, "$store", Uuid(invoice.StoreId));
            Add(command, "$invoice", Uuid(invoice.InvoiceId));
            Add(command, "$operation", invoice.CreatedOperationId);
            Add(command, "$device", Uuid(invoice.CreatedByDeviceId));
            Add(command, "$created", Date(invoice.CreatedAt));
            Add(command, "$external", invoice.ExternalInvoiceNumber);
            Add(command, "$display", invoice.DisplayNumber);
            Add(command, "$status", (int)invoice.Status);
            Add(command, "$retired", invoice.RetiredAt is DateTimeOffset retired ? Date(retired) : null);
            Add(command, "$retirement", invoice.RetirementReason is RetirementReason reason ? (int)reason : null);
            Add(command, "$issued", invoice.IssuedAt is DateTimeOffset issued ? Date(issued) : null);
            Add(command, "$payload", invoice.IssuedPayload);
            Add(command, "$digest", invoice.IssuedContentSha256);
            Add(command, "$customer", invoice.CustomerId is Guid customer ? Uuid(customer) : null);
            command.ExecuteNonQuery();
        }

        using (var delete = Command("DELETE FROM invoice_items WHERE store_id = $store AND invoice_id = $invoice;"))
        {
            Add(delete, "$store", Uuid(invoice.StoreId));
            Add(delete, "$invoice", Uuid(invoice.InvoiceId));
            delete.ExecuteNonQuery();
        }

        foreach (var item in invoice.Items)
        {
            using var command = Command("""
                INSERT INTO invoice_items
                    (store_id, invoice_id, invoice_item_id, line_number, description,
                     quantity_millis, unit_price_rials, line_total_rials, product_id)
                VALUES ($store, $invoice, $id, $line, $description, $quantity, $price, $total, $product);
                """);
            Add(command, "$store", Uuid(invoice.StoreId));
            Add(command, "$invoice", Uuid(invoice.InvoiceId));
            Add(command, "$id", Uuid(item.InvoiceItemId));
            Add(command, "$line", item.LineNumber);
            Add(command, "$description", item.Description);
            Add(command, "$quantity", item.QuantityMillis);
            Add(command, "$price", item.UnitPriceRials);
            Add(command, "$total", item.LineTotalRials);
            Add(command, "$product", item.ProductId);
            command.ExecuteNonQuery();
        }
    }

    public long GetDisplayNumberHighWatermark(Guid storeId)
    {
        using var command = Command("SELECT display_number_high_watermark FROM store_counters WHERE store_id = $store;");
        Add(command, "$store", Uuid(storeId));
        var value = command.ExecuteScalar();
        return value is null || value is DBNull ? 0 : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    public void SetDisplayNumberHighWatermark(Guid storeId, long value)
    {
        if (value < 0 || value < GetDisplayNumberHighWatermark(storeId))
        {
            throw new InvalidOperationException("DISPLAY_NUMBER_REGRESSION");
        }

        using var command = Command("""
            INSERT INTO store_counters (store_id, display_number_high_watermark) VALUES ($store, $value)
            ON CONFLICT(store_id) DO UPDATE SET display_number_high_watermark = excluded.display_number_high_watermark;
            """);
        Add(command, "$store", Uuid(storeId));
        Add(command, "$value", value);
        command.ExecuteNonQuery();
    }

    public long GetProductTagHighWatermark(Guid storeId)
    {
        using var command = Command("SELECT product_tag_high_watermark FROM store_counters WHERE store_id = $store;");
        Add(command, "$store", Uuid(storeId));
        var value = command.ExecuteScalar();
        return value is null || value is DBNull ? 0 : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    public long AllocateProductTag(Guid storeId, long? requestedTag = null)
    {
        if (requestedTag is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requestedTag));
        }

        var current = GetProductTagHighWatermark(storeId);
        long maximum;
        using (var command = Command("SELECT COALESCE(MAX(product_tag_number), 0) FROM products WHERE store_id = $store;"))
        {
            Add(command, "$store", Uuid(storeId));
            maximum = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        if (current < maximum)
        {
            throw new InvalidOperationException("PRODUCT_TAG_COUNTER_CONFLICT");
        }

        if (requestedTag is long requested)
        {
            if (requested > current)
            {
                SetProductTagHighWatermark(storeId, requested);
            }

            return requested;
        }

        var next = checked(current + 1);
        SetProductTagHighWatermark(storeId, next);
        return next;
    }

    public void SetProductTagHighWatermark(Guid storeId, long value)
    {
        if (value < 0 || value < GetProductTagHighWatermark(storeId))
        {
            throw new InvalidOperationException("PRODUCT_TAG_REGRESSION");
        }

        using (var command = Command("SELECT COALESCE(MAX(product_tag_number), 0) FROM products WHERE store_id = $store;"))
        {
            Add(command, "$store", Uuid(storeId));
            if (value < Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture))
            {
                throw new InvalidOperationException("PRODUCT_TAG_COUNTER_CONFLICT");
            }
        }

        using var write = Command("""
            INSERT INTO store_counters (store_id, display_number_high_watermark, product_tag_high_watermark)
            VALUES ($store, 0, $value)
            ON CONFLICT(store_id) DO UPDATE SET product_tag_high_watermark = excluded.product_tag_high_watermark;
            """);
        Add(write, "$store", Uuid(storeId));
        Add(write, "$value", value);
        write.ExecuteNonQuery();
    }

    public Product? GetProductByTag(Guid storeId, long productTagNumber)
    {
        using var command = Command("""
            SELECT store_id, product_id, name, normalized_name, similarity_key,
                   unit_price_rials, version, is_active, specification, product_tag_number,
                   category, color, size, material, brand, barcode, purchase_price_rials
            FROM products WHERE store_id = $store AND product_tag_number = $tag;
            """);
        Add(command, "$store", Uuid(storeId));
        Add(command, "$tag", productTagNumber);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadProduct(reader) : null;
    }

    internal IReadOnlyList<HubDeviceRestore> GetRestoreDevices(Guid storeId)
    {
        using var command = Command("""
            SELECT store_id, device_id, device_role, pairing_status, owner_user_id, label, platform,
                   device_public_key, credential_hash, paired_at, revoked_at
            FROM devices WHERE store_id = $store ORDER BY device_id;
            """);
        Add(command, "$store", Uuid(storeId));
        using var reader = command.ExecuteReader();
        var values = new List<HubDeviceRestore>();
        while (reader.Read())
        {
            values.Add(new HubDeviceRestore
            {
                StoreId = Guid.Parse(reader.GetString(0)),
                DeviceId = Guid.Parse(reader.GetString(1)),
                DeviceRole = (HubDeviceRole)reader.GetInt32(2),
                PairingStatus = (HubPairingStatus)reader.GetInt32(3),
                OwnerUserId = reader.IsDBNull(4) ? null : Guid.Parse(reader.GetString(4)),
                Label = reader.GetString(5),
                Platform = reader.GetString(6),
                DevicePublicKey = reader.IsDBNull(7) ? null : reader.GetString(7),
                CredentialHash = reader.GetString(8),
                PairedAt = DateTimeOffset.Parse(reader.GetString(9), CultureInfo.InvariantCulture),
                RevokedAt = reader.IsDBNull(10) ? null : DateTimeOffset.Parse(reader.GetString(10), CultureInfo.InvariantCulture)
            });
        }

        return values;
    }

    internal void RestoreDevice(HubDeviceRestore value)
    {
        using var command = Command("""
            INSERT INTO devices (store_id, device_id, device_role, pairing_status, owner_user_id, label, platform,
                                 device_public_key, credential_hash, paired_at, revoked_at)
            VALUES ($store, $device, $role, $status, $owner, $label, $platform, $publicKey, $credential, $paired, $revoked);
            """);
        Add(command, "$store", Uuid(value.StoreId));
        Add(command, "$device", Uuid(value.DeviceId));
        Add(command, "$role", (int)value.DeviceRole);
        Add(command, "$status", (int)value.PairingStatus);
        Add(command, "$owner", value.OwnerUserId is Guid owner ? Uuid(owner) : null);
        Add(command, "$label", value.Label);
        Add(command, "$platform", value.Platform);
        Add(command, "$publicKey", value.DevicePublicKey);
        Add(command, "$credential", value.CredentialHash);
        Add(command, "$paired", Date(value.PairedAt));
        Add(command, "$revoked", value.RevokedAt is DateTimeOffset revoked ? Date(revoked) : null);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<OperationRecord> GetOperations()
    {
        using var command = Command("""
            SELECT store_id, device_id, operation_id, operation_type, request_hash, operation_seq, response
            FROM operations ORDER BY store_id, operation_seq;
            """);
        using var reader = command.ExecuteReader();
        var records = new List<OperationRecord>();
        while (reader.Read())
        {
            records.Add(ReadOperation(reader));
        }

        return records;
    }

    internal OperationRecord? FindOperation(Guid storeId, string operationId)
    {
        using var command = Command("""
            SELECT store_id, device_id, operation_id, operation_type, request_hash, operation_seq, response
            FROM operations WHERE store_id = $store AND operation_id = $operation;
            """);
        Add(command, "$store", Uuid(storeId));
        Add(command, "$operation", operationId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadOperation(reader) : null;
    }

    internal long NextOperationSequence(Guid storeId)
    {
        using var command = Command("SELECT COALESCE(MAX(operation_seq), 0) + 1 FROM operations WHERE store_id = $store;");
        Add(command, "$store", Uuid(storeId));
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    internal void AppendOperation(OperationRecord record)
    {
        using var command = Command("""
            INSERT INTO operations (store_id, device_id, operation_id, operation_type, request_hash, operation_seq, response)
            VALUES ($store, $device, $operation, $type, $hash, $sequence, $response);
            """);
        Add(command, "$store", Uuid(record.StoreId));
        Add(command, "$device", Uuid(record.DeviceId));
        Add(command, "$operation", record.OperationId);
        Add(command, "$type", record.OperationType);
        Add(command, "$hash", record.RequestHash);
        Add(command, "$sequence", record.OperationSeq);
        Add(command, "$response", record.Response);
        command.ExecuteNonQuery();
    }

    internal void AppendOutbox(OperationRecord record, OperationRequest request, HubWriterIdentity writer, DateTimeOffset recordedAt)
    {
        using (var command = Command("""
            INSERT INTO operation_outbox
                (store_id, operation_seq, operation_id, device_id, operation_type,
                 base_version, payload, request_hash, response)
            VALUES ($store, $sequence, $operation, $device, $type,
                    $base, $payload, $hash, $response);
            """))
        {
            Add(command, "$store", Uuid(record.StoreId));
            Add(command, "$sequence", record.OperationSeq);
            Add(command, "$operation", record.OperationId);
            Add(command, "$device", Uuid(record.DeviceId));
            Add(command, "$type", record.OperationType);
            Add(command, "$base", request.BaseVersion);
            Add(command, "$payload", CanonicalJson.Serialize(request.Payload));
            Add(command, "$hash", record.RequestHash);
            Add(command, "$response", record.Response);
            command.ExecuteNonQuery();
        }

        string? previousHash;
        using (var command = Command("SELECT entry_hash FROM replication_log WHERE store_id = $store ORDER BY operation_seq DESC LIMIT 1;"))
        {
            Add(command, "$store", Uuid(record.StoreId));
            previousHash = command.ExecuteScalar()?.ToString();
        }

        if (record.OperationSeq > 1 && previousHash is null)
        {
            throw new InvalidOperationException("LOCAL_REPLICATION_GAP");
        }

        var (outcome, responseSnapshot) = ReplicationOutcome(record);
        var entry = new ReplicationLogEntry
        {
            StoreId = record.StoreId,
            WriterDeviceId = writer.DeviceId,
            WriterEpoch = writer.Epoch,
            OperationSeq = record.OperationSeq,
            PreviousEntryHash = previousHash ?? HubReplicationConstants.ZeroHash,
            OperationId = record.OperationId,
            DeviceId = record.DeviceId,
            OperationType = record.OperationType,
            BaseVersion = request.BaseVersion,
            RequestHash = record.RequestHash,
            Payload = request.Payload.Clone(),
            Outcome = outcome,
            ResponseSnapshot = responseSnapshot,
            HubRecordedAt = recordedAt.ToUniversalTime().ToString("O")
        };
        entry.EntryHash = CanonicalJson.EntryHash(JsonSerializer.SerializeToElement(entry, HubJson.Options));
        var entryJson = CanonicalJson.Serialize(JsonSerializer.SerializeToElement(entry, HubJson.Options));
        using (var command = Command("""
            INSERT INTO replication_log (store_id, operation_seq, operation_id, entry_hash, entry_json)
            VALUES ($store, $sequence, $operation, $hash, $json);
            """))
        {
            Add(command, "$store", Uuid(record.StoreId));
            Add(command, "$sequence", record.OperationSeq);
            Add(command, "$operation", record.OperationId);
            Add(command, "$hash", entry.EntryHash);
            Add(command, "$json", entryJson);
            command.ExecuteNonQuery();
        }
    }

    internal void RestoreReplicationEntry(ReplicationLogEntry entry, string canonicalEntryJson)
    {
        using var command = Command("""
            INSERT INTO replication_log (store_id, operation_seq, operation_id, entry_hash, entry_json, acknowledged_at)
            VALUES ($store, $sequence, $operation, $hash, $json, $acknowledged);
            """);
        Add(command, "$store", Uuid(entry.StoreId));
        Add(command, "$sequence", entry.OperationSeq);
        Add(command, "$operation", entry.OperationId);
        Add(command, "$hash", entry.EntryHash);
        Add(command, "$json", canonicalEntryJson);
        Add(command, "$acknowledged", DateTimeOffset.UnixEpoch.ToString("O", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }

    private static (JsonElement Outcome, JsonElement ResponseSnapshot) ReplicationOutcome(OperationRecord record)
    {
        try
        {
            using var document = JsonDocument.Parse(record.Response);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                var status = root.TryGetProperty("status", out var statusElement) && statusElement.ValueKind == JsonValueKind.String
                    ? statusElement.GetString() ?? "SUCCEEDED"
                    : "SUCCEEDED";
                var resultStatus = root.TryGetProperty("result_status", out var resultElement) && resultElement.ValueKind == JsonValueKind.String
                    ? resultElement.GetString() ?? status
                    : status;
                var response = root.TryGetProperty("response", out var responseElement) ? responseElement.Clone() : root.Clone();
                var snapshot = JsonSerializer.SerializeToElement(new
                {
                    operation_seq = record.OperationSeq,
                    status,
                    result_status = resultStatus,
                    response
                }, HubJson.Options);
                return (root.Clone(), snapshot);
            }

            return FallbackReplicationOutcome(record, root.Clone());
        }
        catch (JsonException)
        {
            return FallbackReplicationOutcome(record, JsonSerializer.SerializeToElement(record.Response));
        }
    }

    private static (JsonElement Outcome, JsonElement ResponseSnapshot) FallbackReplicationOutcome(OperationRecord record, JsonElement response)
    {
        var outcome = JsonSerializer.SerializeToElement(new { result_status = "SUCCEEDED", response }, HubJson.Options);
        var snapshot = JsonSerializer.SerializeToElement(new
        {
            operation_seq = record.OperationSeq,
            status = "SUCCEEDED",
            result_status = "SUCCEEDED",
            response
        }, HubJson.Options);
        return (outcome, snapshot);
    }

    public DraftLock? GetDraftLock(Guid storeId, Guid lockId)
    {
        using var command = Command("""
            SELECT store_id, lock_id, customer_phone, invoice_id, device_id,
                   acquired_at, last_activity_at, timeout_seconds, state,
                   release_reason, released_at
            FROM draft_locks WHERE store_id = $store AND lock_id = $lock;
            """);
        Add(command, "$store", Uuid(storeId));
        Add(command, "$lock", Uuid(lockId));
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadDraftLock(reader) : null;
    }

    public IReadOnlyList<DraftLock> GetDraftLocks()
    {
        using var command = Command("""
            SELECT store_id, lock_id, customer_phone, invoice_id, device_id,
                   acquired_at, last_activity_at, timeout_seconds, state,
                   release_reason, released_at
            FROM draft_locks ORDER BY rowid;
            """);
        using var reader = command.ExecuteReader();
        var locks = new List<DraftLock>();
        while (reader.Read())
        {
            locks.Add(ReadDraftLock(reader));
        }

        return locks;
    }

    public void SaveDraftLock(DraftLock draftLock)
    {
        using var command = Command("""
            INSERT INTO draft_locks
                (store_id, lock_id, customer_phone, invoice_id, device_id,
                 acquired_at, last_activity_at, timeout_seconds, state,
                 release_reason, released_at)
            VALUES ($store, $lock, $phone, $invoice, $device, $acquired,
                    $activity, $timeout, $state, $reason, $released)
            ON CONFLICT(store_id, lock_id) DO UPDATE SET
                last_activity_at = excluded.last_activity_at,
                state = excluded.state,
                release_reason = excluded.release_reason,
                released_at = excluded.released_at;
            """);
        Add(command, "$store", Uuid(draftLock.StoreId));
        Add(command, "$lock", Uuid(draftLock.LockId));
        Add(command, "$phone", draftLock.CustomerPhone);
        Add(command, "$invoice", Uuid(draftLock.InvoiceId));
        Add(command, "$device", Uuid(draftLock.DeviceId));
        Add(command, "$acquired", Date(draftLock.AcquiredAt));
        Add(command, "$activity", Date(draftLock.LastActivityAt));
        Add(command, "$timeout", draftLock.TimeoutSeconds);
        Add(command, "$state", (int)draftLock.State);
        Add(command, "$reason", draftLock.ReleaseReason is LockReleaseReason reason ? (int)reason : null);
        Add(command, "$released", draftLock.ReleasedAt is DateTimeOffset released ? Date(released) : null);
        command.ExecuteNonQuery();
    }

    internal Dictionary<Guid, long> GetCounters()
    {
        using var command = Command("SELECT store_id, display_number_high_watermark FROM store_counters;");
        using var reader = command.ExecuteReader();
        var counters = new Dictionary<Guid, long>();
        while (reader.Read())
        {
            counters[Guid.Parse(reader.GetString(0))] = reader.GetInt64(1);
        }

        return counters;
    }

    internal void Commit()
    {
        _transaction.Commit();
        _completed = true;
    }

    internal void Rollback()
    {
        if (!_completed)
        {
            _transaction.Rollback();
            _completed = true;
        }
    }

    public void Dispose()
    {
        if (!_completed)
        {
            _transaction.Dispose();
            _completed = true;
        }
        else
        {
            _transaction.Dispose();
        }
    }

    private Invoice HydrateInvoice(InvoiceRow row)
    {
        var invoice = new Invoice(row.InvoiceId, row.StoreId, row.CreatedOperationId, row.CreatedByDeviceId, ParseDate(row.CreatedAt), row.ExternalInvoiceNumber);
        using (var command = Command("""
            SELECT invoice_item_id, store_id, line_number, description, quantity_millis,
                   unit_price_rials, line_total_rials, product_id
            FROM invoice_items WHERE store_id = $store AND invoice_id = $invoice ORDER BY line_number;
            """))
        {
            Add(command, "$store", Uuid(row.StoreId));
            Add(command, "$invoice", Uuid(row.InvoiceId));
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                invoice.AddItem(new InvoiceItem(
                    Guid.Parse(reader.GetString(0)),
                    Guid.Parse(reader.GetString(1)),
                    reader.GetInt32(2),
                    reader.GetString(3),
                    reader.GetInt64(4),
                    reader.GetInt64(5),
                    reader.GetInt64(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7)));
            }
        }

        invoice.RestoreForPersistence(
            row.DisplayNumber,
            (InvoiceStatus)row.Status,
            row.RetiredAt is null ? null : ParseDate(row.RetiredAt),
            row.RetirementReason is int reason ? (RetirementReason)reason : null,
            row.IssuedAt is null ? null : ParseDate(row.IssuedAt),
            row.IssuedPayload,
            row.IssuedContentSha256,
            row.CustomerId);
        return invoice;
    }

    private SqliteCommand Command(string sql)
    {
        var command = _connection.CreateCommand();
        command.Transaction = _transaction;
        command.CommandText = sql;
        return command;
    }

    private static Product ReadProduct(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(0)),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetString(4),
        reader.GetInt64(5),
        reader.GetInt32(6),
        reader.GetInt64(7) != 0,
        reader.IsDBNull(8) ? null : reader.GetString(8),
        reader.IsDBNull(9) ? null : reader.GetInt64(9),
        reader.IsDBNull(10) ? null : reader.GetString(10),
        reader.IsDBNull(11) ? null : reader.GetString(11),
        reader.IsDBNull(12) ? null : reader.GetString(12),
        reader.IsDBNull(13) ? null : reader.GetString(13),
        reader.IsDBNull(14) ? null : reader.GetString(14),
        reader.IsDBNull(15) ? null : reader.GetString(15),
        reader.IsDBNull(16) ? null : reader.GetInt64(16));

    private static OperationRecord ReadOperation(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(0)),
        Guid.Parse(reader.GetString(1)),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetString(4),
        reader.GetInt64(5),
        reader.GetString(6));

    private static DraftLock ReadDraftLock(SqliteDataReader reader)
    {
        var draftLock = new DraftLock(
            Guid.Parse(reader.GetString(1)),
            Guid.Parse(reader.GetString(0)),
            reader.GetString(2),
            Guid.Parse(reader.GetString(3)),
            Guid.Parse(reader.GetString(4)),
            ParseDate(reader.GetString(5)),
            reader.GetInt32(7));
        draftLock.RestoreForPersistence(
            ParseDate(reader.GetString(6)),
            (DraftLockState)reader.GetInt32(8),
            reader.IsDBNull(9) ? null : (LockReleaseReason)reader.GetInt32(9),
            reader.IsDBNull(10) ? null : ParseDate(reader.GetString(10)));
        return draftLock;
    }

    private static InvoiceRow ReadInvoiceRow(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(0)),
        Guid.Parse(reader.GetString(1)),
        reader.GetString(2),
        Guid.Parse(reader.GetString(3)),
        reader.GetString(4),
        reader.IsDBNull(5) ? null : reader.GetString(5),
        reader.IsDBNull(6) ? null : reader.GetInt64(6),
        reader.GetInt32(7),
        reader.IsDBNull(8) ? null : reader.GetString(8),
        reader.IsDBNull(9) ? null : reader.GetInt32(9),
        reader.IsDBNull(10) ? null : reader.GetString(10),
        reader.IsDBNull(11) ? null : reader.GetString(11),
        reader.IsDBNull(12) ? null : reader.GetString(12),
        reader.IsDBNull(13) ? null : Guid.Parse(reader.GetString(13)));

    private static string Uuid(Guid value) => value.ToString("D");
    private static string Date(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);
    private static DateTimeOffset ParseDate(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    private static void Add(SqliteCommand command, string name, object? value) => command.Parameters.AddWithValue(name, value ?? DBNull.Value);

    private sealed record InvoiceRow(
        Guid StoreId,
        Guid InvoiceId,
        string CreatedOperationId,
        Guid CreatedByDeviceId,
        string CreatedAt,
        string? ExternalInvoiceNumber,
        long? DisplayNumber,
        int Status,
        string? RetiredAt,
        int? RetirementReason,
        string? IssuedAt,
        string? IssuedPayload,
        string? IssuedContentSha256,
        Guid? CustomerId);
}
