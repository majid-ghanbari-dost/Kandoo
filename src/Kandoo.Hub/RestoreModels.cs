using System.Text.Json;
using Kandoo.Core;

namespace Kandoo.Hub;

public sealed record RestoreAuthorizationRequest(
    string RestoreRequestId,
    Guid StoreId,
    Guid NewHubDeviceId,
    DateTimeOffset Now);

public sealed record RestorePairedDeviceApproval(
    Guid DeviceId,
    DateTimeOffset ExpiresAt,
    string ManifestStateHash,
    Guid NewHubDeviceId,
    string RestoreRequestId,
    string Signature);

public sealed record RestoreSecondFactor(
    string Type,
    string? RecoveryKeyProof = null,
    RestorePairedDeviceApproval? Approval = null);

public sealed record RestoreAuthorizationContext(
    string RestoreRequestId,
    Guid StoreId,
    Guid NewHubDeviceId,
    RestoreManifest Manifest,
    DateTimeOffset Now);

public interface IRestoreOtpProofVerifier
{
    bool Verify(Guid storeId, string otpProof, DateTimeOffset now);
}

public interface IRestoreSecondFactorVerifier
{
    bool Verify(RestoreSecondFactor factor, RestoreAuthorizationContext context);
}

public sealed class RestoreAuthorizationGrant
{
    internal RestoreAuthorizationGrant(RestoreAuthorizationContext context, string secondFactor)
    {
        RestoreRequestId = context.RestoreRequestId;
        StoreId = context.StoreId;
        NewHubDeviceId = context.NewHubDeviceId;
        Manifest = context.Manifest;
        SecondFactor = secondFactor;
    }

    public string RestoreRequestId { get; }
    public Guid StoreId { get; }
    public Guid NewHubDeviceId { get; }
    public RestoreManifest Manifest { get; }
    public string SecondFactor { get; }
}

public sealed record RestoreAuthorizationResult(string Status, string? SecondFactor, RestoreAuthorizationGrant? Grant);
public sealed record RestoreVerificationResult(
    string Status,
    string ConsistencyCheck,
    string ComputedStateHash,
    bool CanActivate,
    long? NewWriterEpoch,
    string? Quarantine = null);

public sealed record RestoreCompletionResult(
    string Status,
    string ComputedStateHash,
    long RestoredThroughSeq,
    Guid NewWriterDeviceId,
    long? NewWriterEpoch,
    long NextOperationSeq,
    bool OwnerLossAcknowledgementRequired,
    IReadOnlyList<long> UnrecoveredOperationSeqs,
    bool SessionsRestored = false);

public sealed class HubStoreRestore
{
    public Guid StoreId { get; set; }
}

