using System.Text.Json;
using Kandoo.Core;

namespace Kandoo.Hub;

public sealed partial class SqliteHubStore
{
    public JsonElement ExportRestoreState(Guid storeId, long writerEpoch)
    {
        if (writerEpoch <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(writerEpoch));
        }

        lock (_gate)
        {
            ThrowIfDisposed();
            using var transaction = BeginTransaction();
            var state = ReadRestoreState(transaction, storeId, writerEpoch);
            transaction.Commit();
            return JsonSerializer.SerializeToElement(state, HubJson.Options);
        }
    }

    public void RestoreReplayedState(JsonElement stateJson, IReadOnlyList<ReplicationLogEntry> entries, string expectedStateHash)
    {
        var state = JsonSerializer.Deserialize<HubReplayedState>(stateJson.GetRawText(), HubJson.Options)
            ?? throw new InvalidOperationException("RESTORE_STATE_INVALID");
        var storeId = state.Store.StoreId;
        if (storeId == Guid.Empty || state.ThroughOperationSeq < 0 || state.WriterEpoch <= 0 ||
            state.Operations.Count != state.ThroughOperationSeq || entries.Count != state.ThroughOperationSeq)
        {
            throw new InvalidOperationException("RESTORE_STATE_INVALID");
        }

        for (var sequence = 1L; sequence <= state.ThroughOperationSeq; sequence++)
        {
            if (state.Operations.SingleOrDefault(value => value.OperationSeq == sequence) is not { } operation ||
                operation.StoreId != storeId || entries[(int)sequence - 1].OperationSeq != sequence ||
                !string.Equals(entries[(int)sequence - 1].OperationId, operation.OperationId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("CLOUD_REPLICA_INVALID");
            }
        }

        if (state.Devices.Any(value => value.StoreId != storeId) ||
            state.Products.Any(value => value.StoreId != storeId) ||
            state.InventoryEvents.Any(value => value.StoreId != storeId) ||
            state.Invoices.Any(value => value.StoreId != storeId) ||
            state.InvoiceItems.Any(value => value.StoreId != storeId) ||
            state.Locks.Any(value => value.StoreId != storeId) ||
            state.InvoiceItems.Any(item => state.Invoices.All(invoice => invoice.InvoiceId != item.InvoiceId)))
        {
            throw new InvalidOperationException("RESTORE_STATE_INVALID");
        }

        lock (_gate)
        {
            ThrowIfDisposed();
            using (var command = _connection.CreateCommand())
            {
                command.CommandText = """
                    SELECT (SELECT COUNT(*) FROM devices WHERE store_id = $store) +
                           (SELECT COUNT(*) FROM products WHERE store_id = $store) +
                           (SELECT COUNT(*) FROM invoices WHERE store_id = $store) +
                           (SELECT COUNT(*) FROM operations WHERE store_id = $store) +
                           (SELECT COUNT(*) FROM replication_log WHERE store_id = $store) +
                           (SELECT COUNT(*) FROM store_counters WHERE store_id = $store) +
                           (SELECT COUNT(*) FROM operation_outbox WHERE store_id = $store);
                    """;
                command.Parameters.AddWithValue("$store", storeId.ToString());
                if (Convert.ToInt64(command.ExecuteScalar()) != 0)
                {
                    throw new InvalidOperationException("RESTORE_TARGET_NOT_FRESH");
                }
            }

            using var transaction = BeginTransaction();
            try
            {
                foreach (var device in state.Devices)
                {
                    transaction.RestoreDevice(device);
                }

                foreach (var product in state.Products)
                {
                    transaction.SaveProduct(product);
                }

                foreach (var invoice in state.Invoices)
                {
                    transaction.SaveInvoice(invoice.ToInvoice(state.InvoiceItems
                        .Where(item => item.InvoiceId == invoice.InvoiceId)
                        .Select(item => item.ToItem())));
                }

                foreach (var inventoryEvent in state.InventoryEvents)
                {
                    transaction.AppendInventory(inventoryEvent);
                }

                foreach (var operation in state.Operations.OrderBy(value => value.OperationSeq))
                {
                    transaction.AppendOperation(operation);
                }

                foreach (var draftLock in state.Locks)
                {
                    transaction.SaveDraftLock(draftLock.ToLock());
                }

                transaction.SetDisplayNumberHighWatermark(storeId, state.DisplayNumberHighWatermark);
                foreach (var entry in entries.OrderBy(value => value.OperationSeq))
                {
                    transaction.RestoreReplicationEntry(entry, CanonicalJson.Serialize(JsonSerializer.SerializeToElement(entry, HubJson.Options)));
                }

                var staged = JsonSerializer.SerializeToElement(ReadRestoreState(transaction, storeId, state.WriterEpoch), HubJson.Options);
                if (!string.Equals(CanonicalJson.ReplayedStateHash(staged), expectedStateHash, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("RESTORE_HASH_MISMATCH");
                }

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
    }

    private static HubReplayedState ReadRestoreState(SqliteHubTransaction transaction, Guid storeId, long writerEpoch)
    {
        var products = transaction.GetProducts().Where(value => value.StoreId == storeId).ToList();
        var invoices = transaction.GetInvoices().Where(value => value.StoreId == storeId).ToList();
        var operations = transaction.GetOperations().Where(value => value.StoreId == storeId).ToList();
        return new HubReplayedState
        {
            Store = new HubStoreRestore { StoreId = storeId },
            Devices = transaction.GetRestoreDevices(storeId).ToList(),
            Products = products,
            InventoryEvents = transaction.GetInventoryEvents().Where(value => value.StoreId == storeId).ToList(),
            Invoices = invoices.Select(HubInvoiceRestore.FromInvoice).ToList(),
            InvoiceItems = invoices.SelectMany(invoice => invoice.Items
                .Select(item => HubInvoiceItemRestore.FromItem(invoice.InvoiceId, item))).ToList(),
            Operations = operations,
            Locks = transaction.GetDraftLocks().Where(value => value.StoreId == storeId).Select(HubDraftLockRestore.FromLock).ToList(),
            ThroughOperationSeq = operations.Count == 0 ? 0 : operations.Max(value => value.OperationSeq),
            WriterEpoch = writerEpoch,
            DisplayNumberHighWatermark = transaction.GetDisplayNumberHighWatermark(storeId)
        };
    }
}
