using System.Net.Http.Headers;
using System.Text.Json;

namespace Kandoo.App;

/// <summary>Data source for customer operations against the Hub.</summary>
public sealed class HubCustomersDataSource
{
    private readonly HttpClient _client;
    private readonly HubConnectionSettings _settings;
    private readonly string _baseUrl;

    public HubCustomersDataSource(HttpClient client, HubConnectionSettings settings)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _baseUrl = HubApiUtilities.ValidatedBaseUrl(settings);
    }

    public async Task<IReadOnlyList<CustomerRow>> LoadCustomersAsync(
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get, ${_baseUrl}/v1/stores/{_settings.StoreId:D}/customers");
        HubApiUtilities.Authorize(request, _settings);
        using var response = await _client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Hub customers request failed with HTTP {(int)response.StatusCode}.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return CustomerMapper.Map(document.RootElement);
    }
}

/// <summary>Customer row for display.</summary>
public sealed record CustomerRow(
    string CustomerId,
    string Name,
    string Phone,
    long TotalPurchasesRials)
{
    public string TotalDisplay => FormattableString.Invariant($"{TotalPurchasesRials:N0} ریال");
}

internal static class CustomerMapper
{
    public static IReadOnlyList<CustomerRow> Map(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("Hub customer response must contain an items array.");
        }

        var rows = new List<CustomerRow>();
        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;

            var customerId = RequiredString(item, "customer_id");
            var name = RequiredString(item, "name");
            var phone = RequiredString(item, "phone");
            var totalPurchases = OptionalInt64(item, "total_purchases_rials") ?? 0;

            rows.Add(new CustomerRow(customerId, name, phone, totalPurchases));
        }

        return rows;
    }

    private static string RequiredString(JsonElement parent, string name)
    {
        var value = OptionalString(parent, name);
        return value ?? throw new JsonException($"Customer field {name} must be a non-empty string.");
    }

    private static string? OptionalString(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
            return null;
        if (value.ValueKind != JsonValueKind.String)
            return null;
        var text = value.GetString();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static long? OptionalInt64(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
            return null;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var result))
            return null;
        return result;
    }
}
