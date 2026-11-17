using System.Text.Json;

namespace Kandoo.Hub;

public sealed record ModeBReviewRecord(
    string ReviewId,
    Guid NativeStoreId,
    string QueueStatus,
    JsonElement Source,
    string? OperatorPhone,
    Guid? IssuedInvoiceId);

public sealed record ModeBAttentionCell(string Field, string RawValue, string Reason);

public sealed record ModeBReviewCorrection(
    string ReviewId,
    string FieldPath,
    string? OldValue,
    string NewValue,
    string OperatorId,
    DateTimeOffset ConfirmedAt);

public sealed record ModeBReviewActionResult(
    string Status,
    ModeBReviewRecord? Review = null,
    bool IdempotentReplay = false,
    Guid? InvoiceId = null,
    long? DisplayNumber = null);
