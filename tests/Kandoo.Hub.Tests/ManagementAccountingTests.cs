using System.Net;
using Kandoo.Core;
using Kandoo.Hub;
using Xunit;

namespace Kandoo.Hub.Tests;

/// <summary>
/// C-94: light read-only management accounting derived from synthetic issue events
/// (PM-06: synthetic fixtures only; no real data in git/CI). Drives the same Hub
/// store the API uses and asserts the summary endpoint exposes sales by payment
/// method, receivables, stock value, profit for products with a purchase price,
/// and the professional debit/credit view.
/// </summary>
public sealed class ManagementAccountingTests : IDisposable
{
    private readonly HubApiFactory _factory = new();
    private readonly HubApiClient _api;

    public ManagementAccountingTests()
    {
        _api = new HubApiClient(_factory);
    }

    [Fact]
    public async Task C94_AccountingSummary_DerivesLightFiguresFromSyntheticEvents()
    {
        var storeId = Guid.NewGuid();
        var owner = await _api.Owner(storeId, Guid.NewGuid(), Guid.NewGuid());
        var paired = await _api.Pair(owner, storeId, Guid.NewGuid());

        var catalog = new HubProductCatalogService(_factory.Store);
        var priced = catalog.Register(
            storeId,
            paired.Session.DeviceId,
            "c94-product-priced",
            new ProductRegistrationInput(
                "کالای تستی قیمت‌دار", 2_000, 10_000, ProductId: "p-c94-priced", PurchasePriceRials: 1_200));
        Assert.Equal(ProductRegistrationStatus.Created, priced.Status);
        var unpriced = catalog.Register(
            storeId,
            paired.Session.DeviceId,
            "c94-product-unpriced",
            new ProductRegistrationInput(
                "کالای تستی بدون قیمت خرید", 6_000, 5_000, ProductId: "p-c94-unpriced"));
        Assert.Equal(ProductRegistrationStatus.Created, unpriced.Status);

        var inventory = _factory.Store.LoadInventoryLedger();
        Assert.Equal(10_000, inventory.StockMillis(storeId, "p-c94-priced"));
        Assert.Equal(5_000, inventory.StockMillis(storeId, "p-c94-unpriced"));

        var now = new DateTimeOffset(2026, 11, 23, 10, 0, 0, TimeSpan.Zero);

        // Invoice A: priced item (2 × 2000 = 4000, cost 2400) + unpriced item
        // (1 × 6000 = 6000); payments cash 3000 + card-reader 5000; remaining 2000.
        var invoiceA = Guid.NewGuid();
        SeedDraft(storeId, paired.Session.DeviceId, invoiceA, now.AddMinutes(-2), new[]
        {
            new InvoiceItem(Guid.NewGuid(), storeId, 1, "قلم قیمت‌دار", 2_000, 2_000, 4_000, ProductId: "p-c94-priced"),
            new InvoiceItem(Guid.NewGuid(), storeId, 2, "قلم بدون قیمت خرید", 1_000, 6_000, 6_000, ProductId: "p-c94-unpriced"),
        });
        SeedIssue(
            storeId, paired.Session.DeviceId, invoiceA, now,
            new object[]
            {
                new { type = "cash", amount = 3_000L },
                new { type = "card-reader", amount = 5_000L },
            },
            2_000L);

        // Invoice B: item without a product; no payments and no remaining balance
        // recorded → explicit unrecorded buckets (nothing guessed).
        var invoiceB = Guid.NewGuid();
        SeedDraft(storeId, paired.Session.DeviceId, invoiceB, now.AddMinutes(-1), new[]
        {
            new InvoiceItem(Guid.NewGuid(), storeId, 1, "قلم بدون محصول", 1_000, 4_000, 4_000),
        });
        SeedIssue(storeId, paired.Session.DeviceId, invoiceB, now.AddSeconds(30), null, null);

        // Invoice C: issued then revoked → excluded from the summary entirely.
        var invoiceC = Guid.NewGuid();
        SeedDraft(storeId, paired.Session.DeviceId, invoiceC, now.AddMinutes(-1), new[]
        {
            new InvoiceItem(Guid.NewGuid(), storeId, 1, "قلم ابطالی", 1_000, 9_000, 9_000, ProductId: "p-c94-priced"),
        });
        SeedIssue(storeId, paired.Session.DeviceId, invoiceC, now.AddSeconds(10), null, null);
        var revoked = _factory.Store.GetInvoice(storeId, invoiceC)!;
        revoked.Revoke();
        _factory.Store.SaveInvoice(revoked);

        var response = await _api.Get($"/v1/stores/{storeId}/accounting/summary/v0", paired.Session.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("OK", response.Body.GetProperty("status").GetString());

        var methods = response.Body.GetProperty("payment_methods");
        Assert.Equal(1, methods.GetProperty("card_reader").GetProperty("invoice_count").GetInt64());
        Assert.Equal(5_000, methods.GetProperty("card_reader").GetProperty("amount_rials").GetInt64());
        Assert.Equal(1, methods.GetProperty("cash").GetProperty("invoice_count").GetInt64());
        Assert.Equal(3_000, methods.GetProperty("cash").GetProperty("amount_rials").GetInt64());
        Assert.Equal(0, methods.GetProperty("card_to_card").GetProperty("invoice_count").GetInt64());
        Assert.Equal(0, methods.GetProperty("credit").GetProperty("invoice_count").GetInt64());
        Assert.Equal(1, methods.GetProperty("unrecorded_invoices").GetInt64());
        Assert.Equal(
            ManagementAccountingService.UnrecordedPaymentReason,
            methods.GetProperty("unrecorded_reason").GetString());

        var receivables = response.Body.GetProperty("receivables");
        Assert.Equal(1, receivables.GetProperty("invoice_count").GetInt64());
        Assert.Equal(2_000, receivables.GetProperty("outstanding_rials").GetInt64());
        Assert.Equal(1, receivables.GetProperty("unrecorded_invoices").GetInt64());
        Assert.Equal(
            ManagementAccountingService.UnrecordedRemainingReason,
            receivables.GetProperty("unrecorded_reason").GetString());

        var stock = response.Body.GetProperty("stock_value");
        Assert.Equal(1, stock.GetProperty("priced_products").GetInt64());
        Assert.Equal(12_000, stock.GetProperty("value_rials").GetInt64());
        Assert.Equal(1, stock.GetProperty("unpriced_products").GetInt64());
        Assert.Equal(5_000, stock.GetProperty("unpriced_stock_millis").GetInt64());

        var profit = response.Body.GetProperty("profit");
        Assert.Equal(1, profit.GetProperty("included_items").GetInt64());
        Assert.Equal(4_000, profit.GetProperty("sales_rials").GetInt64());
        Assert.Equal(2_400, profit.GetProperty("cost_rials").GetInt64());
        Assert.Equal(1_600, profit.GetProperty("profit_rials").GetInt64());
        Assert.Equal(2, profit.GetProperty("excluded_items").GetInt64());
        Assert.Equal(
            ManagementAccountingService.ExcludedItemReason,
            profit.GetProperty("excluded_reason").GetString());

        var ledger = response.Body.GetProperty("professional").GetProperty("ledger");
        Assert.Equal(6, ledger.GetArrayLength());
        Assert.Equal(8_000, ledger[0].GetProperty("debit_rials").GetInt64());
        Assert.Equal(2_000, ledger[1].GetProperty("debit_rials").GetInt64());
        Assert.Equal(12_000, ledger[2].GetProperty("debit_rials").GetInt64());
        Assert.Equal(14_000, ledger[3].GetProperty("credit_rials").GetInt64());
        Assert.Equal(2_400, ledger[4].GetProperty("credit_rials").GetInt64());
        Assert.Equal(1_600, ledger[5].GetProperty("credit_rials").GetInt64());
    }

    [Fact]
    public async Task C94_AccountingRouteIsStoreScoped()
    {
        var storeId = Guid.NewGuid();
        var otherStoreId = Guid.NewGuid();
        var owner = await _api.Owner(storeId, Guid.NewGuid(), Guid.NewGuid());
        var paired = await _api.Pair(owner, storeId, Guid.NewGuid());

        var isolated = await _api.Get($"/v1/stores/{otherStoreId}/accounting/summary/v0", paired.Session.Token);
        Assert.Equal(HttpStatusCode.Forbidden, isolated.StatusCode);
        Assert.Equal("STORE_ACCESS_DENIED", isolated.Body.GetProperty("code").GetString());
    }

    private void SeedDraft(
        Guid storeId,
        Guid deviceId,
        Guid invoiceId,
        DateTimeOffset createdAt,
        InvoiceItem[] items)
    {
        var draftRequest = new OperationRequest(
            storeId,
            deviceId,
            $"c94-draft-{invoiceId:N}",
            "invoice.draft_create",
            null,
            HubJson.Element(new { invoice_id = invoiceId }));
        var draft = _factory.Store.CreateDraft(draftRequest, invoiceId, createdAt, items);
        Assert.Equal(OperationExecutionStatus.Succeeded, draft.Status);
    }

    private void SeedIssue(
        Guid storeId,
        Guid deviceId,
        Guid invoiceId,
        DateTimeOffset issuedAt,
        object[]? payments,
        long? remainingBalance)
    {
        var values = new Dictionary<string, object?>
        {
            ["invoice_id"] = invoiceId
        };
        if (payments is not null)
        {
            values["payments"] = payments;
        }

        if (remainingBalance is not null)
        {
            values["remaining_balance"] = remainingBalance;
        }

        var issueRequest = new OperationRequest(
            storeId,
            deviceId,
            $"c94-issue-{invoiceId:N}",
            "invoice.issue",
            null,
            HubJson.Element(values));
        var issued = _factory.Store.IssueInvoice(issueRequest, invoiceId, issuedAt);
        Assert.Equal(OperationExecutionStatus.Succeeded, issued.Status);
    }

    public void Dispose()
    {
        _api.Dispose();
        _factory.Dispose();
    }
}
