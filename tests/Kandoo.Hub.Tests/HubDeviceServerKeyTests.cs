using System.Net;
using Xunit;

namespace Kandoo.Hub.Tests;

public sealed class HubDeviceServerKeyTests : IClassFixture<HubApiFactory>
{
    private readonly HubApiClient _api;

    public HubDeviceServerKeyTests(HubApiFactory factory) => _api = new HubApiClient(factory);

    [Fact]
    public async Task Owner_can_issue_verify_and_reject_device_server_key()
    {
        var storeId = Guid.NewGuid();
        var hubDeviceId = Guid.NewGuid();
        var ownerUserId = Guid.NewGuid();
        var owner = await _api.Owner(storeId, hubDeviceId, ownerUserId);

        var companionDeviceId = Guid.NewGuid();
        var paired = await _api.Pair(owner, storeId, companionDeviceId);

        var issued = await _api.Post(
            $"/v1/stores/{storeId.ToString("D")}/devices/{companionDeviceId.ToString("D")}/server-key",
            null,
            owner.Token);
        Assert.Equal(HttpStatusCode.OK, issued.StatusCode);
        var key = issued.Body.GetProperty("device_server_key").GetString();
        Assert.False(string.IsNullOrEmpty(key));

        var verified = await _api.Post(
            $"/v1/stores/{storeId.ToString("D")}/devices/{companionDeviceId.ToString("D")}/verify-server-key",
            new { device_server_key = key },
            owner.Token);
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
        Assert.Equal("DEVICE_SERVER_KEY_VERIFIED", verified.Body.GetProperty("status").GetString());
        Assert.True(verified.Body.GetProperty("verified").GetBoolean());

        var invalid = await _api.Post(
            $"/v1/stores/{storeId.ToString("D")}/devices/{companionDeviceId.ToString("D")}/verify-server-key",
            new { device_server_key = "wrong-key" },
            owner.Token);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var unknown = await _api.Post(
            $"/v1/stores/{storeId.ToString("D")}/devices/{Guid.NewGuid().ToString("D")}/server-key",
            null,
            owner.Token);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task Companion_cannot_generate_server_key()
    {
        var storeId = Guid.NewGuid();
        var hubDeviceId = Guid.NewGuid();
        var ownerUserId = Guid.NewGuid();
        var owner = await _api.Owner(storeId, hubDeviceId, ownerUserId);

        var companionDeviceId = Guid.NewGuid();
        var paired = await _api.Pair(owner, storeId, companionDeviceId);

        var forbidden = await _api.Post(
            $"/v1/stores/{storeId.ToString("D")}/devices/{companionDeviceId.ToString("D")}/server-key",
            null,
            paired.Session.Token);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }
}
