using System.Text.Json;
using Kandoo.Core;

namespace Kandoo.Cloud;

/// <summary>
/// Atomic in-memory Cloud replica for the C-03 vertical slice. All validation
/// completes before a suffix, projection version, or watermark is mutated.
/// </summary>
public sealed class InMemoryCloudStore
{
    public const int MaxBatchEntries = 100;
    public const int MaxBatchBytes = 1_048_576;
    public const int ThrottledMaxBatchEntries = 2;
    public const int ThrottledMaxBatchBytes = 65_536;
    public const int ThrottledRetryAfterMs = 2_000;
    public const string ZeroHash = "0000000000000000000000000000000000000000000000000000000000000000";

    private readonly object _sync = new();
    private readonly Dictionary<Guid, StoreState> _stores = new();

    public void Seed(CloudSeed seed)
    {
        lock (_sync)
        {
            var state = new StoreState
            {
                StoreId = seed.StoreId,
                ActiveWriterDeviceId = seed.ActiveWriterDeviceId,
                ActiveWriterEpoch = seed.ActiveWriterEpoch,
                AcceptedThroughSeq = seed.AcceptedThroughSeq,
                EntryCount = seed.EntryCount,
                ProjectionVersion = seed.ProjectionVersion ?? seed.AcceptedThroughSeq,
                Throttled = seed.Throttled
            };

            foreach (var entry in seed.Entries)
            {
                var copy = entry.Clone();
                state.Entries[copy.OperationSeq] = copy;
                state.OperationIndex[copy.OperationId] = copy;
            }

            state.HeadEntryHash = seed.HeadEntryHash
                ?? state.Entries.Values.OrderBy(entry => entry.OperationSeq).LastOrDefault()?.EntryHash
                ?? (seed.AcceptedThroughSeq > 0 ? ZeroHash : null);
            _stores[seed.StoreId] = state;
        }
    }

    public CloudSnapshot? GetSnapshot(Guid storeId)
    {
        lock (_sync)
        {
            if (!_stores.TryGetValue(storeId, out var state))
            {
                return null;
            }

            return new CloudSnapshot
            {
                StoreId = state.StoreId,
                ActiveWriterDeviceId = state.ActiveWriterDeviceId,
                ActiveWriterEpoch = state.ActiveWriterEpoch,
                AcceptedThroughSeq = state.AcceptedThroughSeq,
                EntryCount = state.EntryCount,
                HeadEntryHash = state.HeadEntryHash,
                ProjectionVersion = state.ProjectionVersion,
                OperationIdCount = state.OperationIndex.Count,
                Entries = state.Entries.Values.OrderBy(entry => entry.OperationSeq).Select(entry => entry.Clone()).ToArray()
            };
        }
    }

