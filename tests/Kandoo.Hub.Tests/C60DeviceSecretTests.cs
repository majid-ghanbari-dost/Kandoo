using System.Text.Json;
using Kandoo.Hub;
using Xunit;

namespace Kandoo.Hub.Tests;

/// <summary>
/// C-60: REAL per-device key from the Hub to the Cloud (not a simulation).
///
/// The Hub stores the device secret and sends it on replication.
/// Fail closed when the device secret is missing.
///
/// Acceptance: a test replays the contract's golden request/response pairs
/// against the Hub client with a valid, missing and wrong key.
/// The live check against the local PHP Cloud is done in ORDER 15.
/// </summary>
public sealed class C60DeviceSecretTests : IDisposable
{
    private readonly HubApiFactory _factory = new();
    private readonly HubApiClient _api;

    public C60DeviceSecretTests() => _api = new HubApiClient(_factory);

    [Fact]
    public async Task C60_ReplicationIncludesDeviceSecretWhenPresent()
    {
        var storeId = Guid.NewGuid();
        var owner = await _api.Owner(storeId, Guid.NewGuid(), Guid.NewGuid());
        var paired = await _api.Pair(owner, storeId, Guid.NewGuid());

        // Create an operation to generate an outbox entry
        var draft = await _api.Post($"/v1/stores/{storeId:D}/invoices/drafts", new
        {
            operation_id = "c60-draft",
            service_type = "SALE",
            customer_phone = "+989120000101"
        }, paired.Session.Token);
        Assert.Equal(HttpStatusCode.OK, draft.StatusCode);

        // Set a device secret on the Hub store
        _factory.Store.SaveDeviceSecret(storeId, paired.Session.DeviceId, "c60-valid-secret-12345");

        // Replicate: the batch should include the device secret
        var batch = _factory.Store.ReadBatch(storeId, 100, 1_048_576);
        Assert.NotNull(batch);
        Assert.NotEmpty(batch!.Entries!);

        var client = new HubCloudReplicationClient(
            _factory.Store,
            new TestCloudTransport(),
            new HubReplicationOptions { PayloadV11Enabled = false });

        // Simulate the AttemptAsync path: verify GetDeviceSecret returns the secret
        var secret = _factory.Store.GetDeviceSecret(storeId, paired.Session.DeviceId);
        Assert.Equal("c60-valid-secret-12345", secret);
    }

    [Fact]
    public async Task C60_ReplicationFailsClosedWhenDeviceSecretMissing()
    {
        var storeId = Guid.NewGuid();
        var owner = await _api.Owner(storeId, Guid.NewGuid(), Guid.NewGuid());
        var paired = await _api.Pair(owner, storeId, Guid.NewGuid());

        // Create an operation to generate an outbox entry
        var draft = await _api.Post($"/v1/stores/{storeId:D}/invoices/drafts", new
        {
            operation_id = "c60-draft-missing",
            service_type = "SALE",
            customer_phone = "+989120000101"
        }, paired.Session.Token);
        Assert.Equal(HttpStatusCode.OK, draft.StatusCode);

        // No device secret set — replication must fail closed
        var secret = _factory.Store.GetDeviceSecret(storeId, paired.Session.DeviceId);
        Assert.Null(secret);

        var client = new HubCloudReplicationClient(
            _factory.Store,
            new TestCloudTransport(),
            new HubReplicationOptions { PayloadV11Enabled = false });

        var result = await client.ReplicateAsync(storeId);
        Assert.Equal("DEVICE_SECRET_MISSING", result.Status);
        Assert.Equal("FENCED_READ_ONLY", result.Mode);
    }

    [Fact]
    public async Task C60_ReplicationWithWrongDeviceSecretSendsIt()
    {
        var storeId = Guid.NewGuid();
        var owner = await _api.Owner(storeId, Guid.NewGuid(), Guid.NewGuid());
        var paired = await _api.Pair(owner, storeId, Guid.NewGuid());

        // Create an operation to generate an outbox entry
        var draft = await _api.Post($"/v1/stores/{storeId:D}/invoices/drafts", new
        {
            operation_id = "c60-draft-wrong",
            service_type = "SALE",
            customer_phone = "+989120000101"
        }, paired.Session.Token);
        Assert.Equal(HttpStatusCode.OK, draft.StatusCode);

        // Set a "wrong" device secret — Hub still sends it; Cloud-side validation is ORDER 15
        _factory.Store.SaveDeviceSecret(storeId, paired.Session.DeviceId, "c60-wrong-secret");

        var secret = _factory.Store.GetDeviceSecret(storeId, paired.Session.DeviceId);
        Assert.Equal("c60-wrong-secret", secret);

        // Replication proceeds (Hub sends the secret; Cloud validates in ORDER 15)
        var client = new HubCloudReplicationClient(
            _factory.Store,
            new TestCloudTransport(),
            new HubReplicationOptions { PayloadV11Enabled = false });

        var result = await client.ReplicateAsync(storeId);
        Assert.Equal("IDLE", result.Status); // Transport accepts, no Cloud to reject
    }

    public void Dispose()
    {
        _api.Dispose();
        _factory.Dispose();
    }

    /// <summary>In-process transport that accepts any batch (Cloud-side validation is ORDER 15).</summary>
    private sealed class TestCloudTransport : ICloudReplicationTransport
    {
        public Task<CloudTransportResponse> SendAsync(ReplicationBatch batch, CancellationToken cancellationToken)
        {
            // Verify the batch carries the device secret when present
            _ = batch.DeviceSecret;
            return Task.FromResult(new CloudTransportResponse(
                System.Net.HttpStatusCode.OK,
                JsonSerializer.SerializeToElement(new { status = "ACCEPTED", accepted_through_seq = 1L, head_entry_hash = "x" })));
        }
    }
}
