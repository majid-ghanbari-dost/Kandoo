using System.Net.Http.Headers;
using System.Text.Json;

namespace Kandoo.App;

public sealed class HubProductDataSource : IProductDataSource
{
    private readonly HttpClient _client;
    private readonly HubConnectionSettings _settings;
    private readonly string _baseUrl;

    public HubProductDataSource(HttpClient client, HubConnectionSettings settings)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _baseUrl = HubApiUtilities.ValidatedBaseUrl(settings);

        if (string.IsNullOrWhiteSpace(settings.SessionToken))
        {
            throw new ArgumentException("A Hub session token is required for real product data.", nameof(settings));
        }
    }

    public async Task<IReadOnlyList<ProductRow>> LoadProductsAsync(string? groupId, CancellationToken cancellationToken = default)
    {
        var url = $"{_baseUrl}/v1/stores/{_settings.StoreId:D}/products";
        if (!string.IsNullOrWhiteSpace(groupId))
        {
            url += $"?group_id={Uri.EscapeDataString(groupId.Trim())}";
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.SessionToken);
        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Hub product request failed with HTTP {(int)response.StatusCode}.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return ProductMapper.Map(document.RootElement);
    }
}
