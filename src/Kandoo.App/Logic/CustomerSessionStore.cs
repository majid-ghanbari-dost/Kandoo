using System.IO;
using System.Text.Json;

namespace Kandoo.App;

/// <summary>
/// Persisted customer session (C-92): the verified device is remembered and the
/// mobile number is the identity. One machine-local JSON file holds the device id
/// (always present after first run), the remembered session token and the verified
/// phone. Raw codes are never stored; the token is opaque.
/// </summary>
public sealed class CustomerSessionRecord
{
    public CustomerSessionRecord(string deviceId, string? sessionToken, string? phone)
    {
        CustomerDeviceId.EnsureValid(deviceId);
        DeviceId = deviceId;
        SessionToken = sessionToken;
        Phone = phone;
    }

    public string DeviceId { get; }
    public string? SessionToken { get; }
    public string? Phone { get; }

    public bool IsLoggedIn => !string.IsNullOrWhiteSpace(SessionToken) && !string.IsNullOrWhiteSpace(Phone);
}

public sealed class CustomerSessionStore
{
    public const string FileName = "kandoo.customer-session.v1.json";

    private readonly string _path;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false,
    };

    public CustomerSessionStore(string? folderPath = null)
    {
        folderPath ??= Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        Directory.CreateDirectory(folderPath);
        _path = Path.Combine(folderPath, FileName);
    }

    /// <summary>Load the remembered session; null when nothing valid is stored.</summary>
    public CustomerSessionRecord? Load()
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(_path);
            var stored = JsonSerializer.Deserialize<StoredSession>(json, JsonOptions);
            if (stored is null || !CustomerDeviceId.IsValid(stored.DeviceId))
            {
                return null;
            }

            return new CustomerSessionRecord(
                stored.DeviceId,
                string.IsNullOrWhiteSpace(stored.SessionToken) ? null : stored.SessionToken,
                string.IsNullOrWhiteSpace(stored.Phone) ? null : stored.Phone);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>Load the device id, creating and persisting one on first use.</summary>
    public CustomerSessionRecord LoadOrCreateDevice()
    {
        var existing = Load();
        if (existing is not null)
        {
            return existing;
        }

        var created = new CustomerSessionRecord(CustomerDeviceId.New(), null, null);
        Save(created);
        return created;
    }

    /// <summary>Persist the session. A device id is always required; token/phone clear on logout.</summary>
    public void Save(CustomerSessionRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var stored = new StoredSession
        {
            DeviceId = record.DeviceId,
            SessionToken = record.SessionToken,
            Phone = record.Phone,
        };
        File.WriteAllText(_path, JsonSerializer.Serialize(stored, JsonOptions));
    }

    /// <summary>Forget the session token but keep the remembered device identity.</summary>
    public void ClearSession()
    {
        var current = Load();
        if (current is not null)
        {
            Save(new CustomerSessionRecord(current.DeviceId, null, null));
        }
    }

    private sealed class StoredSession
    {
        public string DeviceId { get; set; } = string.Empty;
        public string? SessionToken { get; set; }
        public string? Phone { get; set; }
    }
}

/// <summary>
/// Invoice ids already opened on this device — the Customer-New-Banner shows the
/// newest invoice the customer has not opened yet («فاکتور جدید از [فروشگاه]»).
/// </summary>
public sealed class CustomerSeenInvoicesStore
{
    public const string FileName = "kandoo.customer-seen.json";

    private readonly string _path;

    public CustomerSeenInvoicesStore(string? folderPath = null)
    {
        folderPath ??= Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        Directory.CreateDirectory(folderPath);
        _path = Path.Combine(folderPath, FileName);
    }

    public IReadOnlySet<string> Load()
    {
        if (!File.Exists(_path))
        {
            return new HashSet<string>();
        }

        try
        {
            var ids = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(_path)) ?? new List<string>();
            return new HashSet<string>(ids, StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new HashSet<string>();
        }
        catch (UnauthorizedAccessException)
        {
            return new HashSet<string>();
        }
        catch (IOException)
        {
            return new HashSet<string>();
        }
    }

    public void MarkSeen(string invoiceId)
    {
        var ids = new HashSet<string>(Load(), StringComparer.Ordinal) { invoiceId };
        File.WriteAllText(_path, JsonSerializer.Serialize(ids.ToList()));
    }
}

/// <summary>
/// Optional local configuration for the customer mode, read from
/// `kandoo.customer-config.json` in the app data folder. It exists so the local
/// demo kit (LAN PHP Cloud over http) can point the app at the kit and provide the
/// Telegram support link without inventing any settings screen (boards have none).
/// Keys: api_base_url (e.g. http://192.168.1.5:8080), telegram_url (optional).
/// When the file is absent the production default base URL is used.
/// </summary>
public sealed class CustomerLocalConfig
{
    public const string FileName = "kandoo.customer-config.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public CustomerLocalConfig(string? apiBaseUrl, string? telegramUrl)
    {
        ApiBaseUrl = string.IsNullOrWhiteSpace(apiBaseUrl) ? CustomerApiClient.DefaultBaseUrl : apiBaseUrl.Trim().TrimEnd('/');
        TelegramUrl = string.IsNullOrWhiteSpace(telegramUrl) ? null : telegramUrl.Trim();
    }

    public string ApiBaseUrl { get; }
    public string? TelegramUrl { get; }

    public static CustomerLocalConfig Load(string? folderPath = null)
    {
        folderPath ??= Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var path = Path.Combine(folderPath, FileName);
        if (!File.Exists(path))
        {
            return new CustomerLocalConfig(null, null);
        }

        try
        {
            var json = File.ReadAllText(path);
            var stored = JsonSerializer.Deserialize<StoredConfig>(json, JsonOptions);
            return new CustomerLocalConfig(stored?.ApiBaseUrl, stored?.TelegramUrl);
        }
        catch (JsonException)
        {
            return new CustomerLocalConfig(null, null);
        }
        catch (UnauthorizedAccessException)
        {
            return new CustomerLocalConfig(null, null);
        }
        catch (IOException)
        {
            return new CustomerLocalConfig(null, null);
        }
    }

    private sealed class StoredConfig
    {
        public string? ApiBaseUrl { get; set; }
        public string? TelegramUrl { get; set; }
    }
}
