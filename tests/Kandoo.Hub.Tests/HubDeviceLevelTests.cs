using System.Net;
using System.Text.Json;
using Kandoo.Hub;
using Xunit;

namespace Kandoo.Hub.Tests;

public sealed class HubDeviceLevelTests : IDisposable
{
    private readonly HubApiFactory _factory = new();
    private readonly HubApiClient _api;

    public HubDeviceLevelTests() => _api = new HubApiClient(_factory);

    [Fact]
    public async Task C36_OwnerChosenLevelsAreRedeemedByPhonesAndListed()
    {
        var storeId = Guid.NewGuid();
        var owner = await _api.Owner(storeId, Guid.NewGuid(), Guid.NewGuid());
        var expected = new Dictionary<string, Guid>();
        foreach (var level in new[] { HubOperatorLevel.Seller, HubOperatorLevel.Operator, HubOperatorLevel.Manager })
        {
            var deviceId = Guid.NewGuid();
            expected.Add(level, deviceId);
            await PairPhone(owner, storeId, deviceId, level);
        }

        var devices = await _api.Get($"/v1/stores/{storeId:D}/devices", owner.Token);

        Assert.Equal(HttpStatusCode.OK, devices.StatusCode);
        Assert.Equal(4, devices.Body.GetArrayLength());
        foreach (var pair in expected)
        {
            var device = devices.Body.EnumerateArray().Single(value => value.GetProperty("device_id").GetGuid() == pair.Value);
            Assert.Equal("COMPANION", device.GetProperty("device_role").GetString());
            Assert.Equal(pair.Key, device.GetProperty("operator_level").GetString());
            Assert.Equal("ANDROID", device.GetProperty("platform").GetString());
        }

        var hub = devices.Body.EnumerateArray().Single(value => value.GetProperty("device_id").GetGuid() == owner.DeviceId);
        Assert.Equal(JsonValueKind.Null, hub.GetProperty("operator_level").ValueKind);
    }

    [Fact]
    public async Task C36_LegacyQrWithoutRequestedLevelDefaultsToOperator()
    {
        var storeId = Guid.NewGuid();
        var owner = await _api.Owner(storeId, Guid.NewGuid(), Guid.NewGuid());
        var grant = await _api.IssueGrant(owner, storeId);
        var qr = grant.Body.GetProperty("qr");
        Assert.Equal(HubOperatorLevel.Operator, qr.GetProperty("operator_level").GetString());

        var consume = await _api.Consume(
            qr.GetProperty("pairing_id").GetString()!, qr.GetProperty("nonce").GetString()!,
            Guid.NewGuid(), "Legacy phone", "ANDROID");

        Assert.Equal(HttpStatusCode.OK, consume.StatusCode);
        Assert.Equal(HubOperatorLevel.Operator,
            consume.Body.GetProperty("device").GetProperty("operator_level").GetString());
    }

    [Fact]
    public async Task C36_ChosenLevelCodeExpiresAndCannotBeReused()
    {
        var storeId = Guid.NewGuid();
        var owner = await _api.Owner(storeId, Guid.NewGuid(), Guid.NewGuid());
        var expiredGrant = await _api.IssueGrant(owner, storeId, level: HubOperatorLevel.Seller);
        var expiredQr = expiredGrant.Body.GetProperty("qr");
        _factory.Clock.UtcNow = expiredQr.GetProperty("expires_at").GetDateTimeOffset();

        var expired = await _api.Consume(
            expiredQr.GetProperty("pairing_id").GetString()!,
            expiredQr.GetProperty("nonce").GetString()!,
            Guid.NewGuid(), "Expired phone", "ANDROID");

        Assert.Equal(HttpStatusCode.Gone, expired.StatusCode);
        Assert.Equal("PAIRING_EXPIRED", expired.Body.GetProperty("code").GetString());
        var afterExpiry = await _api.Get($"/v1/stores/{storeId:D}/devices", owner.Token);
        Assert.Single(afterExpiry.Body.EnumerateArray());

        var paired = await PairPhone(owner, storeId, Guid.NewGuid(), HubOperatorLevel.Operator);
        _factory.Clock.UtcNow = _factory.Clock.UtcNow.AddMinutes(1);
        var replay = await _api.Consume(paired.PairingId, paired.Nonce, Guid.NewGuid(), "Replay phone", "ANDROID");

        Assert.Equal(HttpStatusCode.Conflict, replay.StatusCode);
        Assert.Equal("PAIRING_REPLAY_REJECTED", replay.Body.GetProperty("code").GetString());
        var afterReplay = await _api.Get($"/v1/stores/{storeId:D}/devices", owner.Token);
        Assert.Equal(2, afterReplay.Body.GetArrayLength());
    }

