using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kandoo.App;

/// <summary>
/// Wire models for the accepted P-31 customer API contract
/// (docs/vectors/customer-api-v1-2-proposed.json, cross-lane review ACCEPT by PM-13).
///
/// The mobile number is the identity; roles live under it (ORDER 16 / C-92).
/// All bodies are snake_case JSON exactly as the PHP Cloud customer API serves them.
/// Unknown fields are tolerated (the vectors compare required subsets); missing
/// optional fields are displayed as data gaps, never invented.
/// </summary>
public static class CustomerApiStatus
{
    // OTP challenge issue (POST /v1/customer/otp/challenges).
    public const string OtpSent = "OTP_SENT";
    public const string Duplicate = "DUPLICATE";
    public const string Conflict = "CONFLICT";
    public const string Incomplete = "INCOMPLETE";
    public const string RateLimited = "RATE_LIMITED";
    public const string Telegram = "TELEGRAM";
    public const string Invalid = "INVALID";
    public const string Unavailable = "UNAVAILABLE";

    // OTP verify (POST /v1/customer/otp/challenges/{id}/verify).
    public const string Ok = "OK";

    // Session scope (GET /v1/customer/invoices, GET detail, DELETE session).
    public const string Unauthenticated = "UNAUTHENTICATED";
    public const string NotFound = "NOT_FOUND";
    public const string Revoked = "REVOKED";
}

/// <summary>POST /v1/customer/otp/challenges — request body.</summary>
public sealed class OtpIssueRequest
{
    [JsonPropertyName("phone")]
    public string Phone { get; set; } = string.Empty;
}

/// <summary>POST /v1/customer/otp/challenges — response body (required subset per vectors).</summary>
public sealed class OtpIssueResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("challenge_id")]
    public string? ChallengeId { get; set; }

    [JsonPropertyName("expires_at")]
    public long? ExpiresAtUnix { get; set; }
}

/// <summary>POST /v1/customer/otp/challenges/{id}/verify — request body.</summary>
public sealed class OtpVerifyRequest
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;
}

/// <summary>POST /v1/customer/otp/challenges/{id}/verify — response body.</summary>
public sealed class OtpVerifyResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("token")]
    public string? Token { get; set; }
}

/// <summary>GET /v1/customer/invoices — one invoice in the per-store grouping.</summary>
public sealed class CustomerListInvoice
{
    [JsonPropertyName("invoice_id")]
    public string InvoiceId { get; set; } = string.Empty;

    [JsonPropertyName("display_number")]
    public long DisplayNumber { get; set; }

    // Optional extras beyond the accepted vector subset; rendered only when present.
    [JsonPropertyName("lifecycle_status")]
    public string? LifecycleStatus { get; set; }

    [JsonPropertyName("issued_at")]
    public string? IssuedAt { get; set; }

    [JsonPropertyName("payable_rials")]
    public long? PayableRials { get; set; }

    [JsonPropertyName("currency_unit")]
    public string? CurrencyUnit { get; set; }

    [JsonPropertyName("remaining_balance")]
    public long? RemainingBalance { get; set; }
}

/// <summary>GET /v1/customer/invoices — one store group.</summary>
public sealed class CustomerStoreGroup
{
    [JsonPropertyName("store_id")]
    public string StoreId { get; set; } = string.Empty;

    [JsonPropertyName("store_name")]
    public string StoreName { get; set; } = string.Empty;

    [JsonPropertyName("invoices")]
    public List<CustomerListInvoice> Invoices { get; set; } = new();
}

/// <summary>GET /v1/customer/invoices — response body.</summary>
public sealed class CustomerInvoicesResponse
{
    [JsonPropertyName("stores")]
    public List<CustomerStoreGroup> Stores { get; set; } = new();
}

/// <summary>One recorded line item of a customer invoice detail.</summary>
public sealed class CustomerLineItem
{
    [JsonPropertyName("product_name")]
    public string? ProductName { get; set; }

    [JsonPropertyName("quantity_millis")]
    public long? QuantityMillis { get; set; }

    [JsonPropertyName("unit_price_rials")]
    public long? UnitPriceRials { get; set; }

    [JsonPropertyName("line_total_rials")]
    public long? LineTotalRials { get; set; }
}

/// <summary>One recorded payment of a customer invoice detail (v1.1 recorded values).</summary>
public sealed class CustomerPaymentRecord
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("amount")]
    public long? Amount { get; set; }
}

/// <summary>GET /v1/customer/invoices/{store}/{invoice} — the invoice object.</summary>
public sealed class CustomerInvoiceDetail
{
    [JsonPropertyName("store_id")]
    public string StoreId { get; set; } = string.Empty;

    [JsonPropertyName("invoice_id")]
    public string InvoiceId { get; set; } = string.Empty;

    [JsonPropertyName("display_number")]
    public long DisplayNumber { get; set; }

    [JsonPropertyName("payable_rials")]
    public long PayableRials { get; set; }

    [JsonPropertyName("line_items")]
    public List<CustomerLineItem> LineItems { get; set; } = new();

    // Optional extras beyond the accepted vector subset; rendered only when present
    // (same honesty policy as the PHP customer web page: recorded values only).
    [JsonPropertyName("store_name")]
    public string? StoreName { get; set; }

    [JsonPropertyName("customer_name")]
    public string? CustomerName { get; set; }

    [JsonPropertyName("payments")]
    public List<CustomerPaymentRecord>? Payments { get; set; }

    [JsonPropertyName("remaining_balance")]
    public long? RemainingBalance { get; set; }

    [JsonPropertyName("currency_unit")]
    public string? CurrencyUnit { get; set; }

    [JsonPropertyName("issued_at")]
    public string? IssuedAt { get; set; }

    [JsonPropertyName("lifecycle_status")]
    public string? LifecycleStatus { get; set; }
}

/// <summary>GET /v1/customer/invoices/{store}/{invoice} — response body.</summary>
public sealed class CustomerInvoiceDetailResponse
{
    [JsonPropertyName("invoice")]
    public CustomerInvoiceDetail Invoice { get; set; } = new();
}

/// <summary>Generic {"status": "..."} envelope (errors, logout REVOKED).</summary>
public sealed class CustomerStatusResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// Outcome of one customer API call: the HTTP status plus the parsed body status.
/// Status is always read from the body envelope (the contract's source of truth).
/// </summary>
public sealed class CustomerApiResult<T>
{
    private CustomerApiResult(int httpStatus, string status, T? value, string? rawBody)
    {
        HttpStatus = httpStatus;
        Status = status;
        Value = value;
        RawBody = rawBody;
    }

    public int HttpStatus { get; }
    public string Status { get; }
    public T? Value { get; }
    public string? RawBody { get; }

    public bool IsSuccess => HttpStatus is >= 200 and < 300;

    public static CustomerApiResult<T> Ok(int httpStatus, T value, string rawBody) =>
        new(httpStatus, ReadStatus(rawBody) ?? string.Empty, value, rawBody);

    public static CustomerApiResult<T> Failure(int httpStatus, string? status, string? rawBody) =>
        new(httpStatus, status ?? StatusFromHttp(httpStatus), default, rawBody);

    private static string? ReadStatus(string rawBody)
    {
        try
        {
            using var document = JsonDocument.Parse(rawBody);
            return document.RootElement.TryGetProperty("status", out var value)
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string StatusFromHttp(int httpStatus) => httpStatus switch
    {
        401 => CustomerApiStatus.Unauthenticated,
        404 => CustomerApiStatus.NotFound,
        429 => CustomerApiStatus.RateLimited,
        _ => string.Empty,
    };
}
