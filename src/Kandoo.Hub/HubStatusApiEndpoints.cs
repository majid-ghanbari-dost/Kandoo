using System.Globalization;
using System.Reflection;
using Kandoo.Core;
using Microsoft.Data.Sqlite;

namespace Kandoo.Hub;

public sealed record HubStatusTextObservation(string? Value, string? Reason)
{
    public static HubStatusTextObservation Known(string value) => new(value, null);
    public static HubStatusTextObservation Unknown(string reason) => new(null, reason);
}

public sealed record HubStatusObservation<T>(T? Value, string? Reason) where T : struct
{
    public static HubStatusObservation<T> Known(T value) => new(value, null);
    public static HubStatusObservation<T> Unknown(string reason) => new(null, reason);
}

public sealed record HubStatusSnapshot(
    HubStatusTextObservation Version,
    HubStatusObservation<DateTimeOffset> LastSync,
    HubStatusObservation<DateTimeOffset> LastBackup,
    HubStatusObservation<long> FreeDiskBytes,
    HubStatusObservation<bool> SmsEnabled);

public interface IHubStatusProbe
{
    HubStatusSnapshot Read(Guid storeId);
}

/// <summary>
/// Read-only operational observations. No backup is created, no replication is
/// triggered, and no gateway call is made. SMS is configuration state only.
/// </summary>
public sealed class SystemHubStatusProbe : IHubStatusProbe
{
    public const string SmsConfigurationKey = "FarazSms:Enabled";
    public const string NoAcknowledgedSync = "NO_ACKNOWLEDGED_SYNC";
    public const string BackupDisabled = "BACKUP_DISABLED";
    public const string NoBackupFound = "NO_BACKUP_FOUND";
    public const string BackupUnavailable = "BACKUP_STATUS_UNAVAILABLE";
    public const string DiskUnavailable = "DISK_SPACE_UNAVAILABLE";
    public const string SmsConfigurationInvalid = "SMS_CONFIG_INVALID";
    public const string VersionUnavailable = "VERSION_UNAVAILABLE";

    private const string BackupPrefix = "kandoo-hub-";
    private const string BackupTimeFormat = "yyyyMMddTHHmmssZ";

    private readonly IHubStore _store;
    private readonly HubBackupOptions _backupOptions;
    private readonly IConfiguration _configuration;
    private readonly string _hubDataPath;

    public SystemHubStatusProbe(
        IHubStore store,
        HubBackupOptions backupOptions,
        IConfiguration configuration,
        string hubDataPath)
    {
        _store = store;
        _backupOptions = backupOptions;
        _configuration = configuration;
        _hubDataPath = hubDataPath;
    }

    public HubStatusSnapshot Read(Guid storeId) => new(
        ReadVersion(),
        ReadLastSync(storeId),
        ReadLastBackup(),
        ReadFreeDiskBytes(),
        ReadSmsEnabled());

    public static string DataPathFromConnectionString(string connectionString)
    {
        var dataSource = new SqliteConnectionStringBuilder(connectionString).DataSource;
        return string.IsNullOrWhiteSpace(dataSource) || dataSource == ":memory:"
            ? AppContext.BaseDirectory
            : dataSource;
    }

    private static HubStatusTextObservation ReadVersion()
    {
        var assembly = typeof(SystemHubStatusProbe).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(version))
        {
            version = assembly.GetName().Version?.ToString();
        }

