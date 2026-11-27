using Xunit;

namespace Kandoo.App.Tests;

/// <summary>
/// C-97 customer screen view-state tests (the logic behind Customer-Invoices-v2,
/// Customer-Empty, Customer-New-Banner, Customer-Invoice-Detail). Synthetic data only.
/// </summary>
public sealed class CustomerViewStateTests
{
    private static CustomerInvoicesResponse Fixture() => new()
    {
        Stores =
        {
            new CustomerStoreGroup
            {
                StoreId = "11111111-1111-4111-8111-111111111111",
                StoreName = "فروشگاه لوازم جشن نمونه",
                Invoices =
                {
                    new CustomerInvoiceSummary
                    {
                        InvoiceId = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
                        DisplayNumber = 14025,
                        IssuedAt = "2026-11-20T08:05:00Z",
                        PayableRials = 120000,
                        CurrencyUnit = "toman",
                    },
                    new CustomerInvoiceSummary
                    {
                        InvoiceId = "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb",
                        DisplayNumber = 14026,
                        IssuedAt = "2026-11-19T08:05:00Z",
                        PayableRials = 285000,
                        CurrencyUnit = "toman",
                    },
                },
            },
        },
    };

    [Fact]
    public void C97_Groups_Per_Store_And_Marks_New_Invoices()
    {
        var state = CustomerInvoicesViewBuilder.Build(Fixture(), searchQuery: null, new HashSet<string>());

        Assert.False(state.IsEmpty);
        var section = Assert.Single(state.Stores);
        Assert.Equal("فروشگاه لوازم جشن نمونه", section.StoreName);
        Assert.Equal(2, section.Invoices.Count);

        var first = section.Invoices[0];
        Assert.Equal("فاکتور ۱۴۰۲۵", CustomerInvoicesViewBuilder.CardTitle(first.DisplayNumber));
        Assert.True(first.IsNew);
        Assert.Equal(CustomerBadgeKind.New, first.Badge);
        Assert.Equal("جدید", first.BadgeText);
        Assert.Equal("۱۲۰٬۰۰۰ تومان", first.AmountText);
        Assert.Equal(ExpectedCardDate("2026-11-20T08:05:00Z"), first.DateText);
    }

    /// <summary>
    /// Independent expectation for the board card format «۲۰ آبان ۱۴۰۵»:
    /// day + month name + year in Persian digits (month names listed here so a
    /// transposed name table in the production code cannot pass unnoticed).
    /// </summary>
    private static string ExpectedCardDate(string iso)
    {
        var date = Kandoo.Core.JalaliDateConverter.FromTehran(DateTimeOffset.Parse(iso));
        var names = new[]
        {
            "", "فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور",
            "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند",
        };
        return $"{PersianDigits.ToPersian(date.Day.ToString())} {names[date.Month]} {PersianDigits.ToPersian(date.Year.ToString())}";
    }

    [Fact]
    public void C97_NewInvoiceBanner_Shows_First_Unseen_Invoice_Only()
    {
        var state = CustomerInvoicesViewBuilder.Build(Fixture(), searchQuery: null, new HashSet<string>());
        Assert.NotNull(state.NewInvoiceBanner);
        Assert.Equal("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa", state.NewInvoiceBanner!.InvoiceId);

        // With everything seen there is no banner (Customer-New-Banner state only for new).
        var seen = new HashSet<string> { "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa", "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb" };
        var without = CustomerInvoicesViewBuilder.Build(Fixture(), searchQuery: null, seen);
        Assert.Null(without.NewInvoiceBanner);

        // The banner never appears while searching.
        var searching = CustomerInvoicesViewBuilder.Build(Fixture(), "فاکتور ۱۴۰۲۵", new HashSet<string>());
        Assert.Null(searching.NewInvoiceBanner);
    }

    [Fact]
    public void C97_Empty_State_When_No_Invoices()
    {
        var empty = CustomerInvoicesViewBuilder.Build(new CustomerInvoicesResponse(), null, new HashSet<string>());
        Assert.True(empty.IsEmpty);
        Assert.Empty(empty.Stores);
        Assert.Null(empty.NewInvoiceBanner);
    }

    [Fact]
    public void C97_Search_Filters_By_Store_And_Title()
    {
        var state = CustomerInvoicesViewBuilder.Build(Fixture(), "جشن", new HashSet<string>());
        Assert.Single(state.Stores);

        var byTitle = CustomerInvoicesViewBuilder.Build(Fixture(), "۱۴۰۲۶", new HashSet<string>());
        var section = Assert.Single(byTitle.Stores);
        Assert.Equal("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb", Assert.Single(section.Invoices).InvoiceId);

        var noHit = CustomerInvoicesViewBuilder.Build(Fixture(), "چیزی دیگر", new HashSet<string>());
        Assert.True(noHit.IsEmpty);
    }

