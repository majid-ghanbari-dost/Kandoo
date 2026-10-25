using System.Text.Json;
using System.Text.Json.Serialization;
using Kandoo.Core;
using Microsoft.Data.Sqlite;

namespace Kandoo.Hub;

/// <summary>SQLite-backed Hub store. One connection and one lock make transaction boundaries explicit.</summary>
public sealed partial class SqliteHubStore : IHubStore, IHubAccessStore
{
    private readonly object _gate = new();
    private readonly SqliteConnection _connection;
    private bool _disposed;

    public SqliteHubStore(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("A SQLite connection string is required.", nameof(connectionString));
        }

        _connection = new SqliteConnection(connectionString);
        _connection.Open();
        using var command = _connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON;";
        command.ExecuteNonQuery();
        InitializeSchema();
        InitializeAccessSchema();
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
                transaction.AppendOutbox(record, request);
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

    public void SaveProduct(Product product) => Write(transaction => transaction.SaveProduct(product));

    public void AppendInventory(InventoryEvent inventoryEvent) => Write(transaction => transaction.AppendInventory(inventoryEvent));

    public void SaveInvoice(Invoice invoice) => Write(transaction => transaction.SaveInvoice(invoice));

    public Invoice? GetInvoice(Guid storeId, Guid invoiceId)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            using var transaction = new SqliteHubTransaction(_connection);
            return transaction.GetInvoice(storeId, invoiceId);
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
        });
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

    private void Write(Action<IHubTransaction> action)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
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
                display_number_high_watermark INTEGER NOT NULL
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
            CREATE TRIGGER IF NOT EXISTS counters_regression_forbidden
            BEFORE UPDATE ON store_counters
            WHEN NEW.display_number_high_watermark < OLD.display_number_high_watermark
            BEGIN SELECT RAISE(ABORT, 'DISPLAY_NUMBER_REGRESSION'); END;
            """;
        command.ExecuteNonQuery();
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private sealed record IssuedSnapshot(
        [property: JsonPropertyName("invoice_id")] Guid InvoiceId,
        [property: JsonPropertyName("display_number")] long DisplayNumber);
}
