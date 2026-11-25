using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Kandoo.App;

/// <summary>Data source for invoice operations against the Hub.</summary>
public sealed class HubInvoiceDataSource
{
    private readonly HttpClient _client;
    private readonly HubConnectionSettings _settings;
    private readonly string _baseUrl;

    public HubInvoiceDataSource(HttpClient client, HubConnectionSettings settings)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _baseUrl = HubApiUtilities.ValidatedBaseUrl(settings);
    }

    public async Task<string> CreateDraftAsync(
        string operationId,
        string serviceType,
        string? customerPhone = null,
        CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["operation_id"] = operationId,
            ["service_type"] = serviceType,
        };
        if (customerPhone is not null)
            body["customer_phone"] = customerPhone;

        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"{_baseUrl}/v1/stores/{_settings.StoreId:D}/invoices/drafts");
        HubApiUtilities.Authorize(request, _settings);
        request.Content = HubApiUtilities.JsonBody(body);
        using var response = await _client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var bodyText = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Hub draft creation failed with HTTP {(int)response.StatusCode}: {bodyText}.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        // The Hub answers with an operation envelope:
        //   { status, result_status, operation_seq, request_hash, idempotent_replay, response, currency }
        // and the invoice lives at response.invoice.invoice_id. Reading invoice_id off the root
        // (what this method used to do) always threw "Hub draft response missing invoice_id",
        // so creating an invoice from the app could never succeed.
        if (!OperationResponse.TryReadInvoiceId(document.RootElement, out var invoiceId))
            throw new JsonException("Hub draft response missing response.invoice.invoice_id.");
        return invoiceId;
    }

    /// <summary>Adds one line to a draft invoice.</summary>
    /// <param name="description">Required by the Hub: an invoice line without text is rejected.</param>
    public async Task AddItemAsync(
        string invoiceId,
        string description,
        string? productId,
        int quantity,
        long unitPriceRials,
        CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["operation_id"] = $"item-{invoiceId}-{Guid.NewGuid():N}",
            ["service_type"] = "SALE",
            ["description"] = description,
            ["quantity_millis"] = quantity * 1000L,
            ["unit_price_rials"] = unitPriceRials,
        };
        if (!string.IsNullOrWhiteSpace(productId))
            body["product_id"] = productId;

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{_baseUrl}/v1/stores/{_settings.StoreId:D}/invoices/{invoiceId}/items");
        HubApiUtilities.Authorize(request, _settings);
        request.Content = HubApiUtilities.JsonBody(body);
        using var response = await _client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var bodyText = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Hub add item failed with HTTP {(int)response.StatusCode}: {bodyText}.");
        }
    }

    public async Task IssueAsync(string invoiceId, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["operation_id"] = $"issue-{invoiceId}",
            ["service_type"] = "SALE",
        };

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{_baseUrl}/v1/stores/{_settings.StoreId:D}/invoices/{invoiceId}/issue");
        HubApiUtilities.Authorize(request, _settings);
        request.Content = HubApiUtilities.JsonBody(body);
        using var response = await _client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var bodyText = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Hub issue failed with HTTP {(int)response.StatusCode}: {bodyText}.");
        }
    }

    public async Task ResendSmsAsync(string invoiceId, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{_baseUrl}/v1/stores/{_settings.StoreId:D}/invoices/{invoiceId}/sms/resend");
        HubApiUtilities.Authorize(request, _settings);
        using var response = await _client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var bodyText = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Hub SMS resend failed with HTTP {(int)response.StatusCode}: {bodyText}.");
        }
    }

    public async Task<string> GetInvoiceDetailsAsync(
        string invoiceId,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{_baseUrl}/v1/stores/{_settings.StoreId:D}/invoices/{invoiceId}");
        HubApiUtilities.Authorize(request, _settings);
        using var response = await _client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var bodyText = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Hub invoice details failed with HTTP {(int)response.StatusCode}: {bodyText}.");
        }

        return await response.Content.ReadAsStringAsync(cancellationToken);
    }
}

/// <summary>Result of creating an invoice draft.</summary>
public sealed record InvoiceDraftResult(string InvoiceId);

/// <summary>Invoice line item for display.</summary>
/// <remarks>
/// <paramref name="ProductName"/> is both the label and the Hub line description: the Hub
/// requires a non-empty description on every line.
/// </remarks>
public sealed record InvoiceItemRow(
    string ProductName,
    int Quantity,
    long UnitPriceRials,
    long TotalRials,
    string? ProductId = null)
{
    public string Description => ProductName;

    public string TotalDisplay => FormattableString.Invariant($"{TotalRials:N0} ریال");
}

/// <summary>Invoice summary for the details page.</summary>
public sealed record InvoiceSummary(
    string InvoiceId,
    string InvoiceNumber,
    string Status,
    string JalaliDate,
    long TotalRials,
    IReadOnlyList<InvoiceItemRow> Items)
{
    public string TotalDisplay => FormattableString.Invariant($"{TotalRials:N0} ریال");
}

/// <summary>Reads the Hub's operation envelope, which wraps every invoice response.</summary>
internal static class OperationResponse
{
    /// <summary>Extracts <c>response.invoice.invoice_id</c> from a Hub operation reply.</summary>
    public static bool TryReadInvoiceId(JsonElement root, out string invoiceId)
    {
        invoiceId = string.Empty;
        if (!root.TryGetProperty("response", out var response) ||
            response.ValueKind != JsonValueKind.Object ||
            !response.TryGetProperty("invoice", out var invoice) ||
            invoice.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!invoice.TryGetProperty("invoice_id", out var id))
        {
            return false;
        }

        // Guid or string: accept both rather than depending on the serializer's choice.
        invoiceId = id.ValueKind == JsonValueKind.String
            ? id.GetString() ?? string.Empty
            : id.GetRawText().Trim('"');

        return !string.IsNullOrWhiteSpace(invoiceId);
    }
}
