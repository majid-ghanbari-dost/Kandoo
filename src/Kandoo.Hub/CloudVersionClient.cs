using System.Net;
using System.Text.Json;

namespace Kandoo.Hub;

/// <summary>Discovers the Cloud API's advertised protocol version via its /health endpoint.</summary>
public sealed class CloudVersionClient : ICloudVersionClient
{
    private readonly HttpClient _client;
    private readonly string _cloudUrl;

    public CloudVersionClient(HttpClient client, string cloudUrl)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _cloudUrl = cloudUrl ?? string.Empty;
    }

    public async Task<string?> GetAdvertisedProtocolVersionAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_cloudUrl))
        {
            return null;
        }

        try
        {
            using var response = await _client.GetAsync($"{_cloudUrl.TrimEnd('/')}/health", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(content))
            {
                return null;
            }

            using var document = JsonDocument.Parse(content);
            if (document.RootElement.TryGetProperty("protocol_version", out var version))
            {
                return version.GetString();
            }

            return null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
