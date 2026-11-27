using System.Data;
using System.Data.Common;
using System.Globalization;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;

namespace Kandoo.HolooConnector;

public interface IHolooDataSource
{
    string SourceKind { get; }
    Task<HolooSchemaInspection> InspectSchemaAsync(CancellationToken cancellationToken = default);
    Task<HolooDataset> ReadDatasetAsync(
        HolooSchemaInspection schema,
        string? sinceMarker = null,
        CancellationToken cancellationToken = default);
}

internal static class HolooSqlDatasetBuilder
{
    private static readonly string[] ArticleColumns =
    [
        "A_Code", "A_Code_C", "A_Name", "A_Country", "Buy_Price", "EndBuy_Price",
        "Sel_Price", "Exist", "Type_Anbar_C", "VahedCode", "Model",
        "Endeditdate", "Delete", "IsActive",
    ];

    private static readonly string[] ArticlePriceColumns =
    [
        "ID", "A_Code", "SelID", "Sel_Price", "ModifyDate", "Min_Price", "Max_Price",
    ];

    private static readonly string[] CustomerColumns =
    [
        "C_Code", "C_Code_C", "C_Name", "C_AliasName", "C_Tel", "C_Mobile",
        "C_Address", "Zip_Code", "National_Code", "Endeditdate", "Delete",
    ];

    private static readonly string[] CustTellColumns =
    [
        "CCode", "C_Tell1", "C_Tell2", "C_Tell3", "C_Tell4", "C_Tell5",
        "C_Mobile1", "C_Mobile2", "C_Mobile3", "C_Mobile4", "C_Mobile5",
    ];

    private static readonly string[] FactureColumns =
    [
        "Fac_Code", "Fac_Type", "Fac_Date", "C_Code", "Sum_Price",
        "FNaghd", "FCheck", "FNesieh", "Card", "Takhfif", "Delete",
    ];

    private static readonly string[] FactArtColumns =
    [
        "Fac_Code", "Fac_Type", "A_Index", "A_Code", "Few_Article",
        "Price_BS", "Sel_Price", "Buy_Price", "DarsadTakhfif", "TakhfifSatriR",
    ];

    private static readonly string[] MainGroupColumns =
    [
        "M_groupcode", "M_groupname", "Type_Anbar_C", "Endeditdate",
    ];

    private static readonly string[] SubGroupColumns =
    [
        "M_groupcode", "S_groupcode", "S_groupname", "Endeditdate",
    ];

    private static readonly string[] TypeAnbColumns =
    [
        "Type_Anbar_C", "Type_Anbar_N",
    ];

    private static readonly string[] UnitColumns =
    [
        "Unit_Code", "Unit_Name", "Endeditdate",
    ];

