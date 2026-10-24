using System.Globalization;
using Kandoo.Core;
using Microsoft.Data.Sqlite;

namespace Kandoo.Hub;

internal sealed class SqliteHubTransaction : IHubTransaction
{
    private readonly SqliteConnection _connection;
    private readonly SqliteTransaction _transaction;
    private bool _completed;

    internal SqliteHubTransaction(SqliteConnection connection)
    {
        _connection = connection;
        _transaction = connection.BeginTransaction();
    }

    public Product? GetProduct(Guid storeId, string productId)
    {
        using var command = Command("""
            SELECT store_id, product_id, name, normalized_name, similarity_key,
                   unit_price_rials, version, is_active, specification
            FROM products WHERE store_id = $store AND product_id = $product;
            """);
        Add(command, "$store", Uuid(storeId));
        Add(command, "$product", productId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadProduct(reader) : null;
    }

    public IReadOnlyList<Product> GetProducts()
    {
        using var command = Command("""
            SELECT store_id, product_id, name, normalized_name, similarity_key,
                   unit_price_rials, version, is_active, specification
            FROM products ORDER BY store_id, product_id;
            """);
        using var reader = command.ExecuteReader();
        var products = new List<Product>();
        while (reader.Read())
        {
            products.Add(ReadProduct(reader));
        }

        return products;
    }

    public void SaveProduct(Product product)
    {
        using var command = Command("""
            INSERT INTO products
                (store_id, product_id, name, normalized_name, similarity_key,
                 unit_price_rials, version, is_active, specification)
            VALUES ($store, $product, $name, $normalized, $similarity, $price, $version, $active, $specification)
            ON CONFLICT(store_id, product_id) DO UPDATE SET
                name = excluded.name, normalized_name = excluded.normalized_name,
                similarity_key = excluded.similarity_key, unit_price_rials = excluded.unit_price_rials,
                version = excluded.version, is_active = excluded.is_active, specification = excluded.specification;
            """);
        Add(command, "$store", Uuid(product.StoreId));
        Add(command, "$product", product.ProductId);
        Add(command, "$name", product.Name);
        Add(command, "$normalized", product.NormalizedName);
        Add(command, "$similarity", product.SimilarityKey);
        Add(command, "$price", product.UnitPriceRials);
        Add(command, "$version", product.Version);
        Add(command, "$active", product.IsActive ? 1 : 0);
        Add(command, "$specification", product.Specification);
        command.ExecuteNonQuery();
    }

    public void AppendInventory(InventoryEvent inventoryEvent)
    {
        if (inventoryEvent.QuantityDeltaMillis == 0)
        {
            throw new ArgumentException("Inventory deltas must be non-zero.", nameof(inventoryEvent));
        }

        using var command = Command("""
            INSERT INTO inventory_events
                (inventory_event_id, store_id, product_id, quantity_delta_millis,
                 event_type, operation_id, device_id, reverses_event_id, reason)
            VALUES ($id, $store, $product, $delta, $type, $operation, $device, $reverses, $reason);
            """);
        Add(command, "$id", Uuid(inventoryEvent.InventoryEventId));
        Add(command, "$store", Uuid(inventoryEvent.StoreId));
        Add(command, "$product", inventoryEvent.ProductId);
        Add(command, "$delta", inventoryEvent.QuantityDeltaMillis);
        Add(command, "$type", (int)inventoryEvent.EventType);
        Add(command, "$operation", inventoryEvent.OperationId);
        Add(command, "$device", Uuid(inventoryEvent.DeviceId));
        Add(command, "$reverses", inventoryEvent.ReversesEventId is Guid id ? Uuid(id) : null);
        Add(command, "$reason", inventoryEvent.Reason);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<InventoryEvent> GetInventoryEvents()
    {
        using var command = Command("""
            SELECT inventory_event_id, store_id, product_id, quantity_delta_millis,
                   event_type, operation_id, device_id, reverses_event_id, reason
            FROM inventory_events ORDER BY event_order;
            """);
        using var reader = command.ExecuteReader();
        var events = new List<InventoryEvent>();
        while (reader.Read())
        {
            events.Add(new InventoryEvent(
                Guid.Parse(reader.GetString(0)),
                Guid.Parse(reader.GetString(1)),
                reader.GetString(2),
                reader.GetInt64(3),
                (InventoryEventType)reader.GetInt32(4),
                reader.GetString(5),
                Guid.Parse(reader.GetString(6)),
                reader.IsDBNull(7) ? null : Guid.Parse(reader.GetString(7)),
                reader.IsDBNull(8) ? null : reader.GetString(8)));
        }

        return events;
    }

    public Invoice? GetInvoice(Guid storeId, Guid invoiceId)
    {
        InvoiceRow? row = null;
        using (var command = Command("""
            SELECT store_id, invoice_id, created_operation_id, created_by_device_id,
                   created_at, external_invoice_number, display_number, status,
                   retired_at, retirement_reason, issued_at, issued_payload,
                   issued_content_sha256, customer_id
            FROM invoices WHERE store_id = $store AND invoice_id = $invoice;
            """))
        {
            Add(command, "$store", Uuid(storeId));
            Add(command, "$invoice", Uuid(invoiceId));
            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                row = ReadInvoiceRow(reader);
            }
        }

        return row is null ? null : HydrateInvoice(row);
    }

    public IReadOnlyList<Invoice> GetInvoices()
    {
        var ids = new List<(Guid StoreId, Guid InvoiceId)>();
        using (var command = Command("SELECT store_id, invoice_id FROM invoices ORDER BY store_id, invoice_id;"))
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                ids.Add((Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1))));
            }
        }

        var invoices = new List<Invoice>(ids.Count);
        foreach (var (storeId, invoiceId) in ids)
        {
            invoices.Add(GetInvoice(storeId, invoiceId)!);
        }

        return invoices;
    }

    public void SaveInvoice(Invoice invoice)
    {
        using (var guard = Command("SELECT status, retired_at FROM invoices WHERE store_id = $store AND invoice_id = $invoice;"))
        {
            Add(guard, "$store", Uuid(invoice.StoreId));
            Add(guard, "$invoice", Uuid(invoice.InvoiceId));
            using var reader = guard.ExecuteReader();
            if (reader.Read())
            {
                var oldStatus = (InvoiceStatus)reader.GetInt32(0);
                var oldRetired = !reader.IsDBNull(1);
                if (oldStatus is InvoiceStatus.Issued or InvoiceStatus.Revoked or InvoiceStatus.Superseded || oldRetired)
                {
                    throw new InvalidOperationException("ISSUED_INVOICE_IMMUTABLE");
                }
            }
        }

        using (var command = Command("""
            INSERT INTO invoices
                (store_id, invoice_id, created_operation_id, created_by_device_id,
                 created_at, external_invoice_number, display_number, status,
                 retired_at, retirement_reason, issued_at, issued_payload,
                 issued_content_sha256, customer_id)
            VALUES ($store, $invoice, $operation, $device, $created, $external,
                    $display, $status, $retired, $retirement, $issued, $payload, $digest, $customer)
            ON CONFLICT(store_id, invoice_id) DO UPDATE SET
                created_operation_id = excluded.created_operation_id,
                created_by_device_id = excluded.created_by_device_id,
                created_at = excluded.created_at, external_invoice_number = excluded.external_invoice_number,
                display_number = excluded.display_number, status = excluded.status,
                retired_at = excluded.retired_at, retirement_reason = excluded.retirement_reason,
                issued_at = excluded.issued_at, issued_payload = excluded.issued_payload,
                issued_content_sha256 = excluded.issued_content_sha256, customer_id = excluded.customer_id;
            """))
        {
            Add(command, "$store", Uuid(invoice.StoreId));
            Add(command, "$invoice", Uuid(invoice.InvoiceId));
            Add(command, "$operation", invoice.CreatedOperationId);
            Add(command, "$device", Uuid(invoice.CreatedByDeviceId));
            Add(command, "$created", Date(invoice.CreatedAt));
            Add(command, "$external", invoice.ExternalInvoiceNumber);
            Add(command, "$display", invoice.DisplayNumber);
            Add(command, "$status", (int)invoice.Status);
            Add(command, "$retired", invoice.RetiredAt is DateTimeOffset retired ? Date(retired) : null);
            Add(command, "$retirement", invoice.RetirementReason is RetirementReason reason ? (int)reason : null);
            Add(command, "$issued", invoice.IssuedAt is DateTimeOffset issued ? Date(issued) : null);
            Add(command, "$payload", invoice.IssuedPayload);
            Add(command, "$digest", invoice.IssuedContentSha256);
            Add(command, "$customer", invoice.CustomerId is Guid customer ? Uuid(customer) : null);
            command.ExecuteNonQuery();
        }

        using (var delete = Command("DELETE FROM invoice_items WHERE store_id = $store AND invoice_id = $invoice;"))
        {
            Add(delete, "$store", Uuid(invoice.StoreId));
            Add(delete, "$invoice", Uuid(invoice.InvoiceId));
            delete.ExecuteNonQuery();
        }

        foreach (var item in invoice.Items)
        {
            using var command = Command("""
                INSERT INTO invoice_items
                    (store_id, invoice_id, invoice_item_id, line_number, description,
                     quantity_millis, unit_price_rials, line_total_rials, product_id)
                VALUES ($store, $invoice, $id, $line, $description, $quantity, $price, $total, $product);
                """);
            Add(command, "$store", Uuid(invoice.StoreId));
            Add(command, "$invoice", Uuid(invoice.InvoiceId));
            Add(command, "$id", Uuid(item.InvoiceItemId));
            Add(command, "$line", item.LineNumber);
            Add(command, "$description", item.Description);
            Add(command, "$quantity", item.QuantityMillis);
            Add(command, "$price", item.UnitPriceRials);
            Add(command, "$total", item.LineTotalRials);
            Add(command, "$product", item.ProductId);
            command.ExecuteNonQuery();
        }
    }

    public long GetDisplayNumberHighWatermark(Guid storeId)
    {
        using var command = Command("SELECT display_number_high_watermark FROM store_counters WHERE store_id = $store;");
        Add(command, "$store", Uuid(storeId));
        var value = command.ExecuteScalar();
        return value is null || value is DBNull ? 0 : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    public void SetDisplayNumberHighWatermark(Guid storeId, long value)
    {
        if (value < 0 || value < GetDisplayNumberHighWatermark(storeId))
        {
            throw new InvalidOperationException("DISPLAY_NUMBER_REGRESSION");
        }

        using var command = Command("""
            INSERT INTO store_counters (store_id, display_number_high_watermark) VALUES ($store, $value)
            ON CONFLICT(store_id) DO UPDATE SET display_number_high_watermark = excluded.display_number_high_watermark;
            """);
        Add(command, "$store", Uuid(storeId));
        Add(command, "$value", value);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<OperationRecord> GetOperations()
    {
        using var command = Command("""
            SELECT store_id, device_id, operation_id, operation_type, request_hash, operation_seq, response
            FROM operations ORDER BY store_id, operation_seq;
            """);
        using var reader = command.ExecuteReader();
        var records = new List<OperationRecord>();
        while (reader.Read())
        {
            records.Add(ReadOperation(reader));
        }

        return records;
    }

    internal OperationRecord? FindOperation(Guid storeId, string operationId)
    {
        using var command = Command("""
            SELECT store_id, device_id, operation_id, operation_type, request_hash, operation_seq, response
            FROM operations WHERE store_id = $store AND operation_id = $operation;
            """);
        Add(command, "$store", Uuid(storeId));
        Add(command, "$operation", operationId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadOperation(reader) : null;
    }

    internal long NextOperationSequence(Guid storeId)
    {
        using var command = Command("SELECT COALESCE(MAX(operation_seq), 0) + 1 FROM operations WHERE store_id = $store;");
        Add(command, "$store", Uuid(storeId));
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    internal void AppendOperation(OperationRecord record)
    {
        using var command = Command("""
            INSERT INTO operations (store_id, device_id, operation_id, operation_type, request_hash, operation_seq, response)
            VALUES ($store, $device, $operation, $type, $hash, $sequence, $response);
            """);
        Add(command, "$store", Uuid(record.StoreId));
        Add(command, "$device", Uuid(record.DeviceId));
        Add(command, "$operation", record.OperationId);
        Add(command, "$type", record.OperationType);
        Add(command, "$hash", record.RequestHash);
        Add(command, "$sequence", record.OperationSeq);
        Add(command, "$response", record.Response);
        command.ExecuteNonQuery();
    }

    internal Dictionary<Guid, long> GetCounters()
    {
        using var command = Command("SELECT store_id, display_number_high_watermark FROM store_counters;");
        using var reader = command.ExecuteReader();
        var counters = new Dictionary<Guid, long>();
        while (reader.Read())
        {
            counters[Guid.Parse(reader.GetString(0))] = reader.GetInt64(1);
        }

        return counters;
    }

    internal void Commit()
    {
        _transaction.Commit();
        _completed = true;
    }

    internal void Rollback()
    {
        if (!_completed)
        {
            _transaction.Rollback();
            _completed = true;
        }
    }

    public void Dispose()
    {
        if (!_completed)
        {
            _transaction.Dispose();
            _completed = true;
        }
        else
        {
            _transaction.Dispose();
        }
    }

    private Invoice HydrateInvoice(InvoiceRow row)
    {
        var invoice = new Invoice(row.InvoiceId, row.StoreId, row.CreatedOperationId, row.CreatedByDeviceId, ParseDate(row.CreatedAt), row.ExternalInvoiceNumber);
        using (var command = Command("""
            SELECT invoice_item_id, store_id, line_number, description, quantity_millis,
                   unit_price_rials, line_total_rials, product_id
            FROM invoice_items WHERE store_id = $store AND invoice_id = $invoice ORDER BY line_number;
            """))
        {
            Add(command, "$store", Uuid(row.StoreId));
            Add(command, "$invoice", Uuid(row.InvoiceId));
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                invoice.AddItem(new InvoiceItem(
                    Guid.Parse(reader.GetString(0)),
                    Guid.Parse(reader.GetString(1)),
                    reader.GetInt32(2),
                    reader.GetString(3),
                    reader.GetInt64(4),
                    reader.GetInt64(5),
                    reader.GetInt64(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7)));
            }
        }

        invoice.RestoreForPersistence(
            row.DisplayNumber,
            (InvoiceStatus)row.Status,
            row.RetiredAt is null ? null : ParseDate(row.RetiredAt),
            row.RetirementReason is int reason ? (RetirementReason)reason : null,
            row.IssuedAt is null ? null : ParseDate(row.IssuedAt),
            row.IssuedPayload,
            row.IssuedContentSha256,
            row.CustomerId);
        return invoice;
    }

    private SqliteCommand Command(string sql)
    {
        var command = _connection.CreateCommand();
        command.Transaction = _transaction;
        command.CommandText = sql;
        return command;
    }

    private static Product ReadProduct(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(0)),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetString(4),
        reader.GetInt64(5),
        reader.GetInt32(6),
        reader.GetInt64(7) != 0,
        reader.IsDBNull(8) ? null : reader.GetString(8));

    private static OperationRecord ReadOperation(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(0)),
        Guid.Parse(reader.GetString(1)),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetString(4),
        reader.GetInt64(5),
        reader.GetString(6));

    private static InvoiceRow ReadInvoiceRow(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(0)),
        Guid.Parse(reader.GetString(1)),
        reader.GetString(2),
        Guid.Parse(reader.GetString(3)),
        reader.GetString(4),
        reader.IsDBNull(5) ? null : reader.GetString(5),
        reader.IsDBNull(6) ? null : reader.GetInt64(6),
        reader.GetInt32(7),
        reader.IsDBNull(8) ? null : reader.GetString(8),
        reader.IsDBNull(9) ? null : reader.GetInt32(9),
        reader.IsDBNull(10) ? null : reader.GetString(10),
        reader.IsDBNull(11) ? null : reader.GetString(11),
        reader.IsDBNull(12) ? null : reader.GetString(12),
        reader.IsDBNull(13) ? null : Guid.Parse(reader.GetString(13)));

    private static string Uuid(Guid value) => value.ToString("D");
    private static string Date(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);
    private static DateTimeOffset ParseDate(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    private static void Add(SqliteCommand command, string name, object? value) => command.Parameters.AddWithValue(name, value ?? DBNull.Value);

    private sealed record InvoiceRow(
        Guid StoreId,
        Guid InvoiceId,
        string CreatedOperationId,
        Guid CreatedByDeviceId,
        string CreatedAt,
        string? ExternalInvoiceNumber,
        long? DisplayNumber,
        int Status,
        string? RetiredAt,
        int? RetirementReason,
        string? IssuedAt,
        string? IssuedPayload,
        string? IssuedContentSha256,
        Guid? CustomerId);
}
