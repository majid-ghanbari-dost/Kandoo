using System.Net;
using System.Text.Json;
using Kandoo.Core;
using Kandoo.Hub;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Kandoo.Hub.Tests;

public sealed class HubStatusApiTests : IDisposable
{
    private readonly string _backupDirectory = Path.Combine(Path.GetTempPath(), "kandoo-c43", Guid.NewGuid().ToString("N"));
    private readonly HubApiFactory _factory = new();
    private readonly HubApiClient _api;

    public HubStatusApiTests()
    {
        _factory.ExtraConfiguration["Hub:Backup:DirectoryPath"] = _backupDirectory;
        _factory.ExtraConfiguration[SystemHubStatusProbe.SmsConfigurationKey] = "true";
        _api = new HubApiClient(_factory);
    }

    [Fact]
    public async Task C43_StatusRouteUsesExistingAuthAndProjectsRealKnownObservations()
    {
        var storeId = Guid.NewGuid();
        var generatedAt = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
        var syncAt = generatedAt.AddMinutes(-10);
        var backupAt = generatedAt.AddMinutes(-20);
        _factory.Clock.UtcNow = generatedAt;
        var owner = await _api.Owner(storeId, Guid.NewGuid(), Guid.NewGuid());
        var paired = await _api.Pair(owner, storeId, Guid.NewGuid());
        _api.SeedOperations(storeId, paired.Session.DeviceId, 1);
        var entry = Assert.Single(_api.Store.ReadReplicationEntries(storeId));
        _api.Store.Acknowledge(storeId, entry.OperationSeq, entry.EntryHash, syncAt);
        var backup = new SqliteBackupService(
            _api.Store,
            new HubBackupOptions { Enabled = false, DirectoryPath = _backupDirectory },
            new ManualHubClock { UtcNow = backupAt }).BackupOnce();

        var response = await _api.Get($"/v1/stores/{storeId:D}/status/v0", paired.Session.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("OK", response.Body.GetProperty("status").GetString());
        Assert.Equal(storeId, response.Body.GetProperty("store_id").GetGuid());
        Assert.Equal(generatedAt, response.Body.GetProperty("generated_at_utc").GetDateTimeOffset());
        Assert.Equal("UTC", response.Body.GetProperty("time_zone").GetString());
        var version = response.Body.GetProperty("version");
        Assert.True(version.GetProperty("value").ValueKind == JsonValueKind.String, response.Body.GetRawText());
        Assert.False(string.IsNullOrWhiteSpace(version.GetProperty("value").GetString()));
        Assert.Equal(JsonValueKind.Null, version.GetProperty("reason").ValueKind);
        AssertTime(response.Body.GetProperty("last_sync"), syncAt);
        AssertTime(response.Body.GetProperty("last_backup"), backupAt);
        Assert.Equal(backupAt, DateTimeOffset.ParseExact(
            Path.GetFileName(backup.BackupPath).Substring("kandoo-hub-".Length, "yyyyMMddTHHmmssZ".Length),
            "yyyyMMddTHHmmssZ",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal));
        var freeDisk = response.Body.GetProperty("free_disk");
        Assert.True(freeDisk.GetProperty("bytes").GetInt64() > 0);
        Assert.Equal(JsonValueKind.Null, freeDisk.GetProperty("reason").ValueKind);
        var sms = response.Body.GetProperty("sms");
        Assert.True(sms.GetProperty("enabled").GetBoolean());
        Assert.Equal(JsonValueKind.Null, sms.GetProperty("reason").ValueKind);
        Assert.Equal("CONFIGURATION_ONLY", sms.GetProperty("scope").GetString());
        Assert.Equal(SystemHubStatusProbe.SmsConfigurationKey, sms.GetProperty("configuration_key").GetString());

        var unauthenticated = await _api.Get($"/v1/stores/{storeId:D}/status/v0");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
        Assert.Equal("SESSION_INVALID", unauthenticated.Body.GetProperty("code").GetString());
        var otherStore = await _api.Get($"/v1/stores/{Guid.NewGuid():D}/status/v0", paired.Session.Token);
        Assert.Equal(HttpStatusCode.Forbidden, otherStore.StatusCode);
        Assert.Equal("STORE_ACCESS_DENIED", otherStore.Body.GetProperty("code").GetString());

        // The observation did not create another operation or trigger replication.
        Assert.Single(_api.Store.ReadReplicationEntries(storeId));
        Assert.Equal(syncAt, _api.Store.GetLastSyncAtUtc(storeId));
    }

    [Fact]
    public async Task C43_StatusRouteReturnsNullAndStableReasonForEveryUnknownValue()
    {
        using var factory = new HubApiFactory();
        factory.TestStatus = new FixedStatusProbe(new HubStatusSnapshot(
            HubStatusTextObservation.Unknown(SystemHubStatusProbe.VersionUnavailable),
            HubStatusObservation<DateTimeOffset>.Unknown(SystemHubStatusProbe.NoAcknowledgedSync),
            HubStatusObservation<DateTimeOffset>.Unknown(SystemHubStatusProbe.BackupUnavailable),
            HubStatusObservation<long>.Unknown(SystemHubStatusProbe.DiskUnavailable),
            HubStatusObservation<bool>.Unknown(SystemHubStatusProbe.SmsConfigurationInvalid)));
        using var api = new HubApiClient(factory);
        var storeId = Guid.NewGuid();
        var owner = await api.Owner(storeId, Guid.NewGuid(), Guid.NewGuid());

        var response = await api.Get($"/v1/stores/{storeId:D}/status/v0", owner.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertUnknown(response.Body.GetProperty("version"), "value", SystemHubStatusProbe.VersionUnavailable);
        AssertUnknown(response.Body.GetProperty("last_sync"), "at_utc", SystemHubStatusProbe.NoAcknowledgedSync);
        AssertUnknown(response.Body.GetProperty("last_backup"), "at_utc", SystemHubStatusProbe.BackupUnavailable);
        AssertUnknown(response.Body.GetProperty("free_disk"), "bytes", SystemHubStatusProbe.DiskUnavailable);
        AssertUnknown(response.Body.GetProperty("sms"), "enabled", SystemHubStatusProbe.SmsConfigurationInvalid);
    }

    [Fact]
    public void C43_SystemProbeDistinguishesMissingDisabledUnavailableAndInvalidStates()
    {
        using var store = new SqliteHubStore("Data Source=:memory:;Pooling=False");
        var storeId = Guid.NewGuid();
        var missingDirectory = Path.Combine(Path.GetTempPath(), "kandoo-c43-missing", Guid.NewGuid().ToString("N"));
        var invalidSmsConfiguration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { [SystemHubStatusProbe.SmsConfigurationKey] = "not-a-boolean" }).Build();
        var disabled = new SystemHubStatusProbe(
            store,
            new HubBackupOptions { Enabled = false, DirectoryPath = missingDirectory },
            invalidSmsConfiguration,
            "\0").Read(storeId);

        AssertUnknown(disabled.LastSync, SystemHubStatusProbe.NoAcknowledgedSync);
        AssertUnknown(disabled.LastBackup, SystemHubStatusProbe.BackupDisabled);
        AssertUnknown(disabled.FreeDiskBytes, SystemHubStatusProbe.DiskUnavailable);
        AssertUnknown(disabled.SmsEnabled, SystemHubStatusProbe.SmsConfigurationInvalid);

        var defaultConfiguration = new ConfigurationBuilder().Build();
        var enabledWithoutBackup = new SystemHubStatusProbe(
            store,
            new HubBackupOptions { Enabled = true, DirectoryPath = missingDirectory },
            defaultConfiguration,
            AppContext.BaseDirectory).Read(storeId);
        AssertUnknown(enabledWithoutBackup.LastBackup, SystemHubStatusProbe.NoBackupFound);
        Assert.Equal(false, enabledWithoutBackup.SmsEnabled.Value);
        Assert.Null(enabledWithoutBackup.SmsEnabled.Reason);
        Assert.True(enabledWithoutBackup.FreeDiskBytes.Value > 0);
        Assert.Null(enabledWithoutBackup.FreeDiskBytes.Reason);
        Assert.False(string.IsNullOrWhiteSpace(enabledWithoutBackup.Version.Value));
        Assert.Null(enabledWithoutBackup.Version.Reason);

        var unavailableBackup = new SystemHubStatusProbe(
            store,
            new HubBackupOptions { Enabled = true, DirectoryPath = "\0" },
            defaultConfiguration,
            AppContext.BaseDirectory).Read(storeId);
        AssertUnknown(unavailableBackup.LastBackup, SystemHubStatusProbe.BackupUnavailable);
    }

    public void Dispose()
    {
        _api.Dispose();
        _factory.Dispose();
        if (Directory.Exists(_backupDirectory))
        {
            Directory.Delete(_backupDirectory, recursive: true);
        }
    }

    private static void AssertTime(JsonElement value, DateTimeOffset expected)
    {
        Assert.Equal(expected, value.GetProperty("at_utc").GetDateTimeOffset());
        Assert.Equal(JsonValueKind.Null, value.GetProperty("reason").ValueKind);
    }

    private static void AssertUnknown(JsonElement value, string valueName, string reason)
    {
        Assert.Equal(JsonValueKind.Null, value.GetProperty(valueName).ValueKind);
        Assert.Equal(reason, value.GetProperty("reason").GetString());
    }

    private static void AssertUnknown<T>(HubStatusObservation<T> value, string reason) where T : struct
    {
        Assert.Null(value.Value);
        Assert.Equal(reason, value.Reason);
    }

    private sealed class FixedStatusProbe(HubStatusSnapshot snapshot) : IHubStatusProbe
    {
        public HubStatusSnapshot Read(Guid storeId) => snapshot;
    }
}