    public static async Task<HolooDataset> ReadFromConnectionAsync(
        DbConnection connection,
        IReadOnlyDictionary<string, HashSet<string>> columnsByTable,
        bool isSqlServer,
        string? sinceMarker,
        CancellationToken cancellationToken)
    {
        var articleRows = await QueryTableAsync(connection, columnsByTable, "ARTICLE", ArticleColumns, isSqlServer,
            sinceColumn: HasColumn(columnsByTable, "ARTICLE", "Endeditdate") ? "Endeditdate" : null,
            sinceValue: sinceMarker, strictGreater: true, cancellationToken: cancellationToken).ConfigureAwait(false);

        var priceRows = await QueryTableAsync(connection, columnsByTable, "ArticlePrice", ArticlePriceColumns, isSqlServer,
            sinceColumn: null, sinceValue: null, strictGreater: true, cancellationToken: cancellationToken).ConfigureAwait(false);

        var customerRows = await QueryTableAsync(connection, columnsByTable, "CUSTOMER", CustomerColumns, isSqlServer,
            sinceColumn: HasColumn(columnsByTable, "CUSTOMER", "Endeditdate") ? "Endeditdate" : null,
            sinceValue: sinceMarker, strictGreater: true, cancellationToken: cancellationToken).ConfigureAwait(false);

        var custTellRows = await QueryTableAsync(connection, columnsByTable, "Cust_Tell", CustTellColumns, isSqlServer,
            sinceColumn: null, sinceValue: null, strictGreater: true, cancellationToken: cancellationToken).ConfigureAwait(false);

        var factureRows = await QueryTableAsync(connection, columnsByTable, "FACTURE", FactureColumns, isSqlServer,
            sinceColumn: HasColumn(columnsByTable, "FACTURE", "Fac_Date") ? "Fac_Date" : null,
            sinceValue: sinceMarker, strictGreater: false, cancellationToken: cancellationToken).ConfigureAwait(false);

        var factArtRows = await QueryFactArtAsync(connection, columnsByTable, isSqlServer, sinceMarker, cancellationToken).ConfigureAwait(false);

        var mainGroupRows = await QueryTableAsync(connection, columnsByTable, "M_GROUP", MainGroupColumns, isSqlServer,
            sinceColumn: null, sinceValue: null, strictGreater: true, cancellationToken: cancellationToken).ConfigureAwait(false);

        var subGroupRows = await QueryTableAsync(connection, columnsByTable, "S_GROUP", SubGroupColumns, isSqlServer,
            sinceColumn: null, sinceValue: null, strictGreater: true, cancellationToken: cancellationToken).ConfigureAwait(false);

        var typeAnbRows = await QueryTableAsync(connection, columnsByTable, "TYPE_ANB", TypeAnbColumns, isSqlServer,
            sinceColumn: null, sinceValue: null, strictGreater: true, cancellationToken: cancellationToken).ConfigureAwait(false);

        var unitRows = await QueryTableAsync(connection, columnsByTable, "UNIT", UnitColumns, isSqlServer,
            sinceColumn: null, sinceValue: null, strictGreater: true, cancellationToken: cancellationToken).ConfigureAwait(false);

        var custTellByCode = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in custTellRows)
        {
            if (!row.TryGetValue("CCode", out var code) || string.IsNullOrWhiteSpace(code))
                continue;
            if (!custTellByCode.TryGetValue(code, out var list))
            {
                list = new List<string>();
                custTellByCode[code] = list;
            }

            list.AddRange(HolooMapper.ExtractCustTellPhones(row));
        }

        var customers = customerRows
            .Select(HolooMapper.MapCustomerFull)
            .Where(static c => !string.IsNullOrWhiteSpace(c.CustomerCode))
            .Select(c =>
            {
                if (!custTellByCode.TryGetValue(c.CustomerCode, out var extra))
                    return c;

                var merged = c.AdditionalPhones.Concat(extra)
                    .Where(static p => !string.IsNullOrWhiteSpace(p))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                return c with { AdditionalPhones = merged };
            })
            .ToList();

        var products = articleRows
            .Select(HolooMapper.MapArticleFull)
            .Where(static p => !string.IsNullOrWhiteSpace(p.ProductCode))
            .ToList();

        var prices = priceRows
            .Select(HolooMapper.MapArticlePrice)
            .Where(static ap => !string.IsNullOrWhiteSpace(ap.ProductCode))
            .ToList();

        var invoices = factureRows
            .Select(HolooMapper.MapFactureFull)
            .Where(static inv => !string.IsNullOrWhiteSpace(inv.InvoiceCode))
            .ToList();

        var items = factArtRows
            .Select(HolooMapper.MapFactArtFull)
            .Where(static it => !string.IsNullOrWhiteSpace(it.ProductCode))
            .ToList();

