using System.Net;
using System.Text;
using System.Text.Json;
using Kandoo.Core;
using Xunit;

namespace Kandoo.App.Tests;

public sealed class C96Group3ProductsCustomersTests
{
    private static readonly HubConnectionSettings TestSettings = new(
        "https://hub.kandoo.local:8443",
        Guid.Parse("11111111-2222-3333-4444-555555555555"),
        "tok_owner_session");

    [Fact]
    public void ProductMapper_MapsTagStockVersionAndBoardStatuses()
    {
        using var doc = JsonDocument.Parse("""
            {
              "items": [
                {
                  "product_id": "prod-101",
                  "name": "بادکنک",
                  "type": "کرومی",
                  "color": "سبز",
                  "unit_price_rials": 250000,
                  "purchase_price_rials": 180000,
                  "product_tag_number": 101,
                  "stock_millis": 12000,
                  "version": 3,
                  "operator_fields": [
                    { "key": "جنس", "value": "لاتکس" }
                  ]
                },
                {
                  "product_id": "prod-102",
                  "name": "شمع تولد عددی",
                  "unit_price_rials": 0,
                  "product_tag_number": 102,
                  "stock_millis": 4000,
                  "version": 1
                },
                {
                  "product_id": "prod-103",
                  "name": "ریسه کاغذی",
                  "unit_price_rials": 180000,
                  "product_tag_number": 103,
                  "stock_millis": 3000,
                  "stock_unconfirmed": true
                }
              ]
            }
            """);

        var rows = ProductMapper.Map(doc.RootElement);
        Assert.Equal(3, rows.Count);

        Assert.Equal("101", rows[0].CodeDisplay);
        Assert.Equal("250,000 ریال", rows[0].TablePriceDisplay);
        Assert.Equal("12", rows[0].StockDisplay);
        Assert.Equal("آماده فروش", rows[0].StatusBadgeText);
        Assert.Equal("#256B43", rows[0].StatusBadgeHex);
        Assert.Equal(3, rows[0].Version);
        Assert.Single(rows[0].CustomFields);
        Assert.Equal("لاتکس", rows[0].CustomFields[0].Value);

        Assert.True(rows[1].IsIncompletePrice);
        Assert.Equal("—", rows[1].TablePriceDisplay);
        Assert.Equal("قیمت ناقص", rows[1].StatusBadgeText);
        Assert.Equal("#9A3B0A", rows[1].StatusBadgeHex);

        Assert.True(rows[2].IsStockUnconfirmed);
        Assert.True(rows[2].IsLowStock);
        Assert.Equal("موجودی تأییدنشده", rows[2].StatusBadgeText);
        Assert.Equal("#6B5A3E", rows[2].StatusBadgeHex);
    }

    [Fact]
    public void ProductCatalogFilter_SupportsBoardChipsAndPersianDigitSearch()
    {
        var rows = new[]
        {
            new ProductRow("p1", "بادکنک کرومی", 250_000, ProductTagNumber: 101, StockMillis: 12_000),
            new ProductRow("p2", "شمع عددی", 0, ProductTagNumber: 102, StockMillis: 2_000),
            new ProductRow("p3", "ریسه کاغذی", 180_000, ProductTagNumber: 103, StockMillis: 3_000, StockUnconfirmed: true),
        };

        Assert.Single(ProductCatalogFilter.Apply(rows, ProductListFilter.IncompletePrice, null));
        Assert.Single(ProductCatalogFilter.Apply(rows, ProductListFilter.UnconfirmedStock, null));
        Assert.Equal(2, ProductCatalogFilter.Apply(rows, ProductListFilter.LowStock, null).Count);

        var searchByPersianCode = ProductCatalogFilter.Apply(rows, ProductListFilter.All, "۱۰۲");
        Assert.Single(searchByPersianCode);
        Assert.Equal("p2", searchByPersianCode[0].ProductId);
    }

    [Fact]
    public void TryValidateCreateForm_EnforcesThreeMandatoryFieldsAndParsesPersianDigits()
    {
        Assert.False(HubProductEditorDataSource.TryValidateCreateForm(
            "", "250000", "10", null, null,
            out _, out _, out _, out _, out var missingNameError));
        Assert.Contains("نام کالا", missingNameError);

        Assert.False(HubProductEditorDataSource.TryValidateCreateForm(
            "بادکنک", "", "10", null, null,
            out _, out _, out _, out _, out var missingPriceError));
        Assert.Contains("قیمت", missingPriceError);

        Assert.False(HubProductEditorDataSource.TryValidateCreateForm(
            "بادکنک", "250000", "", null, null,
            out _, out _, out _, out _, out var missingStockError));
        Assert.Contains("تعداد", missingStockError);

        Assert.True(HubProductEditorDataSource.TryValidateCreateForm(
            "بادکنک", "۲۵۰۰۰۰", "۱۲.۵", "۱۸۰۰۰۰", "",
            out var unitPrice, out var stockMillis, out var purchasePrice, out var tagNumber, out _));
        Assert.Equal(250_000, unitPrice);
        Assert.Equal(12_500, stockMillis);
        Assert.Equal(180_000, purchasePrice);
        Assert.Null(tagNumber);
    }

