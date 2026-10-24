namespace Kandoo.Cloud;

public interface ICloudStore
{
    CloudIngestResult Ingest(Guid routeStoreId, OperationBatch batch, string? idempotencyKey);
    CloudSnapshot? GetSnapshot(Guid storeId);
}
