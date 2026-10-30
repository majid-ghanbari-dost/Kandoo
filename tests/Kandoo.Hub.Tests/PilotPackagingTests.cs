using Kandoo.Core;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Kandoo.Hub.Tests;

public sealed class PilotPackagingTests
{
    [Fact]
    public void C22_ScheduledBackupProducesConsistentExternalCopyAndAppliesRetention()
    {
        var root = Path.Combine(Path.GetTempPath(), "kandoo-c22-backup", Guid.NewGuid().ToString("N"));
        var install = Path.Combine(root, "install");
        var data = Path.Combine(root, "data");
        var backups = Path.Combine(data, "backups");
        Directory.CreateDirectory(install);
        Directory.CreateDirectory(data);
        try
        {
            var storeId = Guid.NewGuid();
            using (var store = new SqliteHubStore($"Data Source={Path.Combine(data, "hub.db")};Pooling=False"))
            {
                var request = new OperationRequest(
                    storeId,
                    Guid.NewGuid(),
                    "c22-backup-seed",
                    "test.seed",
                    null,
                    HubJson.Element(new { marker = "outside-install" }));
                Assert.Equal(OperationExecutionStatus.Succeeded, store.Execute(request, _ => "{}").Status);
                var clock = new ManualHubClock();
                var backup = new SqliteBackupService(store, new HubBackupOptions
                {
                    Enabled = true,
                    DirectoryPath = backups,
                    RetentionFiles = 1,
                    IntervalSeconds = 1
                }, clock);

                var first = backup.BackupOnce();
                Assert.True(File.Exists(first.BackupPath));
                Assert.Equal(backups, Path.GetDirectoryName(first.BackupPath));
                Assert.NotEqual(Path.Combine(data, "hub.db"), first.BackupPath);
                clock.UtcNow = clock.UtcNow.AddSeconds(1);
                var second = backup.BackupOnce();
                Assert.False(File.Exists(first.BackupPath));
                Assert.True(File.Exists(second.BackupPath));
                Assert.Single(Directory.GetFiles(backups, "kandoo-hub-*.db"));
                Assert.Equal(new FileInfo(second.BackupPath).Length, second.Bytes);
                Assert.Equal(64, second.Sha256.Length);

                using var connection = new SqliteConnection($"Data Source={second.BackupPath};Pooling=False");
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM operations WHERE store_id = $store;";
                command.Parameters.AddWithValue("$store", storeId.ToString("D"));
                Assert.Equal(1L, Convert.ToInt64(command.ExecuteScalar()));
            }
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void C22_OperationalLogWritesExternalEventsWithoutPairingPayloads()
    {
        var root = Path.Combine(Path.GetTempPath(), "kandoo-c22-log", Guid.NewGuid().ToString("N"));
        var logs = Path.Combine(root, "data", "logs");
        try
        {
            var log = new HubOperationalLog(logs);
            log.Write("HOST_STARTED");
            log.Write("HOST_STOPPED");
            var file = Assert.Single(Directory.GetFiles(logs, "hub-*.log"));
            var lines = File.ReadAllLines(file);
            Assert.Equal(2, lines.Length);
            Assert.EndsWith(" HOST_STARTED", lines[0], StringComparison.Ordinal);
            Assert.EndsWith(" HOST_STOPPED", lines[1], StringComparison.Ordinal);
            Assert.DoesNotContain("KANDOO_PAIRING_GRANT", File.ReadAllText(file), StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void C22_EnabledBackupRejectsRelativeInstallDirectory()
    {
        var options = new HubBackupOptions { Enabled = true, DirectoryPath = "backups" };
        Assert.Throws<ArgumentException>(options.Validate);
    }
}
