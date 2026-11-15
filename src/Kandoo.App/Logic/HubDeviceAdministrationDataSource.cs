using System.Text.Json;

namespace Kandoo.App;

public sealed class HubDeviceAdministrationDataSource
{
    private readonly HttpClient _client;
    private readonly HubConnectionSettings _settings;
    private readonly string _baseUrl;

    public HubDeviceAdministrationDataSource(HttpClient client, HubConnectionSettings settings)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _baseUrl = HubApiUtilities.ValidatedBaseUrl(settings);
        if (string.IsNullOrWhiteSpace(settings.SessionToken))
        {
            throw new ArgumentException("A Hub owner session token is required.", nameof(settings));
        }
    }

    public async Task<IReadOnlyList<DeviceRow>> ListAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"{_baseUrl}/v1/stores/{_settings.StoreId:D}/devices");
        HubApiUtilities.Authorize(request, _settings);
        using var response = await _client.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw Error(response, body);
        }

        using var document = JsonDocument.Parse(body);
        return DeviceMapper.MapArray(document.RootElement);
    }

    public async Task<DeviceRow> ChangeLevelAsync(
        Guid deviceId,
        string operatorLevel,
        CancellationToken cancellationToken = default)
    {
        if (deviceId == Guid.Empty || !DeviceLevels.IsSupported(operatorLevel))
        {
            throw new ArgumentException("A companion device and supported operator level are required.");
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Patch,
            $"{_baseUrl}/v1/stores/{_settings.StoreId:D}/devices/{deviceId:D}/level");
        HubApiUtilities.Authorize(request, _settings);
        request.Content = HubApiUtilities.JsonBody(new { operator_level = operatorLevel });
        using var response = await _client.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var root = await HubApiUtilities.ReadResponseAsync(response, cancellationToken);
        if (HubApiUtilities.RequiredString(root, "status") != "DEVICE_LEVEL_CHANGED" ||
            !root.TryGetProperty("device", out var device))
        {
            throw new JsonException("Hub returned an unsupported level-change result.");
        }

        return DeviceMapper.Map(device);
    }

    public async Task RevokeAsync(Guid deviceId, CancellationToken cancellationToken = default)
    {
        if (deviceId == Guid.Empty)
        {
            throw new ArgumentException("A companion device ID is required.", nameof(deviceId));
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{_baseUrl}/v1/stores/{_settings.StoreId:D}/devices/{deviceId:D}/revoke");
        HubApiUtilities.Authorize(request, _settings);
        request.Content = HubApiUtilities.JsonBody(new { });
        using var response = await _client.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var root = await HubApiUtilities.ReadResponseAsync(response, cancellationToken);
        if (HubApiUtilities.RequiredString(root, "status") != "DEVICE_REVOKED")
        {
            throw new JsonException("Hub returned an unsupported revoke result.");
        }
    }

    private static InvalidOperationException Error(
        HttpResponseMessage response,
        string body)
    {
        string? code = null;
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("code", out var errorCode))
            {
                code = errorCode.GetString();
            }
        }
        catch (JsonException)
        {
            // Preserve the HTTP failure when an intermediary returns non-JSON.
        }

        return new InvalidOperationException(
            $"Hub device request failed with HTTP {(int)response.StatusCode}" +
            (string.IsNullOrWhiteSpace(code) ? "." : $" ({code})."));
    }
}
