using System.Security.Cryptography;
using System.Text;

namespace Kandoo.HolooConnector;

public sealed record HolooSchemaColumn(
    string TableName,
    string ColumnName,
    string DataType,
    int? MaxLength = null,
    bool? IsNullable = null);

public sealed record HolooSchemaInspection(
    HolooSchemaVersion Version,
    string SchemaFingerprintSha256,
    int TableCount,
    int ColumnCount,
    IReadOnlyList<string> MissingRequiredColumns,
    bool IsCompatible,
    string DetectionSource);

/// <summary>
/// Detects the Holoo schema version and structural fingerprint from either
/// a bundle structure CSV or live/synthetic SQL column metadata.
/// Never guesses; returns Unknown when version markers and required schema signatures are missing.
/// </summary>
public sealed class HolooSchemaVersion
{
    public string Value { get; }
    public bool IsKnown { get; }

    private HolooSchemaVersion(string value, bool isKnown)
    {
        Value = value;
        IsKnown = isKnown;
    }

    public static HolooSchemaVersion Unknown { get; } = new("unknown", false);

    private static readonly HashSet<string> TrustedVersions = new(StringComparer.OrdinalIgnoreCase)
    {
        "H1-6",
        "H1-7",
        "H1-8",
    };

    private static readonly Dictionary<string, string[]> RequiredCoreH16Columns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ARTICLE"] = ["A_Code", "A_Name", "Buy_Price", "Sel_Price", "Exist", "Endeditdate"],
        ["ArticlePrice"] = ["A_Code", "Sel_Price", "ModifyDate"],
        ["CUSTOMER"] = ["C_Code", "C_Name", "C_Tel", "C_Mobile", "Endeditdate"],
        ["Cust_Tell"] = ["CCode", "C_Tell1", "C_Mobile1"],
        ["FACTURE"] = ["Fac_Code", "Fac_Type", "C_Code", "Fac_Date", "Sum_Price", "FNaghd", "FCheck", "FNesieh", "Card"],
        ["FACTART"] = ["Fac_Code", "Fac_Type", "A_Code", "A_Index", "Few_Article", "Price_BS", "Sel_Price"],
        ["M_GROUP"] = ["M_groupcode", "M_groupname"],
        ["S_GROUP"] = ["M_groupcode", "S_groupcode", "S_groupname"],
        ["TYPE_ANB"] = ["Type_Anbar_C", "Type_Anbar_N"],
        ["UNIT"] = ["Unit_Code", "Unit_Name"],
    };

    /// <summary>
    /// Creates a known schema version value. Returns null when the value is not a trusted version.
    /// </summary>
    public static HolooSchemaVersion? Known(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        foreach (var trusted in TrustedVersions)
        {
            if (trusted.Equals(trimmed, StringComparison.OrdinalIgnoreCase))
                return new(trusted, true);
        }

        return null;
    }

    /// <summary>
    /// Tries to parse a raw schema version string into a known version.
    /// </summary>
    public static bool TryParse(string? raw, out HolooSchemaVersion? version)
    {
        version = Known(raw);
        return version is not null;
    }

    /// <summary>
    /// Reads schema version from bundle/structure.csv if a version marker row or
    /// recognized Holoo schema signature exists. Falls back to Unknown rather than guessing.
    /// </summary>
    public static HolooSchemaVersion Detect(string bundleRoot) =>
        DetectDetailed(bundleRoot).Version;

    public static HolooSchemaInspection DetectDetailed(string bundleRoot)
    {
        var structurePath = Path.Combine(bundleRoot, "structure.csv");
        if (!File.Exists(structurePath))
        {
            return new HolooSchemaInspection(
                Unknown,
                string.Empty,
                0,
                0,
                Array.Empty<string>(),
                false,
                "missing_structure_csv");
        }

        try
        {
            using var stream = File.OpenRead(structurePath);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var records = HolooCsvReader.ReadRawRecords(reader);

            string? explicitMarker = null;
            var columns = new List<HolooSchemaColumn>();
            var startIndex = 0;

            if (records.Count > 0 && records[0].Count >= 2 &&
                records[0][0].Trim().Equals("table_name", StringComparison.OrdinalIgnoreCase) &&
                records[0][1].Trim().Equals("column_name", StringComparison.OrdinalIgnoreCase))
            {
                startIndex = 1;
            }

            for (var i = startIndex; i < records.Count; i++)
            {
                var parts = records[i];
                if (parts.Count >= 2 &&
                    parts[0].Trim().Equals("schema_version", StringComparison.OrdinalIgnoreCase))
                {
                    explicitMarker = parts[1].Trim();
                    continue;
                }

                if (parts.Count >= 3)
                {
                    var table = parts[0].Trim();
                    var column = parts[1].Trim();
                    var dataType = parts[2].Trim();
                    if (table.Length > 0 && column.Length > 0)
                    {
                        int? maxLen = parts.Count >= 4 && int.TryParse(parts[3].Trim(), out var parsedLen)
                            ? parsedLen
                            : null;
                        bool? isNullable = parts.Count >= 5 ? ParseNullable(parts[4]) : null;
                        columns.Add(new HolooSchemaColumn(table, column, dataType, maxLen, isNullable));
                    }
                }
            }

            if (explicitMarker is not null && columns.Count == 0)
            {
                var explicitVersion = Known(explicitMarker) ?? new HolooSchemaVersion(explicitMarker, true);
                var markerHash = ComputeHash($"schema_version:{explicitVersion.Value}");
                return new HolooSchemaInspection(
                    explicitVersion,
                    markerHash,
                    0,
                    0,
                    Array.Empty<string>(),
                    true,
                    "explicit_marker");
            }

            return InspectColumns(columns, explicitMarker, "structure_csv");
        }
        catch
        {
            return new HolooSchemaInspection(
                Unknown,
                string.Empty,
                0,
                0,
                Array.Empty<string>(),
                false,
                "unreadable_structure_csv");
        }
    }

    public static HolooSchemaInspection DetectFromColumns(
        IEnumerable<HolooSchemaColumn> columns,
        string? explicitVersion = null)
    {
        ArgumentNullException.ThrowIfNull(columns);
        return InspectColumns(columns.ToList(), explicitVersion, "sql_metadata");
    }

    private static HolooSchemaInspection InspectColumns(
        IReadOnlyList<HolooSchemaColumn> columns,
        string? explicitVersion,
        string source)
    {
        if (columns.Count == 0)
        {
            return new HolooSchemaInspection(
                Unknown,
                string.Empty,
                0,
                0,
                Array.Empty<string>(),
                false,
                source);
        }

        var byTable = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var normalizedSpecs = new List<string>(columns.Count);

        foreach (var col in columns)
        {
            if (string.IsNullOrWhiteSpace(col.TableName) || string.IsNullOrWhiteSpace(col.ColumnName))
                continue;

            if (!byTable.TryGetValue(col.TableName, out var colSet))
            {
                colSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                byTable[col.TableName] = colSet;
            }

            colSet.Add(col.ColumnName);
            normalizedSpecs.Add($"{col.TableName.Trim().ToUpperInvariant()}.{col.ColumnName.Trim().ToUpperInvariant()}:{col.DataType.Trim().ToLowerInvariant()}");
        }

        var missing = new List<string>();
        foreach (var (table, requiredCols) in RequiredCoreH16Columns)
        {
            if (!byTable.TryGetValue(table, out var presentCols))
            {
                missing.Add($"{table}.*");
                continue;
            }

            foreach (var required in requiredCols)
            {
                if (!presentCols.Contains(required))
                    missing.Add($"{table}.{required}");
            }
        }

        normalizedSpecs.Sort(StringComparer.Ordinal);
        var fingerprint = ComputeHash(string.Join("\n", normalizedSpecs.Distinct(StringComparer.Ordinal)));

        HolooSchemaVersion detectedVersion;
        if (!string.IsNullOrWhiteSpace(explicitVersion))
        {
            detectedVersion = Known(explicitVersion) ?? Unknown;
        }
        else if (missing.Count == 0)
        {
            detectedVersion = Known("H1-6")!;
        }
        else
        {
            detectedVersion = Unknown;
        }

        return new HolooSchemaInspection(
            detectedVersion,
            fingerprint,
            byTable.Count,
            columns.Count,
            missing,
            detectedVersion.IsKnown && missing.Count == 0,
            source);
    }

    private static bool? ParseNullable(string raw)
    {
        var trimmed = raw.Trim();
        if (trimmed.Equals("YES", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("true", StringComparison.OrdinalIgnoreCase))
            return true;
        if (trimmed.Equals("NO", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("false", StringComparison.OrdinalIgnoreCase))
            return false;
        return null;
    }

    private static string ComputeHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
