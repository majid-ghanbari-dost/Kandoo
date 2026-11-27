using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Kandoo.HolooConnector;

/// <summary>
/// Durable SQLite persistence for the Holoo shadow catalog, customers, invoice history,
/// schema fingerprints / incremental sync checkpoints, and D-M owner consent records.
/// Never stores SQL Server credentials or passwords.
/// </summary>
public sealed class SqliteHolooShadowStore : IDisposable
{
    private readonly object _gate = new();
    private readonly SqliteConnection _connection;
    private bool _disposed;

    public SqliteHolooShadowStore(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("SQLite connection string is required.", nameof(connectionString));

        _connection = new SqliteConnection(connectionString);
        _connection.Open();
        using var pragma = _connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON;";
        pragma.ExecuteNonQuery();
        InitializeSchema();
    }

    public static SqliteHolooShadowStore OpenInMemory() =>
        new("Data Source=:memory:");

    public static SqliteHolooShadowStore OpenFile(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
            throw new ArgumentException("Database path is required.", nameof(databasePath));

        var dir = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(databasePath),
            Mode = SqliteOpenMode.ReadWriteCreate,
        };
        return new SqliteHolooShadowStore(builder.ConnectionString);
    }

    public void UpsertProduct(ShadowProduct product) => UpsertProducts([product]);

    public void UpsertProducts(IEnumerable<ShadowProduct> products)
    {
        ArgumentNullException.ThrowIfNull(products);
        lock (_gate)
        {
            ThrowIfDisposed();
            using var tx = _connection.BeginTransaction();
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO holoo_shadow_products (
                    product_code, legacy_code, product_name, country_of_origin,
                    buy_price, explicit_sale_price, last_sold_price,
                    last_sold_invoice_code, last_sold_invoice_date,
                    holoo_visible_stock, unit_code, unit_name,
                    main_group_code, main_group_name, sub_group_code, sub_group_name,
                    model, source_edited_at, schema_version, imported_at,
                    stock_unverified, verified_stock, stock_verified_at, stock_verified_by,
                    is_deleted, is_active
                )
                VALUES (
                    @code, @legacy, @name, @country,
                    @buy, @explicitSale, @lastSold,
                    @lastInvCode, @lastInvDate,
                    @stock, @unitCode, @unitName,
                    @mainGroupCode, @mainGroupName, @subGroupCode, @subGroupName,
                    @model, @sourceEdited, @schemaVer, @importedAt,
                    1, NULL, NULL, NULL,
                    @deleted, @active
                )
                ON CONFLICT(product_code) DO UPDATE SET
                    legacy_code = excluded.legacy_code,
                    product_name = excluded.product_name,
                    country_of_origin = excluded.country_of_origin,
                    buy_price = excluded.buy_price,
                    explicit_sale_price = excluded.explicit_sale_price,
                    last_sold_price = excluded.last_sold_price,
                    last_sold_invoice_code = excluded.last_sold_invoice_code,
                    last_sold_invoice_date = excluded.last_sold_invoice_date,
                    holoo_visible_stock = excluded.holoo_visible_stock,
                    unit_code = excluded.unit_code,
                    unit_name = excluded.unit_name,
                    main_group_code = excluded.main_group_code,
                    main_group_name = excluded.main_group_name,
                    sub_group_code = excluded.sub_group_code,
                    sub_group_name = excluded.sub_group_name,
                    model = excluded.model,
                    source_edited_at = excluded.source_edited_at,
                    schema_version = excluded.schema_version,
                    imported_at = excluded.imported_at,
                    stock_unverified = 1,
                    verified_stock = NULL,
                    stock_verified_at = NULL,
                    stock_verified_by = NULL,
                    is_deleted = excluded.is_deleted,
                    is_active = excluded.is_active;
                """;

            var pCode = cmd.Parameters.Add("@code", SqliteType.Text);
            var pLegacy = cmd.Parameters.Add("@legacy", SqliteType.Text);
            var pName = cmd.Parameters.Add("@name", SqliteType.Text);
            var pCountry = cmd.Parameters.Add("@country", SqliteType.Text);
            var pBuy = cmd.Parameters.Add("@buy", SqliteType.Text);
            var pExplicit = cmd.Parameters.Add("@explicitSale", SqliteType.Text);
            var pLastSold = cmd.Parameters.Add("@lastSold", SqliteType.Text);
            var pLastInvCode = cmd.Parameters.Add("@lastInvCode", SqliteType.Text);
            var pLastInvDate = cmd.Parameters.Add("@lastInvDate", SqliteType.Text);
            var pStock = cmd.Parameters.Add("@stock", SqliteType.Text);
            var pUnitCode = cmd.Parameters.Add("@unitCode", SqliteType.Text);
            var pUnitName = cmd.Parameters.Add("@unitName", SqliteType.Text);
            var pMainCode = cmd.Parameters.Add("@mainGroupCode", SqliteType.Text);
            var pMainName = cmd.Parameters.Add("@mainGroupName", SqliteType.Text);
            var pSubCode = cmd.Parameters.Add("@subGroupCode", SqliteType.Text);
            var pSubName = cmd.Parameters.Add("@subGroupName", SqliteType.Text);
            var pModel = cmd.Parameters.Add("@model", SqliteType.Text);
            var pSourceEdited = cmd.Parameters.Add("@sourceEdited", SqliteType.Text);
            var pSchemaVer = cmd.Parameters.Add("@schemaVer", SqliteType.Text);
            var pImportedAt = cmd.Parameters.Add("@importedAt", SqliteType.Text);
            var pDeleted = cmd.Parameters.Add("@deleted", SqliteType.Integer);
            var pActive = cmd.Parameters.Add("@active", SqliteType.Integer);

            foreach (var product in products)
            {
                if (string.IsNullOrWhiteSpace(product.ProductCode))
                    throw new ArgumentException("ProductCode is required for every shadow product.");

                pCode.Value = product.ProductCode;
                pLegacy.Value = ToDbValue(product.LegacyCode);
                pName.Value = ToDbValue(product.ProductName);
                pCountry.Value = ToDbValue(product.CountryOfOrigin);
                pBuy.Value = ToDbDecimal(product.BuyPrice);
                pExplicit.Value = ToDbDecimal(product.ExplicitSalePrice);
                pLastSold.Value = ToDbDecimal(product.LastSoldPrice);
                pLastInvCode.Value = ToDbValue(product.LastSoldInvoiceCode);
                pLastInvDate.Value = ToDbValue(product.LastSoldInvoiceDate);
                pStock.Value = ToDbDecimal(product.HolooVisibleStock);
                pUnitCode.Value = ToDbValue(product.UnitCode);
                pUnitName.Value = ToDbValue(product.UnitName);
                pMainCode.Value = ToDbValue(product.MainGroupCode);
                pMainName.Value = ToDbValue(product.MainGroupName);
                pSubCode.Value = ToDbValue(product.SubGroupCode);
                pSubName.Value = ToDbValue(product.SubGroupName);
                pModel.Value = ToDbValue(product.Model);
                pSourceEdited.Value = ToDbValue(product.SourceEditedAt);
                pSchemaVer.Value = product.SchemaVersion;
                pImportedAt.Value = product.ImportedAt.ToString("O", CultureInfo.InvariantCulture);
                pDeleted.Value = product.IsDeleted ? 1 : 0;
                pActive.Value = product.IsActive ? 1 : 0;
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
        }
    }

    public ShadowProduct ConfirmProductStock(
        string productCode,
        decimal verifiedStock,
        string operatorName,
        DateTimeOffset confirmedAt)
    {
        if (string.IsNullOrWhiteSpace(productCode))
            throw new ArgumentException("ProductCode is required.", nameof(productCode));
        if (verifiedStock < 0m)
            throw new ArgumentOutOfRangeException(nameof(verifiedStock), "Verified stock cannot be negative.");
        if (string.IsNullOrWhiteSpace(operatorName))
            throw new ArgumentException("Operator name is required.", nameof(operatorName));

        lock (_gate)
        {
            ThrowIfDisposed();
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                UPDATE holoo_shadow_products
                SET stock_unverified = 0,
                    verified_stock = @stock,
                    stock_verified_at = @at,
                    stock_verified_by = @by
                WHERE product_code = @code;
                """;
            cmd.Parameters.AddWithValue("@stock", verifiedStock.ToString(CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("@at", confirmedAt.ToString("O", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("@by", operatorName.Trim());
            cmd.Parameters.AddWithValue("@code", productCode);
            var changed = cmd.ExecuteNonQuery();
            if (changed == 0)
                throw new KeyNotFoundException($"Shadow product '{productCode}' was not found.");
        }

        return GetProduct(productCode)!;
    }

    public void UpsertCustomer(ShadowCustomer customer) => UpsertCustomers([customer]);

    public void UpsertCustomers(IEnumerable<ShadowCustomer> customers)
    {
        ArgumentNullException.ThrowIfNull(customers);
        lock (_gate)
        {
            ThrowIfDisposed();
            using var tx = _connection.BeginTransaction();
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO holoo_shadow_customers (
                    customer_code, legacy_code, customer_name, customer_family,
                    phone, mobile, additional_phones_json,
                    address, postal_code, national_code,
                    source_edited_at, is_deleted, schema_version, imported_at
                )
                VALUES (
                    @code, @legacy, @name, @family,
                    @phone, @mobile, @extraPhones,
                    @address, @postal, @national,
                    @sourceEdited, @deleted, @schemaVer, @importedAt
                )
                ON CONFLICT(customer_code) DO UPDATE SET
                    legacy_code = excluded.legacy_code,
                    customer_name = excluded.customer_name,
                    customer_family = excluded.customer_family,
                    phone = excluded.phone,
                    mobile = excluded.mobile,
                    additional_phones_json = excluded.additional_phones_json,
                    address = excluded.address,
                    postal_code = excluded.postal_code,
                    national_code = excluded.national_code,
                    source_edited_at = excluded.source_edited_at,
                    is_deleted = excluded.is_deleted,
                    schema_version = excluded.schema_version,
                    imported_at = excluded.imported_at;
                """;

            var pCode = cmd.Parameters.Add("@code", SqliteType.Text);
            var pLegacy = cmd.Parameters.Add("@legacy", SqliteType.Text);
            var pName = cmd.Parameters.Add("@name", SqliteType.Text);
            var pFamily = cmd.Parameters.Add("@family", SqliteType.Text);
            var pPhone = cmd.Parameters.Add("@phone", SqliteType.Text);
            var pMobile = cmd.Parameters.Add("@mobile", SqliteType.Text);
            var pExtraPhones = cmd.Parameters.Add("@extraPhones", SqliteType.Text);
            var pAddress = cmd.Parameters.Add("@address", SqliteType.Text);
            var pPostal = cmd.Parameters.Add("@postal", SqliteType.Text);
            var pNational = cmd.Parameters.Add("@national", SqliteType.Text);
            var pSourceEdited = cmd.Parameters.Add("@sourceEdited", SqliteType.Text);
            var pDeleted = cmd.Parameters.Add("@deleted", SqliteType.Integer);
            var pSchemaVer = cmd.Parameters.Add("@schemaVer", SqliteType.Text);
            var pImportedAt = cmd.Parameters.Add("@importedAt", SqliteType.Text);

            foreach (var customer in customers)
            {
                if (string.IsNullOrWhiteSpace(customer.CustomerCode))
                    throw new ArgumentException("CustomerCode is required for every shadow customer.");

                pCode.Value = customer.CustomerCode;
                pLegacy.Value = ToDbValue(customer.LegacyCode);
                pName.Value = ToDbValue(customer.CustomerName);
                pFamily.Value = ToDbValue(customer.CustomerFamily);
                pPhone.Value = ToDbValue(customer.Phone);
                pMobile.Value = ToDbValue(customer.Mobile);
                pExtraPhones.Value = JsonSerializer.Serialize(customer.AdditionalPhones);
                pAddress.Value = ToDbValue(customer.Address);
                pPostal.Value = ToDbValue(customer.PostalCode);
                pNational.Value = ToDbValue(customer.NationalCode);
                pSourceEdited.Value = ToDbValue(customer.SourceEditedAt);
                pDeleted.Value = customer.IsDeleted ? 1 : 0;
                pSchemaVer.Value = customer.SchemaVersion;
                pImportedAt.Value = customer.ImportedAt.ToString("O", CultureInfo.InvariantCulture);
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
        }
    }

    public void UpsertInvoice(ShadowInvoice invoice) => UpsertInvoices([invoice]);

    public void UpsertInvoices(IEnumerable<ShadowInvoice> invoices)
    {
        ArgumentNullException.ThrowIfNull(invoices);
        lock (_gate)
        {
            ThrowIfDisposed();
            using var tx = _connection.BeginTransaction();
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO holoo_shadow_invoices (
                    invoice_code, invoice_type, invoice_date, customer_code,
                    total_amount, cash_amount, cheque_amount, credit_amount,
                    card_amount, discount_amount, is_deleted, schema_version, imported_at
                )
                VALUES (
                    @code, @type, @date, @cust,
                    @total, @cash, @cheque, @credit,
                    @card, @discount, @deleted, @schemaVer, @importedAt
                )
                ON CONFLICT(invoice_code) DO UPDATE SET
                    invoice_type = excluded.invoice_type,
                    invoice_date = excluded.invoice_date,
                    customer_code = excluded.customer_code,
                    total_amount = excluded.total_amount,
                    cash_amount = excluded.cash_amount,
                    cheque_amount = excluded.cheque_amount,
                    credit_amount = excluded.credit_amount,
                    card_amount = excluded.card_amount,
                    discount_amount = excluded.discount_amount,
                    is_deleted = excluded.is_deleted,
                    schema_version = excluded.schema_version,
                    imported_at = excluded.imported_at;
                """;

            using var delItemsCmd = _connection.CreateCommand();
            delItemsCmd.Transaction = tx;
            delItemsCmd.CommandText = "DELETE FROM holoo_shadow_invoice_items WHERE invoice_code = @inv;";
            var pDelInv = delItemsCmd.Parameters.Add("@inv", SqliteType.Text);

            using var insItemCmd = _connection.CreateCommand();
            insItemCmd.Transaction = tx;
            insItemCmd.CommandText = """
                INSERT INTO holoo_shadow_invoice_items (
                    invoice_code, line_index, product_code, quantity,
                    unit_price, buy_price, discount_percent, line_discount_amount
                )
                VALUES (@inv, @idx, @prod, @qty, @unit, @buy, @discPct, @discAmt);
                """;
            var pItemInv = insItemCmd.Parameters.Add("@inv", SqliteType.Text);
            var pItemIdx = insItemCmd.Parameters.Add("@idx", SqliteType.Integer);
            var pItemProd = insItemCmd.Parameters.Add("@prod", SqliteType.Text);
            var pItemQty = insItemCmd.Parameters.Add("@qty", SqliteType.Text);
            var pItemUnit = insItemCmd.Parameters.Add("@unit", SqliteType.Text);
            var pItemBuy = insItemCmd.Parameters.Add("@buy", SqliteType.Text);
            var pItemDiscPct = insItemCmd.Parameters.Add("@discPct", SqliteType.Text);
            var pItemDiscAmt = insItemCmd.Parameters.Add("@discAmt", SqliteType.Text);

            var pCode = cmd.Parameters.Add("@code", SqliteType.Text);
            var pType = cmd.Parameters.Add("@type", SqliteType.Text);
            var pDate = cmd.Parameters.Add("@date", SqliteType.Text);
            var pCust = cmd.Parameters.Add("@cust", SqliteType.Text);
            var pTotal = cmd.Parameters.Add("@total", SqliteType.Text);
            var pCash = cmd.Parameters.Add("@cash", SqliteType.Text);
            var pCheque = cmd.Parameters.Add("@cheque", SqliteType.Text);
            var pCredit = cmd.Parameters.Add("@credit", SqliteType.Text);
            var pCard = cmd.Parameters.Add("@card", SqliteType.Text);
            var pDiscount = cmd.Parameters.Add("@discount", SqliteType.Text);
            var pDeleted = cmd.Parameters.Add("@deleted", SqliteType.Integer);
            var pSchemaVer = cmd.Parameters.Add("@schemaVer", SqliteType.Text);
            var pImportedAt = cmd.Parameters.Add("@importedAt", SqliteType.Text);

            foreach (var invoice in invoices)
            {
                if (string.IsNullOrWhiteSpace(invoice.InvoiceCode))
                    throw new ArgumentException("InvoiceCode is required for every shadow invoice.");

                pCode.Value = invoice.InvoiceCode;
                pType.Value = ToDbValue(invoice.InvoiceType);
                pDate.Value = ToDbValue(invoice.InvoiceDate);
                pCust.Value = ToDbValue(invoice.CustomerCode);
                pTotal.Value = ToDbDecimal(invoice.TotalAmount);
                pCash.Value = ToDbDecimal(invoice.CashAmount);
                pCheque.Value = ToDbDecimal(invoice.ChequeAmount);
                pCredit.Value = ToDbDecimal(invoice.CreditAmount);
                pCard.Value = ToDbDecimal(invoice.CardAmount);
                pDiscount.Value = ToDbDecimal(invoice.DiscountAmount);
                pDeleted.Value = invoice.IsDeleted ? 1 : 0;
                pSchemaVer.Value = invoice.SchemaVersion;
                pImportedAt.Value = invoice.ImportedAt.ToString("O", CultureInfo.InvariantCulture);
                cmd.ExecuteNonQuery();

                pDelInv.Value = invoice.InvoiceCode;
                delItemsCmd.ExecuteNonQuery();

                foreach (var item in invoice.Items)
                {
                    pItemInv.Value = invoice.InvoiceCode;
                    pItemIdx.Value = item.LineIndex;
                    pItemProd.Value = item.ProductCode;
                    pItemQty.Value = ToDbDecimal(item.Quantity);
                    pItemUnit.Value = ToDbDecimal(item.UnitPrice);
                    pItemBuy.Value = ToDbDecimal(item.BuyPrice);
                    pItemDiscPct.Value = ToDbDecimal(item.DiscountPercent);
                    pItemDiscAmt.Value = ToDbDecimal(item.LineDiscountAmount);
                    insItemCmd.ExecuteNonQuery();
                }
            }

            tx.Commit();
        }
    }

    public ShadowProduct? GetProduct(string productCode)
    {
        if (string.IsNullOrWhiteSpace(productCode))
            return null;

        lock (_gate)
        {
            ThrowIfDisposed();
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT * FROM holoo_shadow_products WHERE product_code = @code;";
            cmd.Parameters.AddWithValue("@code", productCode);
            using var reader = cmd.ExecuteReader();
            return reader.Read() ? ReadProduct(reader) : null;
        }
    }

    public IReadOnlyList<ShadowProduct> ListProducts()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT * FROM holoo_shadow_products ORDER BY product_code;";
            using var reader = cmd.ExecuteReader();
            var list = new List<ShadowProduct>();
            while (reader.Read())
                list.Add(ReadProduct(reader));
            return list;
        }
    }

    public IReadOnlyList<ShadowProduct> ListProductsMissingPrice() =>
        ListProducts().Where(static p => p.EvaluatePriceAndStock().EffectivePrice is null).ToList();

    public IReadOnlyList<ShadowProduct> ListProductsWithUnverifiedStock() =>
        ListProducts().Where(static p => p.StockUnverified).ToList();

    public decimal? EffectiveSalePrice(string productCode) =>
        GetProduct(productCode)?.EvaluatePriceAndStock().EffectivePrice;

    public ShadowCustomer? GetCustomer(string customerCode)
    {
        if (string.IsNullOrWhiteSpace(customerCode))
            return null;

        lock (_gate)
        {
            ThrowIfDisposed();
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT * FROM holoo_shadow_customers WHERE customer_code = @code;";
            cmd.Parameters.AddWithValue("@code", customerCode);
            using var reader = cmd.ExecuteReader();
            return reader.Read() ? ReadCustomer(reader) : null;
        }
    }

    public IReadOnlyList<ShadowCustomer> ListCustomers()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT * FROM holoo_shadow_customers ORDER BY customer_code;";
            using var reader = cmd.ExecuteReader();
            var list = new List<ShadowCustomer>();
            while (reader.Read())
                list.Add(ReadCustomer(reader));
            return list;
        }
    }

    public ShadowInvoice? GetInvoice(string invoiceCode)
    {
        if (string.IsNullOrWhiteSpace(invoiceCode))
            return null;

        lock (_gate)
        {
            ThrowIfDisposed();
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT * FROM holoo_shadow_invoices WHERE invoice_code = @code;";
            cmd.Parameters.AddWithValue("@code", invoiceCode);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
                return null;

            var items = ReadInvoiceItemsLocked(invoiceCode);
            return ReadInvoice(reader, items);
        }
    }

    public IReadOnlyList<ShadowInvoice> ListInvoices()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            var itemsByInvoice = ReadAllInvoiceItemsLocked();
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT * FROM holoo_shadow_invoices ORDER BY invoice_date DESC, invoice_code DESC;";
            using var reader = cmd.ExecuteReader();
            var list = new List<ShadowInvoice>();
            while (reader.Read())
            {
                var code = reader.GetString(reader.GetOrdinal("invoice_code"));
                itemsByInvoice.TryGetValue(code, out var items);
                list.Add(ReadInvoice(reader, items ?? Array.Empty<ShadowInvoiceItem>()));
            }

            return list;
        }
    }

    public IReadOnlyList<ShadowInvoice> ListInvoicesForCustomer(string customerCode) =>
        ListInvoices()
            .Where(inv => string.Equals(inv.CustomerCode, customerCode, StringComparison.OrdinalIgnoreCase))
            .ToList();

    public HolooSyncCheckpoint SaveCheckpoint(
        string sourceKind,
        string schemaVersion,
        string schemaFingerprint,
        string lastSyncMarker,
        string syncMode,
        DateTimeOffset syncedAt,
        int productsImported,
        int customersImported,
        int invoicesImported)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO holoo_sync_checkpoints (
                    source_kind, schema_version, schema_fingerprint, last_sync_marker,
                    sync_mode, synced_at, products_imported, customers_imported, invoices_imported
                )
                VALUES (@kind, @ver, @fp, @marker, @mode, @at, @prods, @custs, @invs);
                SELECT last_insert_rowid();
                """;
            cmd.Parameters.AddWithValue("@kind", sourceKind);
            cmd.Parameters.AddWithValue("@ver", schemaVersion);
            cmd.Parameters.AddWithValue("@fp", schemaFingerprint);
            cmd.Parameters.AddWithValue("@marker", lastSyncMarker);
            cmd.Parameters.AddWithValue("@mode", syncMode);
            cmd.Parameters.AddWithValue("@at", syncedAt.ToString("O", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("@prods", productsImported);
            cmd.Parameters.AddWithValue("@custs", customersImported);
            cmd.Parameters.AddWithValue("@invs", invoicesImported);
            var id = Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);

            return new HolooSyncCheckpoint(
                id,
                sourceKind,
                schemaVersion,
                schemaFingerprint,
                lastSyncMarker,
                syncMode,
                syncedAt,
                productsImported,
                customersImported,
                invoicesImported);
        }
    }

    public HolooSyncCheckpoint? GetLatestCheckpoint(string? sourceKind = null)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = string.IsNullOrWhiteSpace(sourceKind)
                ? "SELECT * FROM holoo_sync_checkpoints ORDER BY checkpoint_id DESC LIMIT 1;"
                : "SELECT * FROM holoo_sync_checkpoints WHERE source_kind = @kind ORDER BY checkpoint_id DESC LIMIT 1;";
            if (!string.IsNullOrWhiteSpace(sourceKind))
                cmd.Parameters.AddWithValue("@kind", sourceKind);

            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
                return null;

            return new HolooSyncCheckpoint(
                reader.GetInt64(reader.GetOrdinal("checkpoint_id")),
                reader.GetString(reader.GetOrdinal("source_kind")),
                reader.GetString(reader.GetOrdinal("schema_version")),
                reader.GetString(reader.GetOrdinal("schema_fingerprint")),
                reader.GetString(reader.GetOrdinal("last_sync_marker")),
                reader.GetString(reader.GetOrdinal("sync_mode")),
                DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("synced_at")), CultureInfo.InvariantCulture),
                reader.GetInt32(reader.GetOrdinal("products_imported")),
                reader.GetInt32(reader.GetOrdinal("customers_imported")),
                reader.GetInt32(reader.GetOrdinal("invoices_imported")));
        }
    }

    public void RecordConsent(HolooConsentRecord consent)
    {
        ArgumentNullException.ThrowIfNull(consent);
        lock (_gate)
        {
            ThrowIfDisposed();
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO holoo_dm_consents (
                    consent_id, consent_granted, manager_name,
                    sql_instance_name, database_name, tier,
                    granted_at, revoked, revoked_at
                )
                VALUES (@id, @granted, @mgr, @inst, @db, @tier, @at, @revoked, @revokedAt)
                ON CONFLICT(consent_id) DO UPDATE SET
                    consent_granted = excluded.consent_granted,
                    manager_name = excluded.manager_name,
                    sql_instance_name = excluded.sql_instance_name,
                    database_name = excluded.database_name,
                    tier = excluded.tier,
                    granted_at = excluded.granted_at,
                    revoked = excluded.revoked,
                    revoked_at = excluded.revoked_at;
                """;
            cmd.Parameters.AddWithValue("@id", consent.ConsentId.ToString("D"));
            cmd.Parameters.AddWithValue("@granted", consent.ConsentGranted ? 1 : 0);
            cmd.Parameters.AddWithValue("@mgr", consent.ManagerName);
            cmd.Parameters.AddWithValue("@inst", consent.SqlInstanceName);
            cmd.Parameters.AddWithValue("@db", consent.DatabaseName);
            cmd.Parameters.AddWithValue("@tier", (int)consent.Tier);
            cmd.Parameters.AddWithValue("@at", consent.GrantedAt.ToString("O", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("@revoked", consent.Revoked ? 1 : 0);
            cmd.Parameters.AddWithValue("@revokedAt", consent.RevokedAt.HasValue
                ? consent.RevokedAt.Value.ToString("O", CultureInfo.InvariantCulture)
                : DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }

    public HolooConsentRecord? GetConsent(Guid consentId)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT * FROM holoo_dm_consents WHERE consent_id = @id;";
            cmd.Parameters.AddWithValue("@id", consentId.ToString("D"));
            using var reader = cmd.ExecuteReader();
            return reader.Read() ? ReadConsent(reader) : null;
        }
    }

    public HolooConsentRecord? GetLatestConsent()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT * FROM holoo_dm_consents ORDER BY rowid DESC LIMIT 1;";
            using var reader = cmd.ExecuteReader();
            return reader.Read() ? ReadConsent(reader) : null;
        }
    }

    public void RevokeConsent(Guid consentId, DateTimeOffset revokedAt)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                UPDATE holoo_dm_consents
                SET revoked = 1,
                    revoked_at = @at
                WHERE consent_id = @id;
                """;
            cmd.Parameters.AddWithValue("@id", consentId.ToString("D"));
            cmd.Parameters.AddWithValue("@at", revokedAt.ToString("O", CultureInfo.InvariantCulture));
            cmd.ExecuteNonQuery();
        }
    }

    public HolooShadowStore ExportToMemoryStore(string? schemaVersionOverride = null)
    {
        var checkpoint = GetLatestCheckpoint();
        var store = new HolooShadowStore
        {
            SchemaVersion = schemaVersionOverride ?? checkpoint?.SchemaVersion ?? string.Empty,
        };

        foreach (var p in ListProducts())
            store.UpsertProduct(p);
        foreach (var c in ListCustomers())
            store.UpsertCustomer(c);
        foreach (var inv in ListInvoices())
            store.UpsertInvoice(inv);

        return store;
    }

    private void InitializeSchema()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS holoo_shadow_products (
                product_code TEXT PRIMARY KEY,
                legacy_code TEXT NULL,
                product_name TEXT NULL,
                country_of_origin TEXT NULL,
                buy_price TEXT NULL,
                explicit_sale_price TEXT NULL,
                last_sold_price TEXT NULL,
                last_sold_invoice_code TEXT NULL,
                last_sold_invoice_date TEXT NULL,
                holoo_visible_stock TEXT NULL,
                unit_code TEXT NULL,
                unit_name TEXT NULL,
                main_group_code TEXT NULL,
                main_group_name TEXT NULL,
                sub_group_code TEXT NULL,
                sub_group_name TEXT NULL,
                model TEXT NULL,
                source_edited_at TEXT NULL,
                schema_version TEXT NOT NULL,
                imported_at TEXT NOT NULL,
                stock_unverified INTEGER NOT NULL DEFAULT 1,
                verified_stock TEXT NULL,
                stock_verified_at TEXT NULL,
                stock_verified_by TEXT NULL,
                is_deleted INTEGER NOT NULL DEFAULT 0,
                is_active INTEGER NOT NULL DEFAULT 1
            );

            CREATE TABLE IF NOT EXISTS holoo_shadow_customers (
                customer_code TEXT PRIMARY KEY,
                legacy_code TEXT NULL,
                customer_name TEXT NULL,
                customer_family TEXT NULL,
                phone TEXT NULL,
                mobile TEXT NULL,
                additional_phones_json TEXT NOT NULL DEFAULT '[]',
                address TEXT NULL,
                postal_code TEXT NULL,
                national_code TEXT NULL,
                source_edited_at TEXT NULL,
                is_deleted INTEGER NOT NULL DEFAULT 0,
                schema_version TEXT NOT NULL,
                imported_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS holoo_shadow_invoices (
                invoice_code TEXT PRIMARY KEY,
                invoice_type TEXT NULL,
                invoice_date TEXT NULL,
                customer_code TEXT NULL,
                total_amount TEXT NULL,
                cash_amount TEXT NULL,
                cheque_amount TEXT NULL,
                credit_amount TEXT NULL,
                card_amount TEXT NULL,
                discount_amount TEXT NULL,
                is_deleted INTEGER NOT NULL DEFAULT 0,
                schema_version TEXT NOT NULL,
                imported_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS holoo_shadow_invoice_items (
                invoice_code TEXT NOT NULL,
                line_index INTEGER NOT NULL,
                product_code TEXT NOT NULL,
                quantity TEXT NULL,
                unit_price TEXT NULL,
                buy_price TEXT NULL,
                discount_percent TEXT NULL,
                line_discount_amount TEXT NULL,
                PRIMARY KEY (invoice_code, line_index),
                FOREIGN KEY (invoice_code) REFERENCES holoo_shadow_invoices(invoice_code) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS holoo_sync_checkpoints (
                checkpoint_id INTEGER PRIMARY KEY AUTOINCREMENT,
                source_kind TEXT NOT NULL,
                schema_version TEXT NOT NULL,
                schema_fingerprint TEXT NOT NULL,
                last_sync_marker TEXT NOT NULL,
                sync_mode TEXT NOT NULL,
                synced_at TEXT NOT NULL,
                products_imported INTEGER NOT NULL,
                customers_imported INTEGER NOT NULL,
                invoices_imported INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS holoo_dm_consents (
                consent_id TEXT PRIMARY KEY,
                consent_granted INTEGER NOT NULL,
                manager_name TEXT NOT NULL,
                sql_instance_name TEXT NOT NULL,
                database_name TEXT NOT NULL,
                tier INTEGER NOT NULL,
                granted_at TEXT NOT NULL,
                revoked INTEGER NOT NULL DEFAULT 0,
                revoked_at TEXT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }

    private IReadOnlyList<ShadowInvoiceItem> ReadInvoiceItemsLocked(string invoiceCode)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM holoo_shadow_invoice_items WHERE invoice_code = @inv ORDER BY line_index;";
        cmd.Parameters.AddWithValue("@inv", invoiceCode);
        using var reader = cmd.ExecuteReader();
        var items = new List<ShadowInvoiceItem>();
        while (reader.Read())
            items.Add(ReadInvoiceItem(reader));
        return items;
    }

    private Dictionary<string, IReadOnlyList<ShadowInvoiceItem>> ReadAllInvoiceItemsLocked()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM holoo_shadow_invoice_items ORDER BY invoice_code, line_index;";
        using var reader = cmd.ExecuteReader();
        var map = new Dictionary<string, List<ShadowInvoiceItem>>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
        {
            var invCode = reader.GetString(reader.GetOrdinal("invoice_code"));
            if (!map.TryGetValue(invCode, out var list))
            {
                list = new List<ShadowInvoiceItem>();
                map[invCode] = list;
            }

            list.Add(ReadInvoiceItem(reader));
        }

        return map.ToDictionary(
            static kv => kv.Key,
            static kv => (IReadOnlyList<ShadowInvoiceItem>)kv.Value,
            StringComparer.OrdinalIgnoreCase);
    }

    private static ShadowProduct ReadProduct(SqliteDataReader reader) => new()
    {
        ProductCode = reader.GetString(reader.GetOrdinal("product_code")),
        LegacyCode = GetNullableString(reader, "legacy_code"),
        ProductName = GetNullableString(reader, "product_name"),
        CountryOfOrigin = GetNullableString(reader, "country_of_origin"),
        BuyPrice = GetNullableDecimal(reader, "buy_price"),
        ExplicitSalePrice = GetNullableDecimal(reader, "explicit_sale_price"),
        LastSoldPrice = GetNullableDecimal(reader, "last_sold_price"),
        LastSoldInvoiceCode = GetNullableString(reader, "last_sold_invoice_code"),
        LastSoldInvoiceDate = GetNullableString(reader, "last_sold_invoice_date"),
        HolooVisibleStock = GetNullableDecimal(reader, "holoo_visible_stock"),
        UnitCode = GetNullableString(reader, "unit_code"),
        UnitName = GetNullableString(reader, "unit_name"),
        MainGroupCode = GetNullableString(reader, "main_group_code"),
        MainGroupName = GetNullableString(reader, "main_group_name"),
        SubGroupCode = GetNullableString(reader, "sub_group_code"),
        SubGroupName = GetNullableString(reader, "sub_group_name"),
        Model = GetNullableString(reader, "model"),
        SourceEditedAt = GetNullableString(reader, "source_edited_at"),
        SchemaVersion = reader.GetString(reader.GetOrdinal("schema_version")),
        ImportedAt = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("imported_at")), CultureInfo.InvariantCulture),
        StockUnverified = reader.GetInt32(reader.GetOrdinal("stock_unverified")) != 0,
        VerifiedStock = GetNullableDecimal(reader, "verified_stock"),
        StockVerifiedAt = GetNullableDateTimeOffset(reader, "stock_verified_at"),
        StockVerifiedBy = GetNullableString(reader, "stock_verified_by"),
        IsDeleted = reader.GetInt32(reader.GetOrdinal("is_deleted")) != 0,
        IsActive = reader.GetInt32(reader.GetOrdinal("is_active")) != 0,
    };

    private static ShadowCustomer ReadCustomer(SqliteDataReader reader)
    {
        var phonesJson = GetNullableString(reader, "additional_phones_json") ?? "[]";
        var phones = JsonSerializer.Deserialize<string[]>(phonesJson) ?? Array.Empty<string>();
        return new ShadowCustomer
        {
            CustomerCode = reader.GetString(reader.GetOrdinal("customer_code")),
            LegacyCode = GetNullableString(reader, "legacy_code"),
            CustomerName = GetNullableString(reader, "customer_name"),
            CustomerFamily = GetNullableString(reader, "customer_family"),
            Phone = GetNullableString(reader, "phone"),
            Mobile = GetNullableString(reader, "mobile"),
            AdditionalPhones = phones,
            Address = GetNullableString(reader, "address"),
            PostalCode = GetNullableString(reader, "postal_code"),
            NationalCode = GetNullableString(reader, "national_code"),
            SourceEditedAt = GetNullableString(reader, "source_edited_at"),
            IsDeleted = reader.GetInt32(reader.GetOrdinal("is_deleted")) != 0,
            SchemaVersion = reader.GetString(reader.GetOrdinal("schema_version")),
            ImportedAt = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("imported_at")), CultureInfo.InvariantCulture),
        };
    }

    private static ShadowInvoice ReadInvoice(SqliteDataReader reader, IReadOnlyList<ShadowInvoiceItem> items) => new()
    {
        InvoiceCode = reader.GetString(reader.GetOrdinal("invoice_code")),
        InvoiceType = GetNullableString(reader, "invoice_type"),
        InvoiceDate = GetNullableString(reader, "invoice_date"),
        CustomerCode = GetNullableString(reader, "customer_code"),
        TotalAmount = GetNullableDecimal(reader, "total_amount"),
        CashAmount = GetNullableDecimal(reader, "cash_amount"),
        ChequeAmount = GetNullableDecimal(reader, "cheque_amount"),
        CreditAmount = GetNullableDecimal(reader, "credit_amount"),
        CardAmount = GetNullableDecimal(reader, "card_amount"),
        DiscountAmount = GetNullableDecimal(reader, "discount_amount"),
        IsDeleted = reader.GetInt32(reader.GetOrdinal("is_deleted")) != 0,
        Items = items,
        SchemaVersion = reader.GetString(reader.GetOrdinal("schema_version")),
        ImportedAt = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("imported_at")), CultureInfo.InvariantCulture),
    };

    private static ShadowInvoiceItem ReadInvoiceItem(SqliteDataReader reader) => new()
    {
        LineIndex = reader.GetInt32(reader.GetOrdinal("line_index")),
        ProductCode = reader.GetString(reader.GetOrdinal("product_code")),
        Quantity = GetNullableDecimal(reader, "quantity"),
        UnitPrice = GetNullableDecimal(reader, "unit_price"),
        BuyPrice = GetNullableDecimal(reader, "buy_price"),
        DiscountPercent = GetNullableDecimal(reader, "discount_percent"),
        LineDiscountAmount = GetNullableDecimal(reader, "line_discount_amount"),
    };

    private static HolooConsentRecord ReadConsent(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(reader.GetOrdinal("consent_id"))),
        reader.GetInt32(reader.GetOrdinal("consent_granted")) != 0,
        reader.GetString(reader.GetOrdinal("manager_name")),
        reader.GetString(reader.GetOrdinal("sql_instance_name")),
        reader.GetString(reader.GetOrdinal("database_name")),
        (HolooAccessTier)reader.GetInt32(reader.GetOrdinal("tier")),
        DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("granted_at")), CultureInfo.InvariantCulture),
        reader.GetInt32(reader.GetOrdinal("revoked")) != 0,
        GetNullableDateTimeOffset(reader, "revoked_at"));

    private static object ToDbValue(string? value) =>
        value is null ? DBNull.Value : value;

    private static object ToDbDecimal(decimal? value) =>
        value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : DBNull.Value;

    private static string? GetNullableString(SqliteDataReader reader, string column)
    {
        var ord = reader.GetOrdinal(column);
        return reader.IsDBNull(ord) ? null : reader.GetString(ord);
    }

    private static decimal? GetNullableDecimal(SqliteDataReader reader, string column)
    {
        var ord = reader.GetOrdinal(column);
        if (reader.IsDBNull(ord))
            return null;
        var raw = reader.GetString(ord);
        return decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    private static DateTimeOffset? GetNullableDateTimeOffset(SqliteDataReader reader, string column)
    {
        var ord = reader.GetOrdinal(column);
        if (reader.IsDBNull(ord))
            return null;
        return DateTimeOffset.Parse(reader.GetString(ord), CultureInfo.InvariantCulture);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _connection.Dispose();
            _disposed = true;
        }
    }
}
