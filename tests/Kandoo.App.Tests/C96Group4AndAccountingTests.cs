using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Kandoo.App.Tests;

public sealed class C96Group4AndAccountingTests
{
    private static readonly HubConnectionSettings TestSettings = new(
        "https://hub.kandoo.local:8443",
        Guid.Parse("22222222-3333-4444-5555-666666666666"),
        "tok_owner_session");

    [Fact]
    public void ReturnValidation_EnforcesInvoiceQuantitySoldCapAndMandatoryReason()
    {
        Assert.False(HubReturnsDataSource.TryValidateReturnForm(
            "", "1", "خرابی", null, out _, out _, out var missingInvoice));
        Assert.Contains("شماره", missingInvoice);

        Assert.False(HubReturnsDataSource.TryValidateReturnForm(
            "105", "0", "خرابی", null, out _, out _, out var zeroQty));
        Assert.Contains("بزرگ‌تر از صفر", zeroQty);

        Assert.False(HubReturnsDataSource.TryValidateReturnForm(
            "105", "4", "خرابی", maxSoldQuantityMillis: 3_000, out _, out _, out var overQty));
        Assert.Contains("فروخته‌شده", overQty);

        Assert.False(HubReturnsDataSource.TryValidateReturnForm(
            "105", "2", "   ", null, out _, out _, out var missingReason));
        Assert.Contains("دلیل", missingReason);

        Assert.True(HubReturnsDataSource.TryValidateReturnForm(
            "۱۰۵", "۲", "مرجوعی مشتری", maxSoldQuantityMillis: 3_000,
            out var invoiceNo, out var qtyMillis, out _));
        Assert.Equal("105", invoiceNo);
        Assert.Equal(2_000, qtyMillis);
    }

    [Fact]
    public async Task SubmitReturnAsync_On404AppendsImmutableLedgerAndRestoresLinkedProductStock()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"kandoo-returns-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string? patchedProductBody = null;
            var handler = new StubHandler(async request =>
            {
                if (request.RequestUri!.AbsolutePath.EndsWith("/returns", StringComparison.Ordinal))
                {
                    return JsonResponse(HttpStatusCode.NotFound, """{"code":"NOT_FOUND"}""");
                }

                if (request.Method == HttpMethod.Patch &&
                    request.RequestUri.AbsolutePath.EndsWith("/products/prod-101", StringComparison.Ordinal))
                {
                    patchedProductBody = await request.Content!.ReadAsStringAsync();
                    return JsonResponse(HttpStatusCode.OK, """
                        {
                          "status": "SUCCEEDED",
                          "response": {
                            "product": { "product_id": "prod-101", "version": 3 },
                            "version": 3,
                            "stock_millis": 12000
                          }
                        }
                        """);
                }

                throw new InvalidOperationException($"Unexpected call: {request.Method} {request.RequestUri}");
            });

            var source = new HubReturnsDataSource(new HttpClient(handler), TestSettings, tempDir);
            var outcome = await source.SubmitReturnAsync(new ReturnSubmissionInput(
                InvoiceNumber: "105",
                ReturnQuantityMillis: 2_000,
                Reason: "بازگشت مشتری",
                RefundMethod: ReturnRefundMethod.Card,
                ProductId: "prod-101",
                ProductName: "بادکنک کرومی",
                CurrentProductStockMillis: 10_000,
                ProductBaseVersion: 2,
                OperationId: "op-ret-1"));

            Assert.False(outcome.Event.SyncedToHubReturnEndpoint);
            Assert.True(outcome.Event.StockAdjustedInHub);
            Assert.Equal("کارت", outcome.Event.RefundDisplay);

            Assert.NotNull(patchedProductBody);
            using var patchDoc = JsonDocument.Parse(patchedProductBody!);
            Assert.Equal(2, patchDoc.RootElement.GetProperty("base_version").GetInt64());
            Assert.Equal(12_000, patchDoc.RootElement.GetProperty("payload").GetProperty("stock_millis").GetInt64());

