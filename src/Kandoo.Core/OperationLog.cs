using System.Text.Json;

namespace Kandoo.Core;

public sealed record OperationRequest(
    Guid StoreId,
    Guid DeviceId,
    string OperationId,
    string OperationType,
    long? BaseVersion,
    JsonElement Payload)
{
    public string RequestHash => CanonicalJson.RequestHash(StoreId, DeviceId, OperationId, OperationType, BaseVersion, Payload);
}

public enum OperationExecutionStatus
{
    Succeeded,
    Replayed,
    OperationIdConflict
}

public sealed record OperationResult(
    OperationExecutionStatus Status,
    string? Response,
    long? OperationSeq,
    string? ErrorCode = null)
{
    public bool IsReplay => Status == OperationExecutionStatus.Replayed;
}

public sealed record OperationRecord(
    Guid StoreId,
    Guid DeviceId,
    string OperationId,
    string OperationType,
    string RequestHash,
    long OperationSeq,
    string Response);

/// <summary>Store-scoped idempotency and attribution; no transport or persistence is implied.</summary>
public sealed class OperationLog
{
    private readonly object _gate = new();
    private readonly Dictionary<(Guid StoreId, string OperationId), OperationRecord> _records = new();
    private readonly Dictionary<Guid, long> _nextSequenceByStore = new();

    public OperationResult Execute(OperationRequest request, Func<string> action)
    {
        if (request.StoreId == Guid.Empty || request.DeviceId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.OperationId) || string.IsNullOrWhiteSpace(request.OperationType))
        {
            throw new ArgumentException("Every mutation requires store, device, operation ID, and operation type.");
        }

        if (action is null)
        {
            throw new ArgumentNullException(nameof(action));
        }

        lock (_gate)
        {
            var key = (request.StoreId, request.OperationId);
            var requestHash = request.RequestHash;
            if (_records.TryGetValue(key, out var existing))
            {
                if (existing.DeviceId != request.DeviceId || existing.OperationType != request.OperationType || existing.RequestHash != request.RequestHash)
                {
                    return new(OperationExecutionStatus.OperationIdConflict, null, null, "OPERATION_ID_CONFLICT");
                }

                return new(OperationExecutionStatus.Replayed, existing.Response, existing.OperationSeq);
            }

            // The action and record update are inside one core critical section.
            // A real Hub persistence adapter must make the same boundary durable.
            var response = action();
            var sequence = _nextSequenceByStore.GetValueOrDefault(request.StoreId, 1);
            var record = new OperationRecord(request.StoreId, request.DeviceId, request.OperationId, request.OperationType, requestHash, sequence, response);
            _records.Add(key, record);
            _nextSequenceByStore[request.StoreId] = checked(sequence + 1);
            return new(OperationExecutionStatus.Succeeded, response, sequence);
        }
    }

    internal void Restore(OperationRecord record)
    {
        lock (_gate)
        {
            _records[(record.StoreId, record.OperationId)] = record;
            var next = checked(record.OperationSeq + 1);
            if (_nextSequenceByStore.GetValueOrDefault(record.StoreId) < next)
            {
                _nextSequenceByStore[record.StoreId] = next;
            }
        }
    }

    public int Count(Guid storeId)
    {
        lock (_gate)
        {
            return _records.Keys.Count(k => k.StoreId == storeId);
        }
    }

    public OperationRecord? Get(Guid storeId, string operationId)
    {
        lock (_gate)
        {
            return _records.GetValueOrDefault((storeId, operationId));
        }
    }
}
