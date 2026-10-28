using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kandoo.Cloud;

/// <summary>JSON settings for the HTTP boundary. Hashing still uses Kandoo.Core directly.</summary>
public static class CloudJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };
}

public sealed class OperationBatch
{
    [JsonPropertyName("batch_id")]
    public string BatchId { get; set; } = string.Empty;

    [JsonPropertyName("store_id")]
    public Guid StoreId { get; set; }

    [JsonPropertyName("writer_device_id")]
    public Guid WriterDeviceId { get; set; }

    [JsonPropertyName("writer_epoch")]
    public long WriterEpoch { get; set; }

    // Null distinguishes an omitted entries field from an explicitly empty batch.
    // CLOUD-006's entry_count is harness metadata and is intentionally not a DTO field.
    [JsonPropertyName("entries")]
    public List<LogEntry>? Entries { get; set; }
}

public sealed class LogEntry
{
    [JsonPropertyName("protocol_version")]
    public string ProtocolVersion { get; set; } = string.Empty;

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

    public LogEntry Clone() => new()
    {
        ProtocolVersion = ProtocolVersion,
        StoreId = StoreId,
        WriterDeviceId = WriterDeviceId,
        WriterEpoch = WriterEpoch,
        OperationSeq = OperationSeq,
        PreviousEntryHash = PreviousEntryHash,
        EntryHash = EntryHash,
        OperationId = OperationId,
        DeviceId = DeviceId,
        OperationType = OperationType,
        BaseVersion = BaseVersion,
        RequestHash = RequestHash,
        Payload = Payload.Clone(),
        Outcome = Outcome.Clone(),
        ResponseSnapshot = ResponseSnapshot.Clone(),
        HubRecordedAt = HubRecordedAt
    };
}

public sealed class IngestAck
{
    [JsonPropertyName("batch_id")]
    public string BatchId { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("accepted_through_seq")]
    public long AcceptedThroughSeq { get; set; }

    [JsonPropertyName("ingested_count")]
    public int IngestedCount { get; set; }

    [JsonPropertyName("duplicate_count")]
    public int DuplicateCount { get; set; }

    [JsonPropertyName("head_entry_hash")]
    public string? HeadEntryHash { get; set; }

    [JsonPropertyName("next_expected_seq")]
    public long NextExpectedSeq { get; set; }

    [JsonPropertyName("cloud_cursor")]
    public string? CloudCursor { get; set; }
}

public sealed class ErrorResponse
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("retry_hint")]
    public Dictionary<string, object?> RetryHint { get; set; } = new(StringComparer.Ordinal);
}

public sealed class CloudSeed
{
    public Guid StoreId { get; set; }
    public Guid ActiveWriterDeviceId { get; set; }
    public long ActiveWriterEpoch { get; set; }
    public long AcceptedThroughSeq { get; set; }
    public long EntryCount { get; set; }
    public string? HeadEntryHash { get; set; }
    public IReadOnlyList<LogEntry> Entries { get; set; } = Array.Empty<LogEntry>();
    public long? ProjectionVersion { get; set; }
    public bool Throttled { get; set; }
}

public sealed class CloudSnapshot
{
    public Guid StoreId { get; init; }
    public Guid ActiveWriterDeviceId { get; init; }
    public long ActiveWriterEpoch { get; init; }
    public long AcceptedThroughSeq { get; init; }
    public long EntryCount { get; init; }
    public string? HeadEntryHash { get; init; }
    public long ProjectionVersion { get; init; }
    public int OperationIdCount { get; init; }
    public IReadOnlyList<LogEntry> Entries { get; init; } = Array.Empty<LogEntry>();
}

public sealed class CloudIngestResult
{
    private CloudIngestResult(int httpStatus, object body)
    {
        HttpStatus = httpStatus;
        Body = body;
    }

    public int HttpStatus { get; }
    public object Body { get; }
    public bool IsSuccess => HttpStatus == 200;

    public static CloudIngestResult Success(IngestAck ack) => new(200, ack);

    public static CloudIngestResult Failure(int httpStatus, string code, string message, Dictionary<string, object?> retryHint)
        => new(httpStatus, new ErrorResponse { Code = code, Message = message, RetryHint = retryHint });
}

public static class CloudErrors
{
    public static Dictionary<string, object?> Hint(string action, params (string Key, object? Value)[] fields)
    {
        var hint = new Dictionary<string, object?>(StringComparer.Ordinal) { ["action"] = action };
        foreach (var (key, value) in fields)
        {
            if (value is not null)
            {
                hint[key] = value;
            }
        }

        return hint;
    }

    public static CloudIngestResult Failure(
        int httpStatus,
        string code,
        string message,
        string action,
        params (string Key, object? Value)[] fields)
        => CloudIngestResult.Failure(httpStatus, code, message, Hint(action, fields));
}
