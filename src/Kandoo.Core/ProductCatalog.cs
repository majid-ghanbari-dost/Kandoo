namespace Kandoo.Core;

public enum ProductRegistrationStatus
{
    Created,
    DuplicateProductId,
    ExactDuplicate,
    NearDuplicateWarning
}

public sealed record Product(
    Guid StoreId,
    string ProductId,
    string Name,
    string NormalizedName,
    string SimilarityKey,
    long UnitPriceRials,
    int Version,
    bool IsActive = true,
    string? Specification = null);

public sealed record ProductRegistrationResult(
    ProductRegistrationStatus Status,
    Product? Product = null,
    string? CandidateProductId = null);

public enum ProductUpdateStatus
{
    Updated,
    NotFound,
    VersionConflict
}

public sealed record ProductUpdateResult(ProductUpdateStatus Status, Product? Product = null, int? CurrentVersion = null);

/// <summary>Pure in-memory catalog rules; persistence and authorization are outside this core.</summary>
public sealed class ProductCatalog
{
    private readonly object _gate = new();
    private readonly Dictionary<(Guid StoreId, string ProductId), Product> _products = new();

    public ProductRegistrationResult Register(
        Guid storeId,
        string productId,
        string name,
        long unitPriceRials,
        string? acknowledgedCandidateId = null)
    {
        if (storeId == Guid.Empty || string.IsNullOrWhiteSpace(productId) || unitPriceRials < 0)
        {
            throw new ArgumentException("Store, product ID, and a non-negative integer Rial price are required.");
        }

        var normalized = ProductNameNormalizer.Normalize(name);
        var similarity = ProductNameNormalizer.SimilarityKey(name);
        lock (_gate)
        {
            var key = (storeId, productId);
            if (_products.ContainsKey(key))
            {
                return new(ProductRegistrationStatus.DuplicateProductId);
            }

            var storeProducts = _products.Values.Where(p => p.StoreId == storeId && p.IsActive).ToArray();
            if (storeProducts.Any(p => p.NormalizedName == normalized))
            {
                return new(ProductRegistrationStatus.ExactDuplicate);
            }

            var candidate = storeProducts.FirstOrDefault(p => p.SimilarityKey == similarity && p.NormalizedName != normalized);
            if (candidate is not null && acknowledgedCandidateId != candidate.ProductId)
            {
                return new(ProductRegistrationStatus.NearDuplicateWarning, CandidateProductId: candidate.ProductId);
            }

            var product = new Product(storeId, productId, name, normalized, similarity, unitPriceRials, 1);
            _products.Add(key, product);
            return new(ProductRegistrationStatus.Created, product);
        }
    }

    public ProductUpdateResult Update(Guid storeId, string productId, int baseVersion, long? unitPriceRials = null, string? specification = null)
    {
        if (unitPriceRials is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(unitPriceRials));
        }

        lock (_gate)
        {
            if (!_products.TryGetValue((storeId, productId), out var current))
            {
                return new(ProductUpdateStatus.NotFound);
            }

            if (current.Version != baseVersion)
            {
                return new(ProductUpdateStatus.VersionConflict, current, current.Version);
            }

            var updated = current with
            {
                UnitPriceRials = unitPriceRials ?? current.UnitPriceRials,
                Specification = specification ?? current.Specification,
                Version = checked(current.Version + 1)
            };
            _products[(storeId, productId)] = updated;
            return new(ProductUpdateStatus.Updated, updated, updated.Version);
        }
    }

    public Product? Get(Guid storeId, string productId)
    {
        lock (_gate)
        {
            return _products.GetValueOrDefault((storeId, productId));
        }
    }

    public int Count(Guid storeId)
    {
        lock (_gate)
        {
            return _products.Values.Count(p => p.StoreId == storeId);
        }
    }
}
