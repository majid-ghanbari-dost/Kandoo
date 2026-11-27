using System.Text;

namespace Kandoo.HolooConnector;

/// <summary>
/// RFC-4180 CSV reader for Holoo export bundles (UTF-8 / UTF-8 BOM safe).
/// </summary>
public static class HolooCsvReader
{
    public static IReadOnlyList<Dictionary<string, string>> ReadRows(string csvPath)
    {
        if (string.IsNullOrWhiteSpace(csvPath))
            throw new ArgumentException("CSV path is required.", nameof(csvPath));

        using var stream = File.OpenRead(csvPath);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return ReadRows(reader);
    }

    public static IReadOnlyList<Dictionary<string, string>> ReadRows(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var rows = new List<Dictionary<string, string>>();
        string[]? headers = null;

        while (TryReadRecord(reader, out var fields))
        {
            if (fields.Count == 1 && fields[0].Length == 0)
                continue;

            if (headers is null)
            {
                headers = fields.Select(static h => h.Trim()).ToArray();
                continue;
            }

            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < headers.Length; i++)
            {
                var header = headers[i];
                if (header.Length == 0)
                    continue;
                row[header] = i < fields.Count ? fields[i] : string.Empty;
            }

            rows.Add(row);
        }

        return rows;
    }

    public static IReadOnlyList<IReadOnlyList<string>> ReadRawRecords(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var records = new List<IReadOnlyList<string>>();
        while (TryReadRecord(reader, out var fields))
        {
            if (fields.Count == 1 && fields[0].Length == 0)
                continue;
            records.Add(fields);
        }

        return records;
    }

    private static bool TryReadRecord(TextReader reader, out List<string> fields)
    {
        fields = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        var hasAnyChar = false;

        while (true)
        {
            var next = reader.Read();
            if (next == -1)
            {
                if (!hasAnyChar && fields.Count == 0 && current.Length == 0)
                    return false;

                fields.Add(current.ToString());
                return true;
            }

            hasAnyChar = true;
            var ch = (char)next;

            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (reader.Peek() == '"')
                    {
                        _ = reader.Read();
                        current.Append('"');
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(ch);
                }

                continue;
            }

            if (ch == '"')
            {
                inQuotes = true;
            }
            else if (ch == ',')
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else if (ch == '\r')
            {
                if (reader.Peek() == '\n')
                    _ = reader.Read();

                fields.Add(current.ToString());
                return true;
            }
            else if (ch == '\n')
            {
                fields.Add(current.ToString());
                return true;
            }
            else
            {
                current.Append(ch);
            }
        }
    }
}
