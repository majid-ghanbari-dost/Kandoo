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

    [Fact]
    public async Task C23_ProductApiSearchesCascadesAndEditsWithOccAndOrderedFields()
    {
        var storeId = Guid.NewGuid();
        var owner = await _api.Owner(storeId, Guid.NewGuid(), Guid.NewGuid());
        var paired = await _api.Pair(owner, storeId, Guid.NewGuid());
        var createBody = new
        {
            operation_id = "c23-create-alpha",
            name = "Alpha",
            type = "dairy",
            color = "white",
            unit_price_rials = 1_200,
            initial_stock_millis = 5_000,
            product_tag_number = 40,
            operator_fields = new[]
            {
                new { key = "z-field", value = "first" },
                new { key = "a-field", value = "second" }
            }
        };
        var created = await _api.Post($"/v1/stores/{storeId:D}/products", createBody, paired.Session.Token);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var productId = created.Body.GetProperty("product").GetProperty("product_id").GetString()!;
        Assert.Equal(40, created.Body.GetProperty("product").GetProperty("product_tag_number").GetInt64());

        var search = await _api.Get($"/v1/stores/{storeId:D}/products?query=white&limit=1", paired.Session.Token);
        Assert.Equal(HttpStatusCode.OK, search.StatusCode);
        var found = Assert.Single(search.Body.GetProperty("items").EnumerateArray());
        Assert.Equal(productId, found.GetProperty("product_id").GetString());
        Assert.Equal("IRR", found.GetProperty("currency").GetProperty("code").GetString());
        Assert.Equal(0, found.GetProperty("operator_fields")[0].GetProperty("position").GetInt32());
        Assert.Equal("z-field", found.GetProperty("operator_fields")[0].GetProperty("key").GetString());

        var otherStore = Guid.NewGuid();
        var catalog = new HubProductCatalogService(_factory.Store);
        Assert.Equal(ProductRegistrationStatus.Created, catalog.Register(
            otherStore, Guid.NewGuid(), "c23-other-store",
            new ProductRegistrationInput("Alpha", 9_999, 0, "other-store-alpha",
                OperatorFields: new[] { new ProductOperatorField("private", "other-store") })).Status);
        var cascade = await _api.Get($"/v1/stores/{storeId:D}/products/cascade-options?name=Alpha", paired.Session.Token);
        var option = Assert.Single(cascade.Body.GetProperty("items").EnumerateArray());
        Assert.Equal(productId, option.GetProperty("product_id").GetString());
        Assert.DoesNotContain("other-store", cascade.Body.GetRawText(), StringComparison.Ordinal);

        var editBody = new
        {
            operation_id = "c23-edit-alpha",
            base_version = 1,
            payload = new
            {
                name = "Alpha edited",
                type = "grocery",
                color = "blue",
                unit_price_rials = 1_300,
                stock_millis = 7_000,
                operator_fields = new[]
                {
                    new { key = "note", value = "keep-this-order" },
                    new { key = "priority", value = "2" }
                }
            }
        };
        var edited = await _api.Patch($"/v1/stores/{storeId:D}/products/{productId}", editBody, paired.Session.Token);
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        Assert.Equal("SUCCEEDED", edited.Body.GetProperty("status").GetString());
        Assert.Equal(2, edited.Body.GetProperty("response").GetProperty("version").GetInt32());
        Assert.Equal(7_000, edited.Body.GetProperty("response").GetProperty("stock_millis").GetInt64());
        Assert.Equal("note", edited.Body.GetProperty("response").GetProperty("operator_fields")[0].GetProperty("key").GetString());
        Assert.Equal(7_000, catalog.GetStockMillis(storeId, productId));
        var adjustment = _factory.Store.LoadInventoryLedger().Events.Last();
        Assert.Equal(InventoryEventType.Adjustment, adjustment.EventType);
        Assert.Equal(2_000, adjustment.QuantityDeltaMillis);

        var replay = await _api.Patch($"/v1/stores/{storeId:D}/products/{productId}", editBody, paired.Session.Token);
        Assert.True(replay.Body.GetProperty("idempotent_replay").GetBoolean());
        Assert.Equal(7_000, catalog.GetStockMillis(storeId, productId));
        var staleBody = new
        {
            operation_id = "c23-stale-alpha",
            base_version = 1,
            payload = new { unit_price_rials = 1_400 }
        };
        var stale = await _api.Patch($"/v1/stores/{storeId:D}/products/{productId}", staleBody, paired.Session.Token);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("VERSION_CONFLICT", stale.Body.GetProperty("status").GetString());
        Assert.Equal(1_300, catalog.GetByTag(storeId, 40)!.UnitPriceRials);

        var beta = await _api.Post($"/v1/stores/{storeId:D}/products", new
        {
            operation_id = "c23-create-beta",
            name = "Beta",
            unit_price_rials = 1_000,
            initial_stock_millis = 0
        }, paired.Session.Token);
        Assert.Equal(HttpStatusCode.OK, beta.StatusCode);
        var firstPage = await _api.Get($"/v1/stores/{storeId:D}/products?limit=1", paired.Session.Token);
        var cursor = firstPage.Body.GetProperty("next_cursor").GetString();
        Assert.False(string.IsNullOrWhiteSpace(cursor));
        var secondPage = await _api.Get(
            $"/v1/stores/{storeId:D}/products?limit=1&cursor={Uri.EscapeDataString(cursor!)}", paired.Session.Token);
        Assert.Equal(HttpStatusCode.OK, secondPage.StatusCode);
        Assert.Single(secondPage.Body.GetProperty("items").EnumerateArray());
        var wrongCursorKind = await _api.Get(
            $"/v1/stores/{storeId:D}/products/cascade-options?name=Alpha%20edited&cursor={Uri.EscapeDataString(cursor!)}",
            paired.Session.Token);
        Assert.Equal(HttpStatusCode.BadRequest, wrongCursorKind.StatusCode);
        Assert.Equal("CURSOR_INVALID", wrongCursorKind.Body.GetProperty("code").GetString());

        var wrongStore = await _api.Get($"/v1/stores/{otherStore:D}/products", paired.Session.Token);
        Assert.Equal(HttpStatusCode.Forbidden, wrongStore.StatusCode);
        Assert.Equal("STORE_ACCESS_DENIED", wrongStore.Body.GetProperty("code").GetString());
    }

    public void Dispose()
    {
        _http.Dispose();
        _api.Dispose();
        _factory.Dispose();
    }
}