    [Fact]
    public void C97_Badges_Use_Recorded_Payment_Data_Only()
    {
        var response = Fixture();
        response.Stores[0].Invoices[1].RemainingBalance = 35000; // recorded مانده
        response.Stores[0].Invoices[0].RemainingBalance = 0; // recorded تسویه
        var seen = new HashSet<string> { "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa", "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb" };

        var state = CustomerInvoicesViewBuilder.Build(response, null, seen);
        var cards = state.Stores[0].Invoices;
        Assert.Equal(CustomerBadgeKind.Paid, cards[0].Badge);
        Assert.Equal("پرداخت‌شده", cards[0].BadgeText);
        Assert.Equal(CustomerBadgeKind.BalanceDue, cards[1].Badge);
        Assert.Equal("مانده دارد", cards[1].BadgeText);

        // Seen + no recorded payment data → no invented badge.
        var plain = CustomerInvoicesViewBuilder.Build(Fixture(), null, seen);
        Assert.Equal(CustomerBadgeKind.None, plain.Stores[0].Invoices[0].Badge);
        Assert.Null(plain.Stores[0].Invoices[0].BadgeText);

        // Revoked lifecycle keeps its honest label.
        var revoked = Fixture();
        revoked.Stores[0].Invoices[0].LifecycleStatus = "REVOKED";
        var revokedState = CustomerInvoicesViewBuilder.Build(revoked, null, seen);
        Assert.Equal("باطل شده", revokedState.Stores[0].Invoices[0].BadgeText);
    }

    [Fact]
    public void C97_Detail_Renders_Recorded_Values_And_Explicit_Data_Gaps()
    {
        var detail = new CustomerInvoiceDetail
        {
            StoreId = "11111111-1111-4111-8111-111111111111",
            StoreName = "فروشگاه لوازم جشن نمونه",
            InvoiceId = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
            DisplayNumber = 14025,
            PayableRials = 285000,
            CurrencyUnit = "toman",
            IssuedAt = "2026-11-20T11:05:00Z",
            LineItems =
            {
                new CustomerLineItem { ProductName = "بادکنک فویلی", QuantityMillis = 2000, LineTotalRials = 90000 },
            },
            Payments = new List<CustomerPaymentRecord>
            {
                new CustomerPaymentRecord { Type = "card-reader", Amount = 200000 },
                new CustomerPaymentRecord { Type = "cash", Amount = 50000 },
            },
            RemainingBalance = 35000,
        };

        var state = CustomerInvoiceDetailViewBuilder.Build(detail);
        Assert.Equal("فاکتور ۱۴۰۲۵", state.HeaderTitle);
        Assert.Equal("فروشگاه لوازم جشن نمونه", state.StoreName);
        Assert.Equal("۲۸۵٬۰۰۰ تومان", state.PayableText);
        Assert.Equal("۳۵٬۰۰۰ تومان", state.BalanceText);
        Assert.False(state.HasDataGaps);

        var item = Assert.Single(state.Items);
        Assert.Equal("بادکنک فویلی × ۲", item.Title);
        Assert.Equal("۹۰٬۰۰۰", item.AmountText);

        Assert.Equal(2, state.Payments.Count);
        Assert.Equal("کارت‌خوان", state.Payments[0].MethodText);
        Assert.Equal("نقدی", state.Payments[1].MethodText);
    }

    [Fact]
    public void C97_Detail_Shows_Data_Gaps_Instead_Of_Inventing_Values()
    {
        var detail = new CustomerInvoiceDetail
        {
            StoreId = "s",
            InvoiceId = "i",
            DisplayNumber = 7,
            PayableRials = 25000,
            LineItems = { },
        };

        var state = CustomerInvoiceDetailViewBuilder.Build(detail);
        Assert.Null(state.BuyerText);
        Assert.True(state.HasDataGaps);
        Assert.Contains(CustomerInvoiceDetailViewBuilder.GapCustomerName, state.DataGaps);
        Assert.Contains(CustomerInvoiceDetailViewBuilder.GapPayments, state.DataGaps);
        // Remaining balance is displayed as recorded, never computed.
        Assert.Null(state.BalanceText);
        // Missing optional store name falls back to the id — never invented.
        Assert.Equal("s", state.StoreName);
    }

    [Fact]
    public void C97_Detail_DateTime_And_Quantity_Formats()
    {
        // 2026-11-20 11:05 UTC = 14:35 Tehran → «۱۴۰۵/۰۸/۲۹ — ۱۴:۳۵».
        var formatted = CustomerDates.FormatDetailDateTime("2026-11-20T11:05:00Z");
        Assert.NotNull(formatted);
        Assert.Contains("— ۱۴:۳۵", formatted);
        Assert.StartsWith("۱۴۰۵/", formatted);

        Assert.Null(CustomerDates.FormatCardDate(null));
        Assert.Null(CustomerDates.FormatDetailDateTime("not-a-date"));

        Assert.Equal("۲", CustomerInvoiceDetailViewBuilder.FormatQuantity(2000));
        Assert.Equal("۱\u066B۵", CustomerInvoiceDetailViewBuilder.FormatQuantity(1500));
    }
}
