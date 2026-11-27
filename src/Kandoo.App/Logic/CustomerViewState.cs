using System.Globalization;
using Kandoo.Core;

namespace Kandoo.App;

/// <summary>
/// Pure view-state for the C-97 customer boards — no MAUI types, so the screen
/// logic is fully testable without an emulator:
/// - Customer-Invoices-v2: grouping per store, search, badges (پرداخت‌شده / مانده دارد / جدید),
///   the «فاکتور جدید از [فروشگاه]» banner (Customer-New-Banner), amounts with the recorded currency unit.
/// - Customer-Empty / Customer-Locked states.
/// - Customer-Invoice-Detail: recorded values only; unrecorded fields become explicit
///   data gaps (same honesty policy as the PHP customer web page — never invented,
///   remaining_balance shown as recorded, never computed).
/// </summary>
public enum CustomerBadgeKind
{
    None,
    New,
    Paid,
    BalanceDue,
    Revoked,
    Superseded,
}

public sealed record CustomerInvoiceCard(
    string StoreId,
    string StoreName,
    string InvoiceId,
    long DisplayNumber,
    CustomerBadgeKind Badge,
    string? BadgeText,
    string? DateText,
    string? AmountText,
    bool IsNew);

public sealed record CustomerStoreSection(
    string StoreId,
    string StoreName,
    IReadOnlyList<CustomerInvoiceCard> Invoices);

public sealed record CustomerInvoicesViewState(
    IReadOnlyList<CustomerStoreSection> Stores,
    CustomerInvoiceCard? NewInvoiceBanner,
    bool IsEmpty);

public static class CustomerInvoicesViewBuilder
{
    public const string NewBadgeText = "جدید";
    public const string PaidBadgeText = "پرداخت‌شده";
    public const string BalanceDueBadgeText = "مانده دارد";
    public const string RevokedBadgeText = "باطل شده";
    public const string SupersededBadgeText = "جایگزین شده";

    public static CustomerInvoicesViewState Build(
        CustomerInvoicesResponse? response,
        string? searchQuery,
        IReadOnlySet<string> seenInvoiceIds)
    {
        ArgumentNullException.ThrowIfNull(seenInvoiceIds);
        var sections = new List<CustomerStoreSection>();
        CustomerInvoiceCard? banner = null;

        foreach (var store in response?.Stores ?? new List<CustomerStoreGroup>())
        {
            var cards = new List<CustomerInvoiceCard>();
            foreach (var invoice in store.Invoices)
            {
                var card = ToCard(store, invoice, seenInvoiceIds);
                if (!Matches(card, searchQuery))
                {
                    continue;
                }

                cards.Add(card);
                // The banner shows the newest invoice not yet opened on this device.
                // The API lists newest first (issued_at DESC), so the first unseen wins.
                if (banner is null && card.IsNew)
                {
                    banner = card;
                }
            }

            if (cards.Count > 0)
            {
                sections.Add(new CustomerStoreSection(store.StoreId, store.StoreName, cards));
            }
        }

        // The banner belongs to the unfiltered list only (it is the «new invoice» alert).
        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            banner = null;
        }

