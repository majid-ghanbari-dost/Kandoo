using System.Net;
using Kandoo.Core;
using Kandoo.Hub;
using Xunit;

namespace Kandoo.Hub.Tests;

public sealed class HubStatsServiceTests
{
    private static readonly Guid S1 = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid S2 = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Device = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly TimeSpan TehranOffset = TimeSpan.FromHours(3.5);

    [Fact]
    public void C33_TehranTodayAdvancesBeforeUtcAndUsesSaturdayAndJalaliBoundaries()
    {
        using var store = new SqliteHubStore("Data Source=:memory:;Pooling=False");
        var time = new ManualHubTimeProvider { UtcNow = new DateTimeOffset(2026, 10, 31, 20, 45, 0, TimeSpan.Zero) };
        SeedIssued(store, S1, new DateTimeOffset(2026, 10, 31, 20, 14, 59, TimeSpan.Zero), 1000, HubStatsService.CashMethod);
        SeedIssued(store, S1, new DateTimeOffset(2026, 10, 31, 20, 30, 0, TimeSpan.Zero), 2000, HubStatsService.CardReaderMethod);

        var stats = new HubStatsService(store, time).Get(S1);

        Assert.Equal("0", stats.Version);
        Assert.Equal("Asia/Tehran", stats.TimeZone);
        Assert.Equal("JALALI", stats.Calendar);
        Assert.Equal(DayOfWeek.Saturday, stats.WeekStart);
        Assert.Equal((1405, 8, 10), (stats.JalaliYear, stats.JalaliMonth, stats.JalaliDay));
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TehranOffset), stats.Today.StartLocal);
        Assert.Equal(new DateTimeOffset(2026, 10, 31, 0, 0, 0, TehranOffset), stats.Week.StartLocal);
        Assert.Equal(new DateTimeOffset(2026, 10, 23, 0, 0, 0, TehranOffset), stats.Month.StartLocal);
        Assert.Equal(new HubStatsValues(1, 2000), stats.Today.Total);
        Assert.Equal(new HubStatsValues(1, 2000), stats.Today.PaymentMethods.CardReader);
        Assert.Equal(new HubStatsValues(0, 0), stats.Today.PaymentMethods.Cash);
        Assert.Equal(new HubStatsValues(2, 3000), stats.Week.Total);
        Assert.Equal(new HubStatsValues(1, 1000), stats.Week.PaymentMethods.Cash);
        Assert.Equal(new HubStatsValues(2, 3000), stats.Month.Total);
        Assert.True(stats.Today.PaymentMethods.Complete);
    }

    [Fact]
    public void C33_SaturdayWeekAndJalaliMonthIncludeAndExcludeIndependentEvents()
    {
        using var store = new SqliteHubStore("Data Source=:memory:;Pooling=False");
        var time = new ManualHubTimeProvider { UtcNow = new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero) };
        SeedIssued(store, S1, new DateTimeOffset(2026, 10, 21, 20, 29, 59, TimeSpan.Zero), 50, HubStatsService.CashMethod);
        SeedIssued(store, S1, new DateTimeOffset(2026, 10, 22, 20, 30, 0, TimeSpan.Zero), 100, HubStatsService.CardToCardMethod);
        SeedIssued(store, S1, new DateTimeOffset(2026, 10, 23, 20, 30, 0, TimeSpan.Zero), 200, HubStatsService.CardReaderMethod);
        SeedIssued(store, S1, new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero), 400, HubStatsService.CashMethod);

        var stats = new HubStatsService(store, time).Get(S1);

        Assert.Equal(new DateTimeOffset(2026, 10, 24, 0, 0, 0, TehranOffset), stats.Week.StartLocal);
        Assert.Equal(new DateTimeOffset(2026, 10, 23, 0, 0, 0, TehranOffset), stats.Month.StartLocal);
        Assert.Equal(new HubStatsValues(1, 400), stats.Today.Total);
        Assert.Equal(new HubStatsValues(2, 600), stats.Week.Total);
        Assert.Equal(new HubStatsValues(3, 700), stats.Month.Total);
        Assert.Equal(new HubStatsValues(1, 100), stats.Month.PaymentMethods.CardToCard);
    }

    [Fact]
    public void C33_UnknownPaymentIsNullWithReasonAndStoresAreIsolated()
    {
        using var store = new SqliteHubStore("Data Source=:memory:;Pooling=False");
        var time = new ManualHubTimeProvider { UtcNow = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero) };
        SeedIssued(store, S1, time.UtcNow.AddMinutes(-30), 100, HubStatsService.CashMethod);
        SeedIssued(store, S1, time.UtcNow.AddMinutes(-20), 200, null);
        SeedIssued(store, S1, time.UtcNow.AddMinutes(-10), 300, "UNSUPPORTED_FUTURE_METHOD");
        SeedIssued(store, S2, time.UtcNow.AddMinutes(-5), 900, HubStatsService.CardToCardMethod);
        var service = new HubStatsService(store, time);

        var first = service.Get(S1);
        var second = service.Get(S2);

        Assert.Equal(new HubStatsValues(3, 600), first.Today.Total);
        Assert.Equal(new HubStatsValues(1, 100), first.Today.PaymentMethods.Cash);
        Assert.False(first.Today.PaymentMethods.Complete);
        Assert.Null(first.Today.PaymentMethods.Unknown.InvoiceCount);
        Assert.Null(first.Today.PaymentMethods.Unknown.SalesRials);
        Assert.Equal(HubStatsService.UnknownPaymentReason, first.Today.PaymentMethods.Unknown.Reason);
        Assert.Equal(new HubStatsValues(1, 900), second.Today.Total);
        Assert.Equal(new HubStatsValues(1, 900), second.Today.PaymentMethods.CardToCard);
        Assert.True(second.Today.PaymentMethods.Complete);
        Assert.Equal(new HubStatsUnknownValues(0, 0, null), second.Today.PaymentMethods.Unknown);
    }

    private static void SeedIssued(
        SqliteHubStore store,
        Guid storeId,
        DateTimeOffset issuedAt,
        long totalRials,
        string? paymentMethod)
    {
        HubStatsTestData.SeedIssued(store, storeId, Device, issuedAt, totalRials, paymentMethod);
    }
}

