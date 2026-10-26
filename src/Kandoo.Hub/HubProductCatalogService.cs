using System.Text.Json;
using Kandoo.Core;

namespace Kandoo.Hub;

public sealed record HubProductRegistrationResult(
    ProductRegistrationStatus Status,
    Product? Product,
    long? StockMillis,
    string? CandidateProductId,
    long? OperationSeq,
    bool IdempotentReplay);

public sealed record HubProductRetirementResult(
    ProductUpdateStatus Status,
    Product? Product,
    int? CurrentVersion,
    long? OperationSeq,
    bool IdempotentReplay);

internal sealed record ProductRegistrationResponse(
    ProductRegistrationStatus Status,
    Product? Product,
    long? StockMillis,
    string? CandidateProductId);

public sealed class HubProductCatalogService
{
    private readonly SqliteHubStore _store;

    public HubProductCatalogService(SqliteHubStore store)
    {
        _store = store;
    }

    public HubProductRegistrationResult Register(
        Guid storeId,
        Guid deviceId,
        string operationId,
        ProductRegistrationInput input)
    {
        input = input.Validate();
        var payload = HubJson.Element(new
        {
            product_id = input.ProductId,
            name = input.Name,
            unit_price_rials = input.UnitPriceRials,
            initial_stock_millis = input.InitialStockMillis,
            acknowledged_candidate_id = input.AcknowledgedCandidateId,
            category = input.Category,
            color = input.Color,
            size = input.Size,
            material = input.Material,
            brand = input.Brand,
            barcode = input.Barcode,
            specification = input.Specification
        });
        var request = new OperationRequest(storeId, deviceId, operationId, "product.create", null, payload);
        var result = _store.RegisterProduct(request, input);
        if (result.Status == OperationExecutionStatus.OperationIdConflict)
        {
            throw new InvalidOperationException("OPERATION_ID_CONFLICT");
        }

        var response = JsonSerializer.Deserialize<ProductRegistrationResponse>(result.Response!, HubJson.Options)!;
        return new HubProductRegistrationResult(
            response.Status, response.Product, response.StockMillis, response.CandidateProductId,
            result.OperationSeq, result.IsReplay);
    }

    public HubProductRetirementResult Retire(
        Guid storeId,
        Guid deviceId,
        string operationId,
        string productId,
        int baseVersion)
    {
        var payload = HubJson.Element(new { product_id = productId, base_version = baseVersion });
        var request = new OperationRequest(storeId, deviceId, operationId, "product.retire", baseVersion, payload);
        var result = _store.RetireProduct(request, productId, baseVersion);
        if (result.Status == OperationExecutionStatus.OperationIdConflict)
        {
            throw new InvalidOperationException("OPERATION_ID_CONFLICT");
        }

        var response = JsonSerializer.Deserialize<ProductUpdateResult>(result.Response!, HubJson.Options)!;
        return new HubProductRetirementResult(
            response.Status, response.Product, response.CurrentVersion, result.OperationSeq, result.IsReplay);
    }

    public Product? GetByTag(Guid storeId, long productTagNumber) => _store.GetProductByTag(storeId, productTagNumber);
    public long GetTagHighWatermark(Guid storeId) => _store.GetProductTagHighWatermark(storeId);
    public long GetStockMillis(Guid storeId, string productId) => _store.LoadInventoryLedger().StockMillis(storeId, productId);
}
