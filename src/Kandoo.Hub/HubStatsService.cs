using System.Globalization;
using System.Text.Json;
using Kandoo.Core;

namespace Kandoo.Hub;

public sealed record HubStatsValues(long InvoiceCount, long SalesRials);

public sealed record HubStatsUnknownValues(long? InvoiceCount, long? SalesRials, string? Reason);

public sealed record HubStatsPaymentMethods(
    bool Complete,
    HubStatsValues CardReader,
    HubStatsValues CardToCard,
    HubStatsValues Cash,
    HubStatsUnknownValues Unknown);

public sealed record HubStatsPeriod(
    DateTimeOffset StartLocal,
    DateTimeOffset EndExclusiveLocal,
    HubStatsValues Total,
    HubStatsPaymentMethods PaymentMethods);

public sealed record HubStatsSnapshot(
    string Version,
    Guid StoreId,
    DateTimeOffset GeneratedAtUtc,
    string TimeZone,
    string Calendar,
    DayOfWeek WeekStart,
    int JalaliYear,
    int JalaliMonth,
    int JalaliDay,
    HubStatsPeriod Today,
    HubStatsPeriod Week,
    HubStatsPeriod Month);

/// <summary>Read-only v0 projection of successful immutable invoice.issue events.</summary>
public sealed class HubStatsService
{
    public const string CardReaderMethod = "CARD_READER";
    public const string CardToCardMethod = "CARD_TO_CARD";
    public const string CashMethod = "CASH";
    public const string UnknownPaymentReason = "PAYMENT_METHOD_NOT_RECORDED_OR_UNSUPPORTED";

    private static readonly TimeZoneInfo TehranZone = ResolveTehranZone();
    private readonly IHubStore _store;
    private readonly TimeProvider _time;

    public HubStatsService(IHubStore store, TimeProvider timeProvider)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _time = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public HubStatsSnapshot Get(Guid storeId)
    {
        if (storeId == Guid.Empty)
        {
            throw new ArgumentException("A store is required.", nameof(storeId));
        }

        var nowUtc = _time.GetUtcNow().ToUniversalTime();
        var nowLocal = TimeZoneInfo.ConvertTime(nowUtc, TehranZone);
        var todayStart = LocalMidnight(nowLocal);
        var daysFromSaturday = ((int)nowLocal.DayOfWeek - (int)DayOfWeek.Saturday + 7) % 7;
        var weekStart = todayStart.AddDays(-daysFromSaturday);
        var jalali = new PersianCalendar();
        var year = jalali.GetYear(nowLocal.DateTime);
        var month = jalali.GetMonth(nowLocal.DateTime);
        var day = jalali.GetDayOfMonth(nowLocal.DateTime);
        var monthStart = todayStart.AddDays(-(day - 1));
        var monthEnd = monthStart.AddDays(jalali.GetDaysInMonth(year, month));
        var events = ReadIssuedEvents(storeId);

        return new HubStatsSnapshot(
            "0",
            storeId,
            nowUtc,
            "Asia/Tehran",
            "JALALI",
            DayOfWeek.Saturday,
            year,
            month,
            day,
            CreatePeriod(todayStart, todayStart.AddDays(1), events),
            CreatePeriod(weekStart, weekStart.AddDays(7), events),
            CreatePeriod(monthStart, monthEnd, events));
    }

    private IReadOnlyList<HubStatsEvent> ReadIssuedEvents(Guid storeId)
    {
        var events = new List<HubStatsEvent>();
        foreach (var entry in _store.ReadReplicationEntries(storeId))
        {
            if (entry.StoreId != storeId)
            {
                throw new InvalidOperationException("STATS_STORE_BOUNDARY_INVALID");
            }

            if (!string.Equals(entry.OperationType, "invoice.issue", StringComparison.Ordinal))
            {
                continue;
            }

            var snapshot = entry.ResponseSnapshot;
            if (snapshot.ValueKind != JsonValueKind.Object ||
                !snapshot.TryGetProperty("status", out var statusElement) ||
                statusElement.ValueKind != JsonValueKind.String)
            {
                throw new InvalidOperationException("STATS_EVENT_STATUS_INVALID");
            }

            if (!string.Equals(statusElement.GetString(), "SUCCEEDED", StringComparison.Ordinal))
            {
                continue;
            }

            if (!snapshot.TryGetProperty("response", out var response) || response.ValueKind != JsonValueKind.Object ||
                !TryReadInvoiceId(response, out var invoiceId))
            {
                throw new InvalidOperationException("STATS_ISSUE_EVENT_INVALID");
            }

            var invoice = _store.GetInvoice(storeId, invoiceId)
                ?? throw new InvalidOperationException("STATS_ISSUE_INVOICE_MISSING");
            if (invoice.IssuedAt is not DateTimeOffset issuedAt)
            {
                throw new InvalidOperationException("STATS_ISSUE_TIME_MISSING");
            }

            var total = invoice.Items.Aggregate(0L, (sum, item) => checked(sum + item.LineTotalRials));
            events.Add(new HubStatsEvent(issuedAt, total, ReadPaymentMethod(entry.Payload)));
        }

        return events;
    }

    private static bool TryReadInvoiceId(JsonElement response, out Guid invoiceId)
    {
        if (response.TryGetProperty("invoice", out var invoice) && invoice.ValueKind == JsonValueKind.Object &&
            invoice.TryGetProperty("invoice_id", out var nested) && nested.ValueKind == JsonValueKind.String)
        {
            return Guid.TryParse(nested.GetString(), out invoiceId) && invoiceId != Guid.Empty;
        }

        if (response.TryGetProperty("invoice_id", out var direct) && direct.ValueKind == JsonValueKind.String)
        {
            return Guid.TryParse(direct.GetString(), out invoiceId) && invoiceId != Guid.Empty;
        }

        invoiceId = Guid.Empty;
        return false;
    }

    private static string? ReadPaymentMethod(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("payment_method", out var method) ||
            method.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var value = method.GetString();
        return value is CardReaderMethod or CardToCardMethod or CashMethod ? value : null;
    }

    private static HubStatsPeriod CreatePeriod(
        DateTimeOffset start,
        DateTimeOffset endExclusive,
        IReadOnlyList<HubStatsEvent> events)
    {
        var included = events.Where(value => value.IssuedAt >= start && value.IssuedAt < endExclusive).ToArray();
        var hasUnknown = included.Any(value => value.PaymentMethod is null);
        var unknown = hasUnknown
            ? new HubStatsUnknownValues(null, null, UnknownPaymentReason)
            : new HubStatsUnknownValues(0, 0, null);
        return new HubStatsPeriod(
            start,
            endExclusive,
            Values(included),
            new HubStatsPaymentMethods(
                !hasUnknown,
                Values(included, CardReaderMethod),
                Values(included, CardToCardMethod),
                Values(included, CashMethod),
                unknown));
    }

    private static HubStatsValues Values(IEnumerable<HubStatsEvent> events, string? paymentMethod = null)
    {
        var selected = events.Where(value => paymentMethod is null || value.PaymentMethod == paymentMethod).ToArray();
        return new HubStatsValues(
            selected.LongLength,
            selected.Aggregate(0L, (sum, value) => checked(sum + value.SalesRials)));
    }

    private static DateTimeOffset LocalMidnight(DateTimeOffset value) => new(
        value.Year, value.Month, value.Day, 0, 0, 0, value.Offset);

    private static TimeZoneInfo ResolveTehranZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Iran Standard Time");
        }
    }

    private sealed record HubStatsEvent(DateTimeOffset IssuedAt, long SalesRials, string? PaymentMethod);
}
