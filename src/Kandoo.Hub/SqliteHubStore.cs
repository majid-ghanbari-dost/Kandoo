using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kandoo.Core;
using Microsoft.Data.Sqlite;

namespace Kandoo.Hub;

/// <summary>SQLite-backed Hub store. One connection and one lock make transaction boundaries explicit.</summary>
public sealed partial class SqliteHubStore : IHubStore, IHubAccessStore, IHubReplicationSource
{
    private readonly object _gate = new();
    private readonly SqliteConnection _connection;
    private readonly HubWriterIdentity? _replicationWriter;
    private readonly IClock _clock;
    private bool _disposed;

    public SqliteHubStore(string connectionString, HubWriterIdentity? replicationWriter = null, IClock? clock = null)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("A SQLite connection string is required.", nameof(connectionString));
        }

        _replicationWriter = replicationWriter?.Validate();
        _clock = clock ?? new SystemHubClock();
        _connection = new SqliteConnection(connectionString);
        _connection.Open();
        using var command = _connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON;";
        command.ExecuteNonQuery();
        InitializeSchema();
        InitializeAccessSchema();
        InitializeModeBReviewSchema();
    }

    public OperationResult Execute(OperationRequest request, Func<IHubTransaction, string> action)
    {
        if (request.StoreId == Guid.Empty || request.DeviceId == Guid.Empty
            || string.IsNullOrWhiteSpace(request.OperationId) || string.IsNullOrWhiteSpace(request.OperationType))
        {
            throw new ArgumentException("Every mutation requires store, device, operation ID, and operation type.");
        }

        ArgumentNullException.ThrowIfNull(action);
        lock (_gate)
        {
            ThrowIfDisposed();
            ThrowIfReplicationFenced(request.StoreId);
            using var transaction = new SqliteHubTransaction(_connection);
            var existing = transaction.FindOperation(request.StoreId, request.OperationId);
            if (existing is not null)
            {
                if (existing.DeviceId != request.DeviceId
                    || existing.OperationType != request.OperationType
                    || existing.RequestHash != request.RequestHash)
                {
                    return new OperationResult(OperationExecutionStatus.OperationIdConflict, null, null, "OPERATION_ID_CONFLICT");
                }

                return new OperationResult(OperationExecutionStatus.Replayed, existing.Response, existing.OperationSeq);
            }

            try
            {
                var response = action(transaction);
                var sequence = transaction.NextOperationSequence(request.StoreId);
                var record = new OperationRecord(
                    request.StoreId,
                    request.DeviceId,
                    request.OperationId,
                    request.OperationType,
                    request.RequestHash,
                    sequence,
                    response);
                transaction.AppendOperation(record);
                var writer = _replicationWriter ?? new HubWriterIdentity(request.DeviceId, 1);
                transaction.AppendOutbox(record, request, writer, _clock.UtcNow);
                transaction.Commit();
                return new OperationResult(OperationExecutionStatus.Succeeded, response, sequence);
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
    }

    public OperationResult CreateDraft(
        OperationRequest request,
        Guid invoiceId,
        DateTimeOffset createdAt,
        IEnumerable<InvoiceItem>? items = null,
        string? externalInvoiceNumber = null)
    {
        return Execute(request, transaction =>
        {
            var invoice = new Invoice(invoiceId, request.StoreId, request.OperationId, request.DeviceId, createdAt, externalInvoiceNumber);
            foreach (var item in items ?? Array.Empty<InvoiceItem>())
            {
                invoice.AddItem(item);
            }

            transaction.SaveInvoice(invoice);
            return invoice.InvoiceId.ToString("D");
        });
    }

    public OperationResult IssueInvoice(OperationRequest request, Guid invoiceId, DateTimeOffset issuedAt)
    {
        return Execute(request, transaction =>
        {
            var invoice = transaction.GetInvoice(request.StoreId, invoiceId)
                ?? throw new InvalidOperationException("INVOICE_NOT_FOUND");
            invoice.EnsureIssuable();
            var next = checked(transaction.GetDisplayNumberHighWatermark(request.StoreId) + 1);
            var snapshot = JsonSerializer.SerializeToElement(new IssuedSnapshot(invoice.InvoiceId, next));
            var payload = CanonicalJson.Serialize(snapshot);
            var digest = CanonicalJson.Sha256Hex(snapshot);
            invoice.Issue(next, issuedAt, payload, digest);
            transaction.SaveInvoice(invoice);
            transaction.SetDisplayNumberHighWatermark(request.StoreId, next);
            return payload;
        });
    }

    public void SaveProduct(Product product) => Write(transaction => transaction.SaveProduct(product), product.StoreId);

    public void AppendInventory(InventoryEvent inventoryEvent) => Write(transaction => transaction.AppendInventory(inventoryEvent), inventoryEvent.StoreId);

    public void SaveInvoice(Invoice invoice) => Write(transaction => transaction.SaveInvoice(invoice), invoice.StoreId);

    public Invoice? GetInvoice(Guid storeId, Guid invoiceId)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            using var transaction = new SqliteHubTransaction(_connection);
            return transaction.GetInvoice(storeId, invoiceId);
        }
    }

    public IReadOnlyList<ReplicationLogEntry> ReadReplicationEntries(Guid storeId)
    {
        if (storeId == Guid.Empty)
        {
            throw new ArgumentException("A store is required.", nameof(storeId));
        }

        lock (_gate)
        {
            ThrowIfDisposed();
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT entry_json FROM replication_log WHERE store_id = $store ORDER BY operation_seq;";
            command.Parameters.AddWithValue("$store", storeId.ToString("D"));
            using var reader = command.ExecuteReader();
            var entries = new List<ReplicationLogEntry>();
            while (reader.Read())
            {
                entries.Add(JsonSerializer.Deserialize<ReplicationLogEntry>(reader.GetString(0), HubJson.Options)
                    ?? throw new InvalidOperationException("Invalid durable replication entry."));
            }

            return entries;
        }
    }

    public DateTimeOffset? GetLastSyncAtUtc(Guid storeId)
    {
        if (storeId == Guid.Empty)
        {
            throw new ArgumentException("A store is required.", nameof(storeId));
        }

        lock (_gate)
        {
            ThrowIfDisposed();
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT MAX(acknowledged_at) FROM replication_log WHERE store_id = $store;";
            command.Parameters.AddWithValue("$store", storeId.ToString("D"));
            var persisted = command.ExecuteScalar()?.ToString();
            return string.IsNullOrWhiteSpace(persisted)
                ? null
                : DateTimeOffset.ParseExact(
                    persisted,
                    "O",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None).ToUniversalTime();
        }
    }

    public long GetDisplayNumberHighWatermark(Guid storeId)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            using var transaction = new SqliteHubTransaction(_connection);
            return transaction.GetDisplayNumberHighWatermark(storeId);
        }
    }

    public IReadOnlyList<DraftLock> GetDraftLocks()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            using var transaction = new SqliteHubTransaction(_connection);
            return transaction.GetDraftLocks();
        }
    }

    public void CompleteTimeout(DraftLock draftLock)
    {
        if (draftLock.State != DraftLockState.Released || draftLock.ReleaseReason != LockReleaseReason.Timeout)
        {
            return;
        }

        Write(transaction =>
        {
            var persisted = transaction.GetDraftLock(draftLock.StoreId, draftLock.LockId);
            if (persisted is null || persisted.State == DraftLockState.Released)
            {
                return;
            }

            transaction.SaveDraftLock(draftLock);
            var invoice = transaction.GetInvoice(draftLock.StoreId, draftLock.InvoiceId);
            if (invoice is not null && invoice.RetiredAt is null && invoice.Status != InvoiceStatus.Issued)
            {
                invoice.Retire(draftLock.ReleasedAt ?? draftLock.ExpiresAt, RetirementReason.Timeout);
                transaction.SaveInvoice(invoice);
            }
        }, draftLock.StoreId);
    }

    public int GetOutboxDepth(Guid storeId)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM operation_outbox WHERE store_id = $store AND acknowledged_at IS NULL;";
            command.Parameters.AddWithValue("$store", storeId.ToString("D"));
            return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    public ReplicationBatch? ReadBatch(Guid storeId, int maxEntries, int maxBytes)
    {
        if (maxEntries <= 0 || maxBytes <= 0)
        {
            throw new ArgumentException("Batch limits must be positive.");
        }

        lock (_gate)
        {
            ThrowIfDisposed();
            var entries = new List<ReplicationLogEntry>();
            using (var command = _connection.CreateCommand())
            {
                command.CommandText = "SELECT entry_json FROM replication_log WHERE store_id = $store AND acknowledged_at IS NULL ORDER BY operation_seq;";
                command.Parameters.AddWithValue("$store", storeId.ToString("D"));
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    entries.Add(JsonSerializer.Deserialize<ReplicationLogEntry>(reader.GetString(0), HubJson.Options)
                        ?? throw new InvalidOperationException("Invalid durable replication entry."));
                }
            }

            if (entries.Count == 0)
            {
                return null;
            }

            var expected = entries[0].OperationSeq;
            var previousHash = entries[0].PreviousEntryHash;
            if (expected > 1)
            {
                using var command = _connection.CreateCommand();
                command.CommandText = "SELECT entry_hash FROM replication_log WHERE store_id = $store AND operation_seq = $previous AND acknowledged_at IS NOT NULL;";
                command.Parameters.AddWithValue("$store", storeId.ToString("D"));
                command.Parameters.AddWithValue("$previous", expected - 1);
                var persistedPrevious = command.ExecuteScalar()?.ToString();
                if (!string.Equals(previousHash, persistedPrevious, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"LOCAL_REPLICATION_GAP:{expected - 1}");
                }
            }

            foreach (var entry in entries)
            {
                if (entry.OperationSeq != expected || !string.Equals(entry.PreviousEntryHash, previousHash, StringComparison.Ordinal) ||
                    entry.WriterDeviceId != entries[0].WriterDeviceId || entry.WriterEpoch != entries[0].WriterEpoch)
                {
                    throw new InvalidOperationException($"LOCAL_REPLICATION_INVALID:{entry.OperationSeq}");
                }

                expected = checked(expected + 1);
                previousHash = entry.EntryHash;
            }

            var selected = new List<ReplicationLogEntry>();
            foreach (var entry in entries)
            {
                selected.Add(entry);
                var candidate = ReplicationBatch.Create(
                    new HubWriterIdentity(entry.WriterDeviceId, entry.WriterEpoch), storeId, selected);
                var bytes = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(candidate, HubJson.Options));
                if (bytes > maxBytes)
                {
                    if (selected.Count == 1)
                    {
                        throw new InvalidOperationException("REPLICATION_ENTRY_EXCEEDS_BATCH_LIMIT");
                    }

                    selected.RemoveAt(selected.Count - 1);
                    break;
                }

                if (selected.Count == maxEntries)
                {
                    break;
                }
            }

            return ReplicationBatch.Create(
                new HubWriterIdentity(selected[0].WriterDeviceId, selected[0].WriterEpoch), storeId, selected);
        }
    }

    public void Acknowledge(Guid storeId, long throughSequence, string headEntryHash, DateTimeOffset acknowledgedAt)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            using var transaction = _connection.BeginTransaction();
            long previouslyAcknowledged;
            using (var command = _connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "SELECT COALESCE(MAX(operation_seq), 0) FROM replication_log WHERE store_id = $store AND acknowledged_at IS NOT NULL;";
                command.Parameters.AddWithValue("$store", storeId.ToString("D"));
                previouslyAcknowledged = Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
            }

            string? persistedHead;
            long contiguousCount;
            using (var command = _connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "SELECT entry_hash FROM replication_log WHERE store_id = $store AND operation_seq = $through;";
                command.Parameters.AddWithValue("$store", storeId.ToString("D"));
                command.Parameters.AddWithValue("$through", throughSequence);
                persistedHead = command.ExecuteScalar()?.ToString();
            }

            using (var command = _connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "SELECT COUNT(*) FROM replication_log WHERE store_id = $store AND operation_seq > $previous AND operation_seq <= $through;";
                command.Parameters.AddWithValue("$store", storeId.ToString("D"));
                command.Parameters.AddWithValue("$previous", previouslyAcknowledged);
                command.Parameters.AddWithValue("$through", throughSequence);
                contiguousCount = Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
            }

            if (throughSequence < previouslyAcknowledged || contiguousCount != throughSequence - previouslyAcknowledged ||
                !string.Equals(headEntryHash, persistedHead, StringComparison.Ordinal))
            {
                transaction.Rollback();
                throw new InvalidOperationException("REPLICATION_ACKNOWLEDGEMENT_MISMATCH");
            }

            using (var command = _connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "UPDATE replication_log SET acknowledged_at = $ack WHERE store_id = $store AND operation_seq <= $through AND acknowledged_at IS NULL;";
                command.Parameters.AddWithValue("$store", storeId.ToString("D"));
                command.Parameters.AddWithValue("$through", throughSequence);
                command.Parameters.AddWithValue("$ack", acknowledgedAt.ToUniversalTime().ToString("O"));
                command.ExecuteNonQuery();
            }

            using (var command = _connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "UPDATE operation_outbox SET acknowledged_at = $ack WHERE store_id = $store AND operation_seq <= $through AND acknowledged_at IS NULL;";
                command.Parameters.AddWithValue("$store", storeId.ToString("D"));
                command.Parameters.AddWithValue("$through", throughSequence);
                command.Parameters.AddWithValue("$ack", acknowledgedAt.ToUniversalTime().ToString("O"));
                command.ExecuteNonQuery();
            }

            transaction.Commit();
        }
    }

    public bool IsWriterFenced(Guid storeId)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT 1 FROM replication_fences WHERE store_id = $store LIMIT 1;";
            command.Parameters.AddWithValue("$store", storeId.ToString("D"));
            return command.ExecuteScalar() is not null;
        }
    }

    public void MarkWriterFenced(Guid storeId, HubWriterIdentity writer, string reason)
    {
        writer.Validate();
        lock (_gate)
        {
            ThrowIfDisposed();
            using var command = _connection.CreateCommand();
            command.CommandText = """
                INSERT INTO replication_fences (store_id, writer_device_id, writer_epoch, reason, fenced_at)
                VALUES ($store, $writer, $epoch, $reason, $fenced)
                ON CONFLICT(store_id) DO UPDATE SET writer_device_id = excluded.writer_device_id,
                    writer_epoch = excluded.writer_epoch, reason = excluded.reason, fenced_at = excluded.fenced_at;
                """;
            command.Parameters.AddWithValue("$store", storeId.ToString("D"));
            command.Parameters.AddWithValue("$writer", writer.DeviceId.ToString("D"));
            command.Parameters.AddWithValue("$epoch", writer.Epoch);
            command.Parameters.AddWithValue("$reason", reason);
            command.Parameters.AddWithValue("$fenced", _clock.UtcNow.ToString("O"));
            command.ExecuteNonQuery();
        }
    }

    public ProductCatalog LoadProductCatalog()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            using var transaction = new SqliteHubTransaction(_connection);
            var catalog = new ProductCatalog();
            foreach (var product in transaction.GetProducts())
            {
                catalog.Restore(product);
            }

            return catalog;
        }
    }

    public InventoryLedger LoadInventoryLedger()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            using var transaction = new SqliteHubTransaction(_connection);
            var ledger = new InventoryLedger();
            foreach (var inventoryEvent in transaction.GetInventoryEvents())
            {
                ledger.Append(inventoryEvent);
            }

            return ledger;
        }
    }

    public InvoiceService LoadInvoiceService()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            using var transaction = new SqliteHubTransaction(_connection);
            var service = new InvoiceService(transaction.GetCounters());
            foreach (var invoice in transaction.GetInvoices())
            {
                service.Restore(invoice);
            }

            return service;
        }
    }

    public OperationLog LoadOperationLog()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            using var transaction = new SqliteHubTransaction(_connection);
            var log = new OperationLog();
            foreach (var record in transaction.GetOperations())
            {
                log.Restore(record);
            }

            return log;
        }
    }

    public void BackupTo(string destinationPath)
    {
        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            throw new ArgumentException("A backup destination is required.", nameof(destinationPath));
        }

        lock (_gate)
        {
            ThrowIfDisposed();
            var fullPath = Path.GetFullPath(destinationPath);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var destination = new SqliteConnection($"Data Source={fullPath};Pooling=False");
            destination.Open();
            _connection.BackupDatabase(destination);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (!_disposed)
            {
                _connection.Dispose();
                _disposed = true;
            }
        }
    }

    private void Write(Action<IHubTransaction> action, Guid? storeId = null)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            if (storeId is Guid fencedStoreId)
            {
                ThrowIfReplicationFenced(fencedStoreId);
            }

            using var transaction = new SqliteHubTransaction(_connection);
            try
            {
                action(transaction);
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
    }

    private void InitializeSchema()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS products (
                store_id TEXT NOT NULL,
                product_id TEXT NOT NULL,
                name TEXT NOT NULL,
                normalized_name TEXT NOT NULL,
                similarity_key TEXT NOT NULL,
                unit_price_rials INTEGER NOT NULL,
                version INTEGER NOT NULL,
                is_active INTEGER NOT NULL,
                specification TEXT NULL,
                product_tag_number INTEGER NULL,
                category TEXT NULL,
                color TEXT NULL,
                size TEXT NULL,
                material TEXT NULL,
                brand TEXT NULL,
                barcode TEXT NULL,
                purchase_price_rials INTEGER NULL,
                PRIMARY KEY (store_id, product_id)
            );
            CREATE TABLE IF NOT EXISTS inventory_events (
                event_order INTEGER PRIMARY KEY AUTOINCREMENT,
                inventory_event_id TEXT NOT NULL,
                store_id TEXT NOT NULL,
                product_id TEXT NOT NULL,
                quantity_delta_millis INTEGER NOT NULL,
                event_type INTEGER NOT NULL,
                operation_id TEXT NOT NULL,
                device_id TEXT NOT NULL,
                reverses_event_id TEXT NULL,
                reason TEXT NULL,
                UNIQUE (store_id, inventory_event_id)
            );
            CREATE TABLE IF NOT EXISTS invoices (
                store_id TEXT NOT NULL,
                invoice_id TEXT NOT NULL,
                created_operation_id TEXT NOT NULL,
                created_by_device_id TEXT NOT NULL,
                created_at TEXT NOT NULL,
                external_invoice_number TEXT NULL,
                display_number INTEGER NULL,
                status INTEGER NOT NULL,
                retired_at TEXT NULL,
                retirement_reason INTEGER NULL,
                issued_at TEXT NULL,
                issued_payload TEXT NULL,
                issued_content_sha256 TEXT NULL,
                customer_id TEXT NULL,
                PRIMARY KEY (store_id, invoice_id)
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_invoices_display_number
                ON invoices(store_id, display_number) WHERE display_number IS NOT NULL;
            CREATE TABLE IF NOT EXISTS invoice_items (
                store_id TEXT NOT NULL,
                invoice_id TEXT NOT NULL,
                invoice_item_id TEXT NOT NULL,
                line_number INTEGER NOT NULL,
                description TEXT NOT NULL,
                quantity_millis INTEGER NOT NULL,
                unit_price_rials INTEGER NOT NULL,
                line_total_rials INTEGER NOT NULL,
                product_id TEXT NULL,
                PRIMARY KEY (store_id, invoice_item_id),
                UNIQUE (store_id, invoice_id, line_number),
                FOREIGN KEY (store_id, invoice_id) REFERENCES invoices(store_id, invoice_id)
            );
            CREATE TABLE IF NOT EXISTS operations (
                store_id TEXT NOT NULL,
                operation_id TEXT NOT NULL,
                device_id TEXT NOT NULL,
                operation_type TEXT NOT NULL,
                request_hash TEXT NOT NULL,
                operation_seq INTEGER NOT NULL,
                response TEXT NOT NULL,
                PRIMARY KEY (store_id, operation_id),
                UNIQUE (store_id, operation_seq)
            );
            CREATE TABLE IF NOT EXISTS store_counters (
                store_id TEXT PRIMARY KEY,
                display_number_high_watermark INTEGER NOT NULL,
                product_tag_high_watermark INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE IF NOT EXISTS draft_locks (
                store_id TEXT NOT NULL,
                lock_id TEXT NOT NULL,
                customer_phone TEXT NOT NULL,
                invoice_id TEXT NOT NULL,
                device_id TEXT NOT NULL,
                acquired_at TEXT NOT NULL,
                last_activity_at TEXT NOT NULL,
                timeout_seconds INTEGER NOT NULL,
                state INTEGER NOT NULL,
                release_reason INTEGER NULL,
                released_at TEXT NULL,
                PRIMARY KEY (store_id, lock_id),
                UNIQUE (store_id, invoice_id),
                FOREIGN KEY (store_id, invoice_id) REFERENCES invoices(store_id, invoice_id)
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_draft_locks_active_phone
                ON draft_locks(store_id, customer_phone) WHERE state = 0;
            CREATE TABLE IF NOT EXISTS operation_outbox (
                store_id TEXT NOT NULL,
                operation_seq INTEGER NOT NULL,
                operation_id TEXT NOT NULL,
                device_id TEXT NOT NULL,
                operation_type TEXT NOT NULL,
                base_version INTEGER NULL,
                payload TEXT NOT NULL,
                request_hash TEXT NOT NULL,
                response TEXT NOT NULL,
                acknowledged_at TEXT NULL,
                PRIMARY KEY (store_id, operation_seq),
                UNIQUE (store_id, operation_id),
                FOREIGN KEY (store_id, operation_id) REFERENCES operations(store_id, operation_id)
            );
            CREATE TABLE IF NOT EXISTS replication_log (
                store_id TEXT NOT NULL,
                operation_seq INTEGER NOT NULL,
                operation_id TEXT NOT NULL,
                entry_hash TEXT NOT NULL,
                entry_json TEXT NOT NULL,
                acknowledged_at TEXT NULL,
                PRIMARY KEY (store_id, operation_seq),
                UNIQUE (store_id, operation_id),
                FOREIGN KEY (store_id, operation_id) REFERENCES operations(store_id, operation_id)
            );
            CREATE TABLE IF NOT EXISTS replication_fences (
                store_id TEXT PRIMARY KEY,
                writer_device_id TEXT NOT NULL,
                writer_epoch INTEGER NOT NULL,
                reason TEXT NOT NULL,
                fenced_at TEXT NOT NULL
            );
            CREATE TRIGGER IF NOT EXISTS invoices_terminal_update_forbidden
            BEFORE UPDATE ON invoices
            WHEN OLD.status IN (3, 4, 5) OR OLD.retired_at IS NOT NULL
            BEGIN SELECT RAISE(ABORT, 'ISSUED_INVOICE_IMMUTABLE'); END;
            CREATE TRIGGER IF NOT EXISTS invoices_terminal_delete_forbidden
            BEFORE DELETE ON invoices
            WHEN OLD.status IN (3, 4, 5) OR OLD.retired_at IS NOT NULL
            BEGIN SELECT RAISE(ABORT, 'ISSUED_INVOICE_IMMUTABLE'); END;
            CREATE TRIGGER IF NOT EXISTS inventory_update_forbidden
            BEFORE UPDATE ON inventory_events
            BEGIN SELECT RAISE(ABORT, 'INVENTORY_APPEND_ONLY'); END;
            CREATE TRIGGER IF NOT EXISTS inventory_delete_forbidden
            BEFORE DELETE ON inventory_events
            BEGIN SELECT RAISE(ABORT, 'INVENTORY_APPEND_ONLY'); END;
            CREATE TRIGGER IF NOT EXISTS operations_update_forbidden
            BEFORE UPDATE ON operations
            BEGIN SELECT RAISE(ABORT, 'OPERATION_APPEND_ONLY'); END;
            CREATE TRIGGER IF NOT EXISTS operations_delete_forbidden
            BEFORE DELETE ON operations
            BEGIN SELECT RAISE(ABORT, 'OPERATION_APPEND_ONLY'); END;
            CREATE TRIGGER IF NOT EXISTS replication_content_update_forbidden
            BEFORE UPDATE ON replication_log
            WHEN NEW.entry_json <> OLD.entry_json OR NEW.entry_hash <> OLD.entry_hash
            BEGIN SELECT RAISE(ABORT, 'REPLICATION_ENTRY_IMMUTABLE'); END;
            CREATE TRIGGER IF NOT EXISTS replication_delete_forbidden
            BEFORE DELETE ON replication_log
            BEGIN SELECT RAISE(ABORT, 'REPLICATION_LOG_APPEND_ONLY'); END;
            CREATE TRIGGER IF NOT EXISTS counters_regression_forbidden
            BEFORE UPDATE ON store_counters
            WHEN NEW.display_number_high_watermark < OLD.display_number_high_watermark
            BEGIN SELECT RAISE(ABORT, 'DISPLAY_NUMBER_REGRESSION'); END;
            """;
        command.ExecuteNonQuery();
        EnsureColumn("products", "product_tag_number", "INTEGER NULL");
        EnsureColumn("products", "category", "TEXT NULL");
        EnsureColumn("products", "color", "TEXT NULL");
        EnsureColumn("products", "size", "TEXT NULL");
        EnsureColumn("products", "material", "TEXT NULL");
        EnsureColumn("products", "brand", "TEXT NULL");
        EnsureColumn("products", "barcode", "TEXT NULL");
        EnsureColumn("products", "purchase_price_rials", "INTEGER NULL");
        EnsureColumn("store_counters", "product_tag_high_watermark", "INTEGER NOT NULL DEFAULT 0");
        using var tagCommand = _connection.CreateCommand();
        tagCommand.CommandText = """
            CREATE TABLE IF NOT EXISTS product_operator_fields (
                store_id TEXT NOT NULL,
                product_id TEXT NOT NULL,
                position INTEGER NOT NULL,
                field_key TEXT NOT NULL,
                field_value TEXT NOT NULL,
                PRIMARY KEY (store_id, product_id, position),
                UNIQUE (store_id, product_id, field_key),
                FOREIGN KEY (store_id, product_id) REFERENCES products(store_id, product_id)
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_products_store_tag
                ON products(store_id, product_tag_number) WHERE product_tag_number IS NOT NULL;
            CREATE TRIGGER IF NOT EXISTS product_tag_regression_forbidden
            BEFORE UPDATE ON store_counters
            WHEN NEW.product_tag_high_watermark < OLD.product_tag_high_watermark
            BEGIN SELECT RAISE(ABORT, 'PRODUCT_TAG_REGRESSION'); END;
            """;
        tagCommand.ExecuteNonQuery();
    }

    private void EnsureColumn(string table, string column, string definition)
    {
        using var inspect = _connection.CreateCommand();
        inspect.CommandText = $"PRAGMA table_info({table});";
        using var reader = inspect.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.Ordinal))
            {
                return;
            }
        }

        reader.Close();
        using var alter = _connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition};";
        alter.ExecuteNonQuery();
    }

    private void ThrowIfReplicationFenced(Guid storeId)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM replication_fences WHERE store_id = $store LIMIT 1;";
        command.Parameters.AddWithValue("$store", storeId.ToString("D"));
        if (command.ExecuteScalar() is not null)
        {
            throw new HubWriterFencedException(storeId);
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private sealed record IssuedSnapshot(
        [property: JsonPropertyName("invoice_id")] Guid InvoiceId,
        [property: JsonPropertyName("display_number")] long DisplayNumber);
}