        return new HolooDataset(
            products,
            prices,
            customers,
            invoices,
            items,
            mainGroupRows.Select(HolooMapper.MapMainGroup).Where(static g => !string.IsNullOrWhiteSpace(g.GroupCode)).ToList(),
            subGroupRows.Select(HolooMapper.MapSubGroup).Where(static g => !string.IsNullOrWhiteSpace(g.GroupCode)).ToList(),
            unitRows.Select(HolooMapper.MapUnit).Where(static u => !string.IsNullOrWhiteSpace(u.UnitCode)).ToList(),
            typeAnbRows.Select(HolooMapper.MapWarehouseType).Where(static t => !string.IsNullOrWhiteSpace(t.TypeCode)).ToList());
    }

    private static async Task<IReadOnlyList<Dictionary<string, string>>> QueryFactArtAsync(
        DbConnection connection,
        IReadOnlyDictionary<string, HashSet<string>> columnsByTable,
        bool isSqlServer,
        string? sinceMarker,
        CancellationToken cancellationToken)
    {
        if (!columnsByTable.TryGetValue("FACTART", out var present))
            return Array.Empty<Dictionary<string, string>>();

        var selected = FactArtColumns.Where(c => present.Contains(c)).ToList();
        if (selected.Count == 0)
            return Array.Empty<Dictionary<string, string>>();

        var colsSql = string.Join(", ", selected.Select(c => QuoteIdent(c, isSqlServer)));
        var factArtTable = QuoteIdent("FACTART", isSqlServer);

        string sql;
        var hasSince = !string.IsNullOrWhiteSpace(sinceMarker) &&
                       HasColumn(columnsByTable, "FACTURE", "Fac_Date") &&
                       present.Contains("Fac_Code");

        if (hasSince)
        {
            var factureTable = QuoteIdent("FACTURE", isSqlServer);
            var facCodeCol = QuoteIdent("Fac_Code", isSqlServer);
            var facDateCol = QuoteIdent("Fac_Date", isSqlServer);
            sql = $"SELECT {colsSql} FROM {factArtTable} WHERE {facCodeCol} IN (SELECT {facCodeCol} FROM {factureTable} WHERE {facDateCol} >= @since);";
        }
        else
        {
            sql = $"SELECT {colsSql} FROM {factArtTable};";
        }

        return await ExecuteSelectAsync(connection, sql, hasSince ? sinceMarker : null, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<Dictionary<string, string>>> QueryTableAsync(
        DbConnection connection,
        IReadOnlyDictionary<string, HashSet<string>> columnsByTable,
        string tableName,
        string[] candidateColumns,
        bool isSqlServer,
        string? sinceColumn,
        string? sinceValue,
        bool strictGreater,
        CancellationToken cancellationToken)
    {
        if (!columnsByTable.TryGetValue(tableName, out var present))
            return Array.Empty<Dictionary<string, string>>();

        var selected = candidateColumns.Where(c => present.Contains(c)).ToList();
        if (selected.Count == 0)
            return Array.Empty<Dictionary<string, string>>();

        var colsSql = string.Join(", ", selected.Select(c => QuoteIdent(c, isSqlServer)));
        var tableSql = QuoteIdent(tableName, isSqlServer);

        var applySince = !string.IsNullOrWhiteSpace(sinceColumn) && !string.IsNullOrWhiteSpace(sinceValue);
        var sql = applySince
            ? $"SELECT {colsSql} FROM {tableSql} WHERE {QuoteIdent(sinceColumn!, isSqlServer)} {(strictGreater ? ">" : ">=")} @since;"
            : $"SELECT {colsSql} FROM {tableSql};";

        return await ExecuteSelectAsync(connection, sql, applySince ? sinceValue : null, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<IReadOnlyList<Dictionary<string, string>>> ExecuteSelectAsync(
        DbConnection connection,
        string sql,
        string? sinceValue,
        CancellationToken cancellationToken)
    {
        HolooQueryGuard.EnsureReadOnlySelect(sql);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandType = CommandType.Text;

        if (sinceValue is not null)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = "@since";
            p.Value = sinceValue;
            cmd.Parameters.Add(p);
        }

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var rows = new List<Dictionary<string, string>>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var row = new Dictionary<string, string>(reader.FieldCount, StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                if (reader.IsDBNull(i))
                    continue;

                var val = reader.GetValue(i);
                row[reader.GetName(i)] = val switch
                {
                    DateTime dt => dt.ToString("O", CultureInfo.InvariantCulture),
                    DateTimeOffset dto => dto.ToString("O", CultureInfo.InvariantCulture),
                    bool b => b ? "1" : "0",
                    _ => Convert.ToString(val, CultureInfo.InvariantCulture) ?? string.Empty,
                };
            }

            rows.Add(row);
        }

        return rows;
    }

    private static bool HasColumn(
        IReadOnlyDictionary<string, HashSet<string>> columnsByTable,
        string tableName,
        string columnName) =>
        columnsByTable.TryGetValue(tableName, out var cols) && cols.Contains(columnName);

    private static string QuoteIdent(string ident, bool isSqlServer) =>
        isSqlServer
            ? $"[{ident.Replace("]", "]]", StringComparison.Ordinal)}]"
            : $"\"{ident.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
}

