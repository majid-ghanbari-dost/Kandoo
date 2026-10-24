namespace Kandoo.Core;

public enum InventoryEventType
{
    Receipt,
    Sale,
    Return,
    Adjustment,
    Correction
}

public sealed record InventoryEvent(
    Guid InventoryEventId,
    Guid StoreId,
    string ProductId,
    long QuantityDeltaMillis,
    InventoryEventType EventType,
    string OperationId,
    Guid DeviceId,
    Guid? ReversesEventId = null,
    string? Reason = null);

/// <summary>An append-only, store-scoped event ledger with derived integer stock.</summary>
public sealed class InventoryLedger
{
    private readonly object _gate = new();
    private readonly List<InventoryEvent> _events = new();

    public IReadOnlyList<InventoryEvent> Events
    {
        get
        {
            lock (_gate)
            {
                return _events.ToArray();
            }
        }
    }

    public InventoryEvent Append(InventoryEvent inventoryEvent)
    {
        if (inventoryEvent.StoreId == Guid.Empty || inventoryEvent.InventoryEventId == Guid.Empty ||
            inventoryEvent.DeviceId == Guid.Empty || string.IsNullOrWhiteSpace(inventoryEvent.ProductId) ||
            string.IsNullOrWhiteSpace(inventoryEvent.OperationId) || inventoryEvent.QuantityDeltaMillis == 0)
        {
            throw new ArgumentException("Inventory events require store, event, device, product, operation, and a non-zero signed delta.");
        }

        lock (_gate)
        {
            if (_events.Any(e => e.InventoryEventId == inventoryEvent.InventoryEventId))
            {
                throw new InvalidOperationException("Inventory event IDs are immutable and cannot be reused.");
            }

            if (inventoryEvent.EventType == InventoryEventType.Correction)
            {
                if (inventoryEvent.ReversesEventId is null || string.IsNullOrWhiteSpace(inventoryEvent.Reason))
                {
                    throw new ArgumentException("A correction requires a reason and a referenced event.");
                }

                var original = _events.SingleOrDefault(e => e.InventoryEventId == inventoryEvent.ReversesEventId.Value);
                if (original is null || original.StoreId != inventoryEvent.StoreId || original.ProductId != inventoryEvent.ProductId)
                {
                    throw new InvalidOperationException("A correction must reference an event in the same store and product ledger.");
                }
            }

            _events.Add(inventoryEvent);
            return inventoryEvent;
        }
    }

    public InventoryEvent AppendCorrection(InventoryEvent original, Guid eventId, long quantityDeltaMillis, string operationId, Guid deviceId, string reason)
    {
        return Append(new InventoryEvent(
            eventId,
            original.StoreId,
            original.ProductId,
            quantityDeltaMillis,
            InventoryEventType.Correction,
            operationId,
            deviceId,
            original.InventoryEventId,
            reason));
    }

    public long StockMillis(Guid storeId, string productId)
    {
        lock (_gate)
        {
            return _events
                .Where(e => e.StoreId == storeId && e.ProductId == productId)
                .Aggregate(0L, (stock, e) => checked(stock + e.QuantityDeltaMillis));
        }
    }

    // These explicit guards make the append-only boundary testable; no mutation path is provided.
    public void Update(InventoryEvent inventoryEvent) =>
        throw new InvalidOperationException("Inventory events are append-only; append a CORRECTION instead.");

    public void Delete(Guid inventoryEventId) =>
        throw new InvalidOperationException("Inventory events are append-only; deletion is forbidden.");
}
