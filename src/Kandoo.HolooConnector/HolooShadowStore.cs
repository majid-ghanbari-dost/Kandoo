using System.Collections.Generic;

namespace Kandoo.HolooConnector;

/// <summary>
/// سایه‌انبار فقط‌خواندنی برای محصولات، مشتریان و تاریخچه Holoo.
/// واردات از Holoo در اینجا نگهداری می‌شود؛ نام‌دار نوشته نمی‌شود.
/// </summary>
public sealed class HolooShadowStore
{
    private readonly Dictionary<string, CustomerShadowRecord> _customers = new();
    private readonly Dictionary<string, ProductShadowRecord> _products = new();
    private readonly List<HistoryShadowRecord> _history = new();

    public IReadOnlyDictionary<string, CustomerShadowRecord> Customers => _customers;
    public IReadOnlyDictionary<string, ProductShadowRecord> Products => _products;
    public IReadOnlyList<HistoryShadowRecord> History => _history;

    public void UpsertCustomer(CustomerShadowRecord record)
    {
        _customers[record.CustomerCode] = record;
    }

    public void UpsertProduct(ProductShadowRecord record)
    {
        _products[record.ProductCode] = record;
    }

    public void AppendHistory(HistoryShadowRecord record)
    {
        _history.Add(record);
    }

    public bool TryGetCustomer(string code, out CustomerShadowRecord? record)
    {
        return _customers.TryGetValue(code, out record);
    }

    public bool TryGetProduct(string code, out ProductShadowRecord? record)
    {
        return _products.TryGetValue(code, out record);
    }
}

public sealed record CustomerShadowRecord(
    string CustomerCode,
    string? CustomerName,
    string? Phone,
    bool IsUnverified);

public sealed record ProductShadowRecord(
    string ProductCode,
    string? ProductName,
    decimal? BuyPrice,
    decimal? LastSoldPrice,
    bool IsUnverified);

public sealed record HistoryShadowRecord(
    string InvoiceCode,
    string CustomerCode,
    string ProductCode,
    int Quantity,
    decimal UnitPrice);