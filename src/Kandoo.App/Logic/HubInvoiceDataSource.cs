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
            HttpMethod.Post, $\"{_baseUrl}/v1/stores/{_settings.StoreId:D}/invoices/drafts");
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
        var root = document.RootElement;
        if (!root.TryGetProperty("invoice_id", out var invoiceId))
            throw new JsonException("Hub draft response missing invoice_id.");
        return invoiceId.GetString()!;
    }

    public async Task AddItemAsync(
        string invoiceId,
        string productId,
        int quantity,
        long unitPriceRials,
        CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["product_id"] = productId,
            ["quantity"] = quantity,
            ["unit_price_rials"] = unitPriceRials,
        };

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $\"{_baseUrl}/v1/stores/{_settings.StoreId:D}/invoices/{invoiceId}/items");
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
        };

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $\"{_baseUrl}/v1/stores/{_settings.StoreId:D}/invoices/{invoiceId}/issue");
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
            $\"{_baseUrl}/v1/stores/{_settings.StoreId:D}/invoices/{invoiceId}/sms/resend");
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
            $\"{_baseUrl}/v1/stores/{_settings.StoreId:D}/invoices/{invoiceId}");
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
public sealed record InvoiceItemRow(
    string ProductName,
    int Quantity,
    long UnitPriceRials,
    long TotalRials)
{
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
