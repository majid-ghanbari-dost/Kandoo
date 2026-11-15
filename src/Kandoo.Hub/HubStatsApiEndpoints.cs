using Kandoo.Core;
using Microsoft.AspNetCore.Http;

namespace Kandoo.Hub;

public static partial class HubApiFirstEndpoints
{
    public static void MapStatsApi(WebApplication app)
    {
        app.MapGet("/v1/stores/{store_id}/stats/v0", (
            Guid store_id,
            HttpContext context,
            IHubAccessStore access,
            IClock clock,
            HubStatsService stats) =>
        {
            var auth = Authenticate(context, store_id, access, clock);
            if (!auth.IsAuthenticated)
            {
                return AuthError(auth.Code);
            }

            var value = stats.Get(store_id);
            return Results.Json(new
            {
                status = "OK",
                version = value.Version,
                store_id = value.StoreId,
                scope = "SUCCESSFUL_INVOICE_ISSUE_EVENTS",
                generated_at_utc = value.GeneratedAtUtc,
                time_zone = value.TimeZone,
                calendar = value.Calendar,
                week_start = value.WeekStart.ToString().ToUpperInvariant(),
                jalali_today = new { year = value.JalaliYear, month = value.JalaliMonth, day = value.JalaliDay },
                currency = HubInvoiceCurrency.Body,
                accepted_payment_method_values = new[]
                {
                    HubStatsService.CardReaderMethod,
                    HubStatsService.CardToCardMethod,
                    HubStatsService.CashMethod
                },
                periods = new
                {
                    today = PeriodBody(value.Today),
                    week = PeriodBody(value.Week),
                    month = PeriodBody(value.Month)
                }
            }, HubJson.Options);
        });
    }

    private static object PeriodBody(HubStatsPeriod period) => new
    {
        start_local = period.StartLocal,
        end_exclusive_local = period.EndExclusiveLocal,
        invoice_count = period.Total.InvoiceCount,
        sales_rials = period.Total.SalesRials,
        payment_methods = new
        {
            complete = period.PaymentMethods.Complete,
            card_reader = ValuesBody(period.PaymentMethods.CardReader),
            card_to_card = ValuesBody(period.PaymentMethods.CardToCard),
            cash = ValuesBody(period.PaymentMethods.Cash),
            unknown = new
            {
                invoice_count = period.PaymentMethods.Unknown.InvoiceCount,
                sales_rials = period.PaymentMethods.Unknown.SalesRials,
                reason = period.PaymentMethods.Unknown.Reason
            }
        }
    };

    private static object ValuesBody(HubStatsValues values) => new
    {
        invoice_count = values.InvoiceCount,
        sales_rials = values.SalesRials
    };
}