/// <summary>
/// Read-only Microsoft.Data.SqlClient reader targeting the owner's local HolooCopy SQL Server database.
/// Enforces ApplicationIntent=ReadOnly and HolooQueryGuard on every statement.
/// </summary>
public sealed class SqlServerHolooSource : IHolooDataSource
{
    private readonly string _readOnlyConnectionString;
    private Dictionary<string, HashSet<string>>? _cachedColumnsByTable;

    public string SourceKind => "sql_server";

    public SqlServerHolooSource(string readOnlyConnectionString)
    {
        if (string.IsNullOrWhiteSpace(readOnlyConnectionString))
            throw new ArgumentException("Connection string is required.", nameof(readOnlyConnectionString));

        var builder = new SqlConnectionStringBuilder(readOnlyConnectionString)
        {
            ApplicationIntent = ApplicationIntent.ReadOnly,
            PersistSecurityInfo = false,
        };
        _readOnlyConnectionString = builder.ConnectionString;
    }

    public async Task<HolooSchemaInspection> InspectSchemaAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(_readOnlyConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        const string metaSql = """
            SELECT TABLE_NAME, COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, IS_NULLABLE
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = 'dbo'
            ORDER BY TABLE_NAME, ORDINAL_POSITION;
            """;

        var rows = await HolooSqlDatasetBuilder.ExecuteSelectAsync(connection, metaSql, null, cancellationToken).ConfigureAwait(false);
        var columns = new List<HolooSchemaColumn>(rows.Count);
        var map = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var r in rows)
        {
            if (!r.TryGetValue("TABLE_NAME", out var table) || !r.TryGetValue("COLUMN_NAME", out var col))
                continue;
            r.TryGetValue("DATA_TYPE", out var dataType);
            int? maxLen = r.TryGetValue("CHARACTER_MAXIMUM_LENGTH", out var rawLen) && int.TryParse(rawLen, out var parsedLen)
                ? parsedLen
                : null;
            bool? isNull = r.TryGetValue("IS_NULLABLE", out var rawNull)
                ? rawNull.Equals("YES", StringComparison.OrdinalIgnoreCase)
                : null;

            columns.Add(new HolooSchemaColumn(table, col, dataType ?? string.Empty, maxLen, isNull));
            if (!map.TryGetValue(table, out var set))
            {
                set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                map[table] = set;
            }

            set.Add(col);
        }