    [Fact]
    public async Task C36_OwnerChangesLevelButCompanionCannotElevateItself()
    {
        var storeId = Guid.NewGuid();
        var owner = await _api.Owner(storeId, Guid.NewGuid(), Guid.NewGuid());
        var paired = await PairPhone(owner, storeId, Guid.NewGuid(), HubOperatorLevel.Seller);
        var deviceId = paired.Session.DeviceId;

        var selfElevation = await _api.Patch(
            $"/v1/stores/{storeId:D}/devices/{deviceId:D}/level",
            new { operator_level = HubOperatorLevel.Manager }, paired.Session.Token);

        Assert.Equal(HttpStatusCode.Forbidden, selfElevation.StatusCode);
        Assert.Equal("OWNER_REQUIRED", selfElevation.Body.GetProperty("code").GetString());
        var afterSelfAttempt = await _api.Get($"/v1/stores/{storeId:D}/devices", owner.Token);
        Assert.Equal(HubOperatorLevel.Seller, Level(afterSelfAttempt, deviceId));

        var changed = await _api.Patch(
            $"/v1/stores/{storeId:D}/devices/{deviceId:D}/level",
            new { operator_level = HubOperatorLevel.Operator }, owner.Token);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal("DEVICE_LEVEL_CHANGED", changed.Body.GetProperty("status").GetString());
        Assert.Equal(HubOperatorLevel.Operator, changed.Body.GetProperty("device").GetProperty("operator_level").GetString());

        var promoted = await _api.Patch(
            $"/v1/stores/{storeId:D}/devices/{deviceId:D}/level",
            new { operator_level = HubOperatorLevel.Manager }, owner.Token);
        Assert.Equal(HttpStatusCode.OK, promoted.StatusCode);
        var devices = await _api.Get($"/v1/stores/{storeId:D}/devices", owner.Token);
        Assert.Equal(HubOperatorLevel.Manager, Level(devices, deviceId));

        using var reopened = new SqliteHubStore(_factory.ConnectionString);
        Assert.Equal(HubOperatorLevel.Manager, reopened.GetDevices(storeId).Single(value => value.DeviceId == deviceId).OperatorLevel);

        var invalid = await _api.Patch(
            $"/v1/stores/{storeId:D}/devices/{deviceId:D}/level",
            new { operator_level = "OWNER" }, owner.Token);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal("DEVICE_LEVEL_INVALID", invalid.Body.GetProperty("code").GetString());
        var hubChange = await _api.Patch(
            $"/v1/stores/{storeId:D}/devices/{owner.DeviceId:D}/level",
            new { operator_level = HubOperatorLevel.Seller }, owner.Token);
        Assert.Equal(HttpStatusCode.Conflict, hubChange.StatusCode);
        Assert.Equal("DEVICE_LEVEL_NOT_APPLICABLE", hubChange.Body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task C36_RevokedDeviceKeepsLevelButCannotAuthenticateResumeOrChange()
    {
        var storeId = Guid.NewGuid();
        var owner = await _api.Owner(storeId, Guid.NewGuid(), Guid.NewGuid());
        var paired = await PairPhone(owner, storeId, Guid.NewGuid(), HubOperatorLevel.Manager);
        var deviceId = paired.Session.DeviceId;

        var revoked = await _api.Post($"/v1/stores/{storeId:D}/devices/{deviceId:D}/revoke", new { }, owner.Token);
        Assert.Equal(HttpStatusCode.OK, revoked.StatusCode);

        var late = await _api.Get($"/v1/stores/{storeId:D}/devices", paired.Session.Token);
        Assert.Equal(HttpStatusCode.Forbidden, late.StatusCode);
        Assert.Equal("DEVICE_REVOKED", late.Body.GetProperty("code").GetString());
        var resumed = await _api.Post(
            $"/v1/stores/{storeId:D}/devices/{deviceId:D}/sessions",
            new { device_credential = paired.DeviceCredential });
        Assert.Equal(HttpStatusCode.Forbidden, resumed.StatusCode);
        Assert.Equal("DEVICE_REVOKED", resumed.Body.GetProperty("code").GetString());
        var changed = await _api.Patch(
            $"/v1/stores/{storeId:D}/devices/{deviceId:D}/level",
            new { operator_level = HubOperatorLevel.Seller }, owner.Token);
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        Assert.Equal("DEVICE_REVOKED", changed.Body.GetProperty("code").GetString());

        var devices = await _api.Get($"/v1/stores/{storeId:D}/devices", owner.Token);
        var device = devices.Body.EnumerateArray().Single(value => value.GetProperty("device_id").GetGuid() == deviceId);
        Assert.Equal("REVOKED", device.GetProperty("pairing_status").GetString());
        Assert.Equal(HubOperatorLevel.Manager, device.GetProperty("operator_level").GetString());
    }

    private async Task<PairedDevice> PairPhone(TestSession owner, Guid storeId, Guid deviceId, string level)
    {
        var grant = await _api.IssueGrant(owner, storeId, level: level);
        Assert.Equal(HttpStatusCode.OK, grant.StatusCode);
        var qr = grant.Body.GetProperty("qr");
        Assert.Equal("COMPANION", qr.GetProperty("device_role").GetString());
        Assert.Equal(level, qr.GetProperty("operator_level").GetString());
        Assert.True(qr.GetProperty("single_use").GetBoolean());
        Assert.Equal(TimeSpan.FromMinutes(5),
            qr.GetProperty("expires_at").GetDateTimeOffset() - qr.GetProperty("issued_at").GetDateTimeOffset());
        var consume = await _api.Post("/v1/pairing-grants/consume", new
        {
            pairing_id = qr.GetProperty("pairing_id").GetString()!,
            nonce = qr.GetProperty("nonce").GetString()!,
            device_id = deviceId,
            label = "Phone",
            platform = "ANDROID",
            device_public_key = "test-only-public-key",
            operator_level = HubOperatorLevel.Manager,
            requested_level = HubOperatorLevel.Manager
        });
        Assert.Equal(HttpStatusCode.OK, consume.StatusCode);
        Assert.Equal("DEVICE_PAIRED", consume.Body.GetProperty("status").GetString());
        Assert.Equal("COMPANION", consume.Body.GetProperty("device").GetProperty("device_role").GetString());
        Assert.Equal(level, consume.Body.GetProperty("device").GetProperty("operator_level").GetString());
        Assert.False(consume.Body.GetProperty("hub_role_granted").GetBoolean());
        var session = new TestSession(
            consume.Body.GetProperty("session_token").GetString()!,
            consume.Body.GetProperty("session").GetProperty("session_id").GetString()!,
            deviceId,
            null);
        return new PairedDevice(
            session,
            consume.Body.GetProperty("device_credential").GetString()!,
            qr.GetProperty("pairing_id").GetString()!,
            qr.GetProperty("nonce").GetString()!,
            qr,
            consume.Body);
    }

    private static string? Level(ApiResponse response, Guid deviceId) => response.Body.EnumerateArray()
        .Single(value => value.GetProperty("device_id").GetGuid() == deviceId)
        .GetProperty("operator_level").GetString();

    public void Dispose()
    {
        _api.Dispose();
        _factory.Dispose();
    }
}
