using System.Net.Http.Headers;
using System.Text.Json;

namespace Kandoo.App;

/// <summary>Data source for Hub status and backup information.</summary>
public sealed class HubStatusDataSource
{
    private readonly HttpClient _client;
    private readonly HubConnectionSettings _settings;
    private readonly string _baseUrl;

    public HubStatusDataSource(HttpClient client, HubConnectionSettings settings)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _baseUrl = HubApiUtilities.ValidatedBaseUrl(settings);
    }

    public async Task<HubStatusRow> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"{_baseUrl}/v1/stores/{_settings.StoreId:D}/status/v0");
        HubApiUtilities.Authorize(request, _settings);
        using var response = await _client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Hub status request failed with HTTP {(int)response.StatusCode}.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return StatusMapper.Map(document.RootElement);
    }

    public async Task<HubStatsRow> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"{_baseUrl}/v1/stores/{_settings.StoreId:D}/stats/v0");
        HubApiUtilities.Authorize(request, _settings);
        using var response = await _client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Hub stats request failed with HTTP {(int)response.StatusCode}.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return StatsMapper.Map(document.RootElement);
    }
}

/// <summary>Hub status row.</summary>
public sealed record HubStatusRow(
    string Status,
    string? SmsConfiguration,
    string? BackupStatus,
    string? LastSync);

/// <summary>Hub stats row.</summary>
public sealed record HubStatsRow(
    long InvoiceCount,
    long SalesRials)
{
    public string SalesDisplay => FormattableString.Invariant($"{SalesRials:N0} ریال");
}

internal static class StatusMapper
{
    public static HubStatusRow Map(JsonElement root)
    {
        return new HubStatusRow(
            RequiredString(root, "status"),
            OptionalString(root, "sms_configuration"),
            OptionalString(root, "backup_status"),
            OptionalString(root, "last_sync"));
    }

    private static string RequiredString(JsonElement parent, string name)
    {
        var value = OptionalString(parent, name);
        return value ?? throw new JsonException($"Status field {name} must be a non-empty string.");
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
}

internal static class StatsMapper
{
    public static HubStatsRow Map(JsonElement root)
    {
        return new HubStatsRow(
            RequiredInt64(root, "invoice_count"),
            RequiredInt64(root, "sales_rials"));
    }

    private static long RequiredInt64(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt64(out var result))
        {
            throw new JsonException($"Stats field {name} must be a non-negative integer.");
        }

        return result;
    }
}
