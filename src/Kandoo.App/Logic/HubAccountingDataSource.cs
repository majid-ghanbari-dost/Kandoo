using System.Text.Json;

namespace Kandoo.App;

public enum AccountingPeriodKind
{
    Today,
    Week,
    Month
}

public sealed record AccountingMethodSummary(
    long InvoiceCount,
    long AmountRials)
{
    public string AmountDisplay => FormattableString.Invariant($"{AmountRials:N0} ریال");
}

public sealed record AccountingPeriodStats(
    string PeriodKey,
    long InvoiceCount,
    long SalesRials,
    AccountingMethodSummary CardReader,
    AccountingMethodSummary CardToCard,
    AccountingMethodSummary Cash,
    AccountingMethodSummary Unknown)
{
    public long AverageInvoiceRials => InvoiceCount > 0 ? SalesRials / InvoiceCount : 0;
    public string SalesDisplay => FormattableString.Invariant($"{SalesRials:N0} ریال");
    public string InvoiceCountDisplay => FormattableString.Invariant($"{InvoiceCount:N0}");
    public string AverageInvoiceDisplay => FormattableString.Invariant($"{AverageInvoiceRials:N0} ریال");
}

public sealed record AccountingStatsView(
    AccountingPeriodStats Today,
    AccountingPeriodStats Week,
    AccountingPeriodStats Month)
{
    public AccountingPeriodStats ForPeriod(AccountingPeriodKind kind) => kind switch
    {
        AccountingPeriodKind.Week => Week,
        AccountingPeriodKind.Month => Month,
        _ => Today
    };
}

public sealed record AccountingLedgerRow(
    int SequenceNumber,
    string Account,
    long DebitRials,
    long CreditRials,
    bool IsManual = false)
{
    public string DebitDisplay => FormattableString.Invariant($"{DebitRials:N0} ریال");
    public string CreditDisplay => FormattableString.Invariant($"{CreditRials:N0} ریال");
    public string SourceBadgeText => IsManual ? "دستی" : "خودکار";
    public string SourceBadgeHex => IsManual ? "#9A3B0A" : "#256B43";
}

public sealed record AccountingSummaryView(
    DateTimeOffset GeneratedAtUtc,
    AccountingMethodSummary CardReader,
    AccountingMethodSummary CardToCard,
    AccountingMethodSummary Cash,
    AccountingMethodSummary Credit,
    long UnrecordedPaymentInvoices,
    long ReceivableInvoiceCount,
    long ReceivableOutstandingRials,
    long UnrecordedReceivableInvoices,
    long PricedProductsCount,
    long StockValueRials,
    long UnpricedProductsCount,
    long UnpricedStockMillis,
    long ProfitIncludedItems,
    long ProfitSalesRials,
    long ProfitCostRials,
    long ProfitRials,
    long ProfitExcludedItems,
    string ProfessionalNote,
    IReadOnlyList<AccountingLedgerRow> LedgerRows)
{
    public string ProfitDisplay => FormattableString.Invariant($"{ProfitRials:N0} ریال");
    public string ReceivablesDisplay => FormattableString.Invariant($"{ReceivableOutstandingRials:N0} ریال");
    public string StockValueDisplay => FormattableString.Invariant($"{StockValueRials:N0} ریال");
}

public sealed record InventoryGroupValueRow(
    string GroupName,
    int ProductCount,
    long TotalValueRials)
{
    public string ValueDisplay => FormattableString.Invariant($"{TotalValueRials:N0} ریال");
}

/// <summary>
/// Data source for C-94 management accounting views (<c>Accounting.dc.html</c>,
/// <c>Pro-View.dc.html</c>, <c>Receivables.dc.html</c>, <c>Inventory-Value.dc.html</c>,
/// <c>Sales-Report.dc.html</c>) backed by Hub <c>/accounting/summary/v0</c> and <c>/stats/v0</c>.
/// </summary>
public sealed class HubAccountingDataSource
{
    private readonly HttpClient _client;
    private readonly HubConnectionSettings _settings;
    private readonly string _baseUrl;