    public CloudIngestResult Ingest(Guid routeStoreId, OperationBatch batch, string? idempotencyKey)
    {
        lock (_sync)
        {
            if (!_stores.TryGetValue(routeStoreId, out var state))
            {
                return CloudErrors.Failure(404, "STORE_NOT_FOUND", "The store is not available in this scope.", "NONE");
            }

            // Fencing deliberately precedes empty-batch and other shape checks (CLOUD-008).
            if (batch.WriterEpoch != state.ActiveWriterEpoch || batch.WriterDeviceId != state.ActiveWriterDeviceId)
            {
                return CloudErrors.Failure(
                    409,
                    "WRITER_FENCED",
                    "The writer is not the active Hub epoch; stop new writes and review unsynced entries.",
                    "STOP_WRITER",
                    ("accepted_through_seq", state.AcceptedThroughSeq),
                    ("current_version", state.ActiveWriterEpoch));
            }

            if (string.IsNullOrWhiteSpace(idempotencyKey))
            {
                return CloudErrors.Failure(400, "IDEMPOTENCY_KEY_REQUIRED", "Idempotency-Key is required.", "FIX_REQUEST");
            }

            if (string.IsNullOrWhiteSpace(batch.BatchId))
            {
                return CloudErrors.Failure(400, "VALIDATION_ERROR", "batch_id is required.", "FIX_REQUEST");
            }

            if (!string.Equals(idempotencyKey, batch.BatchId, StringComparison.Ordinal))
            {
                return CloudErrors.Failure(409, "IDEMPOTENCY_CONFLICT", "Idempotency-Key must equal batch_id.", "REVIEW");
            }

            if (batch.StoreId != routeStoreId)
            {
                return CloudErrors.Failure(400, "VALIDATION_ERROR", "The batch store does not match the route store.", "FIX_REQUEST");
            }

            // Backpressure is evaluated before batch completeness so a throttled
            // harness batch cannot be mistaken for an accepted or partial write.
            if (state.Throttled)
            {
                return CloudErrors.Failure(
                    429,
                    "THROTTLED",
                    "Cloud ingest is throttled; retain the outbox and retry the unchanged batch later.",
                    "RETRY_AFTER",
                    ("retry_after_ms", ThrottledRetryAfterMs),
                    ("max_batch_entries", ThrottledMaxBatchEntries),
                    ("max_batch_bytes", ThrottledMaxBatchBytes),
                    ("accepted_through_seq", state.AcceptedThroughSeq));
            }

            if (batch.Entries is null || batch.Entries.Count == 0)
            {
                return CloudErrors.Failure(
                    400,
                    "INCOMPLETE_BATCH",
                    "An active writer must supply a complete non-empty batch.",
                    "RETRY_SAME",
                    ("accepted_through_seq", state.AcceptedThroughSeq));
            }

            if (batch.Entries.Count > MaxBatchEntries)
            {
                return CloudErrors.Failure(
                    413,
                    "PAYLOAD_TOO_LARGE",
                    "The batch exceeds the entry limit; split only at a valid batch boundary.",
                    "FIX_REQUEST",
                    ("max_batch_entries", MaxBatchEntries),
                    ("max_batch_bytes", MaxBatchBytes),
                    ("accepted_through_seq", state.AcceptedThroughSeq));
            }

            string fingerprint;
            try
            {
                fingerprint = CanonicalJson.Sha256Hex(JsonSerializer.SerializeToElement(batch, CloudJson.Options));
            }
            catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
            {
                return CloudErrors.Failure(400, "VALIDATION_ERROR", "The batch contains a value outside the integer JSON profile.", "FIX_REQUEST");
            }

            if (state.BatchRecords.TryGetValue(batch.BatchId, out var priorBatch))
            {
                if (!string.Equals(priorBatch.Fingerprint, fingerprint, StringComparison.Ordinal))
                {
                    return CloudErrors.Failure(409, "IDEMPOTENCY_CONFLICT", "The batch key was already used for different content.", "REVIEW");
                }

                // Ingest retries are revalidated so an already committed batch
                // returns DUPLICATE with zero new ingests, rather than replaying
                // the first ACK's ingested_count (CLOUD-005).
            }

            var validation = ValidateEntries(state, batch, out var validated, out var duplicateCount);
            if (validation is not null)
            {
                return validation;
            }

            var newEntries = new List<ValidatedEntry>();
            var newOperationIds = new HashSet<string>(StringComparer.Ordinal);
            var firstNewIndex = validated.FindIndex(item => item.Entry.OperationSeq > state.AcceptedThroughSeq);
            if (firstNewIndex >= 0 && validated[firstNewIndex].Entry.OperationSeq != state.AcceptedThroughSeq + 1)
            {
                return Gap(state, validated[firstNewIndex].Entry.OperationSeq);
            }

            for (var index = 0; index < validated.Count; index++)
            {
                var item = validated[index];
                var entry = item.Entry;
                if (entry.OperationSeq <= state.AcceptedThroughSeq)
                {
                    if (!state.Entries.TryGetValue(entry.OperationSeq, out var existing))
                    {
                        return CloudErrors.Failure(
                            409,
                            "LOG_CONFLICT",
                            "The historical entry cannot be hash-verified; no suffix was applied.",
                            "REVIEW",
                            ("accepted_through_seq", state.AcceptedThroughSeq),
                            ("received_seq", entry.OperationSeq));
                    }

                    if (!string.Equals(existing.EntryHash, item.ComputedHash, StringComparison.Ordinal))
                    {
                        return CloudErrors.Failure(
                            409,
                            "LOG_CONFLICT",
                            "An existing operation sequence has different content; Cloud will not overwrite it.",
                            "REVIEW",
                            ("accepted_through_seq", state.AcceptedThroughSeq),
                            ("received_seq", entry.OperationSeq));
                    }

                    duplicateCount++;
                    continue;
                }

                if (state.OperationIndex.ContainsKey(entry.OperationId) || !newOperationIds.Add(entry.OperationId))
                {
                    return CloudErrors.Failure(
                        409,
                        "OPERATION_ID_CONFLICT",
                        "The operation identity is already present with a different log position or content.",
                        "REVIEW",
                        ("accepted_through_seq", state.AcceptedThroughSeq),
                        ("received_seq", entry.OperationSeq));
                }

                newEntries.Add(item);
            }

            if (newEntries.Count > 0)
            {
                var firstNew = newEntries[0].Entry;
                var expectedPrevious = state.HeadEntryHash ?? ZeroHash;
                if (!string.Equals(firstNew.PreviousEntryHash, expectedPrevious, StringComparison.Ordinal))
                {
                    return EntryHashFailure(state, firstNew.EntryHash, expectedPrevious, firstNew.OperationSeq);
                }
            }

            // Commit point: no code above this point mutates the store.
            foreach (var item in newEntries)
            {
                var copy = item.Entry.Clone();
                state.Entries[copy.OperationSeq] = copy;
                state.OperationIndex[copy.OperationId] = copy;
            }

            if (newEntries.Count > 0)
            {
                state.AcceptedThroughSeq = newEntries[^1].Entry.OperationSeq;
                state.EntryCount += newEntries.Count;
                state.HeadEntryHash = newEntries[^1].ComputedHash;
                state.ProjectionVersion += newEntries.Count;
            }

            var ack = new IngestAck
            {
                BatchId = batch.BatchId,
                Status = newEntries.Count == 0 ? "DUPLICATE" : "ACCEPTED",
                AcceptedThroughSeq = state.AcceptedThroughSeq,
                IngestedCount = newEntries.Count,
                DuplicateCount = duplicateCount,
                HeadEntryHash = state.HeadEntryHash,
                NextExpectedSeq = state.AcceptedThroughSeq + 1,
                CloudCursor = newEntries.Count == 0 ? null : $"{state.StoreId:N}:{state.AcceptedThroughSeq}"
            };
            var result = CloudIngestResult.Success(ack);
            state.BatchRecords[batch.BatchId] = new BatchRecord(fingerprint, result);
            return result;
        }
    }

