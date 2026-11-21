namespace Kandoo.HolooConnector;

/// <summary>
/// Verifies imported Holoo stock. All imported stock is marked unverified by default.
/// The last sold price is used only when a product has no explicit sale price in the source;
/// it is never guessed or fabricated.
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
    /// </summary>
    public static StockVerificationResult MarkUnverified(
        string productCode,
        decimal? salePrice,
        decimal? lastSoldPrice)
    {
        if (salePrice.HasValue)
        {
            return new StockVerificationResult
            {
                ProductCode = productCode,
                IsVerified = false,
                EffectivePrice = salePrice.Value,
                PriceSource = "sale_price",
            };
        }

        if (lastSoldPrice.HasValue)
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
