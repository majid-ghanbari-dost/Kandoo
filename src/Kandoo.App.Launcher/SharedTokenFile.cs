using System.IO;
using System.Text.Json;

namespace Kandoo.App.Launcher;

/// <summary>
/// Shared token file: a single, machine-local file that holds the active Hub
/// device/session token so the app can read it without constructor parameters.
///
/// The file lives in the app's local data folder and is named
/// `kandoo.device-token.v1.json`. It contains only the opaque bearer token
/// plus the store ID it was issued for.
/// </summary>
public sealed class SharedTokenFile
{
    private readonly string _path;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false,
    };

    public SharedTokenFile(string? folderPath = null)
    {
        folderPath ??= Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        Directory.CreateDirectory(folderPath);
        _path = Path.Combine(folderPath, "kandoo.device-token.v1.json");
    }

    public DeviceTokenRecord? Load()
    {
        if (!File.Exists(_path))
            return null;

        try
        {
            var json = File.ReadAllText(_path);
            var record = JsonSerializer.Deserialize<DeviceTokenRecord>(json, _jsonOptions);
            if (record is not null &&
                !string.IsNullOrWhiteSpace(record.Token) &&
                record.StoreId != Guid.Empty)
            {
                return record;
            }
        }
        catch { }

        return null;
    }

    public void Save(DeviceTokenRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (string.IsNullOrWhiteSpace(record.Token))
            throw new ArgumentException("Token is required.", nameof(record.Token));
        if (record.StoreId == Guid.Empty)
            throw new ArgumentException("Store ID is required.", nameof(record.StoreId));

        var json = JsonSerializer.Serialize(record, _jsonOptions);
        File.WriteAllText(_path, json);
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(_path))
                File.Delete(_path);
        }
        catch { }
    }
}

/// <summary>Record persisted to the shared token file.</summary>
public sealed record DeviceTokenRecord(string Token, Guid StoreId);
