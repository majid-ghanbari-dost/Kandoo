using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using Kandoo.Core;

namespace Kandoo.Hub;

public sealed class HttpCloudReplicationTransport : ICloudReplicationTransport
{
    private readonly HttpClient _client;

    public HttpCloudReplicationTransport(HttpClient client) => _client = client ?? throw new ArgumentNullException(nameof(client));

    public async Task<CloudTransportResponse> SendAsync(ReplicationBatch batch, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(batch, HubJson.Options);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/v1/stores/{batch.StoreId:D}/operation-batches")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Idempotency-Key", batch.BatchId);
        using var response = await _client.SendAsync(request, cancellationToken);
        JsonElement? body = null;
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(content))
        {
            using var document = JsonDocument.Parse(content);
            body = document.RootElement.Clone();
        }

        return new CloudTransportResponse(response.StatusCode, body, response.Headers.RetryAfter?.Delta);
    }
}

public sealed class HubCloudReplicationClient
{
    private readonly IHubReplicationSource _source;
    private readonly ICloudReplicationTransport _transport;
    private readonly HubReplicationOptions _options;
    private readonly IClock _clock;
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _storeLocks = new();

    public HubCloudReplicationClient(
        IHubReplicationSource source,
        ICloudReplicationTransport transport,
        HubReplicationOptions? options = null,
        IClock? clock = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _options = options ?? new HubReplicationOptions();
        _clock = clock ?? new SystemHubClock();
        if (_options.MaxAttempts <= 0 || _options.MaxBatchEntries <= 0 || _options.MaxBatchBytes <= 0)
        {
            throw new ArgumentException("Replication attempts and batch limits must be positive.", nameof(options));
        }
    }

    public async Task<HubReplicationResult> ReplicateAsync(Guid storeId, CancellationToken cancellationToken = default)
    {
        if (storeId == Guid.Empty)
        {
            throw new ArgumentException("A store ID is required.", nameof(storeId));
        }

        var storeLock = _storeLocks.GetOrAdd(storeId, _ => new SemaphoreSlim(1, 1));
        await storeLock.WaitAsync(cancellationToken);
        try
        {
            var maxEntries = _options.MaxBatchEntries;
            var maxBytes = _options.MaxBatchBytes;
            HubReplicationResult result = new("IDLE", 0, _source.GetOutboxDepth(storeId), 0);
            for (var attempt = 1; attempt <= _options.MaxAttempts; attempt++)
            {
                result = await AttemptAsync(storeId, maxEntries, maxBytes, cancellationToken);
                if (result.Status == "THROTTLED")
                {
                    maxEntries = result.MaxBatchEntries > 0 ? result.MaxBatchEntries : maxEntries;
                    maxBytes = result.MaxBatchBytes > 0 ? result.MaxBatchBytes : maxBytes;
                }

                result = result with
                {
                    Attempts = attempt,
                    MaxBatchEntries = maxEntries,
                    MaxBatchBytes = maxBytes
                };
                if (!result.Retryable || attempt == _options.MaxAttempts)
                {
                    return result;
                }

                var milliseconds = result.RetryAfterMs is > 0
                    ? result.RetryAfterMs.Value
                    : Math.Min(60_000, 1_000 * (1 << (attempt - 1)));
                await _options.DelayAsync(TimeSpan.FromMilliseconds(milliseconds), cancellationToken);
            }

            return result;
        }
        finally
        {
            storeLock.Release();
        }
    }

