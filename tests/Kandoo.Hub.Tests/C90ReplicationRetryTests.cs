using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Kandoo.Hub;
using Xunit;

namespace Kandoo.Hub.Tests;

public sealed class C90ReplicationRetryTests
{
    [Fact]
    public async Task C90_RetryableNetworkErrorRetriesWithBackoff()
    {
        var source = new InMemoryReplicationSource();
        var transport = new FailingTransport(new CloudTransportResponse(HttpStatusCode.ServiceUnavailable, null, null));
        var options = new HubReplicationOptions
        {
            MaxAttempts = 3,
            DelayAsync = (delay, ct) => Task.CompletedTask
        };
        var client = new HubCloudReplicationClient(source, transport, options);

        var result = await client.ReplicateAsync(Guid.Parse("11111111-1111-1111-1111-111111111111"));

        // Network 503 is retryable; after max attempts it should still be retryable
        Assert.Equal("RETRY_REQUIRED", result.Status);
        Assert.True(result.Retryable);
        Assert.Equal(3, result.Attempts);
    }

    [Fact]
    public async Task C90_NonRetryableClientErrorStopsImmediately()
    {
        var source = new InMemoryReplicationSource();
        var transport = new FailingTransport(new CloudTransportResponse(HttpStatusCode.BadRequest, null, null));
        var options = new HubReplicationOptions
        {
            MaxAttempts = 3,
            DelayAsync = (delay, ct) => Task.CompletedTask
        };
        var client = new HubCloudReplicationClient(source, transport, options);

        var result = await client.ReplicateAsync(Guid.Parse("11111111-1111-1111-1111-111111111111"));

        // 400 is not retryable; stops after first attempt
        Assert.Equal("CLOUD_ERROR", result.Status);
        Assert.False(result.Retryable);
        Assert.Equal(1, result.Attempts);
    }

    private sealed class InMemoryReplicationSource : IHubReplicationSource
    {
        public int GetOutboxDepth(Guid storeId) => 1;
        public bool IsWriterFenced(Guid storeId) => false;
        public ReplicationBatch? ReadBatch(Guid storeId, int maxEntries, int maxBytes) =>
            ReplicationBatch.Create(
                new HubWriterIdentity(Guid.Parse("22222222-2222-2222-2222-222222222222"), 1),
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                new List<ReplicationLogEntry>
                {
                    new()
                    {
                        ProtocolVersion = "0",
                        StoreId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                        WriterDeviceId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                        WriterEpoch = 1,
                        OperationSeq = 1,
                        PreviousEntryHash = new string('0', 64),
                        EntryHash = new string('a', 64),
                        OperationId = "33333333-3333-3333-3333-333333333333",
                        DeviceId = Guid.Parse("44444444-4444-4444-4444-444444444444"),
                        OperationType = "invoice.issue",
                        BaseVersion = null,
                        RequestHash = "req-hash",
                        Payload = HubJson.Element(new { }),
                        Outcome = HubJson.Element(new { }),
                        ResponseSnapshot = HubJson.Element(new { }),
                        HubRecordedAt = DateTimeOffset.UtcNow.ToString("O")
                    }
                });
        public string GetDeviceSecret(Guid storeId, Guid deviceId) => "test-device-secret";
        public void Acknowledge(Guid storeId, long operationSeq, string entryHash, DateTimeOffset recordedAt) { }
        public void MarkWriterFenced(Guid storeId, HubWriterIdentity identity, string code) { }
    }

    private sealed class FailingTransport : ICloudReplicationTransport
    {
        private readonly CloudTransportResponse _response;
        public FailingTransport(CloudTransportResponse response) => _response = response;
        public Task<CloudTransportResponse> SendAsync(ReplicationBatch batch, CancellationToken cancellationToken) =>
            Task.FromResult(_response);
    }
}
