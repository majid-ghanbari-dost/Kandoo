using Kandoo.Core;
using Microsoft.AspNetCore.Http;

namespace Kandoo.Hub;

public static partial class HubApiFirstEndpoints
{
    public static void MapAccountingApi(WebApplication app)
    {
        // C-94: the accounting service is stateless and constructed per request from
        // registered dependencies (IHubStore + SqliteHubStore + TimeProvider), so no
        // Program.cs registration change is needed for this read-only endpoint.
        app.MapGet("/v1/stores/{store_id}/accounting/summary/v0", (
            Guid store_id,
            HttpContext context,
            IHubAccessStore access,
            IClock clock,
            IHubStore store,
            SqliteHubStore sqliteStore,
            TimeProvider time) =>
        {
            var auth = Authenticate(context, store_id, access, clock);
            if (!auth.IsAuthenticated)
            {
                return AuthError(auth.Code);
            }

            var value = new ManagementAccountingService(store, sqliteStore, time).Get(store_id);
            return Results.Json(new
            {
                status = "OK",
                version = value.Version,
                store_id = value.StoreId,
                scope = "SUCCESSFUL_INVOICE_ISSUE_EVENTS",
                generated_at_utc = value.GeneratedAtUtc,
                currency = HubInvoiceCurrency.Body,
                payment_methods = new
                {
                    card_reader = MethodBody(value.PaymentMethods.CardReader),
                    card_to_card = MethodBody(value.PaymentMethods.CardToCard),
                    cash = MethodBody(value.PaymentMethods.Cash),
                    credit = MethodBody(value.PaymentMethods.Credit),
                    unrecorded_invoices = value.PaymentMethods.UnrecordedInvoices,
                    unrecorded_reason = value.PaymentMethods.UnrecordedReason
                },
                receivables = new
                {
                    invoice_count = value.Receivables.InvoiceCount,
                    outstanding_rials = value.Receivables.OutstandingRials,
                    unrecorded_invoices = value.Receivables.UnrecordedInvoices,
                    unrecorded_reason = value.Receivables.UnrecordedReason
                },
                stock_value = new
                {
                    priced_products = value.StockValue.PricedProducts,
                    value_rials = value.StockValue.StockValueRials,
                    unpriced_products = value.StockValue.UnpricedProducts,
                    unpriced_stock_millis = value.StockValue.UnpricedStockMillis,
                    unpriced_reason = value.StockValue.UnpricedReason
                },
                profit = new
                {
                    included_items = value.Profit.IncludedItems,
                    sales_rials = value.Profit.SalesRials,
                    cost_rials = value.Profit.CostRials,
                    profit_rials = value.Profit.ProfitRials,
                    excluded_items = value.Profit.ExcludedItems,
                    excluded_reason = value.Profit.ExcludedReason
                },
                professional = new
                {
                    note = ManagementAccountingService.ProfessionalNote,
                    ledger = value.ProfessionalLedger
                }
            }, HubJson.Options);
        });
    }

    private static object MethodBody(AccountingMethodValues values) => new
    {
        invoice_count = values.InvoiceCount,
        amount_rials = values.AmountRials
    };
}