        return string.IsNullOrWhiteSpace(version)
            ? HubStatusTextObservation.Unknown(VersionUnavailable)
            : HubStatusTextObservation.Known(version);
    }

    private HubStatusObservation<DateTimeOffset> ReadLastSync(Guid storeId)
    {
        var value = _store.GetLastSyncAtUtc(storeId);
        return value is null
            ? HubStatusObservation<DateTimeOffset>.Unknown(NoAcknowledgedSync)
            : HubStatusObservation<DateTimeOffset>.Known(value.Value);
    }

    private HubStatusObservation<DateTimeOffset> ReadLastBackup()
    {
        try
        {
            var directory = Path.GetFullPath(_backupOptions.DirectoryPath);
            var latest = Directory.Exists(directory)
                ? Directory.EnumerateFiles(directory, BackupPrefix + "*.db", SearchOption.TopDirectoryOnly)
                    .Select(path => (Path: path, AtUtc: BackupTime(path)))
                    .Where(candidate => candidate.AtUtc is not null)
                    .OrderByDescending(candidate => candidate.AtUtc)
                    .ThenByDescending(candidate => candidate.Path, StringComparer.Ordinal)
                    .Select(candidate => candidate.AtUtc)
                    .FirstOrDefault()
                : null;
            if (latest is not null)
            {
                return HubStatusObservation<DateTimeOffset>.Known(latest.Value);
            }

            return HubStatusObservation<DateTimeOffset>.Unknown(
                _backupOptions.Enabled ? NoBackupFound : BackupDisabled);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return HubStatusObservation<DateTimeOffset>.Unknown(BackupUnavailable);
        }
    }

    private HubStatusObservation<long> ReadFreeDiskBytes()
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(_hubDataPath));
            if (string.IsNullOrWhiteSpace(root))
            {
                return HubStatusObservation<long>.Unknown(DiskUnavailable);
            }

            return HubStatusObservation<long>.Known(new DriveInfo(root).AvailableFreeSpace);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return HubStatusObservation<long>.Unknown(DiskUnavailable);
        }
    }

    private HubStatusObservation<bool> ReadSmsEnabled()
    {
        var configured = _configuration[SmsConfigurationKey];
        // This is the existing FarazSmsOptions default when the key is absent.
        if (string.IsNullOrWhiteSpace(configured))
        {
            return HubStatusObservation<bool>.Known(false);
        }

        return bool.TryParse(configured, out var enabled)
            ? HubStatusObservation<bool>.Known(enabled)
            : HubStatusObservation<bool>.Unknown(SmsConfigurationInvalid);
    }

    private static DateTimeOffset? BackupTime(string path)
    {
        var name = Path.GetFileName(path);
        var suffixLength = 1 + 32 + 3; // '-' + Guid N + '.db'
        if (!name.StartsWith(BackupPrefix, StringComparison.Ordinal) ||
            name.Length != BackupPrefix.Length + BackupTimeFormat.Length + suffixLength ||
            !name.EndsWith(".db", StringComparison.Ordinal))
        {
            return null;
        }

        var timeStart = BackupPrefix.Length;
        var guidStart = timeStart + BackupTimeFormat.Length + 1;
        if (name[timeStart + BackupTimeFormat.Length] != '-' ||
            !Guid.TryParseExact(name.Substring(guidStart, 32), "N", out _))
        {
            return null;
        }

        return DateTimeOffset.TryParseExact(
            name.Substring(timeStart, BackupTimeFormat.Length),
            BackupTimeFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var value)
            ? value
            : null;
    }
}

public static partial class HubApiFirstEndpoints
{
    private static void MapStatusApi(WebApplication app)
    {
        app.MapGet("/v1/stores/{store_id}/status/v0", (
            Guid store_id,
            HttpContext context,
            IHubAccessStore access,
            IClock clock,
            IHubStatusProbe status) =>
        {
            var auth = Authenticate(context, store_id, access, clock);
            if (!auth.IsAuthenticated)
            {
                return AuthError(auth.Code);
            }

            var value = status.Read(store_id);
            return Results.Json(new
            {
                status = "OK",
                store_id,
                generated_at_utc = clock.UtcNow.ToUniversalTime(),
                time_zone = "UTC",
                version = StatusTextBody(value.Version),
                last_sync = StatusTimeBody(value.LastSync),
                last_backup = StatusTimeBody(value.LastBackup),
                free_disk = new
                {
                    bytes = value.FreeDiskBytes.Value,
                    reason = value.FreeDiskBytes.Reason
                },
                sms = new
                {
                    enabled = value.SmsEnabled.Value,
                    reason = value.SmsEnabled.Reason,
                    scope = "CONFIGURATION_ONLY",
                    configuration_key = SystemHubStatusProbe.SmsConfigurationKey
                }
            }, HubJson.Options);
        });
    }

    private static object StatusTextBody(HubStatusTextObservation value) => new
    {
        value = value.Value,
        reason = value.Reason
    };

    private static object StatusTimeBody(HubStatusObservation<DateTimeOffset> value) => new
    {
        at_utc = value.Value,
        reason = value.Reason
    };
}