            var records = source.ReadLocalRecords();
            Assert.Single(records);
            Assert.Equal("105", records[0].InvoiceNumber);
            Assert.Equal("بازگشت مشتری", records[0].Reason);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task AccountingDataSource_ParsesHubSummaryStatsAndStockGroups()
    {
        var handler = new StubHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/accounting/summary/v0", StringComparison.Ordinal))
            {
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, """
                    {
                      "status": "OK",
                      "version": "v0",
                      "store_id": "22222222-3333-4444-5555-666666666666",
                      "generated_at_utc": "2026-11-27T10:00:00Z",
                      "payment_methods": {
                        "card_reader": { "invoice_count": 3, "amount_rials": 4500000 },
                        "card_to_card": { "invoice_count": 1, "amount_rials": 1200000 },
                        "cash": { "invoice_count": 2, "amount_rials": 800000 },
                        "credit": { "invoice_count": 1, "amount_rials": 500000 },
                        "unrecorded_invoices": 0
                      },
                      "receivables": {
                        "invoice_count": 1,
                        "outstanding_rials": 500000,
                        "unrecorded_invoices": 0
                      },
                      "stock_value": {
                        "priced_products": 2,
                        "value_rials": 3600000,
                        "unpriced_products": 1,
                        "unpriced_stock_millis": 4000
                      },
                      "profit": {
                        "included_items": 5,
                        "sales_rials": 7000000,
                        "cost_rials": 4800000,
                        "profit_rials": 2200000,
                        "excluded_items": 1
                      },
                      "professional": {
                        "note": "نمای حرفه‌ای: اصطلاحات بدهکار/بستانکار برای مدیریت؛ اطلاعاتی است و ترازنامهٔ رسمی نیست.",
                        "ledger": [
                          { "account": "دریافت کارت‌خوان", "debit_rials": 4500000, "credit_rials": 0 },
                          { "account": "فروش کالا", "debit_rials": 0, "credit_rials": 7000000 }
                        ]
                      }
                    }
                    """));
            }

            return Task.FromResult(JsonResponse(HttpStatusCode.OK, """
                {
                  "status": "OK",
                  "periods": {
                    "today": {
                      "invoice_count": 4,
                      "sales_rials": 4000000,
                      "payment_methods": {
                        "card_reader": { "invoice_count": 2, "sales_rials": 2500000 },
                        "card_to_card": { "invoice_count": 1, "sales_rials": 1000000 },
                        "cash": { "invoice_count": 1, "sales_rials": 500000 },
                        "unknown": { "invoice_count": 0, "sales_rials": 0 }
                      }
                    },
                    "week": {
                      "invoice_count": 6,
                      "sales_rials": 7000000,
                      "payment_methods": {
                        "card_reader": { "invoice_count": 3, "sales_rials": 4500000 },
                        "card_to_card": { "invoice_count": 1, "sales_rials": 1200000 },
                        "cash": { "invoice_count": 2, "sales_rials": 1300000 },
                        "unknown": { "invoice_count": 0, "sales_rials": 0 }
                      }
                    },
                    "month": {
                      "invoice_count": 6,
                      "sales_rials": 7000000,
                      "payment_methods": {
                        "card_reader": { "invoice_count": 3, "sales_rials": 4500000 },
                        "card_to_card": { "invoice_count": 1, "sales_rials": 1200000 },
                        "cash": { "invoice_count": 2, "sales_rials": 1300000 },
                        "unknown": { "invoice_count": 0, "sales_rials": 0 }
                      }
                    }
                  }
                }
                """));
        });

        var source = new HubAccountingDataSource(new HttpClient(handler), TestSettings);
        var summary = await source.LoadSummaryAsync();
        var stats = await source.LoadStatsAsync();

        Assert.Equal(2_200_000, summary.ProfitRials);
        Assert.Equal(500_000, summary.ReceivableOutstandingRials);
        Assert.Equal(3_600_000, summary.StockValueRials);
        Assert.Equal(1, summary.UnpricedProductsCount);
        Assert.Equal(2, summary.LedgerRows.Count);
        Assert.Equal("خودکار", summary.LedgerRows[0].SourceBadgeText);

        Assert.Equal(4_000_000, stats.Today.SalesRials);
        Assert.Equal(1_000_000, stats.Today.AverageInvoiceRials);
        Assert.Equal(7_000_000, stats.ForPeriod(AccountingPeriodKind.Week).SalesRials);

        var products = new[]
        {
            new ProductRow("p1", "بادکنک", "تزئینات", 250_000, true, StockMillis: 10_000, PurchasePriceRials: 180_000),
            new ProductRow("p2", "شمع", "تزئینات", 100_000, true, StockMillis: 5_000, PurchasePriceRials: null),
            new ProductRow("p3", "ریسه", "تزئینات", 200_000, true, StockMillis: 8_000, PurchasePriceRials: 120_000, StockUnconfirmed: true),
        };
        var groups = HubAccountingDataSource.SummarizeStockByGroup(products);
        Assert.Single(groups);
        Assert.Equal("تزئینات", groups[0].GroupName);
        Assert.Equal(1_800_000, groups[0].TotalValueRials);
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