    [Fact]
    public async Task CreateProductDetailedAsync_UsesExactHubWireContractAndParsesNestedProduct()
    {
        string? capturedBody = null;
        var handler = new StubHandler(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.EndsWith($"/v1/stores/{TestSettings.StoreId:D}/products", request.RequestUri!.AbsoluteUri);
            capturedBody = await request.Content!.ReadAsStringAsync();

            return JsonResponse(HttpStatusCode.Created, """
                {
                  "status": "CREATED",
                  "product": {
                    "product_id": "prod-abc",
                    "name": "بادکنک",
                    "product_tag_number": 1004,
                    "version": 1
                  },
                  "stock_millis": 15000
                }
                """);
        });

        var source = new HubProductEditorDataSource(new HttpClient(handler), TestSettings);
        var created = await source.CreateProductDetailedAsync(new ProductEditorCreateRequest(
            Name: "بادکنک",
            UnitPriceRials: 250_000,
            InitialStockMillis: 15_000,
            Type: "کرومی",
            Color: "سبز",
            PurchasePriceRials: 180_000,
            ProductTagNumber: null,
            OperatorFields: new[] { new ProductOperatorField("جنس", "لاتکس") },
            OperationId: "op-create-1"));

        Assert.Equal("prod-abc", created.ProductId);
        Assert.Equal(1004, created.ProductTagNumber);
        Assert.Equal(15_000, created.StockMillis);
        Assert.Equal(1, created.Version);

        using var sentDoc = JsonDocument.Parse(capturedBody!);
        var root = sentDoc.RootElement;
        Assert.Equal("op-create-1", root.GetProperty("operation_id").GetString());
        Assert.Equal("بادکنک", root.GetProperty("name").GetString());
        Assert.Equal(250_000, root.GetProperty("unit_price_rials").GetInt64());
        Assert.Equal(15_000, root.GetProperty("initial_stock_millis").GetInt64());
        Assert.Equal("کرومی", root.GetProperty("type").GetString());
        Assert.Equal("سبز", root.GetProperty("color").GetString());
        Assert.Equal(180_000, root.GetProperty("purchase_price_rials").GetInt64());
        Assert.False(root.TryGetProperty("product_tag_number", out _));

