using System.Net;
using System.Text;
using System.Text.Json;
using Kandoo.App;
using Xunit;

namespace Kandoo.App.Tests;

public sealed class ProductTests
{
    [Fact]
    public void C63_MapperMapsAllProductFieldsIncludingGroup()
    {
        var rows = ProductMapper.Map(Root("""
            {
              "status": "OK",
              "items": [
                {
                  "product_id": "p-1",
                  "name": "بادکنک کرومی",
                  "group_id": "g-1",
                  "unit_price_rials": 4000,
                  "is_active": true
                },
                {
                  "product_id": "p-2",
                  "name": "نوت‌بک",
                  "group_id": null,
                  "unit_price_rials": 0,
                  "is_active": false
                }
              ],
              "total_count": 2,
              "next_cursor": null
            }
            """));

        Assert.Equal(2, rows.Count);
        Assert.Equal("p-1", rows[0].ProductId);
        Assert.Equal("بادکنک کرومی", rows[0].Name);
        Assert.Equal("g-1", rows[0].GroupId);
        Assert.Equal("4,000 ریال", rows[0].PriceDisplay);
        Assert.Equal("فعال", rows[0].ActiveDisplay);
        Assert.Equal("g-1", rows[0].GroupDisplay);
        Assert.Equal("p-2", rows[1].ProductId);
        Assert.Null(rows[1].GroupId);
        Assert.Equal("0 ریال", rows[1].PriceDisplay);
        Assert.Equal("غیرفعال", rows[1].ActiveDisplay);
        Assert.Equal("بدون گروه", rows[1].GroupDisplay);
    }

    [Fact]
    public void C63_MapperFailClosesOnMissingOrMalformedItems()
    {
        Assert.Throws<JsonException>(() => ProductMapper.Map(Root("{}")));
        Assert.Throws<JsonException>(() => ProductMapper.Map(Root("{\"items\": null}")));
        Assert.Throws<JsonException>(() => ProductMapper.Map(Root("""
            {
              "items": [
                {
                  "product_id": "p-1",
                  "name": "x",
                  "group_id": null,
                  "unit_price_rials": -5,
                  "is_active": true
                }
              ]
            }
            """)));
        Assert.Throws<JsonException>(() => ProductMapper.Map(Root("""
            {
              "items": [
                {
                  "product_id": "",
                  "name": "x",
                  "group_id": null,
                  "unit_price_rials": 1,
                  "is_active": true
                }
              ]
            }
            """)));
    }

    [Fact]
    public async Task C63_DataSourceSendsGroupFilteredAuthorizedRequest()
    {
        var storeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var handler = new StubProductHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal($"https://hub.invalid/v1/stores/{storeId:D}/products?group_id=g1",
                request.RequestUri!.ToString());
            Assert.Equal("Bearer token-123", request.Headers.Authorization!.ToString());
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {"status":"OK","items":[{"product_id":"p-1","name":"کالا","group_id":"g1","unit_price_rials":4000,"is_active":true}]}
                    """, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        });

        var source = new HubProductDataSource(new HttpClient(handler),
            new HubConnectionSettings("https://hub.invalid", storeId, "token-123"));

        var rows = await source.LoadProductsAsync("g1");

        Assert.Single(rows);
        Assert.Equal("p-1", rows[0].ProductId);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task C63_DataSourceOmitsGroupWhenFilterIsEmpty()
    {
        var storeId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var handler = new StubProductHandler((request, _) =>
        {
            Assert.DoesNotContain("group_id", request.RequestUri!.Query);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"status\":\"OK\",\"items\":[]}", Encoding.UTF8, "application/json")
            });
        });

        var source = new HubProductDataSource(new HttpClient(handler),
            new HubConnectionSettings("https://hub.invalid", storeId, "token-123"));

        var rows = await source.LoadProductsAsync(null);
        Assert.Empty(rows);
    }

    [Fact]
    public async Task C63_DataSourceFailsOnNonSuccess()
    {
        var storeId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var handler = new StubProductHandler((request, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));

        var source = new HubProductDataSource(new HttpClient(handler),
            new HubConnectionSettings("https://hub.invalid", storeId, "token-123"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => source.LoadProductsAsync(null));
    }

    [Fact]
    public async Task C63_DataSourceFailsOnMalformedBody()
    {
        var storeId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var handler = new StubProductHandler((request, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("not json", Encoding.UTF8, "application/json")
            }));

        var source = new HubProductDataSource(new HttpClient(handler),
            new HubConnectionSettings("https://hub.invalid", storeId, "token-123"));

        await Assert.ThrowsAssignableAsync<JsonException>(() => source.LoadProductsAsync(null));
    }

    [Fact]
    public void C63_DataSourceRequiresSessionTokenAndValidStore()
    {
        var storeId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        Assert.Throws<ArgumentException>(() => new HubProductDataSource(new HttpClient(),
            new HubConnectionSettings("https://hub.invalid", storeId, "")));
        Assert.Throws<ArgumentException>(() => new HubProductDataSource(new HttpClient(),
            new HubConnectionSettings("not-a-url", Guid.Empty, "token")));
    }

    private static JsonElement Root(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private sealed class StubProductHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return send(request, cancellationToken);
        }
    }
}