public sealed class HubDeviceRestore
{
    public Guid StoreId { get; set; }
    public Guid DeviceId { get; set; }
    public HubDeviceRole DeviceRole { get; set; }
    public HubPairingStatus PairingStatus { get; set; }
    public Guid? OwnerUserId { get; set; }
    public string Label { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string? DevicePublicKey { get; set; }
    public string CredentialHash { get; set; } = string.Empty;
    public DateTimeOffset PairedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

public sealed class HubInvoiceRestore
{
    public Guid InvoiceId { get; set; }
    public Guid StoreId { get; set; }
    public string CreatedOperationId { get; set; } = string.Empty;
    public Guid CreatedByDeviceId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? ExternalInvoiceNumber { get; set; }
    public long? DisplayNumber { get; set; }
    public InvoiceStatus Status { get; set; }
    public DateTimeOffset? RetiredAt { get; set; }
    public RetirementReason? RetirementReason { get; set; }
    public DateTimeOffset? IssuedAt { get; set; }
    public string? IssuedPayload { get; set; }
    public string? IssuedContentSha256 { get; set; }
    public Guid? CustomerId { get; set; }

    public static HubInvoiceRestore FromInvoice(Invoice invoice) => new()
    {
        InvoiceId = invoice.InvoiceId,
        StoreId = invoice.StoreId,
        CreatedOperationId = invoice.CreatedOperationId,
        CreatedByDeviceId = invoice.CreatedByDeviceId,
        CreatedAt = invoice.CreatedAt,
        ExternalInvoiceNumber = invoice.ExternalInvoiceNumber,
        DisplayNumber = invoice.DisplayNumber,
        Status = invoice.Status,
        RetiredAt = invoice.RetiredAt,
        RetirementReason = invoice.RetirementReason,
        IssuedAt = invoice.IssuedAt,
        IssuedPayload = invoice.IssuedPayload,
        IssuedContentSha256 = invoice.IssuedContentSha256,
        CustomerId = invoice.CustomerId
    };

    public Invoice ToInvoice(IEnumerable<InvoiceItem> items)
    {
        var invoice = new Invoice(InvoiceId, StoreId, CreatedOperationId, CreatedByDeviceId, CreatedAt, ExternalInvoiceNumber);
        foreach (var item in items.OrderBy(item => item.LineNumber))
        {
            invoice.AddItem(item);
        }

        invoice.RestoreForPersistence(
            DisplayNumber, Status, RetiredAt, RetirementReason, IssuedAt, IssuedPayload, IssuedContentSha256, CustomerId);
        return invoice;
    }
}

public sealed class HubInvoiceItemRestore
{
    public Guid InvoiceItemId { get; set; }
    public Guid InvoiceId { get; set; }
    public Guid StoreId { get; set; }
    public int LineNumber { get; set; }
    public string Description { get; set; } = string.Empty;
    public long QuantityMillis { get; set; }
    public long UnitPriceRials { get; set; }
    public long LineTotalRials { get; set; }
    public string? ProductId { get; set; }

    public static HubInvoiceItemRestore FromItem(Guid invoiceId, InvoiceItem item) => new()
    {
        InvoiceItemId = item.InvoiceItemId,
        InvoiceId = invoiceId,
        StoreId = item.StoreId,
        LineNumber = item.LineNumber,
        Description = item.Description,
        QuantityMillis = item.QuantityMillis,
        UnitPriceRials = item.UnitPriceRials,
        LineTotalRials = item.LineTotalRials,
        ProductId = item.ProductId
    };

    public InvoiceItem ToItem() => new(
        InvoiceItemId, StoreId, LineNumber, Description, QuantityMillis, UnitPriceRials, LineTotalRials, ProductId);
}

public sealed class HubDraftLockRestore
{
    public Guid LockId { get; set; }
    public Guid StoreId { get; set; }
    public string CustomerPhone { get; set; } = string.Empty;
    public Guid InvoiceId { get; set; }
    public Guid DeviceId { get; set; }
    public DateTimeOffset AcquiredAt { get; set; }
    public DateTimeOffset LastActivityAt { get; set; }
    public int TimeoutSeconds { get; set; }
    public DraftLockState State { get; set; }
    public LockReleaseReason? ReleaseReason { get; set; }
    public DateTimeOffset? ReleasedAt { get; set; }

    public static HubDraftLockRestore FromLock(DraftLock value) => new()
    {
        LockId = value.LockId,
        StoreId = value.StoreId,
        CustomerPhone = value.CustomerPhone,
        InvoiceId = value.InvoiceId,
        DeviceId = value.DeviceId,
        AcquiredAt = value.AcquiredAt,
        LastActivityAt = value.LastActivityAt,
        TimeoutSeconds = value.TimeoutSeconds,
        State = value.State,
        ReleaseReason = value.ReleaseReason,
        ReleasedAt = value.ReleasedAt
    };

    public DraftLock ToLock()
    {
        var value = new DraftLock(LockId, StoreId, CustomerPhone, InvoiceId, DeviceId, AcquiredAt, TimeoutSeconds);
        value.RestoreForPersistence(LastActivityAt, State, ReleaseReason, ReleasedAt);
        return value;
    }
}

public sealed class HubReplayedState
{
    public HubStoreRestore Store { get; set; } = new();
    public List<HubDeviceRestore> Devices { get; set; } = new();
    public List<JsonElement> Memberships { get; set; } = new();
    public List<JsonElement> Customers { get; set; } = new();
    public List<Product> Products { get; set; } = new();
    public List<InventoryEvent> InventoryEvents { get; set; } = new();
    public List<JsonElement> Sales { get; set; } = new();
    public List<HubInvoiceRestore> Invoices { get; set; } = new();
    public List<HubInvoiceItemRestore> InvoiceItems { get; set; } = new();
    public List<JsonElement> DigitalInvoices { get; set; } = new();
    public List<OperationRecord> Operations { get; set; } = new();
    public List<HubDraftLockRestore> Locks { get; set; } = new();
    public long ThroughOperationSeq { get; set; }
    public long WriterEpoch { get; set; }
    public long DisplayNumberHighWatermark { get; set; }
    public long ProductTagHighWatermark { get; set; }
}