        var opField = root.GetProperty("operator_fields")[0];
        Assert.Equal("جنس", opField.GetProperty("key").GetString());
        Assert.Equal("لاتکس", opField.GetProperty("value").GetString());
    }

    [Fact]
    public async Task UpdateProductDetailedAsync_WrapsBaseVersionPayloadAndGroupAssignment()
    {
        var requests = new List<(HttpMethod Method, string Path, string Body)>();
        var handler = new StubHandler(async request =>
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync();
            requests.Add((request.Method, request.RequestUri!.AbsolutePath, body));

            if (request.Method == HttpMethod.Patch)
            {
                return JsonResponse(HttpStatusCode.OK, """
                    {
                      "status": "SUCCEEDED",
                      "result_status": "ACCEPTED",
                      "response": {
                        "product": { "product_id": "prod-101", "version": 4 },
                        "version": 4,
                        "stock_millis": 17000
                      }
                    }
                    """);
            }

            return JsonResponse(HttpStatusCode.OK, """{"status":"ASSIGNED","product_id":"prod-101","group_id":"grp-2"}""");
        });

        var source = new HubProductEditorDataSource(new HttpClient(handler), TestSettings);
        var updated = await source.UpdateProductDetailedAsync(new ProductEditorUpdateRequest(
            ProductId: "prod-101",
            BaseVersion: 3,
            Name: "بادکنک طلایی",
            UnitPriceRials: 290_000,
            StockMillis: 17_000,
            GroupId: "grp-2",
            OperationId: "op-upd-9"));

        Assert.Equal(4, updated.Version);
        Assert.Equal(17_000, updated.StockMillis);
        Assert.Equal(2, requests.Count);
        Assert.Equal(HttpMethod.Patch, requests[0].Method);

        using var patchDoc = JsonDocument.Parse(requests[0].Body);
        Assert.Equal(3, patchDoc.RootElement.GetProperty("base_version").GetInt64());
        var payload = patchDoc.RootElement.GetProperty("payload");
        Assert.Equal("بادکنک طلایی", payload.GetProperty("name").GetString());
        Assert.Equal(290_000, payload.GetProperty("unit_price_rials").GetInt64());
        Assert.Equal(17_000, payload.GetProperty("stock_millis").GetInt64());

        Assert.Equal(HttpMethod.Post, requests[1].Method);
        Assert.EndsWith("/products/prod-101/group", requests[1].Path);
    }

    [Fact]
    public async Task LoadGroupOptionsAsync_ParsesGroupsArrayWithParentHierarchy()
    {
        var handler = new StubHandler(_ => Task.FromResult(JsonResponse(HttpStatusCode.OK, """
            {
              "status": "OK",
              "groups": [
                { "group_id": "g-root", "parent_group_id": null, "name": "تزئینات", "is_active": true },
                { "group_id": "g-sub", "parent_group_id": "g-root", "name": "بادکنک", "is_active": true },
                { "group_id": "g-off", "parent_group_id": null, "name": "غیرفعال", "is_active": false }
              ]
            }
            """)));

        var source = new HubProductEditorDataSource(new HttpClient(handler), TestSettings);
        var groups = await source.LoadGroupOptionsAsync();

        Assert.Equal(2, groups.Count);
        Assert.Equal("تزئینات", groups[0].Name);
        Assert.Equal("تزئینات / بادکنک", groups[1].Name);
    }

    [Fact]
    public void TryValidateStockAdjustment_RequiresSignedDeltaReasonAndNonNegativeBalance()
    {
        Assert.False(HubProductEditorDataSource.TryValidateStockAdjustment(
            5_000, "2", "", out _, out _, out var missingReason));
        Assert.Contains("دلیل", missingReason);

        Assert.False(HubProductEditorDataSource.TryValidateStockAdjustment(
            5_000, "−۶", "ضایعات", out _, out _, out var negativeStock));
        Assert.Contains("منفی", negativeStock);

        Assert.True(HubProductEditorDataSource.TryValidateStockAdjustment(
            5_000, "−۲", "ضایعات", out var nextMillis, out var deltaMillis, out _));
        Assert.Equal(-2_000, deltaMillis);
        Assert.Equal(3_000, nextMillis);
    }

    [Fact]
    public async Task CustomersDirectory_FallsBackToInvoicesAndMarksSyntheticRowsHonestly()
    {
        var invoiceFallbackHandler = new StubHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/customers", StringComparison.Ordinal))
            {
                return Task.FromResult(JsonResponse(HttpStatusCode.NotFound, """{"code":"NOT_FOUND"}"""));
            }

            return Task.FromResult(JsonResponse(HttpStatusCode.OK, """
                {
                  "items": [
                    {
                      "invoice_id": "inv-1",
                      "display_number": 101,
                      "status": "ISSUED",
                      "customer_name": "زهرا حسینی",
                      "masked_mobile": "0912***4455",
                      "amount_rials": 1500000,
                      "remaining_balance_rials": 300000
                    },
                    {
                      "invoice_id": "inv-2",
                      "display_number": 102,
                      "status": "ISSUED",
                      "masked_mobile": "0912***4455",
                      "amount_rials": 500000,
                      "remaining_balance_rials": 0
                    }
                  ]
                }
                """));
        });

        var source = new HubCustomersDataSource(new HttpClient(invoiceFallbackHandler), TestSettings);
        var directory = await source.LoadDirectoryAsync();

        Assert.Equal(CustomerDataOrigin.HubInvoicesProjection, directory.Origin);
        Assert.Single(directory.Customers);
        Assert.Equal("زهرا حسینی", directory.Customers[0].Name);
        Assert.Equal(2_000_000, directory.Customers[0].TotalPurchasesRials);
        Assert.Equal(300_000, directory.Customers[0].OutstandingRials);
        Assert.Equal(2, directory.Customers[0].Invoices.Count);

        var unavailableHandler = new StubHandler(_ =>
            Task.FromResult(JsonResponse(HttpStatusCode.NotFound, """{"code":"NOT_FOUND"}""")));
        var unavailableSource = new HubCustomersDataSource(new HttpClient(unavailableHandler), TestSettings);
        var unavailable = await unavailableSource.LoadDirectoryAsync();
        Assert.Equal(CustomerDataOrigin.HubEndpointUnavailable, unavailable.Origin);
        Assert.Empty(unavailable.Customers);

        var synthetic = HubCustomersDataSource.CreateSyntheticPreviewRows();
        Assert.NotEmpty(synthetic);
        Assert.All(synthetic, row =>
        {
            Assert.True(row.IsSyntheticDemo);
            Assert.Contains("ساختگی", row.Name);
        });
    }

    [Fact]
    public void HubConnectionResolver_ResolvesSavedHubUrlAndSharedToken()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"kandoo-c96-g3-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var storeId = Guid.NewGuid();
            File.WriteAllText(
                Path.Combine(tempDir, HubConnectionResolver.HubUrlFileName),
                "https://192.168.1.25:8443");
            SharedTokenFile.Write(
                new PersistedDeviceSession("dev-1", "tok-abc", storeId, "OWNER"),
                tempDir);

            var resolved = HubConnectionResolver.Resolve(explicitSettings: null, baseDirectory: tempDir);
            Assert.NotNull(resolved);
            Assert.Equal("https://192.168.1.25:8443", resolved!.BaseUrl);
            Assert.Equal(storeId, resolved.StoreId);
            Assert.Equal("tok-abc", resolved.SessionToken);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode code, string json) =>
        new(code)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => responder(request);
    }
}
