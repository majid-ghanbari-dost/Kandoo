using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Kandoo.Hub.Tests;
using Xunit;

namespace Kandoo.Hub.Tests;

public sealed class SetupFlowStoreCreationTests : IClassFixture<HubApiFactory>
{
    private readonly HubApiFactory _factory;

    public SetupFlowStoreCreationTests(HubApiFactory factory)
    {
        _factory = factory;
    }

    // M-005 item 3c: reproduces the first-run setup flow (ConfigureShopAsync in
    // SetupWizardPage) against the real in-process Hub and asserts success.
    // The verbatim cause found: SetupWizardPage sent an empty JSON body "{}" to
    // POST /v1/stores/{storeId}/owner-sessions with the bootstrap proof only in
    // the Authorization header. The endpoint reads HubDeviceId/OwnerUserId/
    // BootstrapProof from the request body (OwnerSessionRequest contract) and
    // ignores the Authorization header, so the request failed with 403.
    [Fact]
    public async Task SetupFlow_CreateStoreOwnerSession_Succeeds()
    {
        var client = _factory.CreateClient();
        var storeId = Guid.NewGuid();
        var hubDeviceId = Guid.NewGuid();
        var ownerUserId = Guid.NewGuid();
        var bootstrapProof = "test-only-owner-bootstrap"; // matches HubApiFactory config

        var ownerSessionBody = new
        {
            hub_device_id = hubDeviceId,
            owner_user_id = ownerUserId,
            bootstrap_proof = bootstrapProof
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $\"/v1/stores/{storeId}/owner-sessions\");
        request.Content = new StringContent(
            JsonSerializer.Serialize(ownerSessionBody),
            Encoding.UTF8,
            "application/json");

        using var response = await client.SendAsync(request);

        // Assert success: owner session issued
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        // The response must contain a session token
        Assert.True(root.TryGetProperty("session_token", out var tokenProp),
            $"Expected session_token in response body: {body}");
        Assert.False(string.IsNullOrWhiteSpace(tokenProp.GetString()),
            $"session_token must not be empty: {body}");
    }

    // Regression test: empty body (the old SetupWizardPage behavior) must fail,
    // confirming the verbatim cause is captured.
    [Fact]
    public async Task SetupFlow_EmptyBody_ReturnsError()
    {
        var client = _factory.CreateClient();
        var storeId = Guid.NewGuid();

        using var request = new HttpRequestMessage(HttpMethod.Post, $\"/v1/stores/{storeId}/owner-sessions\");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-only-owner-bootstrap");
        request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request);

        // Empty body → missing HubDeviceId/OwnerUserId → 403 OWNER_BOOTSTRAP_INVALID
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("OWNER_BOOTSTRAP_INVALID", body);
    }
}
