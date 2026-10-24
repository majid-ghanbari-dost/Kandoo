using System.Net;
using System.Text;
using System.Text.Json;
using Kandoo.Cloud;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Kandoo.Cloud.Tests;

public sealed class CloudApiTests : IClassFixture<PostgresTestDatabase>, IDisposable
{
    private readonly PostgresTestDatabase _database;
    private readonly CloudFactory _factory;
    private readonly HttpClient _client;

    public CloudApiTests(PostgresTestDatabase database)
    {
        _database = database;
        _factory = new CloudFactory(database.Store);
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task Health_IsAvailableWithoutAStore()
    {
        using var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await ReadBody(response);
        Assert.Equal("ok", body.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public Task CLOUD_001_ContiguousBatchIsAtomicAndAdvancesWatermark() => RunVector("CLOUD-001");

    [Fact]
    public Task CLOUD_002_DuplicateBatchHasNoNewEffects() => RunVector("CLOUD-002");

    [Fact]
    public Task CLOUD_003_GapReturnsResumeMetadataAndDoesNotAdvance() => RunVector("CLOUD-003");

    [Fact]
    public Task CLOUD_004_ReorderedBatchIsRejectedWithoutPartialProjection() => RunVector("CLOUD-004");

    [Fact]
    public Task CLOUD_005_CommittedRetryReturnsDuplicateWithoutReplayingEffects() => RunVector("CLOUD-005");

    [Fact]
    public Task CLOUD_006_ThrottleReturnsRetryAndBatchLimitMetadata() => RunVector("CLOUD-006");

    [Fact]
    public Task CLOUD_007_IntentionalEntryCorruptionIsRejectedAndQuarantined() => RunVector("CLOUD-007");

    [Fact]
    public Task CLOUD_008_StaleWriterIsFencedEvenForAnEmptyBatch() => RunVector("CLOUD-008");

    [Fact]
    public async Task C05_CommittedStateAndIdempotencySurviveStoreRecreation()
    {
        var vector = VectorFile.Get("CLOUD-001");
        var batch = VectorFile.FirstBatch(vector);
        var seed = VectorFile.Seed(vector, batch);
        _factory.Store.Seed(seed);
        using var response = await PostBatch(batch);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var restarted = new PostgresCloudStore(_database.ConnectionString);
        var snapshot = restarted.GetSnapshot(seed.StoreId);
        Assert.NotNull(snapshot);
        var saved = snapshot!;
        Assert.Equal(42L, saved.AcceptedThroughSeq);
        Assert.Equal(42L, saved.EntryCount);
        Assert.Equal(2, saved.OperationIdCount);

        var persistedBatch = JsonSerializer.Deserialize<OperationBatch>(batch.GetRawText(), CloudJson.Options)!;
        var replay = restarted.Ingest(seed.StoreId, persistedBatch, batch.GetProperty("batch_id").GetString());
        var ack = Assert.IsType<IngestAck>(replay.Body);
        Assert.Equal("DUPLICATE", ack.Status);
        Assert.Equal(0, ack.IngestedCount);
        Assert.Equal(2, ack.DuplicateCount);
    }

    private async Task RunVector(string vectorId)
    {
        var vector = VectorFile.Get(vectorId);
        var batch = VectorFile.FirstBatch(vector);
        var seed = VectorFile.Seed(vector, batch);
        _factory.Store.Seed(seed);

        if (vectorId == "CLOUD-005")
        {
            using (var firstResponse = await PostBatch(batch))
            {
                Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
                using var firstBody = await ReadBody(firstResponse);
                Assert.Equal("ACCEPTED", firstBody.RootElement.GetProperty("status").GetString());
                Assert.Equal(2, firstBody.RootElement.GetProperty("ingested_count").GetInt32());
            }

            using var retryResponse = await PostBatch(batch);
            using var retryBody = await ReadBody(retryResponse);
            Assert.Equal(HttpStatusCode.OK, retryResponse.StatusCode);
            Assert.Equal("DUPLICATE", retryBody.RootElement.GetProperty("status").GetString());
            Assert.Equal(0, retryBody.RootElement.GetProperty("ingested_count").GetInt32());
            Assert.Equal(2, retryBody.RootElement.GetProperty("duplicate_count").GetInt32());
            Assert.Equal(42L, retryBody.RootElement.GetProperty("accepted_through_seq").GetInt64());
            var retrySnapshot = _factory.Store.GetSnapshot(seed.StoreId)!;
            Assert.Equal(42L, retrySnapshot.EntryCount);
            Assert.Equal(2, retrySnapshot.OperationIdCount);
            Assert.Equal(42L, retrySnapshot.ProjectionVersion);
            return;
        }

        using var response = await PostBatch(batch);
        using var body = await ReadBody(response);
        var root = body.RootElement;
        var snapshot = _factory.Store.GetSnapshot(seed.StoreId)!;

        switch (vectorId)
        {
            case "CLOUD-001":
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal("ACCEPTED", root.GetProperty("status").GetString());
                Assert.Equal(42L, root.GetProperty("accepted_through_seq").GetInt64());
                Assert.Equal(2, root.GetProperty("ingested_count").GetInt32());
                Assert.Equal(0, root.GetProperty("duplicate_count").GetInt32());
                Assert.Equal("fa0daab5586810444f1ecfb1ebf8f3f7e061a42630d77806a4e8d9aa3a98e502", root.GetProperty("head_entry_hash").GetString());
                Assert.Equal(42L, snapshot.EntryCount);
                Assert.Equal(42L, snapshot.ProjectionVersion);
                Assert.Equal(2, snapshot.OperationIdCount);
                break;

            case "CLOUD-002":
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal("DUPLICATE", root.GetProperty("status").GetString());
                Assert.Equal(0, root.GetProperty("ingested_count").GetInt32());
                Assert.Equal(2, root.GetProperty("duplicate_count").GetInt32());
                Assert.Equal(42L, root.GetProperty("accepted_through_seq").GetInt64());
                Assert.Equal(42L, snapshot.EntryCount);
                Assert.Equal(42L, snapshot.ProjectionVersion);
                Assert.Equal(2, snapshot.OperationIdCount);
                break;

            case "CLOUD-003":
                Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
                Assert.Equal("GAP_DETECTED", root.GetProperty("code").GetString());
                var gapHint = root.GetProperty("retry_hint");
                Assert.Equal("RESUME_FROM_SEQUENCE", gapHint.GetProperty("action").GetString());
                Assert.Equal(41L, gapHint.GetProperty("expected_seq").GetInt64());
                Assert.Equal(42L, gapHint.GetProperty("received_seq").GetInt64());
                Assert.Equal(40L, gapHint.GetProperty("accepted_through_seq").GetInt64());
                Assert.Equal(40L, snapshot.AcceptedThroughSeq);
                Assert.Equal(40L, snapshot.EntryCount);
                break;

            case "CLOUD-004":
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
                Assert.Equal("SEQUENCE_ORDER_VIOLATION", root.GetProperty("code").GetString());
                Assert.Equal(40L, snapshot.AcceptedThroughSeq);
                Assert.Equal(40L, snapshot.EntryCount);
                Assert.Equal(40L, snapshot.ProjectionVersion);
                break;

            case "CLOUD-006":
                Assert.Equal((HttpStatusCode)429, response.StatusCode);
                Assert.Equal("THROTTLED", root.GetProperty("code").GetString());
                var throttleHint = root.GetProperty("retry_hint");
                Assert.Equal("RETRY_AFTER", throttleHint.GetProperty("action").GetString());
                Assert.Equal(2_000L, throttleHint.GetProperty("retry_after_ms").GetInt64());
                Assert.Equal(2L, throttleHint.GetProperty("max_batch_entries").GetInt64());
                Assert.Equal(65_536L, throttleHint.GetProperty("max_batch_bytes").GetInt64());
                Assert.Equal(40L, throttleHint.GetProperty("accepted_through_seq").GetInt64());
                Assert.Equal(TimeSpan.FromSeconds(2), response.Headers.RetryAfter!.Delta!.Value);
                Assert.Equal(40L, snapshot.AcceptedThroughSeq);
                Assert.Equal(40L, snapshot.EntryCount);
                break;

            case "CLOUD-007":
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
                Assert.Equal("ENTRY_HASH_MISMATCH", root.GetProperty("code").GetString());
                var corruptionDetails = root.GetProperty("retry_hint").GetProperty("details");
                Assert.Equal("ca15b6e0b3d101f57f9855c6e43871e4d5db6875d87a19b0b8b0ee8baa9975a8", corruptionDetails.GetProperty("supplied_entry_hash").GetString());
                Assert.Equal("387ca0f3d13d21a4b4373898e1c0c58dd44f6d7adca1ec01556027e5ae0a97b8", corruptionDetails.GetProperty("recomputed_entry_hash").GetString());
                Assert.Equal("REVIEW", corruptionDetails.GetProperty("quarantine").GetString());
                Assert.Equal(40L, snapshot.AcceptedThroughSeq);
                Assert.Equal(40L, snapshot.EntryCount);
                break;

            case "CLOUD-008":
                Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
                Assert.Equal("WRITER_FENCED", root.GetProperty("code").GetString());
                Assert.Equal("STOP_WRITER", root.GetProperty("retry_hint").GetProperty("action").GetString());
                Assert.Equal(4L, root.GetProperty("retry_hint").GetProperty("current_version").GetInt64());
                Assert.Equal(4L, snapshot.ActiveWriterEpoch);
                Assert.Equal(42L, snapshot.AcceptedThroughSeq);
                Assert.Equal(42L, snapshot.EntryCount);
                break;

            default:
                throw new InvalidOperationException($"No assertion mapping for {vectorId}.");
        }
    }

    private async Task<HttpResponseMessage> PostBatch(JsonElement batch)
    {
        var storeId = batch.GetProperty("store_id").GetString();
        var request = new HttpRequestMessage(HttpMethod.Post, $"/v1/stores/{storeId}/operation-batches")
        {
            Content = new StringContent(batch.GetRawText(), Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Idempotency-Key", batch.GetProperty("batch_id").GetString());
        return await _client.SendAsync(request);
    }

    private static async Task<JsonDocument> ReadBody(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(content);
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private sealed class CloudFactory : WebApplicationFactory<Program>
    {
        public CloudFactory(PostgresCloudStore store) => Store = store;

        public PostgresCloudStore Store { get; }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ICloudStore>();
                services.AddSingleton<ICloudStore>(Store);
            });
        }
    }

    private static class VectorFile
    {
        private static readonly JsonElement Root = LoadRoot();

        public static JsonElement Get(string id) => Root.GetProperty("vectors").EnumerateArray()
            .Single(vector => vector.GetProperty("id").GetString() == id);

        public static JsonElement FirstBatch(JsonElement vector) => vector.GetProperty("input").GetProperty("steps")
            .EnumerateArray().First().GetProperty("batch");

        public static CloudSeed Seed(JsonElement vector, JsonElement batch)
        {
            var given = vector.GetProperty("input").GetProperty("given");
            var cloud = given.GetProperty("cloud");
            var accepted = cloud.GetProperty("accepted_through_seq").GetInt64();
            var entries = new List<LogEntry>();
            if (cloud.TryGetProperty("entries", out var rawEntries))
            {
                foreach (var rawEntry in rawEntries.EnumerateArray())
                {
                    entries.Add(JsonSerializer.Deserialize<LogEntry>(rawEntry.GetRawText(), CloudJson.Options)!);
                }
            }

            string? head = cloud.TryGetProperty("head_entry_hash", out var rawHead) ? rawHead.GetString() : null;
            if (head is null && batch.TryGetProperty("entries", out var batchEntries) && batchEntries.GetArrayLength() > 0)
            {
                // Some vectors omit the prior head from given.cloud; the first
                // fixture entry supplies that known synthetic chain boundary.
                head = batchEntries.EnumerateArray().First().GetProperty("previous_entry_hash").GetString();
            }

            return new CloudSeed
            {
                StoreId = Guid.Parse(batch.GetProperty("store_id").GetString()!),
                ActiveWriterDeviceId = Guid.Parse(cloud.GetProperty("active_writer_device_id").GetString()!),
                ActiveWriterEpoch = cloud.GetProperty("active_writer_epoch").GetInt64(),
                AcceptedThroughSeq = accepted,
                EntryCount = cloud.TryGetProperty("entry_count", out var rawCount) ? rawCount.GetInt64() : accepted,
                HeadEntryHash = head,
                Entries = entries,
                ProjectionVersion = given.TryGetProperty("projection_version", out var projection) ? projection.GetInt64() : accepted,
                Throttled = cloud.TryGetProperty("capacity", out var capacity) && capacity.GetString() == "THROTTLED"
            };
        }

        private static JsonElement LoadRoot()
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);
            while (directory is not null)
            {
                var candidate = Path.Combine(directory.FullName, "docs", "sync-protocol-test-vectors.json");
                if (File.Exists(candidate))
                {
                    return JsonDocument.Parse(File.ReadAllText(candidate)).RootElement.Clone();
                }

                directory = directory.Parent;
            }

            throw new FileNotFoundException("Could not locate docs/sync-protocol-test-vectors.json from the test output directory.");
        }
    }
}
