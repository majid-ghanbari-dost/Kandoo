using System.Net.Http.Headers;
using System.Text.Json;

namespace Kandoo.App;

/// <summary>Data source for product create/edit operations against the Hub.</summary>
public sealed class HubProductEditorDataSource
{
    private readonly HttpClient _client;
    private readonly HubConnectionSettings _settings;
    private readonly string _baseUrl;

    public HubProductEditorDataSource(HttpClient client, HubConnectionSettings settings)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _baseUrl = HubApiUtilities.ValidatedBaseUrl(settings);
    }

    public async Task<string> CreateProductAsync(
        string name,
        long unitPriceRials,
        string? groupId = null,
        long? purchasePriceRials = null,
        CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["name"] = name,
            ["unit_price_rials"] = unitPriceRials,
        };
        if (groupId is not null)
            body["group_id"] = groupId;
        if (purchasePriceRials is not null)
            body["purchase_price_rials"] = purchasePriceRials;

        using var request = new HttpRequestMessage(
            HttpMethod.Post, $\"{_baseUrl}/v1/stores/{_settings.StoreId:D}/products");
        HubApiUtilities.Authorize(request, _settings);
        request.Content = HubApiUtilities.JsonBody(body);
        using var response = await _client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var bodyText = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Hub product creation failed with HTTP {(int)response.StatusCode}: {bodyText}.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        if (!root.TryGetProperty("product_id", out var productId))
            throw new JsonException("Hub product response missing product_id.");
        return productId.GetString()!;
    }

    public async Task UpdateProductAsync(
        string productId,
        string? name = null,
        long? unitPriceRials = null,
        string? groupId = null,
        CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>();
        if (name is not null)
            body["name"] = name;
        if (unitPriceRials is not null)
            body["unit_price_rials"] = unitPriceRials;
        if (groupId is not null)
            body["group_id"] = groupId;

        using var request = new HttpRequestMessage(
            HttpMethod.Patch,
            $\"{_baseUrl}/v1/stores/{_settings.StoreId:D}/products/{productId}");
        HubApiUtilities.Authorize(request, _settings);
        request.Content = HubApiUtilities.JsonBody(body);
        using var response = await _client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var bodyText = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Hub product update failed with HTTP {(int)response.StatusCode}: {bodyText}.");
        }
    }

    public async Task<IReadOnlyList<GroupOption>> LoadGroupOptionsAsync(
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $\"{_baseUrl}/v1/stores/{_settings.StoreId:D}/products/cascade-options");
        HubApiUtilities.Authorize(request, _settings);
        using var response = await _client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Hub group options request failed with HTTP {(int)response.StatusCode}.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;

        var groups = new List<GroupOption>();
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("groups", out var groupsArray) &&
            groupsArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var g in groupsArray.EnumerateArray())
            {
                var id = RequiredString(g, "group_id");
                var name = RequiredString(g, "name");
                groups.Add(new GroupOption(id, name));
            }
        }

        return groups;
    }

    private static string RequiredString(JsonElement parent, string name)
    {
        var value = OptionalString(parent, name);
        return value ?? throw new JsonException($"{name} must be a non-empty string.");
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

/// <summary>Product group option for the editor dropdown.</summary>
public sealed record GroupOption(string GroupId, string Name);

/// <summary>Product creation input.</summary>
public sealed record ProductEditorInput(
    string Name,
    long UnitPriceRials,
    string? GroupId = null,
    long? PurchasePriceRials = null);