        return new CustomerInvoicesViewState(sections, banner, sections.Count == 0);
    }

    /// <summary>«فاکتور ۱۴۰۲۵» — board shows display numbers ungrouped (unlike amounts).</summary>
    public static string CardTitle(long displayNumber) =>
        $"فاکتور {PersianDigits.ToPersian(displayNumber.ToString(CultureInfo.InvariantCulture))}";

    private static CustomerInvoiceCard ToCard(
        CustomerStoreGroup store,
        CustomerInvoiceSummary invoice,
        IReadOnlySet<string> seenInvoiceIds)
    {
        var isNew = !seenInvoiceIds.Contains(invoice.InvoiceId);
        var (badge, badgeText) = BadgeFor(invoice, isNew);
        return new CustomerInvoiceCard(
            store.StoreId,
            store.StoreName,
            invoice.InvoiceId,
            invoice.DisplayNumber,
            badge,
            badgeText,
            CustomerDates.FormatCardDate(invoice.IssuedAt),
            invoice.PayableRials is null
                ? null
                : PersianDigits.FormatAmount(invoice.PayableRials.Value, invoice.CurrencyUnit),
            isNew);
    }

    /// <summary>
    /// Badge mapping (board texts fixed; payment badges require recorded payment data):
    /// unseen → «جدید»; recorded remaining_balance &gt; 0 → «مانده دارد»;
    /// recorded remaining_balance == 0 → «پرداخت‌شده»; lifecycle REVOKED/SUPERSEDED → their labels.
    /// Seen invoices without recorded payment data show no badge — never invented.
    /// </summary>
    private static (CustomerBadgeKind Badge, string? Text) BadgeFor(CustomerInvoiceSummary invoice, bool isNew)
    {
        if (isNew)
        {
            return (CustomerBadgeKind.New, NewBadgeText);
        }

        if (invoice.RemainingBalance is > 0)
        {
            return (CustomerBadgeKind.BalanceDue, BalanceDueBadgeText);
        }

        if (invoice.RemainingBalance == 0)
        {
            return (CustomerBadgeKind.Paid, PaidBadgeText);
        }

        return invoice.LifecycleStatus switch
        {
            "REVOKED" => (CustomerBadgeKind.Revoked, RevokedBadgeText),
            "SUPERSEDED" => (CustomerBadgeKind.Superseded, SupersededBadgeText),
            _ => (CustomerBadgeKind.None, null),
        };
    }

    private static bool Matches(CustomerInvoiceCard card, string? searchQuery)
    {
        if (string.IsNullOrWhiteSpace(searchQuery))
        {
            return true;
        }

        var query = searchQuery.Trim();
        return card.StoreName.Contains(query, StringComparison.OrdinalIgnoreCase)
            || CardTitle(card.DisplayNumber).Contains(query, StringComparison.OrdinalIgnoreCase)
            || PersianDigits.ToPersian(card.DisplayNumber.ToString(CultureInfo.InvariantCulture))
                .Contains(query, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>Jalali date rendering for the customer screens (board formats).</summary>
public static class CustomerDates
{
    private static readonly string[] MonthNames =
    {
        "", "فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور",
        "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند",
    };

    /// <summary>«۲۰ آبان ۱۴۰۵» — Customer-Invoices-v2 card format; null when not recorded.</summary>
    public static string? FormatCardDate(string? issuedAtIso)
    {
        if (!TryParse(issuedAtIso, out var value))
        {
            return null;
        }

        var date = JalaliDateConverter.FromTehran(value);
        return $"{PersianDigits.ToPersian(date.Day.ToString(CultureInfo.InvariantCulture))} " +
               $"{MonthNames[date.Month]} {PersianDigits.ToPersian(date.Year.ToString(CultureInfo.InvariantCulture))}";
    }

    /// <summary>«۱۴۰۵/۰۸/۲۰ — ۱۴:۳۵» — Customer-Invoice-v2 (web) date+time format; null when not recorded.</summary>
    public static string? FormatDetailDateTime(string? issuedAtIso)
    {
        if (!TryParse(issuedAtIso, out var value))
        {
            return null;
        }

        var tehran = JalaliDateConverter.ToTehran(value);
        var date = JalaliDateConverter.FromTehran(value);
        var year = PersianDigits.ToPersian(date.Year.ToString(CultureInfo.InvariantCulture));
        var month = PersianDigits.ToPersian(date.Month.ToString("00", CultureInfo.InvariantCulture));
        var day = PersianDigits.ToPersian(date.Day.ToString("00", CultureInfo.InvariantCulture));
        var hour = PersianDigits.ToPersian(tehran.Hour.ToString("00", CultureInfo.InvariantCulture));
        var minute = PersianDigits.ToPersian(tehran.Minute.ToString("00", CultureInfo.InvariantCulture));
        return $"{year}/{month}/{day} — {hour}:{minute}";
    }

    private static bool TryParse(string? issuedAtIso, out DateTimeOffset value)
    {
        if (!string.IsNullOrWhiteSpace(issuedAtIso) &&
            DateTimeOffset.TryParse(
                issuedAtIso,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out value))
        {
            return true;
        }

        value = default;
        return false;
    }
}

public sealed record CustomerLineItemRow(string Title, string AmountText);

public sealed record CustomerPaymentRow(string MethodText, string AmountText);

public sealed record CustomerInvoiceDetailViewState(
    string HeaderTitle,
    string StoreName,
    string DateText,
    string? BuyerText,
    IReadOnlyList<CustomerLineItemRow> Items,
    string PayableText,
    IReadOnlyList<CustomerPaymentRow> Payments,
    string? BalanceText,
    IReadOnlyList<string> DataGaps,
    bool HasDataGaps);

public static class CustomerInvoiceDetailViewBuilder
{
    public const string ProductSubtitle = "فاکتور دیجیتال کندو";
    public const string DateRowLabel = "تاریخ";
    public const string BuyerRowLabel = "خریدار";
    public const string ItemsLabel = "اقلام";
    public const string PayableLabel = "قابل پرداخت";
    public const string PaymentRowLabel = "پرداخت";
    public const string BalanceRowLabel = "مانده";
    public const string RecordedBalanceLabel = "مانده باقی‌مانده (ثبت‌شده):";

    // Gap copy mirrors the PHP customer web page verbatim (truthfulness policy v1.0/U14).
    public const string GapSectionTitle = "محدودیت‌های داده صدور";
    public const string GapCustomerName = "در داده صدور این فاکتور، فیلد جداگانه‌ای برای customer_name (نام مشتری) ثبت نشده است.";
    public const string GapPayments = "روش پرداخت (payment_method) به‌صورت فیلد جداگانه در داده صدور این فاکتور موجود نیست.";
    public const string GapClosing = "این صفحه فقط همان داده صدور را نشان می‌دهد؛ هیچ مقدار تازه‌ای محاسبه یا جایگزین نمی‌شود.";

    public static CustomerInvoiceDetailViewState Build(CustomerInvoiceDetail detail)
    {
        ArgumentNullException.ThrowIfNull(detail);
        var gaps = new List<string>();

        var buyerText = string.IsNullOrWhiteSpace(detail.CustomerName) ? null : detail.CustomerName;
        if (buyerText is null)
        {
            gaps.Add(GapCustomerName);
        }

        var items = new List<CustomerLineItemRow>();
        foreach (var item in detail.LineItems)
        {
            var name = string.IsNullOrWhiteSpace(item.ProductName) ? "کالا" : item.ProductName;
            var quantity = item.QuantityMillis is null ? null : FormatQuantity(item.QuantityMillis.Value);
            var title = quantity is null ? name : $"{name} × {quantity}";
            var amount = item.LineTotalRials is null
                ? null
                : PersianDigits.Format(item.LineTotalRials.Value);
            items.Add(new CustomerLineItemRow(title, amount ?? string.Empty));
        }

        var payments = new List<CustomerPaymentRow>();
        if (detail.Payments is { Count: > 0 })
        {
            foreach (var payment in detail.Payments)
            {
                payments.Add(new CustomerPaymentRow(
                    PaymentLabel(payment.Type),
                    payment.Amount is null
                        ? string.Empty
                        : PersianDigits.FormatAmount(payment.Amount.Value, detail.CurrencyUnit)));
            }
        }
        else
        {
            gaps.Add(GapPayments);
        }

        return new CustomerInvoiceDetailViewState(
            HeaderTitle: CustomerInvoicesViewBuilder.CardTitle(detail.DisplayNumber),
            StoreName: string.IsNullOrWhiteSpace(detail.StoreName) ? detail.StoreId : detail.StoreName,
            DateText: CustomerDates.FormatDetailDateTime(detail.IssuedAt) ?? "—",
            BuyerText: buyerText,
            Items: items,
            PayableText: PersianDigits.FormatAmount(detail.PayableRials, detail.CurrencyUnit),
            Payments: payments,
            BalanceText: detail.RemainingBalance is null
                ? null
                : PersianDigits.FormatAmount(detail.RemainingBalance.Value, detail.CurrencyUnit),
            DataGaps: gaps,
            HasDataGaps: gaps.Count > 0);
    }

    public static string PaymentLabel(string? type) => type switch
    {
        // Board/spec wording (Customer-Invoice-Detail «[کارت‌خوان / نقدی / نسیه]»,
        // product-ux-spec §5 «کارت‌خوان، کارت‌به‌کارت، نقدی (و نسیه با «مانده»)»).
        "card-reader" => "کارت‌خوان",
        "card-to-card" => "کارت‌به‌کارت",
        "cash" => "نقدی",
        "credit" => "نسیه",
        _ => type ?? string.Empty,
    };

    /// <summary>quantity_millis → «۲» / «۱٫۵» (the API records thousandths of a unit).</summary>
    public static string FormatQuantity(long quantityMillis)
    {
        if (quantityMillis % 1000 == 0)
        {
            return PersianDigits.Format(quantityMillis / 1000);
        }

        var whole = quantityMillis / 1000;
        var fraction = Math.Abs(quantityMillis % 1000).ToString("000", CultureInfo.InvariantCulture).TrimEnd('0');
        return $"{PersianDigits.Format(whole)}\u066B{PersianDigits.ToPersian(fraction)}"; // ٫ decimal separator
    }
}
