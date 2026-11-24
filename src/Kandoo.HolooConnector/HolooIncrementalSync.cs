using System.IO;

namespace Kandoo.HolooConnector;

/// <summary>
/// Coordinates incremental sync against a Holoo data source.
/// Only records newer than the last sync marker are imported.
/// Schema version is detected before each sync; if it changes, a full re-read
/// is required rather than a delta sync.
/// </summary>
public sealed class HolooIncrementalSync
{
    private readonly HolooShadowStore _store;
    private string _lastSyncMarker = string.Empty;

    public HolooIncrementalSync(HolooShadowStore store)
    {
        _store = store;
    }

    public string SchemaVersion => _store.SchemaVersion;
    public string LastSyncMarker => _lastSyncMarker;

    /// <summary>
    /// Detects schema version from the bundle. Returns Unknown if not found.
    /// Callers must check IsKnown before proceeding with delta sync.
    /// </summary>
    public HolooSchemaVersion DetectSchemaVersion(string bundleRoot) =>
        HolooSchemaVersion.Detect(bundleRoot);

    /// <summary>
    /// Sets the last sync marker after a successful sync.
    /// </summary>
    public void RecordSyncMarker(string marker) => _lastSyncMarker = marker;

    /// <summary>
    /// Determines whether a delta sync is possible given the current schema version.
    /// Returns false when the schema version is unknown or has changed since last sync.
    /// </summary>
    public bool CanDeltaSync(HolooSchemaVersion version) =>
        version.IsKnown && !string.IsNullOrEmpty(_lastSyncMarker);
}