        _cachedColumnsByTable = map;
        return HolooSchemaVersion.DetectFromColumns(columns);
    }

    public async Task<HolooDataset> ReadDatasetAsync(
        HolooSchemaInspection schema,
        string? sinceMarker = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(schema);
        if (!schema.IsCompatible)
            throw new InvalidOperationException($"Incompatible Holoo schema: missing {string.Join(", ", schema.MissingRequiredColumns)}");

        if (_cachedColumnsByTable is null)
            _ = await InspectSchemaAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = new SqlConnection(_readOnlyConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        return await HolooSqlDatasetBuilder.ReadFromConnectionAsync(
            connection,
            _cachedColumnsByTable!,
            isSqlServer: true,
            sinceMarker: sinceMarker,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Read-only SQLite synthetic Holoo fixture source (honesty-labeled synthetic SQL fixture mirroring
/// the 10-table Holoo H1-6 schema) for automated cross-platform CI verification.
/// </summary>
public sealed class SqliteSyntheticHolooSource : IHolooDataSource
{
    private readonly SqliteConnection _connection;
    private Dictionary<string, HashSet<string>>? _cachedColumnsByTable;

    public string SourceKind => "sqlite_synthetic";

    public SqliteSyntheticHolooSource(SqliteConnection connection)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
    }

    public async Task<HolooSchemaInspection> InspectSchemaAsync(CancellationToken cancellationToken = default)
    {
        if (_connection.State != ConnectionState.Open)
            await _connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using (var pragma = _connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA query_only = ON;";
            await pragma.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        const string tablesSql = "SELECT name FROM sqlite_master WHERE type IN ('table', 'view') AND name NOT LIKE 'sqlite_%' ORDER BY name;";
        var tableRows = await HolooSqlDatasetBuilder.ExecuteSelectAsync(_connection, tablesSql, null, cancellationToken).ConfigureAwait(false);

        var columns = new List<HolooSchemaColumn>();
        var map = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var tableRow in tableRows)
        {
            var tableName = tableRow["name"];
            var pragmaSql = $"SELECT name, type, \"notnull\" FROM pragma_table_info('{tableName.Replace("'", "''", StringComparison.Ordinal)}');";
            var colRows = await HolooSqlDatasetBuilder.ExecuteSelectAsync(_connection, pragmaSql, null, cancellationToken).ConfigureAwait(false);

            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var colRow in colRows)
            {
                var colName = colRow["name"];
                var colType = colRow.TryGetValue("type", out var t) ? t : "TEXT";
                bool? nullable = colRow.TryGetValue("notnull", out var nn) ? nn == "0" : null;
                columns.Add(new HolooSchemaColumn(tableName, colName, colType, null, nullable));
                set.Add(colName);
            }

            map[tableName] = set;
        }

        _cachedColumnsByTable = map;
        return HolooSchemaVersion.DetectFromColumns(columns);
    }

    public async Task<HolooDataset> ReadDatasetAsync(
        HolooSchemaInspection schema,
        string? sinceMarker = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(schema);
        if (!schema.IsCompatible)
            throw new InvalidOperationException($"Incompatible Holoo schema: missing {string.Join(", ", schema.MissingRequiredColumns)}");

        if (_cachedColumnsByTable is null)
            _ = await InspectSchemaAsync(cancellationToken).ConfigureAwait(false);

        return await HolooSqlDatasetBuilder.ReadFromConnectionAsync(
            _connection,
            _cachedColumnsByTable!,
            isSqlServer: false,
            sinceMarker: sinceMarker,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates the 10-table real Holoo H1-6 synthetic schema in an open SQLite connection for tests.
    /// </summary>
    public static void CreateRealH16Schema(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (connection.State != ConnectionState.Open)
            connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS ARTICLE (
                A_Code TEXT PRIMARY KEY,
                A_Code_C TEXT NULL,
                A_Name TEXT NOT NULL,
                A_Country TEXT NULL,
                Buy_Price REAL NULL,
                EndBuy_Price REAL NULL,
                Sel_Price REAL NULL,
                Exist REAL NULL,
                Type_Anbar_C TEXT NULL,
                VahedCode TEXT NULL,
                Model TEXT NULL,
                Endeditdate TEXT NULL,
                "Delete" INTEGER NOT NULL DEFAULT 0,
                IsActive INTEGER NOT NULL DEFAULT 1
            );

            CREATE TABLE IF NOT EXISTS ArticlePrice (
                ID INTEGER PRIMARY KEY AUTOINCREMENT,
                A_Code TEXT NOT NULL,
                SelID TEXT NULL,
                Sel_Price REAL NULL,
                ModifyDate TEXT NULL,
                Min_Price REAL NULL,
                Max_Price REAL NULL
            );

            CREATE TABLE IF NOT EXISTS CUSTOMER (
                C_Code TEXT PRIMARY KEY,
                C_Code_C TEXT NULL,
                C_Name TEXT NULL,
                C_AliasName TEXT NULL,
                C_Tel TEXT NULL,
                C_Mobile TEXT NULL,
                C_Address TEXT NULL,
                Zip_Code TEXT NULL,
                National_Code TEXT NULL,
                Endeditdate TEXT NULL,
                "Delete" INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS Cust_Tell (
                CCode TEXT NOT NULL,
                C_Tell1 TEXT NULL,
                C_Tell2 TEXT NULL,
                C_Tell3 TEXT NULL,
                C_Tell4 TEXT NULL,
                C_Tell5 TEXT NULL,
                C_Mobile1 TEXT NULL,
                C_Mobile2 TEXT NULL,
                C_Mobile3 TEXT NULL,
                C_Mobile4 TEXT NULL,
                C_Mobile5 TEXT NULL
            );

            CREATE TABLE IF NOT EXISTS FACTURE (
                Fac_Code TEXT PRIMARY KEY,
                Fac_Type TEXT NULL,
                Fac_Date TEXT NULL,
                C_Code TEXT NULL,
                Sum_Price REAL NULL,
                FNaghd REAL NULL,
                FCheck REAL NULL,
                FNesieh REAL NULL,
                Card REAL NULL,
                Takhfif REAL NULL,
                "Delete" INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS FACTART (
                Fac_Code TEXT NOT NULL,
                Fac_Type TEXT NULL,
                A_Index INTEGER NOT NULL,
                A_Code TEXT NOT NULL,
                Few_Article REAL NULL,
                Price_BS REAL NULL,
                Sel_Price REAL NULL,
                Buy_Price REAL NULL,
                DarsadTakhfif REAL NULL,
                TakhfifSatriR REAL NULL,
                PRIMARY KEY (Fac_Code, A_Index)
            );

            CREATE TABLE IF NOT EXISTS M_GROUP (
                M_groupcode TEXT PRIMARY KEY,
                M_groupname TEXT NULL,
                Type_Anbar_C TEXT NULL,
                Endeditdate TEXT NULL
            );

            CREATE TABLE IF NOT EXISTS S_GROUP (
                M_groupcode TEXT NOT NULL,
                S_groupcode TEXT NOT NULL,
                S_groupname TEXT NULL,
                Endeditdate TEXT NULL,
                PRIMARY KEY (M_groupcode, S_groupcode)
            );

            CREATE TABLE IF NOT EXISTS TYPE_ANB (
                Type_Anbar_C TEXT PRIMARY KEY,
                Type_Anbar_N TEXT NULL
            );

            CREATE TABLE IF NOT EXISTS UNIT (
                Unit_Code TEXT PRIMARY KEY,
                Unit_Name TEXT NULL,
                Endeditdate TEXT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }
}

/// <summary>
/// Read-only CSV bundle source reading either corrected sample bundles (samples-H1-6-corrected-*)
/// or table CSV exports, with optional fail-closed SHA-256 verification against bundle-sha256.tsv.
/// </summary>
public sealed class CsvBundleHolooSource : IHolooDataSource
{
    private readonly string _bundleRoot;
    private readonly string _samplesDirectory;
    private readonly string? _manifestPath;

    public string SourceKind => "csv_bundle";

    public CsvBundleHolooSource(string bundleRoot, string samplesDirectory, string? manifestPath = null)
    {
        if (string.IsNullOrWhiteSpace(bundleRoot))
            throw new ArgumentException("Bundle root is required.", nameof(bundleRoot));
        if (string.IsNullOrWhiteSpace(samplesDirectory))
            throw new ArgumentException("Samples directory is required.", nameof(samplesDirectory));

        _bundleRoot = bundleRoot;
        _samplesDirectory = samplesDirectory;
        _manifestPath = manifestPath;
    }

    public Task<HolooSchemaInspection> InspectSchemaAsync(CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(_manifestPath))
        {
            var verify = HolooBundleVerifier.Verify(_manifestPath, _bundleRoot);
            if (!verify.AllPass)
            {
                var summary = string.Join("; ", verify.Failures.Select(static f => $"{f.RelativePath}:{f.Reason}"));
                throw new InvalidOperationException($"Holoo bundle SHA-256 verification failed: {summary}");
            }
        }

        return Task.FromResult(HolooSchemaVersion.DetectDetailed(_bundleRoot));
    }

    public Task<HolooDataset> ReadDatasetAsync(
        HolooSchemaInspection schema,
        string? sinceMarker = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(schema);
        if (!schema.Version.IsKnown)
            throw new InvalidOperationException("Cannot import CSV bundle with unknown schema version.");

        var products = ReadIfExists("products_sample.csv", "ARTICLE.csv")
            .Select(HolooMapper.MapArticleFull)
            .Where(static p => !string.IsNullOrWhiteSpace(p.ProductCode))
            .Where(p => string.IsNullOrWhiteSpace(sinceMarker) || p.UpdatedAt is null || StringComparer.Ordinal.Compare(p.UpdatedAt, sinceMarker) > 0)
            .ToList();

        var articlePrices = ReadIfExists("ArticlePrice.csv")
            .Select(HolooMapper.MapArticlePrice)
            .Where(static ap => !string.IsNullOrWhiteSpace(ap.ProductCode))
            .ToList();

        var customers = ReadIfExists("customers_sample.csv", "CUSTOMER.csv")
            .Select(HolooMapper.MapCustomerFull)
            .Where(static c => !string.IsNullOrWhiteSpace(c.CustomerCode))
            .ToList();

        var invoices = ReadIfExists("invoices_sample.csv", "FACTURE.csv")
            .Select(HolooMapper.MapFactureFull)
            .Where(static inv => !string.IsNullOrWhiteSpace(inv.InvoiceCode))
            .ToList();

        var items = ReadIfExists("invoice_items_sample.csv", "FACTART.csv")
            .Select(HolooMapper.MapFactArtFull)
            .Where(static it => !string.IsNullOrWhiteSpace(it.ProductCode))
            .ToList();

        var mainGroups = ReadIfExists("M_GROUP.csv").Select(HolooMapper.MapMainGroup).ToList();
        var subGroups = ReadIfExists("S_GROUP.csv").Select(HolooMapper.MapSubGroup).ToList();
        var units = ReadIfExists("UNIT.csv").Select(HolooMapper.MapUnit).ToList();
        var warehouseTypes = ReadIfExists("TYPE_ANB.csv").Select(HolooMapper.MapWarehouseType).ToList();

        return Task.FromResult(new HolooDataset(
            products,
            articlePrices,
            customers,
            invoices,
            items,
            mainGroups,
            subGroups,
            units,
            warehouseTypes));
    }

    private IReadOnlyList<Dictionary<string, string>> ReadIfExists(params string[] candidateFiles)
    {
        foreach (var name in candidateFiles)
        {
            var path = Path.Combine(_samplesDirectory, name);
            if (File.Exists(path))
                return HolooCsvReader.ReadRows(path);
        }

        return Array.Empty<Dictionary<string, string>>();
    }
}
