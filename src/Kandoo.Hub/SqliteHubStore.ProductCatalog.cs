using System.Text.Json;
using Kandoo.Core;

namespace Kandoo.Hub;

public sealed partial class SqliteHubStore
{
    internal OperationResult RegisterProduct(OperationRequest request, ProductRegistrationInput input)
    {
        input = input.Validate();
        return Execute(request, transaction =>
        {
            var concrete = (SqliteHubTransaction)transaction;
            var productId = input.ProductId ?? Guid.NewGuid().ToString("D");
            if (concrete.GetProduct(request.StoreId, productId) is not null)
            {
                return Registration(ProductRegistrationStatus.DuplicateProductId);
            }

            var normalized = ProductNameNormalizer.Normalize(input.Name);
            var similarity = ProductNameNormalizer.SimilarityKey(input.Name);
            var storeProducts = concrete.GetProducts()
                .Where(product => product.StoreId == request.StoreId && product.IsActive)
                .ToArray();
            if (storeProducts.Any(product => product.NormalizedName == normalized))
            {
                return Registration(ProductRegistrationStatus.ExactDuplicate);
            }

            var candidate = storeProducts.FirstOrDefault(product =>
                product.SimilarityKey == similarity && product.NormalizedName != normalized);
            if (candidate is not null && input.AcknowledgedCandidateId != candidate.ProductId)
            {
                return Registration(ProductRegistrationStatus.NearDuplicateWarning, CandidateProductId: candidate.ProductId);
            }

            var tag = concrete.AllocateProductTag(request.StoreId);
            var product = new Product(
                request.StoreId,
                productId,
                input.Name!,
                normalized,
                similarity,
                input.UnitPriceRials!.Value,
                1,
                true,
                input.Specification,
                tag,
                input.Category,
                input.Color,
                input.Size,
                input.Material,
                input.Brand,
                input.Barcode);
            concrete.SaveProduct(product);
            var stock = input.InitialStockMillis!.Value;
            if (stock != 0)
            {
                concrete.AppendInventory(new InventoryEvent(
                    Guid.NewGuid(), request.StoreId, productId, stock, InventoryEventType.Receipt,
                    request.OperationId, request.DeviceId));
            }

            return Registration(ProductRegistrationStatus.Created, product, stock);
        });
    }

    internal OperationResult RetireProduct(OperationRequest request, string productId, int baseVersion)
    {
        return Execute(request, transaction =>
        {
            var current = transaction.GetProduct(request.StoreId, productId);
            if (current is null)
            {
                return JsonSerializer.Serialize(new ProductUpdateResult(ProductUpdateStatus.NotFound), HubJson.Options);
            }

            if (current.Version != baseVersion)
            {
                return JsonSerializer.Serialize(
                    new ProductUpdateResult(ProductUpdateStatus.VersionConflict, current, current.Version), HubJson.Options);
            }

            var retired = current with { IsActive = false, Version = checked(current.Version + 1) };
            transaction.SaveProduct(retired);
            return JsonSerializer.Serialize(
                new ProductUpdateResult(ProductUpdateStatus.Updated, retired, retired.Version), HubJson.Options);
        });
    }

    public Product? GetProductByTag(Guid storeId, long productTagNumber)
    {
        if (storeId == Guid.Empty || productTagNumber <= 0)
        {
            throw new ArgumentException("A store and positive product tag are required.");
        }

        lock (_gate)
        {
            ThrowIfDisposed();
            using var transaction = new SqliteHubTransaction(_connection);
            return transaction.GetProductByTag(storeId, productTagNumber);
        }
    }

    public long GetProductTagHighWatermark(Guid storeId)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            using var transaction = new SqliteHubTransaction(_connection);
            return transaction.GetProductTagHighWatermark(storeId);
        }
    }

    private static string Registration(
        ProductRegistrationStatus status,
        Product? product = null,
        long? stockMillis = null,
        string? CandidateProductId = null) => JsonSerializer.Serialize(
            new ProductRegistrationResponse(status, product, stockMillis, CandidateProductId), HubJson.Options);
}
