using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Kandoo.HolooConnector;

/// <summary>
/// Shadow store for Holoo data imported read-only into Kandoo.
/// All imported records are tagged with their source schema version and
/// an import timestamp. Imported stock is always marked "unverified"
/// until the owner confirms it through a separate flow.
/// </summary>
public sealed class ShadowProduct
{
    public string ProductCode { get; init; } = string.Empty;
    public string? ProductName { get; init; }
    public string? CountryOfOrigin { get; init; }
    public decimal? BuyPrice { get; init; }
    public string? Model { get; init; }
    public decimal? LastSoldPrice { get; init; }
    public string SchemaVersion { get; init; } = string.Empty;
    public DateTimeOffset ImportedAt { get; init; }
    public bool StockUnverified { get; init; } = true;
}

public sealed class ShadowCustomer
{
    public string CustomerCode { get; init; } = string.Empty;
    public string? CustomerName { get; init; }
    public string? CustomerFamily { get; init; }
    public string? Phone { get; init; }
    public string SchemaVersion { get; init; } = string.Empty;
    public DateTimeOffset ImportedAt { get; init; }
}

public sealed class ShadowInvoice
{
    public string InvoiceCode { get; init; } = string.Empty;
    public string? InvoiceDate { get; init; }
    public string? CustomerCode { get; init; }
    public decimal? CashAmount { get; init; }
    public string SchemaVersion { get; init; } = string.Empty;
    public DateTimeOffset ImportedAt { get; init; }
}

/// <summary>
/// In-memory shadow store. Imported products default to StockUnverified=true.
/// LastSoldPrice is populated only when a sale price exists in the source;
/// otherwise it is left null (never guessed).
/// </summary>
public sealed class HolooShadowStore
{
    private readonly ConcurrentDictionary<string, ShadowProduct> _products = new();
    private readonly ConcurrentDictionary<string, ShadowCustomer> _customers = new();
    private readonly ConcurrentDictionary<string, ShadowInvoice> _invoices = new();

    public string SchemaVersion { get; init; } = string.Empty;

    public IReadOnlyList<ShadowProduct> Products => _products.Values.ToList();
    public IReadOnlyList<ShadowCustomer> Customers => _customers.Values.ToList();
    public IReadOnlyList<ShadowInvoice> Invoices => _invoices.Values.ToList();

    public int ProductCount => _products.Count;
    public int CustomerCount => _customers.Count;
    public int InvoiceCount => _invoices.Count;

    public void UpsertProduct(ShadowProduct product) =>
        _products[product.ProductCode] = product with { StockUnverified = true };

    public void UpsertCustomer(ShadowCustomer customer) =>
        _customers[customer.CustomerCode] = customer;

    public void UpsertInvoice(ShadowInvoice invoice) =>
        _invoices[invoice.InvoiceCode] = invoice;

    public ShadowProduct? GetProduct(string productCode) =>
        _products.TryGetValue(productCode, out var p) ? p : null;

    public ShadowCustomer? GetCustomer(string customerCode) =>
        _customers.TryGetValue(customerCode, out var c) ? c : null;

    /// <summary>
    /// Returns the effective sale price for a product: the explicit sale price
    /// if present, otherwise the last sold price from history if available,
    /// otherwise null (never guessed).
    /// </summary>
    public decimal? EffectiveSalePrice(string productCode)
    {
        if (_products.TryGetValue(productCode, out var p))
        {
            // In this shadow store model, BuyPrice serves as the reference cost.
            // A separate sale price would come from ArticlePrice in the source.
            // LastSoldPrice is only set when a real sale was recorded.
            return p.LastSoldPrice;
        }
        return null;
    }
}
