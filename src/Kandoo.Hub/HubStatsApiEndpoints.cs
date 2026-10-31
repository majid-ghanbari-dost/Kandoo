using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Kandoo.Hub;

public static partial class HubApiFirstEndpoints
{
    public static void MapStatsApi(WebApplication app)
    {
        app.MapGet("/v1/stores/{store_id}/stats/v0", (
            HttpContext context,
            [FromRoute(Name = "store_id")] string storeId,
            IHubAccessStore access,
            HubStatsService stats) =>
        {
            if (!context.Request.Headers.TryGetValue("Authorization", out var header) ||
                !header.ToString().StartsWith("Bearer ", StringComparison.Ordinal))
            {
                return Results.Json(new { code = "AUTH_REQUIRED" }, HubJson.Options, statusCode: 401);
            }

            var session = access.Authenticate(header.ToString()["Bearer ".Length..]);
            if (session is null)
            {
                return Results.Json(new { code = "AUTH_INVALID" }, HubJson.Options, statusCode: 401);
            }

            if (!Guid.TryParse(storeId, out var parsedStoreId))
            {
                return Results.Json(new { code = "STORE_ID_INVALID" }, HubJson.Options, statusCode: 400);
            }

            if (session.StoreId != parsedStoreId)
            {
                return Results.Json(new { code = "STORE_ISOLATION" }, HubJson.Options, statusCode: 403);
            }

            var value = stats.Get(parsedStoreId);
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
