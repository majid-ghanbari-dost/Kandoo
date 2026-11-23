using System.Text.Json;
using Kandoo.Core;

namespace Kandoo.Hub;

public sealed record AccountingMethodValues(long InvoiceCount, long AmountRials);

public sealed record AccountingPaymentMethods(
    AccountingMethodValues CardReader,
    AccountingMethodValues CardToCard,
    AccountingMethodValues Cash,
    AccountingMethodValues Credit,
    long UnrecordedInvoices,
    string? UnrecordedReason);

public sealed record AccountingReceivables(
    long InvoiceCount,
    long OutstandingRials,
    long UnrecordedInvoices,
    string? UnrecordedReason);

public sealed record AccountingStockValue(
    long PricedProducts,
    long StockValueRials,
    long UnpricedProducts,
    long UnpricedStockMillis,
    string? UnpricedReason);

public sealed record AccountingProfit(
    long IncludedItems,
    long SalesRials,
    long CostRials,
    long ProfitRials,
    long ExcludedItems,
    string? ExcludedReason);

public sealed record AccountingLedgerEntry(string Account, long DebitRials, long CreditRials);

public sealed record ManagementAccountingSnapshot(
    string Version,
    Guid StoreId,
    DateTimeOffset GeneratedAtUtc,
    AccountingPaymentMethods PaymentMethods,
    AccountingReceivables Receivables,
    AccountingStockValue StockValue,
    AccountingProfit Profit,
    IReadOnlyList<AccountingLedgerEntry> ProfessionalLedger);

/// <summary>
/// C-94: light, read-only management accounting derived only from successful
/// immutable invoice.issue events plus the product catalog and the append-only
/// inventory ledger. Sales by payment method, receivables, stock value, and
/// profit for products that carry a purchase price. Nothing is guessed: issue
/// events without recorded payments or without a recorded remaining balance are
/// counted in explicit "unrecorded" buckets, and invoice items without a linked
/// product or without a purchase price are excluded from profit instead of being
/// assumed. Revoked and superseded invoices are excluded entirely.
/// </summary>
public sealed class ManagementAccountingService
{
    public const string CardReaderMethod = "CARD_READER";
    public const string CardToCardMethod = "CARD_TO_CARD";
    public const string CashMethod = "CASH";
    public const string CreditMethod = "CREDIT";
    public const string UnrecordedPaymentReason = "PAYMENTS_NOT_RECORDED_ON_ISSUE_EVENT";
    public const string UnrecordedRemainingReason = "REMAINING_BALANCE_NOT_RECORDED_ON_ISSUE_EVENT";
    public const string UnpricedStockReason = "PURCHASE_PRICE_NOT_RECORDED";
    public const string ExcludedItemReason = "PRODUCT_OR_PURCHASE_PRICE_NOT_LINKED";
    public const string ProfessionalNote =
        "نمای حرفه‌ای: اصطلاحات بدهکار/بستانکار برای مدیریت؛ اطلاعاتی است و ترازنامهٔ رسمی نیست.";

    private readonly IHubStore _store;
    private readonly SqliteHubStore _sqliteStore;
    private readonly TimeProvider _time;