    private static CloudIngestResult? ValidateEntries(
        StoreState state,
        OperationBatch batch,
        out List<ValidatedEntry> validated,
        out int duplicateCount)
    {
        validated = new List<ValidatedEntry>(batch.Entries!.Count);
        duplicateCount = 0;
        long previousSequence = 0;

        for (var index = 0; index < batch.Entries.Count; index++)
        {
            var entry = batch.Entries[index];
            if (entry.StoreId != batch.StoreId
                || entry.WriterDeviceId != batch.WriterDeviceId
                || entry.WriterEpoch != batch.WriterEpoch
                || !string.Equals(entry.ProtocolVersion, "0", StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(entry.OperationId)
                || string.IsNullOrWhiteSpace(entry.OperationType)
                || entry.OperationSeq < 1
                || entry.Payload.ValueKind != JsonValueKind.Object
                || entry.Outcome.ValueKind != JsonValueKind.Object
                || entry.ResponseSnapshot.ValueKind != JsonValueKind.Object)
            {
                return CloudErrors.Failure(400, "VALIDATION_ERROR", "An entry is outside the store, writer, or P-02 envelope contract.", "FIX_REQUEST");
            }

            if (index > 0 && entry.OperationSeq != previousSequence + 1)
            {
                return CloudErrors.Failure(
                    400,
                    "SEQUENCE_ORDER_VIOLATION",
                    "Entries must be strictly ascending and contiguous inside a batch.",
                    "RESUME_FROM_SEQUENCE",
                    ("expected_seq", previousSequence + 1),
                    ("received_seq", entry.OperationSeq),
                    ("accepted_through_seq", state.AcceptedThroughSeq));
            }

            string computedRequest;
            string computedEntry;
            try
            {
                computedRequest = CanonicalJson.RequestHash(
                    entry.StoreId,
                    entry.DeviceId,
                    entry.OperationId,
                    entry.OperationType,
                    entry.BaseVersion,
                    entry.Payload);
                computedEntry = CanonicalJson.EntryHash(JsonSerializer.SerializeToElement(entry, CloudJson.Options));
            }
            catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
            {
                return CloudErrors.Failure(400, "VALIDATION_ERROR", "The entry contains a value outside the P-02 integer JSON profile.", "FIX_REQUEST");
            }

            if (!string.Equals(computedRequest, entry.RequestHash, StringComparison.Ordinal))
            {
                return CloudErrors.Failure(
                    400,
                    "REQUEST_HASH_MISMATCH",
                    "The request hash does not match the canonical P-02 request.",
                    "REVIEW",
                    ("accepted_through_seq", state.AcceptedThroughSeq),
                    ("details", new Dictionary<string, object?>
                    {
                        ["expected_request_hash"] = computedRequest,
                        ["supplied_request_hash"] = entry.RequestHash
                    }));
            }

            if (!string.Equals(computedEntry, entry.EntryHash, StringComparison.Ordinal))
            {
                return EntryHashFailure(state, entry.EntryHash, computedEntry, entry.OperationSeq);
            }

            if (index > 0 && !string.Equals(entry.PreviousEntryHash, validated[index - 1].ComputedHash, StringComparison.Ordinal))
            {
                return EntryHashFailure(state, entry.PreviousEntryHash, validated[index - 1].ComputedHash, entry.OperationSeq);
            }

            validated.Add(new ValidatedEntry(entry, computedEntry));
            previousSequence = entry.OperationSeq;
        }

        return null;
    }

    private static CloudIngestResult Gap(StoreState state, long receivedSequence)
        => CloudErrors.Failure(
            409,
            "GAP_DETECTED",
            "Upload the earliest missing operation sequence before retrying the batch.",
            "RESUME_FROM_SEQUENCE",
            ("expected_seq", state.AcceptedThroughSeq + 1),
            ("received_seq", receivedSequence),
            ("accepted_through_seq", state.AcceptedThroughSeq));

    private static CloudIngestResult EntryHashFailure(StoreState state, string suppliedHash, string recomputedHash, long sequence)
        => CloudErrors.Failure(
            400,
            "ENTRY_HASH_MISMATCH",
            "The entry hash chain failed verification; the entry is retained for review, not applied.",
            "REVIEW",
            ("accepted_through_seq", state.AcceptedThroughSeq),
            ("received_seq", sequence),
            ("details", new Dictionary<string, object?>
            {
                ["supplied_entry_hash"] = suppliedHash,
                ["recomputed_entry_hash"] = recomputedHash,
                ["quarantine"] = "REVIEW"
            }));

    private sealed class StoreState
    {
        public required Guid StoreId { get; init; }
        public required Guid ActiveWriterDeviceId { get; set; }
        public required long ActiveWriterEpoch { get; set; }
        public long AcceptedThroughSeq { get; set; }
        public long EntryCount { get; set; }
        public string? HeadEntryHash { get; set; }
        public long ProjectionVersion { get; set; }
        public bool Throttled { get; set; }
        public Dictionary<long, LogEntry> Entries { get; } = new();
        public Dictionary<string, LogEntry> OperationIndex { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, BatchRecord> BatchRecords { get; } = new(StringComparer.Ordinal);
    }

    private sealed record ValidatedEntry(LogEntry Entry, string ComputedHash);

    private sealed record BatchRecord(string Fingerprint, CloudIngestResult Result);
}
