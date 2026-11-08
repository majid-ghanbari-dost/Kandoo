using System.Net;
using Kandoo.Core;
using Xunit;

namespace Kandoo.Hub.Tests;

public sealed class HubDeviceServerKeyTests : IClassFixture<HubApiFactory>
{
    private readonly HubApiClient _api;
    private readonly HubApiFactory _factory;

    public HubDeviceServerKeyTests(HubApiFactory factory)
    {
        _factory = factory;
        _api = new HubApiClient(factory);
    }

    [Fact]
    public void Store_issues_verifies_and_rejects_device_server_key()
    {
        var store = _factory.Store;
        var storeId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var ownerUserId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 11, 8, 0, 0, 0, TimeSpan.Zero);

        var issued = store.GenerateDeviceServerKey(storeId, deviceId, ownerUserId, now);
        Assert.NotNull(issued);
        Assert.False(string.IsNullOrEmpty(issued));

        var verified = store.VerifyDeviceServerKey(storeId, deviceId, issued, now);
        Assert.True(verified.Verified);
        Assert.Equal("DEVICE_SERVER_KEY_VERIFIED", verified.Code);

        var invalid = store.VerifyDeviceServerKey(storeId, deviceId, "wrong-key", now);
        Assert.False(invalid.Verified);
        Assert.Equal("DEVICE_SERVER_KEY_INVALID", invalid.Code);

        var unknownIssued = store.GenerateDeviceServerKey(storeId, Guid.NewGuid(), ownerUserId, now);
        Assert.Null(unknownIssued);

        var unknownVerified = store.VerifyDeviceServerKey(storeId, Guid.NewGuid(), "any-key", now);
        Assert.False(unknownVerified.Verified);
        Assert.Equal("DEVICE_NOT_PAIRED", unknownVerified.Code);

        Assert.True(store.RevokeDevice(storeId, deviceId, ownerUserId, now));
        var revokedIssued = store.GenerateDeviceServerKey(storeId, deviceId, ownerUserId, now);
        Assert.Null(revokedIssued);

        var revokedVerified = store.VerifyDeviceServerKey(storeId, deviceId, issued, now);
        Assert.False(revokedVerified.Verified);
        Assert.Equal("DEVICE_REVOKED", revokedVerified.Code);
    }

    [Fact]
    public async Task Unknown_device_cannot_receive_server_key_via_api()
    {
        var storeId = Guid.NewGuid();
        var hubDeviceId = Guid.NewGuid();
        var ownerUserId = Guid.NewGuid();
        var owner = await _api.Owner(storeId, hubDeviceId, ownerUserId);

        var unknown = await _api.Post(
            $"/v1/stores/{storeId.ToString("D")}/devices/{Guid.NewGuid().ToString("D")}/server-key",
            null,
            owner.Token);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }
}
