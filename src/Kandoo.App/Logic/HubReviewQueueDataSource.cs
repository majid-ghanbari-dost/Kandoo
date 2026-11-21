using System.Net.Http.Headers;
using System.Text.Json;

namespace Kandoo.App;

public sealed class HubReviewQueueDataSource : IReviewQueueDataSource
{
    private readonly HttpClient _client;
    private readonly HubConnectionSettings _settings;
    private readonly string _baseUrl;

    public HubReviewQueueDataSource(HttpClient client, HubConnectionSettings settings)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        if (!Uri.TryCreate(settings.BaseUrl?.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("Hub base URL must be an absolute HTTP or HTTPS URL.", nameof(settings));
        }

        if (settings.StoreId == Guid.Empty)
        {
            throw new ArgumentException("A Hub store ID is required.", nameof(settings));
        }

        if (string.IsNullOrWhiteSpace(settings.SessionToken))
        {
            throw new ArgumentException("A Hub session token is required for real data.", nameof(settings));
        }

        _baseUrl = uri.AbsoluteUri.TrimEnd('/');
    }

    public async Task<IReadOnlyList<ReviewQueueRow>> LoadAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"{_baseUrl}/v1/stores/{_settings.StoreId:D}/reviews");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.SessionToken);
        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Hub review request failed with HTTP {(int)response.StatusCode}.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return ReviewQueueMapper.Map(document.RootElement);
    }
}