    private async Task<HubReplicationResult> AttemptAsync(
        Guid storeId,
        int maxEntries,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        var depth = _source.GetOutboxDepth(storeId);
        if (_source.IsWriterFenced(storeId))
        {
            return new HubReplicationResult("WRITER_FENCED", 0, depth, 0, "WRITER_FENCED", Mode: "FENCED_READ_ONLY");
        }

        var batch = _source.ReadBatch(storeId, maxEntries, maxBytes);
        if (batch is null)
        {
            return new HubReplicationResult("IDLE", 0, depth, 0);
        }

        CloudTransportResponse response;
        try
        {
            response = await _transport.SendAsync(batch, cancellationToken);
        }
        catch (Exception exception) when (
            exception is HttpRequestException || exception is IOException || exception is JsonException ||
            (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return new HubReplicationResult("RETRY_REQUIRED", 0, depth, 0, "NETWORK_ERROR", Retryable: true);
        }

        if (response.HttpStatus == HttpStatusCode.OK)
        {
            return Acknowledge(batch, response.Body, depth);
        }

        var code = ReadString(response.Body, "code") ?? (response.HttpStatus == (HttpStatusCode)429 ? "THROTTLED" : "CLOUD_ERROR");
        var hint = ReadObject(response.Body, "retry_hint");
        var acceptedThrough = ReadInt64(hint, "accepted_through_seq") ?? 0;
        switch (code)
        {
            case "GAP_DETECTED":
                return new HubReplicationResult(
                    code, 0, depth, acceptedThrough, code,
                    ReadInt64(hint, "expected_seq"), MaxBatchEntries: maxEntries, MaxBatchBytes: maxBytes);

            case "THROTTLED":
                return new HubReplicationResult(
                    code, 0, depth, acceptedThrough, code,
                    RetryAfterMs: ReadInt32(hint, "retry_after_ms") ?? Seconds(response.RetryAfter),
                    MaxBatchEntries: ReadInt32(hint, "max_batch_entries") ?? maxEntries,
                    MaxBatchBytes: ReadInt32(hint, "max_batch_bytes") ?? maxBytes,
                    Retryable: true);

            case "WRITER_FENCED":
                _source.MarkWriterFenced(storeId, new HubWriterIdentity(batch.WriterDeviceId, batch.WriterEpoch), code);
                return new HubReplicationResult(
                    code, 0, _source.GetOutboxDepth(storeId), acceptedThrough, code, Mode: "FENCED_READ_ONLY");

            default:
                var retryable = (int)response.HttpStatus >= 500;
                return new HubReplicationResult(
                    retryable ? "RETRY_REQUIRED" : code,
                    0,
                    depth,
                    acceptedThrough,
                    code,
                    RetryAfterMs: Seconds(response.RetryAfter),
                    MaxBatchEntries: maxEntries,
                    MaxBatchBytes: maxBytes,
                    Retryable: retryable);
        }
    }

    private HubReplicationResult Acknowledge(ReplicationBatch batch, JsonElement? body, int depth)
    {
        var entries = batch.Entries;
        var status = ReadString(body, "status");
        var acceptedThrough = ReadInt64(body, "accepted_through_seq");
        var headHash = ReadString(body, "head_entry_hash");
        if (entries is null || entries.Count == 0)
        {
            return new HubReplicationResult("REVIEW_REQUIRED", 0, depth, acceptedThrough ?? 0, "INVALID_ACKNOWLEDGEMENT");
        }

        if (status is not ("ACCEPTED" or "DUPLICATE") || acceptedThrough is null || headHash is null)
        {
            return new HubReplicationResult("RETRY_REQUIRED", 0, depth, acceptedThrough ?? 0, "INCOMPLETE_ACKNOWLEDGEMENT", Retryable: true);
        }

        var last = entries[^1];
        if (acceptedThrough != last.OperationSeq || !string.Equals(headHash, last.EntryHash, StringComparison.Ordinal))
        {
            return new HubReplicationResult("REVIEW_REQUIRED", 0, depth, acceptedThrough ?? 0, "ACKNOWLEDGEMENT_MISMATCH");
        }

        _source.Acknowledge(batch.StoreId, last.OperationSeq, last.EntryHash, _clock.UtcNow);
        return new HubReplicationResult(
            status,
            0,
            _source.GetOutboxDepth(batch.StoreId),
            last.OperationSeq);
    }

    private static int? Seconds(TimeSpan? value) => value is { } delay ? (int)Math.Ceiling(delay.TotalSeconds * 1000) : null;

    private static JsonElement? ReadObject(JsonElement? body, string name)
    {
        return body is { ValueKind: JsonValueKind.Object } root && root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object
            ? value
            : null;
    }

    private static string? ReadString(JsonElement? body, string name)
    {
        return body is { ValueKind: JsonValueKind.Object } root && root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static long? ReadInt64(JsonElement? body, string name)
    {
        return body is { ValueKind: JsonValueKind.Object } root &&
               root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)
            ? number
            : null;
    }

    private static int? ReadInt32(JsonElement? body, string name)
    {
        var value = ReadInt64(body, name);
        return value is >= int.MinValue and <= int.MaxValue ? (int)value.Value : null;
    }
}