public sealed class HubStatsApiTests : IDisposable
{
    private readonly HubApiFactory _factory = new();
    private readonly HubApiClient _api;

    public HubStatsApiTests()
    {
        _api = new HubApiClient(_factory);
    }

    [Fact]
    public async Task C33_RealStatsRouteAndStoreAuthorizationExposeExplicitRialProjection()
    {
        var storeId = Guid.NewGuid();
        var otherStoreId = Guid.NewGuid();
        var owner = await _api.Owner(storeId, Guid.NewGuid(), Guid.NewGuid());
        var paired = await _api.Pair(owner, storeId, Guid.NewGuid());
        var now = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
        _factory.StatsTime.UtcNow = now;
        HubStatsTestData.SeedIssued(_api.Store, storeId, paired.Session.DeviceId, now.AddMinutes(-10), 12500, HubStatsService.CashMethod);

        var response = await _api.Get($"/v1/stores/{storeId:D}/stats/v0", paired.Session.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("OK", response.Body.GetProperty("status").GetString());
        Assert.Equal("0", response.Body.GetProperty("version").GetString());
        Assert.Equal("Asia/Tehran", response.Body.GetProperty("time_zone").GetString());
        Assert.Equal("JALALI", response.Body.GetProperty("calendar").GetString());
        Assert.Equal("SATURDAY", response.Body.GetProperty("week_start").GetString());
        Assert.Equal("IRR", response.Body.GetProperty("currency").GetProperty("code").GetString());
        Assert.Equal("Iranian Rial", response.Body.GetProperty("currency").GetProperty("unit").GetString());
        Assert.Equal(HubInvoiceCurrency.InvoiceSource, response.Body.GetProperty("currency").GetProperty("invoice_source").GetString());
        Assert.Equal(now, response.Body.GetProperty("generated_at_utc").GetDateTimeOffset());
        var today = response.Body.GetProperty("periods").GetProperty("today");
        Assert.Equal(1, today.GetProperty("invoice_count").GetInt64());
        Assert.Equal(12500, today.GetProperty("sales_rials").GetInt64());
        Assert.Equal(1, today.GetProperty("payment_methods").GetProperty("cash").GetProperty("invoice_count").GetInt64());
        Assert.Equal(12500, today.GetProperty("payment_methods").GetProperty("cash").GetProperty("sales_rials").GetInt64());
        Assert.True(today.GetProperty("payment_methods").GetProperty("complete").GetBoolean());

        var isolated = await _api.Get($"/v1/stores/{otherStoreId:D}/stats/v0", paired.Session.Token);
        Assert.Equal(HttpStatusCode.Forbidden, isolated.StatusCode);
        Assert.Equal("STORE_ACCESS_DENIED", isolated.Body.GetProperty("code").GetString());
    }

    public void Dispose()
    {
        _api.Dispose();
        _factory.Dispose();
    }
}

internal static class HubStatsTestData
{
    public static void SeedIssued(
        SqliteHubStore store,
        Guid storeId,
        Guid deviceId,
        DateTimeOffset issuedAt,
        long totalRials,
        string? paymentMethod)
    {
        var invoiceId = Guid.NewGuid();
        var draftRequest = new OperationRequest(
            storeId,
            deviceId,
            $"c33-draft-{invoiceId:N}",
            "invoice.draft_create",
            null,
            HubJson.Element(new { invoice_id = invoiceId }));
        var item = new InvoiceItem(Guid.NewGuid(), storeId, 1, "synthetic stats item", 1000, totalRials, totalRials);
        var draft = store.CreateDraft(draftRequest, invoiceId, issuedAt.AddMinutes(-1), new[] { item });
        Assert.Equal(OperationExecutionStatus.Succeeded, draft.Status);
        var issuePayload = paymentMethod is null
            ? HubJson.Element(new { invoice_id = invoiceId })
            : HubJson.Element(new { invoice_id = invoiceId, payment_method = paymentMethod });
        var issueRequest = new OperationRequest(
            storeId,
            deviceId,
            $"c33-issue-{invoiceId:N}",
            "invoice.issue",
            null,
            issuePayload);
        var issued = store.IssueInvoice(issueRequest, invoiceId, issuedAt);
        Assert.Equal(OperationExecutionStatus.Succeeded, issued.Status);
    }
}
