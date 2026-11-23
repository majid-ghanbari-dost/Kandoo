using System.Globalization;
using System.Security.Cryptography;
using Kandoo.Core;
using Microsoft.Data.Sqlite;

namespace Kandoo.Hub;

public sealed class HubBackupOptions
{
    public bool Enabled { get; set; }
    public int IntervalSeconds { get; set; } = 300;
    public int RetentionFiles { get; set; } = 12;
    public string DirectoryPath { get; set; } = "backups";

    public void Validate()
    {
        if (IntervalSeconds <= 0 || RetentionFiles <= 0 || string.IsNullOrWhiteSpace(DirectoryPath))
        {
            throw new ArgumentException("Backup interval, retention, and directory must be positive/configured.");
        }

        if (Enabled && !Path.IsPathRooted(DirectoryPath))
        {
            throw new ArgumentException("An enabled pilot backup directory must be absolute and outside the install folder.");
        }
    }
}

/// <summary>Explicit operational events only; request payloads and pairing capabilities are never written.</summary>
public sealed class HubOperationalLog
{
    private readonly object _gate = new();
    private readonly string? _directoryPath;

    public HubOperationalLog(string? directoryPath)
    {
        _directoryPath = string.IsNullOrWhiteSpace(directoryPath) ? null : Path.GetFullPath(directoryPath);
    }

    public string? DirectoryPath => _directoryPath;

    public void Write(string eventName)
    {
        var directory = _directoryPath;
        if (directory is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(eventName) || eventName.Contains('\r') || eventName.Contains('\n'))
        {
            throw new ArgumentException("An operational event must be one non-empty line.", nameof(eventName));
        }

        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(directory);
                var now = DateTimeOffset.UtcNow;
                var path = Path.Combine(directory, "hub-" + now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".log");
                File.AppendAllText(path, now.ToString("O", CultureInfo.InvariantCulture) + " " + eventName + Environment.NewLine);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Logging must not mask the original host/backup result.
            Console.Error.WriteLine("Kandoo operational file logging is unavailable: " + exception.GetType().Name);
        }
    }
}

public sealed record HubBackupResult(string BackupPath, long Bytes, string Sha256);

public sealed class SqliteBackupService
{
    private readonly SqliteHubStore _store;
    private readonly HubBackupOptions _options;
    private readonly IClock _clock;

    public SqliteBackupService(SqliteHubStore store, HubBackupOptions options, IClock clock)
    {
        _store = store;
        _options = options;
        _options.Validate();
        _clock = clock;
    }

    public HubBackupResult BackupOnce()
    {
        var directory = Path.GetFullPath(_options.DirectoryPath);
        Directory.CreateDirectory(directory);
        var name = "kandoo-hub-" + _clock.UtcNow.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture) +
                   "-" + Guid.NewGuid().ToString("N") + ".db";
        var destination = Path.Combine(directory, name);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            _store.BackupTo(temporary);
            using (var connection = new SqliteConnection($"Data Source={temporary};Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "PRAGMA integrity_check;";
                var result = command.ExecuteScalar()?.ToString();
                if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("The SQLite backup failed its integrity check.");
                }
            }

            File.Move(temporary, destination);
            Prune(directory);
            var bytes = File.ReadAllBytes(destination);
            return new HubBackupResult(
                destination,
                bytes.LongLength,
                Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private void Prune(string directory)
    {
        var obsolete = Directory.GetFiles(directory, "kandoo-hub-*.db")
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.Name, StringComparer.Ordinal)
            .Skip(_options.RetentionFiles);
        foreach (var file in obsolete)
        {
            file.Delete();
        }
    }
}

public sealed class SqliteBackupHostedService : BackgroundService
{
    private readonly SqliteBackupService _backup;
    private readonly HubBackupOptions _options;
    private readonly HubOperationalLog _log;

    public SqliteBackupHostedService(SqliteBackupService backup, HubBackupOptions options, HubOperationalLog log)
    {
        _backup = backup;
        _options = options;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var result = _backup.BackupOnce();
                    _log.Write("BACKUP_SUCCEEDED " + Path.GetFileName(result.BackupPath));
                }
                catch (Exception exception)
                {
                    _log.Write("BACKUP_FAILED " + exception.GetType().Name);
                }

                await Task.Delay(TimeSpan.FromSeconds(_options.IntervalSeconds), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal Host shutdown.
        }
    }
}
