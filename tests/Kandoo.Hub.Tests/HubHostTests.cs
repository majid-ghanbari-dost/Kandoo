using System.Net;
using System.Text.Json;
using Kandoo.Core;
using Xunit;

namespace Kandoo.Hub.Tests;

public sealed class HubHostTests : IDisposable
{
    private readonly HubApiFactory _factory = new();
    private readonly HubApiClient _api;
    private readonly HttpClient _http;

    public HubHostTests()
    {
        _api = new HubApiClient(_factory);
        _http = _factory.CreateClient();
    }

    [Fact]
    public async Task C14_PairingGrantIsAvailableOnTheLoopbackPageAndJsonView()
    {
        var storeId = Guid.NewGuid();
        var owner = await _api.Owner(storeId, Guid.NewGuid(), Guid.NewGuid());
        var issued = await _api.IssueGrant(owner, storeId);
        var nonce = issued.Body.GetProperty("qr").GetProperty("nonce").GetString()!;

        var page = await _http.GetStringAsync("/");
        Assert.Contains("Kandoo Hub local pairing", page);
        Assert.Contains(nonce, page);
        var local = await _api.Get("/pairing-grant");
        Assert.Equal(HttpStatusCode.OK, local.StatusCode);
        Assert.Equal(nonce, local.Body.GetProperty("nonce").GetString());
        Assert.Equal("COMPANION", local.Body.GetProperty("device_role").GetString());
    }

    [Fact]
    public async Task C14_ProductEndpointCreatesDurableStockAndReplaysWithoutDuplicates()
    {
        var storeId = Guid.NewGuid();
        var owner = await _api.Owner(storeId, Guid.NewGuid(), Guid.NewGuid());
        var paired = await _api.Pair(owner, storeId, Guid.NewGuid());
        var body = new
        {
            operation_id = "c14-product-1",
            name = "میز آزمایشی",
            unit_price_rials = 12_500,
            initial_stock_millis = 10_000,
            category = "آزمایشی"
        };

        var created = await _api.Post($"/v1/stores/{storeId:D}/products", body, paired.Session.Token);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        Assert.Equal("CREATED", created.Body.GetProperty("status").GetString());
        Assert.Equal(1, created.Body.GetProperty("product").GetProperty("product_tag_number").GetInt64());
        var replay = await _api.Post($"/v1/stores/{storeId:D}/products", body, paired.Session.Token);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.True(replay.Body.GetProperty("idempotent_replay").GetBoolean());
        Assert.Equal(
            created.Body.GetProperty("operation_seq").GetInt64(),
            replay.Body.GetProperty("operation_seq").GetInt64());

        var catalog = new HubProductCatalogService(_factory.Store);
        Assert.Equal(10_000, catalog.GetStockMillis(storeId, created.Body.GetProperty("product").GetProperty("product_id").GetString()!));
        Assert.Equal(1, _factory.Store.LoadProductCatalog().Count(storeId));
    }

    public void Dispose()
    {
        _http.Dispose();
        _api.Dispose();
        _factory.Dispose();
    }
}
