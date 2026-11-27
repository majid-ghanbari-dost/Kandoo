using System.Collections.Concurrent;

namespace Kandoo.HolooConnector;

/// <summary>
/// Shadow store for Holoo data imported read-only into Kandoo.
/// All imported records are tagged with their source schema version and
/// an import timestamp. Imported stock is always marked "unverified"
/// until the owner confirms it through a separate flow.
/// </summary>
public sealed record ShadowProduct
{
    public string ProductCode { get; init; } = string.Empty;
    public string? LegacyCode { get; init; }
    public string? ProductName { get; init; }
    public string? CountryOfOrigin { get; init; }
    public decimal? BuyPrice { get; init; }
    public decimal? ExplicitSalePrice { get; init; }
    public decimal? LastSoldPrice { get; init; }
    public string? LastSoldInvoiceCode { get; init; }
    public string? LastSoldInvoiceDate { get; init; }
    public decimal? HolooVisibleStock { get; init; }
    public string? UnitCode { get; init; }
    public string? UnitName { get; init; }
    public string? MainGroupCode { get; init; }
    public string? MainGroupName { get; init; }
    public string? SubGroupCode { get; init; }
    public string? SubGroupName { get; init; }
    public string? Model { get; init; }
    public string? SourceEditedAt { get; init; }
    public string SchemaVersion { get; init; } = string.Empty;
    public DateTimeOffset ImportedAt { get; init; }
    public bool StockUnverified { get; init; } = true;
    public decimal? VerifiedStock { get; init; }
    public DateTimeOffset? StockVerifiedAt { get; init; }
    public string? StockVerifiedBy { get; init; }
    public bool IsDeleted { get; init; }
    public bool IsActive { get; init; } = true;

    public StockVerificationResult EvaluatePriceAndStock() =>
        HolooStockVerifier.MarkUnverified(ProductCode, ExplicitSalePrice, LastSoldPrice);
}

public sealed class ShadowCustomer
{
    public string CustomerCode { get; init; } = string.Empty;
    public string? LegacyCode { get; init; }
    public string? CustomerName { get; init; }
    public string? CustomerFamily { get; init; }
    public string? Phone { get; init; }
    public string? Mobile { get; init; }
    public IReadOnlyList<string> AdditionalPhones { get; init; } = Array.Empty<string>();
    public string? Address { get; init; }
    public string? PostalCode { get; init; }
    public string? NationalCode { get; init; }
    public string? SourceEditedAt { get; init; }
    public bool IsDeleted { get; init; }
    public string SchemaVersion { get; init; } = string.Empty;
    public DateTimeOffset ImportedAt { get; init; }
}

public sealed record ShadowInvoiceItem
{
    public int LineIndex { get; init; }
    public string ProductCode { get; init; } = string.Empty;
    public decimal? Quantity { get; init; }
    public decimal? UnitPrice { get; init; }
    public decimal? BuyPrice { get; init; }
    public decimal? DiscountPercent { get; init; }
    public decimal? LineDiscountAmount { get; init; }
}

public sealed class ShadowInvoice
{
    public string InvoiceCode { get; init; } = string.Empty;
    public string? InvoiceType { get; init; }
    public string? InvoiceDate { get; init; }
    public string? CustomerCode { get; init; }
    public decimal? TotalAmount { get; init; }
    public decimal? CashAmount { get; init; }
    public decimal? ChequeAmount { get; init; }
    public decimal? CreditAmount { get; init; }
    public decimal? CardAmount { get; init; }
    public decimal? DiscountAmount { get; init; }
    public bool IsDeleted { get; init; }
    public IReadOnlyList<ShadowInvoiceItem> Items { get; init; } = Array.Empty<ShadowInvoiceItem>();
    public string SchemaVersion { get; init; } = string.Empty;
    public DateTimeOffset ImportedAt { get; init; }
}

public sealed record HolooSyncCheckpoint(
    long CheckpointId,
    string SourceKind,
    string SchemaVersion,
    string SchemaFingerprint,
    string LastSyncMarker,
    string SyncMode,
    DateTimeOffset SyncedAt,
    int ProductsImported,
    int CustomersImported,
    int InvoicesImported);

