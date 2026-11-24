using System.Collections.Generic;
using System.IO;

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
    /// Creates a known schema version value. Returns null when the value is not a trusted version.
    /// Used in tests and callers that know the version upfront.
    /// </summary>
    public static HolooSchemaVersion? Known(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        if (!TrustedVersions.Contains(trimmed))
            return null;

        return new(trimmed, true);
    }

    /// <summary>
    /// Tries to parse a raw schema version string into a known version.
    /// Returns true and sets the version when the string is a trusted version; false otherwise.
    /// </summary>
    public static bool TryParse(string? raw, out HolooSchemaVersion? version)
    {
        version = Known(raw);
        return version is not null;
    }

    private static readonly HashSet<string> TrustedVersions =
    [
        "H1-6",
        "H1-7",
        "H1-8",
    ];

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
