using System.Text.Json;
using Kandoo.Core;
using Npgsql;
using NpgsqlTypes;

namespace Kandoo.Cloud;

/// <summary>
/// PostgreSQL Cloud replica. A store row is locked for the whole validation and
/// commit transaction; no suffix, projection version, or watermark is mutated
/// outside that transaction.
/// </summary>
public sealed class PostgresCloudStore : ICloudStore, IPublicInvoiceStore
{
    public const int MaxBatchEntries = 100;
    public const int MaxBatchBytes = 1_048_576;
    public const int ThrottledMaxBatchEntries = 2;
    public const int ThrottledMaxBatchBytes = 65_536;
    public const int ThrottledRetryAfterMs = 2_000;
    public const string ZeroHash = "0000000000000000000000000000000000000000000000000000000000000000";

    private const string SchemaSql = """
        CREATE TABLE IF NOT EXISTS cloud_stores (
            store_id uuid PRIMARY KEY,
            active_writer_device_id uuid NOT NULL,
            active_writer_epoch bigint NOT NULL,
            accepted_through_seq bigint NOT NULL,
            entry_count bigint NOT NULL,
            head_entry_hash text NULL,
            projection_version bigint NOT NULL,
            throttled boolean NOT NULL
        );
        CREATE TABLE IF NOT EXISTS cloud_entries (
            store_id uuid NOT NULL REFERENCES cloud_stores(store_id) ON DELETE CASCADE,
            operation_seq bigint NOT NULL,
            operation_id text NOT NULL,
            entry_hash text NOT NULL,
            entry_json jsonb NOT NULL,
            PRIMARY KEY (store_id, operation_seq),
            CONSTRAINT cloud_entries_store_operation_unique UNIQUE (store_id, operation_id)
        );
        CREATE TABLE IF NOT EXISTS cloud_batches (
            store_id uuid NOT NULL REFERENCES cloud_stores(store_id) ON DELETE CASCADE,
            batch_id text NOT NULL,
            fingerprint text NOT NULL,
            PRIMARY KEY (store_id, batch_id)
        );
        CREATE TABLE IF NOT EXISTS public_invoices (
            public_token text PRIMARY KEY,
            digital_invoice_id uuid NOT NULL,
            invoice_id uuid NOT NULL,
            store_id uuid NOT NULL,
            store_name text NOT NULL,
            display_number bigint NOT NULL,
            lifecycle_status text NOT NULL,
            issued_at timestamptz NOT NULL,
            issued_payload jsonb NOT NULL,
            issued_content_sha256 text NOT NULL,
            payable_rials bigint NOT NULL,
            line_items jsonb NOT NULL,
            customer_phone text NULL,
            CONSTRAINT public_invoices_store_invoice_unique UNIQUE (store_id, invoice_id)
        );
        ALTER TABLE public_invoices ADD COLUMN IF NOT EXISTS customer_phone text NULL;
        """;

    private readonly string _connectionString;
    private readonly object _schemaSync = new();
    private bool _schemaReady;

    public PostgresCloudStore(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("A PostgreSQL connection string is required.", nameof(connectionString));
        }