/// <summary>
/// In-memory shadow store. Imported products default to StockUnverified=true.
/// LastSoldPrice is used only when explicit positive sale price is absent;
/// BuyPrice is never promoted to a sale price.
/// </summary>
public sealed class HolooShadowStore
{
    private readonly ConcurrentDictionary<string, ShadowProduct> _products = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ShadowCustomer> _customers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ShadowInvoice> _invoices = new(StringComparer.OrdinalIgnoreCase);

    public string SchemaVersion { get; init; } = string.Empty;

    public IReadOnlyList<ShadowProduct> Products => _products.Values.ToList();
    public IReadOnlyList<ShadowCustomer> Customers => _customers.Values.ToList();
    public IReadOnlyList<ShadowInvoice> Invoices => _invoices.Values.ToList();

    public int ProductCount => _products.Count;
    public int CustomerCount => _customers.Count;
    public int InvoiceCount => _invoices.Count;

    public void UpsertProduct(ShadowProduct product)
    {
        ArgumentNullException.ThrowIfNull(product);
        if (string.IsNullOrWhiteSpace(product.ProductCode))
            throw new ArgumentException("ProductCode is required.", nameof(product));

        _products[product.ProductCode] = product with
        {
            StockUnverified = true,
            VerifiedStock = null,
            StockVerifiedAt = null,
            StockVerifiedBy = null,
        };
    }

    public ShadowProduct ConfirmProductStock(
        string productCode,
        decimal verifiedStock,
        string operatorName,
        DateTimeOffset confirmedAt)
    {
        if (string.IsNullOrWhiteSpace(productCode))
            throw new ArgumentException("ProductCode is required.", nameof(productCode));
        if (verifiedStock < 0m)
            throw new ArgumentOutOfRangeException(nameof(verifiedStock), "Verified stock cannot be negative.");
        if (string.IsNullOrWhiteSpace(operatorName))
            throw new ArgumentException("Operator name is required to verify stock.", nameof(operatorName));

        if (!_products.TryGetValue(productCode, out var existing))
            throw new KeyNotFoundException($"Shadow product '{productCode}' was not found.");

        var updated = existing with
        {
            StockUnverified = false,
            VerifiedStock = verifiedStock,
            StockVerifiedAt = confirmedAt,
            StockVerifiedBy = operatorName.Trim(),
        };
        _products[productCode] = updated;
        return updated;
    }

    public void UpsertCustomer(ShadowCustomer customer)
    {
        ArgumentNullException.ThrowIfNull(customer);
        if (string.IsNullOrWhiteSpace(customer.CustomerCode))
            throw new ArgumentException("CustomerCode is required.", nameof(customer));

        _customers[customer.CustomerCode] = customer;
    }

    public void UpsertInvoice(ShadowInvoice invoice)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        if (string.IsNullOrWhiteSpace(invoice.InvoiceCode))
            throw new ArgumentException("InvoiceCode is required.", nameof(invoice));

        _invoices[invoice.InvoiceCode] = invoice;
    }

    public ShadowProduct? GetProduct(string productCode) =>
        _products.TryGetValue(productCode, out var p) ? p : null;

    public ShadowCustomer? GetCustomer(string customerCode) =>
        _customers.TryGetValue(customerCode, out var c) ? c : null;

    public ShadowInvoice? GetInvoice(string invoiceCode) =>
        _invoices.TryGetValue(invoiceCode, out var inv) ? inv : null;

    public IReadOnlyList<ShadowProduct> ListProductsMissingPrice() =>
        _products.Values.Where(p => EffectiveSalePrice(p.ProductCode) is null).ToList();

    public IReadOnlyList<ShadowProduct> ListProductsWithUnverifiedStock() =>
        _products.Values.Where(static p => p.StockUnverified).ToList();

    /// <summary>
    /// Returns the effective sale price for a product: explicit positive sale price
    /// if present, otherwise positive last sold price from history if available,
    /// otherwise null (never guessed from BuyPrice).
    /// </summary>
    public decimal? EffectiveSalePrice(string productCode)
    {
        if (!_products.TryGetValue(productCode, out var p))
            return null;

        return HolooStockVerifier.MarkUnverified(p.ProductCode, p.ExplicitSalePrice, p.LastSoldPrice).EffectivePrice;
    }
}
