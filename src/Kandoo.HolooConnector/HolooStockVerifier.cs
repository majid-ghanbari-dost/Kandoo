namespace Kandoo.HolooConnector;

/// <summary>
/// Verifies imported Holoo stock. All imported stock is marked unverified by default.
/// The last sold price is used only when a product has no explicit positive sale price in the source;
/// it is never guessed or fabricated from buy cost.
/// </summary>
public sealed class StockVerificationResult
{
    public string ProductCode { get; init; } = string.Empty;
    public bool IsVerified { get; init; }
    public decimal? EffectivePrice { get; init; }
    public string? PriceSource { get; init; } // "sale_price", "last_sold", or null
}

public static class HolooStockVerifier
{
    /// <summary>
    /// Marks imported stock as unverified. Returns a result with IsVerified=false
    /// and the effective price derived from available data (never guessed).
    /// Zero or negative source sale prices are treated as absent rather than zero-priced items.
    /// </summary>
    public static StockVerificationResult MarkUnverified(
        string productCode,
        decimal? salePrice,
        decimal? lastSoldPrice)
    {
        if (salePrice.HasValue && salePrice.Value > 0m)
        {
            return new StockVerificationResult
            {
                ProductCode = productCode,
                IsVerified = false,
                EffectivePrice = salePrice.Value,
                PriceSource = "sale_price",
            };
        }

        if (lastSoldPrice.HasValue && lastSoldPrice.Value > 0m)
        {
            return new StockVerificationResult
            {
                ProductCode = productCode,
                IsVerified = false,
                EffectivePrice = lastSoldPrice.Value,
                PriceSource = "last_sold",
            };
        }

        return new StockVerificationResult
        {
            ProductCode = productCode,
            IsVerified = false,
            EffectivePrice = null,
            PriceSource = null,
        };
    }
}

public sealed record HolooResolvedPrice(
    string ProductCode,
    decimal? ExplicitSalePrice,
    decimal? LastSoldPrice,
    string? LastSoldInvoiceCode,
    string? LastSoldInvoiceDate,
    decimal? EffectivePrice,
    string? PriceSource);

/// <summary>
/// Resolves explicit selling price and historical LastSoldPrice across
/// ARTICLE, ArticlePrice, FACTURE, and FACTART without guessing from Buy_Price.
/// </summary>
public static class HolooPriceResolver
{
    public static IReadOnlyDictionary<string, HolooResolvedPrice> ResolveAll(
        IEnumerable<HolooMappedProduct> products,
        IEnumerable<HolooMappedArticlePrice> articlePrices,
        IEnumerable<HolooMappedInvoice> invoices,
        IEnumerable<HolooMappedInvoiceItem> invoiceItems)
    {
        ArgumentNullException.ThrowIfNull(products);
        ArgumentNullException.ThrowIfNull(articlePrices);
        ArgumentNullException.ThrowIfNull(invoices);
        ArgumentNullException.ThrowIfNull(invoiceItems);

        var explicitByProduct = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

        // Highest priority explicit price from ARTICLE.Sel_Price (> 0)
        foreach (var product in products)
        {
            if (string.IsNullOrWhiteSpace(product.ProductCode))
                continue;

            if (product.SalePrice.HasValue && product.SalePrice.Value > 0m)
                explicitByProduct[product.ProductCode] = product.SalePrice.Value;
        }

        // Fallback explicit price from ArticlePrice table (latest ModifyDate / highest ID with Sel_Price > 0)
        foreach (var group in articlePrices
            .Where(static ap => !string.IsNullOrWhiteSpace(ap.ProductCode) && ap.Price.HasValue && ap.Price.Value > 0m)
            .GroupBy(static ap => ap.ProductCode, StringComparer.OrdinalIgnoreCase))
        {
            if (explicitByProduct.ContainsKey(group.Key))
                continue;

            var latestExplicit = group
                .OrderByDescending(static ap => ap.ModifyDate ?? string.Empty, StringComparer.Ordinal)
                .ThenByDescending(static ap => ap.PriceRowId ?? 0L)
                .First();

            explicitByProduct[group.Key] = latestExplicit.Price!.Value;
        }

        var invoiceByCode = invoices
            .Where(static inv => !string.IsNullOrWhiteSpace(inv.InvoiceCode) && !inv.IsDeleted && IsSaleLikeInvoiceType(inv.InvoiceType))
            .ToDictionary(static inv => inv.InvoiceCode, StringComparer.OrdinalIgnoreCase);

        var lastSoldByProduct = new Dictionary<string, (decimal Price, string InvoiceCode, string? InvoiceDate, int LineIndex)>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in invoiceItems)
        {
            if (string.IsNullOrWhiteSpace(item.ProductCode) || !item.UnitPrice.HasValue || item.UnitPrice.Value <= 0m)
                continue;

            if (item.Quantity.HasValue && item.Quantity.Value <= 0m)
                continue;

            string? invoiceDate = null;
            if (!string.IsNullOrWhiteSpace(item.InvoiceCode) && invoiceByCode.TryGetValue(item.InvoiceCode, out var invoice))
            {
                invoiceDate = invoice.InvoiceDate;
            }
            else if (invoices.Any(inv => string.Equals(inv.InvoiceCode, item.InvoiceCode, StringComparison.OrdinalIgnoreCase)
                                         && (inv.IsDeleted || !IsSaleLikeInvoiceType(inv.InvoiceType))))
            {
                // Skip explicitly deleted or non-sale (e.g., return/purchase) headers.
                continue;
            }

            var candidateDate = invoiceDate ?? string.Empty;
            var candidateInvoice = item.InvoiceCode ?? string.Empty;

            if (!lastSoldByProduct.TryGetValue(item.ProductCode, out var existing) ||
                CompareSaleRecency(candidateDate, candidateInvoice, item.LineIndex, existing.InvoiceDate ?? string.Empty, existing.InvoiceCode, existing.LineIndex) > 0)
            {
                lastSoldByProduct[item.ProductCode] = (item.UnitPrice.Value, candidateInvoice, invoiceDate, item.LineIndex);
            }
        }

