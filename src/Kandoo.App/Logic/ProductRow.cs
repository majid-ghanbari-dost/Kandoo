namespace Kandoo.App;

public sealed record ProductRow(
    string ProductId,
    string Name,
    string? GroupId,
    long UnitPriceRials,
    bool IsActive)
{
    public string PriceDisplay => FormattableString.Invariant($"{UnitPriceRials:N0} ریال");
    public string ActiveDisplay => IsActive ? "فعال" : "غیرفعال";
    public string GroupDisplay => GroupId is null ? "بدون گروه" : GroupId;
}

public interface IProductDataSource
{
    Task<IReadOnlyList<ProductRow>> LoadProductsAsync(string? groupId, CancellationToken cancellationToken = default);
}