    public HubAccountingDataSource(HttpClient client, HubConnectionSettings settings)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _baseUrl = HubApiUtilities.ValidatedBaseUrl(settings);
        if (string.IsNullOrWhiteSpace(settings.SessionToken))
        {
            throw new ArgumentException("A Hub session token is required.", nameof(settings));
        }
    }

    public async Task<AccountingSummaryView> LoadSummaryAsync(
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{_baseUrl}/v1/stores/{_settings.StoreId:D}/accounting/summary/v0");
        HubApiUtilities.Authorize(request, _settings);
        using var response = await _client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Hub accounting summary failed with HTTP {(int)response.StatusCode}.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return ParseSummary(doc.RootElement);
    }

    public async Task<AccountingStatsView> LoadStatsAsync(
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{_baseUrl}/v1/stores/{_settings.StoreId:D}/stats/v0");
        HubApiUtilities.Authorize(request, _settings);
        using var response = await _client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Hub stats request failed with HTTP {(int)response.StatusCode}.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return ParseStats(doc.RootElement);
    }

    public static AccountingSummaryView ParseSummary(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Accounting summary root must be a JSON object.");
        }

        var generatedAt = DateTimeOffset.UtcNow;
        if (root.TryGetProperty("generated_at_utc", out var genEl) &&
            genEl.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(genEl.GetString(), out var parsedGen))
        {
            generatedAt = parsedGen;
        }

        var pm = root.GetProperty("payment_methods");
        var cardReader = ParseMethodAmount(pm, "card_reader");
        var cardToCard = ParseMethodAmount(pm, "card_to_card");
        var cash = ParseMethodAmount(pm, "cash");
        var credit = ParseMethodAmount(pm, "credit");
        var unrecordedPayments = OptionalInt64(pm, "unrecorded_invoices") ?? 0;

        var rec = root.GetProperty("receivables");
        var recCount = OptionalInt64(rec, "invoice_count") ?? 0;
        var recOutstanding = OptionalInt64(rec, "outstanding_rials") ?? 0;
        var recUnrecorded = OptionalInt64(rec, "unrecorded_invoices") ?? 0;

        var stock = root.GetProperty("stock_value");
        var pricedProducts = OptionalInt64(stock, "priced_products") ?? 0;
        var stockValue = OptionalInt64(stock, "value_rials") ?? 0;
        var unpricedProducts = OptionalInt64(stock, "unpriced_products") ?? 0;
        var unpricedMillis = OptionalInt64(stock, "unpriced_stock_millis") ?? 0;

        var profit = root.GetProperty("profit");
        var includedItems = OptionalInt64(profit, "included_items") ?? 0;
        var profitSales = OptionalInt64(profit, "sales_rials") ?? 0;
        var profitCost = OptionalInt64(profit, "cost_rials") ?? 0;
        var profitRials = OptionalInt64(profit, "profit_rials") ?? 0;
        var excludedItems = OptionalInt64(profit, "excluded_items") ?? 0;

        var note = "نمای حرفه‌ای: اصطلاحات بدهکار/بستانکار برای مدیریت؛ اطلاعاتی است و ترازنامهٔ رسمی نیست.";
        var ledgerRows = new List<AccountingLedgerRow>();
        if (root.TryGetProperty("professional", out var prof) && prof.ValueKind == JsonValueKind.Object)
        {
            if (prof.TryGetProperty("note", out var noteEl) && noteEl.ValueKind == JsonValueKind.String)
            {
                note = noteEl.GetString() ?? note;
            }

            if (prof.TryGetProperty("ledger", out var ledgerEl) && ledgerEl.ValueKind == JsonValueKind.Array)
            {
                var seq = 0;
                foreach (var entry in ledgerEl.EnumerateArray())
                {
                    if (entry.ValueKind != JsonValueKind.Object)
                        continue;
                    seq++;
                    var account = entry.TryGetProperty("account", out var accEl) && accEl.ValueKind == JsonValueKind.String
                        ? accEl.GetString() ?? $"ردیف {seq}"
                        : $"ردیف {seq}";
                    var debit = OptionalInt64(entry, "debit_rials") ?? 0;
                    var creditVal = OptionalInt64(entry, "credit_rials") ?? 0;
                    ledgerRows.Add(new AccountingLedgerRow(seq, account, debit, creditVal));
                }
            }
        }

        return new AccountingSummaryView(
            generatedAt,
            cardReader,
            cardToCard,
            cash,
            credit,
            unrecordedPayments,
            recCount,
            recOutstanding,
            recUnrecorded,
            pricedProducts,
            stockValue,
            unpricedProducts,
            unpricedMillis,
            includedItems,
            profitSales,
            profitCost,
            profitRials,
            excludedItems,
            note,
            ledgerRows);
    }

    public static AccountingStatsView ParseStats(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("periods", out var periods) ||
            periods.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Hub stats response must contain a periods object.");
        }

        return new AccountingStatsView(
            ParsePeriod("today", periods.GetProperty("today")),
            ParsePeriod("week", periods.GetProperty("week")),
            ParsePeriod("month", periods.GetProperty("month")));
    }

    public static IReadOnlyList<InventoryGroupValueRow> SummarizeStockByGroup(
        IReadOnlyList<ProductRow> products)
    {
        ArgumentNullException.ThrowIfNull(products);
        return products
            .Where(p => p.IsActive && !p.StockUnconfirmed && p.PurchasePriceRials is not null)
            .GroupBy(p => p.GroupDisplay, StringComparer.Ordinal)
            .Select(g =>
            {
                long groupValue = 0;
                foreach (var p in g)
                {
                    var millis = p.StockMillis ?? 0;
                    var cost = p.PurchasePriceRials ?? 0;
                    if (millis > 0 && cost > 0)
                    {
                        groupValue += checked(millis * cost) / 1000;
                    }
                }

                return new InventoryGroupValueRow(g.Key, g.Count(), groupValue);
            })
            .OrderByDescending(r => r.TotalValueRials)
            .ToArray();
    }

    private static AccountingPeriodStats ParsePeriod(string key, JsonElement period)
    {
        var count = OptionalInt64(period, "invoice_count") ?? 0;
        var sales = OptionalInt64(period, "sales_rials") ?? 0;
        var pm = period.GetProperty("payment_methods");
        return new AccountingPeriodStats(
            key,
            count,
            sales,
            ParseMethodSales(pm, "card_reader"),
            ParseMethodSales(pm, "card_to_card"),
            ParseMethodSales(pm, "cash"),
            ParseMethodSales(pm, "unknown"));
    }

    private static AccountingMethodSummary ParseMethodAmount(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var obj) || obj.ValueKind != JsonValueKind.Object)
        {
            return new AccountingMethodSummary(0, 0);
        }

        return new AccountingMethodSummary(
            OptionalInt64(obj, "invoice_count") ?? 0,
            OptionalInt64(obj, "amount_rials") ?? 0);
    }

    private static AccountingMethodSummary ParseMethodSales(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var obj) || obj.ValueKind != JsonValueKind.Object)
        {
            return new AccountingMethodSummary(0, 0);
        }

        return new AccountingMethodSummary(
            OptionalInt64(obj, "invoice_count") ?? 0,
            OptionalInt64(obj, "sales_rials") ?? 0);
    }

    private static long? OptionalInt64(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var val) || val.ValueKind != JsonValueKind.Number)
            return null;
        return val.TryGetInt64(out var n) ? n : null;
    }
}
