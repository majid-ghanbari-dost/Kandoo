namespace Kandoo.HolooConnector;

/// <summary>
/// Detects and validates the Holoo schema version for incremental sync.
/// Tier 1-3 per decision D-M: read-only detection, no password guessing,
/// no stored credentials, never writes to Holoo.
/// </summary>
public sealed class HolooSchemaVersion
{
    private HolooSchemaVersion(string value)
    {
        Value = value;
    }

    public string Value { get; }

    /// <summary>
    /// Creates a schema version from a raw version string read from Holoo.
    /// Returns null when the string is not a recognized schema version.
    /// </summary>
    public static HolooSchemaVersion? Known(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var trimmed = raw.Trim();
        if (trustedVersions.Contains(trimmed))
            return new HolooSchemaVersion(trimmed);

        return null;
    }

    public static bool TryParse(string? raw, out HolooSchemaVersion? version)
    {
        version = Known(raw);
        return version is not null;
    }

    private static readonly HashSet<string> trustedVersions =
    [
        "H1-6",
        "H1-7",
        "H1-8",
    ];

    public override string ToString() => Value;
}