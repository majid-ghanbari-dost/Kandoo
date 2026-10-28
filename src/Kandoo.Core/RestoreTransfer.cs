using System.Text.Json;

namespace Kandoo.Core;

public sealed record RestoreManifest(
    Guid StoreId,
    long ThroughOperationSeq,
    string HeadEntryHash,
    string StateHash,
    long WriterEpoch,
    Guid WriterDeviceId);

public sealed record RestoreExport(
    RestoreManifest Manifest,
    JsonElement ReplayedState,
    IReadOnlyList<JsonElement> Entries);

public interface IRestoreExportStore
{
    RestoreManifest? GetRestoreManifest(Guid storeId);
    RestoreExport? GetRestoreExport(Guid storeId);
    long ActivateRestoredWriter(Guid storeId, long expectedWriterEpoch, string expectedStateHash, Guid replacementWriterDeviceId);
}
