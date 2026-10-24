using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kandoo.Cloud;

public static class PublicInvoiceLegalCopy
{
    public const string ApprovalStatus = "needs lawyer approval";
    public const string Placeholder = "D14 placeholder — needs lawyer approval";
}

public sealed class PublicInvoiceLine
{
    [JsonPropertyName("product_name")]
    public string ProductName { get; set; } = string.Empty;

    [JsonPropertyName("quantity_millis")]
    public long QuantityMillis { get; set; }

    [JsonPropertyName("unit_price_rials")]
    public long UnitPriceRials { get; set; }

    [JsonPropertyName("line_total_rials")]
    public long LineTotalRials { get; set; }
}

public sealed class PublicDigitalInvoice
{
    [JsonPropertyName("invoice_id")]
    public Guid InvoiceId { get; set; }

    [JsonPropertyName("store_id")]
    public Guid StoreId { get; set; }

    [JsonPropertyName("issued_payload")]
    public JsonElement IssuedPayload { get; set; } = JsonSerializer.SerializeToElement(new { });

    [JsonPropertyName("issued_content_sha256")]
    public string IssuedContentSha256 { get; set; } = new('0', 64);

    [JsonPropertyName("issued_at")]
    public DateTimeOffset IssuedAt { get; set; }
}

public sealed class PublicLegalCopyResource
{
    [JsonPropertyName("resource_id")]
    public string ResourceId { get; set; } = "D14-PLACEHOLDER";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "0";

    [JsonPropertyName("content")]
    public string Content { get; set; } = PublicInvoiceLegalCopy.Placeholder;

    [JsonPropertyName("signature")]
    public string Signature { get; set; } = "NOT_SIGNED_NEEDS_LAWYER_APPROVAL";
}

public sealed class PublicInvoiceView
{
    [JsonPropertyName("product_name")]
    public string ProductName { get; set; } = "Kandoo digital invoice";

    [JsonPropertyName("store_name")]
    public string StoreName { get; set; } = string.Empty;

    [JsonPropertyName("display_number")]
    public long DisplayNumber { get; set; }

    [JsonPropertyName("lifecycle_status")]
    public string LifecycleStatus { get; set; } = "ISSUED";

    [JsonPropertyName("issued_at")]
    public DateTimeOffset IssuedAt { get; set; }

    [JsonPropertyName("digital_invoice")]
    public PublicDigitalInvoice DigitalInvoice { get; set; } = new();

    [JsonPropertyName("legal_copy")]
    public PublicLegalCopyResource LegalCopy { get; set; } = new();

    [JsonPropertyName("legal_copy_status")]
    public string LegalCopyStatus { get; set; } = PublicInvoiceLegalCopy.ApprovalStatus;
}

public sealed record PublicInvoice(
    PublicInvoiceView View,
    IReadOnlyList<PublicInvoiceLine> LineItems,
    long PayableRials);

/// <summary>Synthetic projection input used by local tests; Hub issuance remains authoritative.</summary>
public sealed class PublicInvoiceSeed
{
    public Guid DigitalInvoiceId { get; set; } = Guid.NewGuid();
    public Guid InvoiceId { get; set; }
    public Guid StoreId { get; set; }
    public string StoreName { get; set; } = "Synthetic store";
    public long DisplayNumber { get; set; } = 1;
    public string LifecycleStatus { get; set; } = "ISSUED";
    public DateTimeOffset IssuedAt { get; set; } = DateTimeOffset.UnixEpoch;
    public string? PublicToken { get; set; }
    public JsonElement IssuedPayload { get; set; } = JsonSerializer.SerializeToElement(new { });
    public string IssuedContentSha256 { get; set; } = new('0', 64);
    public IReadOnlyList<PublicInvoiceLine> LineItems { get; set; } = Array.Empty<PublicInvoiceLine>();
    public long PayableRials { get; set; }
}