        var resolved = new Dictionary<string, HolooResolvedPrice>(StringComparer.OrdinalIgnoreCase);
        foreach (var product in products)
        {
            if (string.IsNullOrWhiteSpace(product.ProductCode))
                continue;

            decimal? explicitPrice = explicitByProduct.TryGetValue(product.ProductCode, out var exp) ? exp : null;
            decimal? lastSoldPrice = null;
            string? lastInvoiceCode = null;
            string? lastInvoiceDate = null;

            if (lastSoldByProduct.TryGetValue(product.ProductCode, out var sold))
            {
                lastSoldPrice = sold.Price;
                lastInvoiceCode = string.IsNullOrEmpty(sold.InvoiceCode) ? null : sold.InvoiceCode;
                lastInvoiceDate = sold.InvoiceDate;
            }

            var verified = HolooStockVerifier.MarkUnverified(product.ProductCode, explicitPrice, lastSoldPrice);
            resolved[product.ProductCode] = new HolooResolvedPrice(
                product.ProductCode,
                explicitPrice,
                lastSoldPrice,
                lastInvoiceCode,
                lastInvoiceDate,
                verified.EffectivePrice,
                verified.PriceSource);
        }

        return resolved;
    }

    private static bool IsSaleLikeInvoiceType(string? invoiceType)
    {
        if (string.IsNullOrWhiteSpace(invoiceType))
            return true;

        var trimmed = invoiceType.Trim();
        // Holoo Fac_Type: 1 = Sale (فروش), 2 = Purchase (خرید), 3 = Sale Return (برگشت از فروش), 4 = Purchase Return, 6 = Proforma.
        // Accept empty or "1" (sale). Reject purchase/return codes explicitly when present.
        if (trimmed is "2" or "3" or "4" or "5" or "6")
            return false;

        return true;
    }

    private static int CompareSaleRecency(
        string dateA, string invoiceA, int lineA,
        string dateB, string invoiceB, int lineB)
    {
        var dateCmp = CompareDates(dateA, dateB);
        if (dateCmp != 0)
            return dateCmp;

        var invCmp = CompareInvoiceCodes(invoiceA, invoiceB);
        if (invCmp != 0)
            return invCmp;

        return lineA.CompareTo(lineB);
    }

    private static int CompareDates(string dateA, string dateB)
    {
        if (TryParseGregorianTimestamp(dateA, out var parsedA) &&
            TryParseGregorianTimestamp(dateB, out var parsedB))
        {
            return parsedA.CompareTo(parsedB);
        }

        return StringComparer.Ordinal.Compare(dateA, dateB);
    }

    private static bool TryParseGregorianTimestamp(string raw, out DateTimeOffset timestamp)
    {
        timestamp = default;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        // Avoid misinterpreting Jalali YYYY/MM/DD (13xx/14xx) as Gregorian medieval dates.
        if (DateTimeOffset.TryParse(
                raw.Trim(),
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal,
                out var parsed) &&
            parsed.Year >= 1900)
        {
            timestamp = parsed;
            return true;
        }

        return false;
    }

    private static int CompareInvoiceCodes(string codeA, string codeB)
    {
        if (long.TryParse(codeA, out var numA) && long.TryParse(codeB, out var numB))
            return numA.CompareTo(numB);

        return StringComparer.OrdinalIgnoreCase.Compare(codeA, codeB);
    }
}