        _connectionString = connectionString;
    }

    /// <summary>Replaces one synthetic test store atomically; production writes use Ingest.</summary>
    public void Seed(CloudSeed seed)
    {
        var head = seed.HeadEntryHash
            ?? seed.Entries.OrderBy(entry => entry.OperationSeq).LastOrDefault()?.EntryHash
            ?? (seed.AcceptedThroughSeq > 0 ? ZeroHash : null);
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using (var delete = new NpgsqlCommand("DELETE FROM cloud_stores WHERE store_id = @store", connection, transaction))
        {
            delete.Parameters.AddWithValue("store", seed.StoreId);
            delete.ExecuteNonQuery();
        }

        using (var insert = new NpgsqlCommand(
                   "INSERT INTO cloud_stores (store_id, active_writer_device_id, active_writer_epoch, accepted_through_seq, entry_count, head_entry_hash, projection_version, throttled) VALUES (@store, @writer, @epoch, @accepted, @count, @head, @projection, @throttled)",
                   connection,
                   transaction))
        {
            insert.Parameters.AddWithValue("store", seed.StoreId);
            insert.Parameters.AddWithValue("writer", seed.ActiveWriterDeviceId);
            insert.Parameters.AddWithValue("epoch", seed.ActiveWriterEpoch);
            insert.Parameters.AddWithValue("accepted", seed.AcceptedThroughSeq);
            insert.Parameters.AddWithValue("count", seed.EntryCount);
            insert.Parameters.AddWithValue("head", NpgsqlDbType.Text, (object?)head ?? DBNull.Value);
            insert.Parameters.AddWithValue("projection", seed.ProjectionVersion ?? seed.AcceptedThroughSeq);
            insert.Parameters.AddWithValue("throttled", seed.Throttled);
            insert.ExecuteNonQuery();
        }

        foreach (var entry in seed.Entries)
        {
            InsertEntry(connection, transaction, seed.StoreId, entry);
        }

        transaction.Commit();
    }

    public CloudSnapshot? GetSnapshot(Guid storeId)
    {
        using var connection = Open();
        var state = LoadState(connection, null, storeId, false);
        return state is null ? null : Snapshot(state);
    }

    /// <summary>Seeds one synthetic issued projection and returns its capability token.</summary>
    public string SeedPublicInvoice(PublicInvoiceSeed seed)
    {
        var token = string.IsNullOrWhiteSpace(seed.PublicToken) ? PublicTokenGenerator.NewToken() : seed.PublicToken!;
        if (!PublicTokenGenerator.IsValid(token)
            || seed.InvoiceId == Guid.Empty
            || seed.StoreId == Guid.Empty
            || string.IsNullOrWhiteSpace(seed.StoreName)
            || seed.DisplayNumber <= 0
            || seed.LifecycleStatus is not ("ISSUED" or "REVOKED" or "SUPERSEDED")
            || seed.IssuedPayload.ValueKind != JsonValueKind.Object
            || string.IsNullOrWhiteSpace(seed.IssuedContentSha256)
            || seed.PayableRials < 0)
        {
            throw new ArgumentException("A valid issued public-invoice projection is required.", nameof(seed));
        }

        var digitalInvoiceId = seed.DigitalInvoiceId == Guid.Empty ? Guid.NewGuid() : seed.DigitalInvoiceId;
        var customerPhone = string.IsNullOrWhiteSpace(seed.CustomerPhone) ? null : PhoneNormalizer.Normalize(seed.CustomerPhone);
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using (var delete = new NpgsqlCommand(
                   "DELETE FROM public_invoices WHERE public_token = @token OR (store_id = @store AND invoice_id = @invoice)",
                   connection,
                   transaction))
        {
            delete.Parameters.AddWithValue("token", token);
            delete.Parameters.AddWithValue("store", seed.StoreId);
            delete.Parameters.AddWithValue("invoice", seed.InvoiceId);
            delete.ExecuteNonQuery();
        }

        using (var insert = new NpgsqlCommand(
                   "INSERT INTO public_invoices (public_token, digital_invoice_id, invoice_id, store_id, store_name, display_number, lifecycle_status, issued_at, issued_payload, issued_content_sha256, payable_rials, line_items, customer_phone) VALUES (@token, @digital, @invoice, @store, @name, @display, @status, @issuedAt, @payload, @hash, @payable, @items, @phone)",
                   connection,
                   transaction))
        {
            insert.Parameters.AddWithValue("token", token);
            insert.Parameters.AddWithValue("digital", digitalInvoiceId);
            insert.Parameters.AddWithValue("invoice", seed.InvoiceId);
            insert.Parameters.AddWithValue("store", seed.StoreId);
            insert.Parameters.AddWithValue("name", seed.StoreName);
            insert.Parameters.AddWithValue("display", seed.DisplayNumber);
            insert.Parameters.AddWithValue("status", seed.LifecycleStatus);
            insert.Parameters.AddWithValue("issuedAt", seed.IssuedAt.ToUniversalTime());
            insert.Parameters.AddWithValue("payload", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(seed.IssuedPayload, CloudJson.Options));
            insert.Parameters.AddWithValue("hash", seed.IssuedContentSha256);
            insert.Parameters.AddWithValue("payable", seed.PayableRials);
            insert.Parameters.AddWithValue("items", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(seed.LineItems, CloudJson.Options));
            insert.Parameters.AddWithValue("phone", NpgsqlDbType.Text, (object?)customerPhone ?? DBNull.Value);
            insert.ExecuteNonQuery();
        }

        transaction.Commit();
        return token;
    }

    public PublicInvoice? GetPublicInvoice(string token)
    {
        if (!PublicTokenGenerator.IsValid(token))
        {
            return null;
        }

        using var connection = Open();
        using var command = new NpgsqlCommand(
            "SELECT invoice_id, store_id, store_name, display_number, lifecycle_status, issued_at, issued_payload, issued_content_sha256, payable_rials, line_items FROM public_invoices WHERE public_token = @token AND lifecycle_status IN ('ISSUED', 'REVOKED', 'SUPERSEDED') LIMIT 1",
            connection);
        command.Parameters.AddWithValue("token", token);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        var invoiceId = reader.GetGuid(0);
        var storeId = reader.GetGuid(1);
        var storeName = reader.GetString(2);
        var displayNumber = reader.GetInt64(3);
        var lifecycleStatus = reader.GetString(4);
        var issuedAt = reader.GetFieldValue<DateTimeOffset>(5);
        using var payloadDocument = JsonDocument.Parse(reader.GetString(6));
        var issuedPayload = payloadDocument.RootElement.Clone();
        var contentHash = reader.GetString(7);
        var payable = reader.GetInt64(8);
        var lineItems = JsonSerializer.Deserialize<PublicInvoiceLine[]>(reader.GetString(9), CloudJson.Options)
            ?? Array.Empty<PublicInvoiceLine>();
        var digitalInvoice = new PublicDigitalInvoice
        {
            InvoiceId = invoiceId,
            StoreId = storeId,
            IssuedPayload = issuedPayload,
            IssuedContentSha256 = contentHash,
            IssuedAt = issuedAt
        };
        var view = new PublicInvoiceView
        {
            StoreName = storeName,
            DisplayNumber = displayNumber,
            LifecycleStatus = lifecycleStatus,
            IssuedAt = issuedAt,
            DigitalInvoice = digitalInvoice
        };
        return new PublicInvoice(view, lineItems, payable);
    }

    internal void EnsureInitialized() => EnsureSchema();

    private NpgsqlConnection Open()
    {
        EnsureSchema();
        var connection = new NpgsqlConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private void EnsureSchema()
    {
        if (_schemaReady)
        {
            return;
        }

        lock (_schemaSync)
        {
            if (_schemaReady)
            {
                return;
            }

            using var connection = new NpgsqlConnection(_connectionString);
            connection.Open();
            using var command = new NpgsqlCommand(SchemaSql, connection);
            command.ExecuteNonQuery();
            _schemaReady = true;
        }
    }

    private static StoreState? LoadState(NpgsqlConnection connection, NpgsqlTransaction? transaction, Guid storeId, bool forUpdate)
    {
        var sql = "SELECT store_id, active_writer_device_id, active_writer_epoch, accepted_through_seq, entry_count, head_entry_hash, projection_version, throttled FROM cloud_stores WHERE store_id = @store"
            + (forUpdate ? " FOR UPDATE" : string.Empty);
        StoreState state;
        using (var command = new NpgsqlCommand(sql, connection, transaction))
        {
            command.Parameters.AddWithValue("store", storeId);
            using var reader = command.ExecuteReader();
            if (!reader.Read())
            {
                return null;
            }

            state = new StoreState
            {
                StoreId = reader.GetGuid(0),
                ActiveWriterDeviceId = reader.GetGuid(1),
                ActiveWriterEpoch = reader.GetInt64(2),
                AcceptedThroughSeq = reader.GetInt64(3),
                EntryCount = reader.GetInt64(4),
                HeadEntryHash = reader.IsDBNull(5) ? null : reader.GetString(5),
                ProjectionVersion = reader.GetInt64(6),
                Throttled = reader.GetBoolean(7)
            };
        }

        using (var command = new NpgsqlCommand("SELECT entry_json FROM cloud_entries WHERE store_id = @store ORDER BY operation_seq", connection, transaction))
        {
            command.Parameters.AddWithValue("store", storeId);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var entry = JsonSerializer.Deserialize<LogEntry>(reader.GetString(0), CloudJson.Options)!;
                state.Entries[entry.OperationSeq] = entry;
                state.OperationIndex[entry.OperationId] = entry;
            }
        }

        using (var command = new NpgsqlCommand("SELECT batch_id, fingerprint FROM cloud_batches WHERE store_id = @store", connection, transaction))
        {
            command.Parameters.AddWithValue("store", storeId);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                state.BatchRecords[reader.GetString(0)] = new BatchRecord(reader.GetString(1));
            }
        }

        return state;
    }

    private static CloudSnapshot Snapshot(StoreState state) => new()
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

    public CloudIngestResult Ingest(Guid routeStoreId, OperationBatch batch, string? idempotencyKey)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        var state = LoadState(connection, transaction, routeStoreId, true);
        if (state is null)
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

        // Commit point: no code above this point mutates the database.
        foreach (var item in newEntries)
        {
            var copy = item.Entry.Clone();
            state.Entries[copy.OperationSeq] = copy;
            state.OperationIndex[copy.OperationId] = copy;
            InsertEntry(connection, transaction, state.StoreId, copy);
        }

        if (newEntries.Count > 0)
        {
            state.AcceptedThroughSeq = newEntries[^1].Entry.OperationSeq;
            state.EntryCount += newEntries.Count;
            state.HeadEntryHash = newEntries[^1].ComputedHash;
            state.ProjectionVersion += newEntries.Count;
            UpdateStore(connection, transaction, state);
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
        state.BatchRecords[batch.BatchId] = new BatchRecord(fingerprint);
        SaveBatchRecord(connection, transaction, state.StoreId, batch.BatchId, fingerprint);
        transaction.Commit();
        return result;
    }

    private static void InsertEntry(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid storeId, LogEntry entry)
    {
        using var command = new NpgsqlCommand(
            "INSERT INTO cloud_entries (store_id, operation_seq, operation_id, entry_hash, entry_json) VALUES (@store, @sequence, @operation, @hash, @entry)",
            connection,
            transaction);
        command.Parameters.AddWithValue("store", storeId);
        command.Parameters.AddWithValue("sequence", entry.OperationSeq);
        command.Parameters.AddWithValue("operation", entry.OperationId);
        command.Parameters.AddWithValue("hash", entry.EntryHash);
        command.Parameters.AddWithValue("entry", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(entry, CloudJson.Options));
        command.ExecuteNonQuery();
    }

    private static void UpdateStore(NpgsqlConnection connection, NpgsqlTransaction transaction, StoreState state)
    {
        using var command = new NpgsqlCommand(
            "UPDATE cloud_stores SET accepted_through_seq = @accepted, entry_count = @count, head_entry_hash = @head, projection_version = @projection WHERE store_id = @store",
            connection,
            transaction);
        command.Parameters.AddWithValue("store", state.StoreId);
        command.Parameters.AddWithValue("accepted", state.AcceptedThroughSeq);
        command.Parameters.AddWithValue("count", state.EntryCount);
        command.Parameters.AddWithValue("head", NpgsqlDbType.Text, (object?)state.HeadEntryHash ?? DBNull.Value);
        command.Parameters.AddWithValue("projection", state.ProjectionVersion);
        command.ExecuteNonQuery();
    }

    private static void SaveBatchRecord(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid storeId, string batchId, string fingerprint)
    {
        using var command = new NpgsqlCommand(
            "INSERT INTO cloud_batches (store_id, batch_id, fingerprint) VALUES (@store, @batch, @fingerprint) ON CONFLICT (store_id, batch_id) DO UPDATE SET fingerprint = EXCLUDED.fingerprint",
            connection,
            transaction);
        command.Parameters.AddWithValue("store", storeId);
        command.Parameters.AddWithValue("batch", batchId);
        command.Parameters.AddWithValue("fingerprint", fingerprint);
        command.ExecuteNonQuery();
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

    private sealed record BatchRecord(string Fingerprint);
}
