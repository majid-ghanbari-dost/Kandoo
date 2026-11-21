using System.Net;
using Xunit;

namespace Kandoo.Hub.Tests;

public sealed class OwnerBootstrapTests : IDisposable
{
    private readonly HubApiFactory _factory = new();
    private readonly HubApiClient _api;

    public OwnerBootstrapTests()
    {
        _api = new HubApiClient(_factory);
    }

    [Fact]
    public async Task C87_OwnerBootstrapProofIssuesOwnerSessionWithToken()
    {
        // Arrange: a fresh store ID and device/owner IDs, matching the launcher's
        // first-run bootstrap flow (generate proof -> start Hub -> POST /owner-sessions).
        var storeId = Guid.NewGuid();
        var hubDeviceId = Guid.NewGuid();
        var ownerUserId = Guid.NewGuid();

        // Act: issue an owner session with the configured bootstrap proof.
        var owner = await _api.Owner(storeId, hubDeviceId, ownerUserId);

        // Assert: 2xx + non-empty session_token.
        Assert.False(string.IsNullOrWhiteSpace(owner.Token), "session_token must be non-empty.");
        Assert.Equal(hubDeviceId, owner.DeviceId);
        Assert.Equal(ownerUserId, owner.OwnerUserId);
    }

    [Fact]
    public async Task C87_WrongBootstrapProofReturns403OwnerBootstrapInvalid()
    {
        // Arrange: a fresh store ID and device/owner IDs.
        var storeId = Guid.NewGuid();
        var hubDeviceId = Guid.NewGuid();
        var ownerUserId = Guid.NewGuid();

        // Act: POST /owner-sessions with a wrong bootstrap proof.
        var response = await _api.Post($"/v1/stores/{storeId}/owner-sessions", new
        {
            hub_device_id = hubDeviceId,
            owner_user_id = ownerUserId,
            bootstrap_proof = "wrong-proof"
        });

        // Assert: 403 OWNER_BOOTSTRAP_INVALID.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("OWNER_BOOTSTRAP_INVALID", response.Body.GetProperty("code").GetString());
    }

    public void Dispose()
    {
        _api.Dispose();
        _factory.Dispose();
    }
}
