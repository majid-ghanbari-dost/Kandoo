using System.IO;
using System.Text.RegularExpressions;

namespace Kandoo.HolooConnector;

/// <summary>
/// Detects the Holoo bundle schema version from the bundle structure file.
/// Used by incremental sync to decide whether a full re-read or delta sync is needed.
/// Never guesses; returns Unknown when no version marker is found.
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

    /// <summary>
    /// Reads schema version from bundle/structure.csv if a version marker row exists.
    /// Falls back to Unknown when the file is absent or has no version marker.
    /// </summary>
    public static HolooSchemaVersion Detect(string bundleRoot)
    {
        var structurePath = Path.Combine(bundleRoot, "structure.csv");
        if (!File.Exists(structurePath))
            return Unknown;

        try
        {
            foreach (var raw in File.ReadLines(structurePath))
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;

                // Look for a version marker line like: schema_version,H1-6
                var parts = line.Split(',');
                if (parts.Length >= 2 &&
                    parts[0].Trim().Equals("schema_version", StringComparison.OrdinalIgnoreCase))
                {
                    return new HolooSchemaVersion(parts[1].Trim(), true);
                }
            }
        }
        catch
        {
            // If the file can't be read, fall back to Unknown rather than guessing
        }

        return Unknown;
    }
}