    public ManagementAccountingService(IHubStore store, SqliteHubStore sqliteStore, TimeProvider timeProvider)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _sqliteStore = sqliteStore ?? throw new ArgumentNullException(nameof(sqliteStore));
        _time = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public ManagementAccountingSnapshot Get(Guid storeId)
    {
        if (storeId == Guid.Empty)
        {
            throw new ArgumentException("A store is required.", nameof(storeId));
        }

        var products = _sqliteStore.ListProducts(storeId)
            .Where(product => product.IsActive)
            .GroupBy(product => product.ProductId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        long cardReaderAmount = 0, cardToCardAmount = 0, cashAmount = 0, creditAmount = 0;
        long cardReaderInvoices = 0, cardToCardInvoices = 0, cashInvoices = 0, creditInvoices = 0;
        long unrecordedPaymentInvoices = 0;
        long receivableInvoices = 0, outstandingRials = 0, unrecordedRemainingInvoices = 0;
        long includedItems = 0, profitSales = 0, profitCost = 0, excludedItems = 0;
        long totalSalesRials = 0, recordedPaymentsRials = 0;
        long pricedProductsStockValue = 0, pricedProductsCount = 0, unpricedProductsCount = 0, unpricedStockMillis = 0;

        var inventory = _sqliteStore.LoadInventoryLedger();
        foreach (var product in products.Values)
        {
            var stockMillis = inventory.StockMillis(storeId, product.ProductId);
            if (product.PurchasePriceRials is long purchasePrice)
            {
                pricedProductsCount++;
                pricedProductsStockValue += checked(stockMillis * purchasePrice) / 1000;
            }
            else
            {
                unpricedProductsCount++;
                unpricedStockMillis = checked(unpricedStockMillis + stockMillis);
            }
        }

        // C-94: snapshot raw JSON strings immediately after deserialization so they remain
        // valid even if the deserialization JsonDocument is GC'd by subsequent SQLite reads.
        var entryData = _store.ReadReplicationEntries(storeId).Select(entry => new
        {
            Entry = entry,
            PayloadJson = entry.Payload.ValueKind != JsonValueKind.Undefined ? entry.Payload.GetRawText() : null,
            SnapshotJson = entry.ResponseSnapshot.ValueKind != JsonValueKind.Undefined ? entry.ResponseSnapshot.GetRawText() : null
        }).ToList();

        foreach (var data in entryData)
        {
            var entry = data.Entry;
            if (entry.StoreId != storeId)
            {
                throw new InvalidOperationException("ACCOUNTING_STORE_BOUNDARY_INVALID");
            }

            if (!string.Equals(entry.OperationType, "invoice.issue", StringComparison.Ordinal))
            {
                continue;
            }

            JsonElement snapshot = default;
            if (!string.IsNullOrEmpty(data.SnapshotJson))
            {
                snapshot = JsonSerializer.Deserialize<JsonElement>(data.SnapshotJson, HubJson.Options);
            }

            if (snapshot.ValueKind != JsonValueKind.Object ||
                !snapshot.TryGetProperty("status", out var statusElement) ||
                statusElement.ValueKind != JsonValueKind.String ||
                !string.Equals(statusElement.GetString(), "SUCCEEDED", StringComparison.Ordinal))
            {
                continue;
            }

            JsonElement response = default;
            if (!snapshot.TryGetProperty("response", out response) || response.ValueKind != JsonValueKind.Object ||
                !TryReadInvoiceId(response, out var invoiceId))
            {
                throw new InvalidOperationException("ACCOUNTING_ISSUE_EVENT_INVALID");
            }

            var invoice = _store.GetInvoice(storeId, invoiceId);
            if (invoice is null || invoice.Status != InvoiceStatus.Issued)
            {
                continue;
            }

            totalSalesRials = checked(totalSalesRials +
                invoice.Items.Aggregate(0L, (sum, item) => checked(sum + item.LineTotalRials)));

            JsonElement payload = default;
            if (!string.IsNullOrEmpty(data.PayloadJson))
            {
                payload = JsonSerializer.Deserialize<JsonElement>(data.PayloadJson, HubJson.Options);
            }

            var payments = ReadPayments(payload);
            if (payments is null)
            {
                unrecordedPaymentInvoices++;
            }
            else
            {
                var methodAmounts = new Dictionary<string, long>(StringComparer.Ordinal);
                foreach (var (method, amount) in payments)
                {
                    methodAmounts[method] = checked(methodAmounts.GetValueOrDefault(method) + amount);
                }

                recordedPaymentsRials = checked(recordedPaymentsRials + methodAmounts.Values.Sum());
                if (methodAmounts.TryGetValue(CardReaderMethod, out var cardReaderValue))
                {
                    cardReaderInvoices++;
                    cardReaderAmount = checked(cardReaderAmount + cardReaderValue);
                }

                if (methodAmounts.TryGetValue(CardToCardMethod, out var cardToCardValue))
                {
                    cardToCardInvoices++;
                    cardToCardAmount = checked(cardToCardAmount + cardToCardValue);
                }

                if (methodAmounts.TryGetValue(CashMethod, out var cashValue))
                {
                    cashInvoices++;
                    cashAmount = checked(cashAmount + cashValue);
                }

                if (methodAmounts.TryGetValue(CreditMethod, out var creditValue))
                {
                    creditInvoices++;
                    creditAmount = checked(creditAmount + creditValue);
                }
            }

            if (TryReadRemainingBalance(payload, out var remainingRials))
            {
                receivableInvoices++;
                outstandingRials = checked(outstandingRials + remainingRials);
            }
            else
            {
                unrecordedRemainingInvoices++;
            }

            foreach (var item in invoice.Items)
            {
                var purchasePrice = item.ProductId is string productId &&
                    products.TryGetValue(productId, out var product)
                        ? product.PurchasePriceRials
                        : null;
                if (purchasePrice is not long price)
                {
                    excludedItems++;
                    continue;
                }

                var costRials = checked(item.QuantityMillis * price) / 1000;
                includedItems++;
                profitSales = checked(profitSales + item.LineTotalRials);
                profitCost = checked(profitCost + costRials);
            }
        }

        var profitRials = checked(profitSales - profitCost);
        return new ManagementAccountingSnapshot(
            "0",
            storeId,
            _time.GetUtcNow().ToUniversalTime(),
            new AccountingPaymentMethods(
                new AccountingMethodValues(cardReaderInvoices, cardReaderAmount),
                new AccountingMethodValues(cardToCardInvoices, cardToCardAmount),
                new AccountingMethodValues(cashInvoices, cashAmount),
                new AccountingMethodValues(creditInvoices, creditAmount),
                unrecordedPaymentInvoices,
                unrecordedPaymentInvoices > 0 ? UnrecordedPaymentReason : null),
            new AccountingReceivables(
                receivableInvoices,
                outstandingRials,
                unrecordedRemainingInvoices,
                unrecordedRemainingInvoices > 0 ? UnrecordedRemainingReason : null),
            new AccountingStockValue(
                pricedProductsCount,
                pricedProductsStockValue,
                unpricedProductsCount,
                unpricedStockMillis,
                unpricedProductsCount > 0 ? UnpricedStockReason : null),
            new AccountingProfit(
                includedItems,
                profitSales,
                profitCost,
                profitRials,
                excludedItems,
                excludedItems > 0 ? ExcludedItemReason : null),
            new[]
            {
                new AccountingLedgerEntry("دریافتی‌های ثبت‌شده (نقد/کارت)", recordedPaymentsRials, 0),
                new AccountingLedgerEntry("حساب‌های دریافتنی (مطالبات)", outstandingRials, 0),
                new AccountingLedgerEntry("ارزش موجودی کالا به بهای خرید", pricedProductsStockValue, 0),
                new AccountingLedgerEntry("فروش فاکتورهای صادرشده", 0, totalSalesRials),
                new AccountingLedgerEntry("بهای تمام‌شدهٔ کالاهای دارای قیمت خرید", 0, profitCost),
                new AccountingLedgerEntry("سود ناخالص (فقط کالاهای دارای قیمت خرید)", 0, profitRials),
            });
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

    private static IReadOnlyList<(string Method, long Amount)>? ReadPayments(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("payments", out var payments) ||
            payments.ValueKind != JsonValueKind.Array ||
            payments.GetArrayLength() == 0)
        {
            return null;
        }

        var parsed = new List<(string Method, long Amount)>();
        foreach (var payment in payments.EnumerateArray())
        {
            if (payment.ValueKind != JsonValueKind.Object ||
                !payment.TryGetProperty("type", out var typeElement) ||
                typeElement.ValueKind != JsonValueKind.String ||
                !payment.TryGetProperty("amount", out var amountElement) ||
                amountElement.ValueKind != JsonValueKind.Number ||
                !amountElement.TryGetInt64(out var amount))
            {
                continue;
            }

            var method = NormalizeMethod(typeElement.GetString());
            if (method.Length > 0)
            {
                parsed.Add((method, amount));
            }
        }

        return parsed.Count > 0 ? parsed : null;
    }

    private static bool TryReadRemainingBalance(JsonElement payload, out long remainingRials)
    {
        remainingRials = 0;
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("remaining_balance", out var remaining) ||
            remaining.ValueKind != JsonValueKind.Number ||
            !remaining.TryGetInt64(out var value) ||
            value < 0)
        {
            return false;
        }

        remainingRials = value;
        return true;
    }

    private static string NormalizeMethod(string? type) => type switch
    {
        "card-reader" => CardReaderMethod,
        "card-to-card" => CardToCardMethod,
        "cash" => CashMethod,
        "credit" => CreditMethod,
        _ => string.Empty
    };
}
