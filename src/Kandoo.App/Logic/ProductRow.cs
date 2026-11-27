using System.Globalization;
using Kandoo.Core;

namespace Kandoo.App;

public enum ProductListFilter
{
    All,
    IncompletePrice,
    UnconfirmedStock,
    LowStock
}

public enum ProductBadgeKind
{
    ReadyForSale,
    IncompletePrice,
    UnconfirmedStock,
    Inactive
}

public sealed record ProductRow(
    string ProductId,
    string Name,
    string? GroupId,
    long UnitPriceRials,
    bool IsActive,
    long? ProductTagNumber = null,
    long? StockMillis = null,
    long? PurchasePriceRials = null,
    long Version = 1,
    string? Type = null,
    string? Color = null,
    IReadOnlyList<ProductOperatorField>? OperatorFields = null,
    bool StockUnconfirmed = false,
    string? Specification = null,
    string? Barcode = null)
{
    public const long LowStockThresholdMillis = 5_000;

    public string PriceDisplay => FormattableString.Invariant($"{UnitPriceRials:N0} ریال");
    public string TablePriceDisplay => UnitPriceRials > 0 ? PriceDisplay : "—";
    public string ActiveDisplay => IsActive ? "فعال" : "غیرفعال";
    public string GroupDisplay => GroupId is null ? "بدون گروه" : GroupId;

    public string CodeDisplay =>
        ProductTagNumber is long tag
            ? tag.ToString(CultureInfo.InvariantCulture)
            : !string.IsNullOrWhiteSpace(Barcode)
                ? Barcode!
                : ProductId;

    public string StockDisplay => HubConnectionResolver.FormatQuantityMillis(StockMillis);

    public string VariantSubtitle
    {
        get
        {
            var parts = new List<string>(3);
            if (!string.IsNullOrWhiteSpace(Type))
            {
                parts.Add(Type!);
            }

            if (!string.IsNullOrWhiteSpace(Color))
            {
                parts.Add(Color!);
            }

            if (!string.IsNullOrWhiteSpace(GroupId))
            {
                parts.Add(GroupId!);
            }

            return string.Join(" · ", parts);
        }
    }

    public bool HasVariantSubtitle => !string.IsNullOrWhiteSpace(VariantSubtitle);

    public bool IsIncompletePrice => UnitPriceRials <= 0;

    public bool IsLowStock =>
        IsActive &&
        !StockUnconfirmed &&
        StockMillis is long millis &&
        millis <= LowStockThresholdMillis;

    public bool CanSell => IsActive && UnitPriceRials > 0;

    public ProductBadgeKind BadgeKind =>
        !IsActive
            ? ProductBadgeKind.Inactive
            : IsIncompletePrice
                ? ProductBadgeKind.IncompletePrice
                : StockUnconfirmed
                    ? ProductBadgeKind.UnconfirmedStock
                    : ProductBadgeKind.ReadyForSale;

    public string StatusBadgeText => BadgeKind switch
    {
        ProductBadgeKind.IncompletePrice => "قیمت ناقص",
        ProductBadgeKind.UnconfirmedStock => "موجودی تأییدنشده",
        ProductBadgeKind.Inactive => "غیرفعال",
        _ => "آماده فروش"
    };

    public string StatusBadgeHex => BadgeKind switch
    {
        ProductBadgeKind.IncompletePrice => "#9A3B0A",
        ProductBadgeKind.UnconfirmedStock => "#6B5A3E",
        ProductBadgeKind.Inactive => "#6B5A3E",
        _ => "#256B43"
    };
}

public static class ProductCatalogFilter
{
    public static IReadOnlyList<ProductRow> Apply(
        IReadOnlyList<ProductRow> source,
        ProductListFilter filter,
        string? searchQuery = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        IEnumerable<ProductRow> query = source;
        query = filter switch
        {
            ProductListFilter.IncompletePrice => query.Where(row => row.IsIncompletePrice),
            ProductListFilter.UnconfirmedStock => query.Where(row => row.StockUnconfirmed),
            ProductListFilter.LowStock => query.Where(row => row.IsLowStock),
            _ => query
        };

        var normalizedSearch = HubConnectionResolver.NormalizeSearchDigits(searchQuery);
        if (!string.IsNullOrEmpty(normalizedSearch))
        {
            query = query.Where(row => MatchesSearch(row, normalizedSearch));
        }

        return query.ToArray();
    }

    private static bool MatchesSearch(ProductRow row, string normalizedQuery)
    {
        return ContainsNormalized(row.Name, normalizedQuery) ||
               ContainsNormalized(row.CodeDisplay, normalizedQuery) ||
               ContainsNormalized(row.ProductId, normalizedQuery) ||
               ContainsNormalized(row.Type, normalizedQuery) ||
               ContainsNormalized(row.Color, normalizedQuery) ||
               ContainsNormalized(row.Barcode, normalizedQuery);
    }

    private static bool ContainsNormalized(string? candidate, string normalizedQuery)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        return HubConnectionResolver.NormalizeSearchDigits(candidate)
            .Contains(normalizedQuery, StringComparison.Ordinal);
    }
}

public interface IProductDataSource
{
    Task<IReadOnlyList<ProductRow>> LoadProductsAsync(string? groupId, CancellationToken cancellationToken = default);
}
