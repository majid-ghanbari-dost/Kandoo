using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kandoo.Core;

namespace Kandoo.Hub;

public static class HubReplicationConstants
{
    public const string ZeroHash = "0000000000000000000000000000000000000000000000000000000000000000";
}

public sealed class HubWriterFencedException(Guid storeId) : InvalidOperationException($"Store {storeId:D} is fenced read-only.")
{
    public Guid StoreId { get; } = storeId;
}

public sealed record HubWriterIdentity(Guid DeviceId, long Epoch)
{
    public HubWriterIdentity Validate()
    {
        if (DeviceId == Guid.Empty || Epoch <= 0)
        {
            throw new ArgumentException("A replication writer requires a device ID and positive epoch.");
        }

        return this;
    }
}

public sealed class ReplicationLogEntry
{
    [JsonPropertyName("protocol_version")]
    public string ProtocolVersion { get; set; } = "0";

    [JsonPropertyName("store_id")]
    public Guid StoreId { get; set; }

    [JsonPropertyName("writer_device_id")]
    public Guid WriterDeviceId { get; set; }

    [JsonPropertyName("writer_epoch")]
    public long WriterEpoch { get; set; }

    [JsonPropertyName("operation_seq")]
    public long OperationSeq { get; set; }

    [JsonPropertyName("previous_entry_hash")]
    public string PreviousEntryHash { get; set; } = string.Empty;

    [JsonPropertyName("entry_hash")]
    public string EntryHash { get; set; } = string.Empty;

    [JsonPropertyName("operation_id")]
    public string OperationId { get; set; } = string.Empty;

    [JsonPropertyName("device_id")]
    public Guid DeviceId { get; set; }

    [JsonPropertyName("operation_type")]
    public string OperationType { get; set; } = string.Empty;

    [JsonPropertyName("base_version")]
    public long? BaseVersion { get; set; }

    [JsonPropertyName("request_hash")]
    public string RequestHash { get; set; } = string.Empty;

    [JsonPropertyName("payload")]
    public JsonElement Payload { get; set; }

    [JsonPropertyName("outcome")]
    public JsonElement Outcome { get; set; }

    [JsonPropertyName("response_snapshot")]
    public JsonElement ResponseSnapshot { get; set; }

    [JsonPropertyName("hub_recorded_at")]
    public string HubRecordedAt { get; set; } = string.Empty;
}

public sealed class ReplicationBatch
{
    [JsonPropertyName("batch_id")]
    public string BatchId { get; set; } = string.Empty;

    [JsonPropertyName("store_id")]
    public Guid StoreId { get; set; }

    [JsonPropertyName("writer_device_id")]
    public Guid WriterDeviceId { get; set; }

    [JsonPropertyName("writer_epoch")]
    public long WriterEpoch { get; set; }

    [JsonPropertyName("entries")]
    public List<ReplicationLogEntry>? Entries { get; set; }

    public static ReplicationBatch Create(HubWriterIdentity writer, Guid storeId, IReadOnlyList<ReplicationLogEntry> entries)
    {
        if (entries.Count == 0)
        {
            throw new ArgumentException("A durable upload batch requires entries.", nameof(entries));
        }

        var first = entries[0];
        var last = entries[^1];
        return new ReplicationBatch
        {
            BatchId = $"hub-{first.OperationSeq}-{last.OperationSeq}-{last.EntryHash[..16]}",
            StoreId = storeId,
            WriterDeviceId = writer.DeviceId,
            WriterEpoch = writer.Epoch,
            Entries = entries.ToList()
        };
    }
}

public interface IHubReplicationSource
{
    ReplicationBatch? ReadBatch(Guid storeId, int maxEntries, int maxBytes);
    void Acknowledge(Guid storeId, long throughSequence, string headEntryHash, DateTimeOffset acknowledgedAt);
    int GetOutboxDepth(Guid storeId);
    bool IsWriterFenced(Guid storeId);
    void MarkWriterFenced(Guid storeId, HubWriterIdentity writer, string reason);
}

public sealed record CloudTransportResponse(HttpStatusCode HttpStatus, JsonElement? Body, TimeSpan? RetryAfter = null);

public interface ICloudReplicationTransport
{
    Task<CloudTransportResponse> SendAsync(ReplicationBatch batch, CancellationToken cancellationToken);
}

public sealed record HubReplicationResult(
    string Status,
    int Attempts,
    int OutboxDepth,
    long AcceptedThroughSeq,
    string? ErrorCode = null,
    long? RetryFromSequence = null,
    int? RetryAfterMs = null,
    int MaxBatchEntries = 0,
    int MaxBatchBytes = 0,
    string Mode = "ACTIVE",
    bool Retryable = false);

public sealed class HubReplicationOptions
{
    public int MaxAttempts { get; init; } = 3;
    public int MaxBatchEntries { get; init; } = 100;
    public int MaxBatchBytes { get; init; } = 1_048_576;
    public Func<TimeSpan, CancellationToken, Task> DelayAsync { get; init; } = Task.Delay;
}
