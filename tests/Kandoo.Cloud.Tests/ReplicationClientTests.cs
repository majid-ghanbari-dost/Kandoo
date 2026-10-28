extern alias Hub;

using System.Text.Json;
using Kandoo.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using Hub::Kandoo.Hub;

namespace Kandoo.Cloud.Tests;

[Collection("Postgres")]
public sealed class ReplicationClientTests : IDisposable
{
    private readonly PostgresTestDatabase _database;
    private readonly CloudFactory _factory;
    private readonly HttpClient _http;

    public ReplicationClientTests(PostgresTestDatabase database)
    {
        _database = database;
        _factory = new CloudFactory(database.Store);
        _http = _factory.CreateClient();
    }

    [Fact]
    public Task C10_CLOUD_001_ContiguousUploadIsAccepted() => RunVector("CLOUD-001");

    [Fact]
    public Task C10_CLOUD_002_DuplicateDeliveryChangesNothing() => RunVector("CLOUD-002");

    [Fact]
    public Task C10_CLOUD_003_GapIsReportedWithoutAcknowledgement() => RunVector("CLOUD-003");

    [Fact]
    public Task C10_CLOUD_004_ReorderedBatchStopsForReview() => RunVector("CLOUD-004");

    [Fact]
    public Task C10_CLOUD_005_LostResponseRetryIsDuplicateFree() => RunVector("CLOUD-005");

    [Fact]
    public Task C10_CLOUD_006_ThrottleRetainsOutboxAndLimitsNextBatch() => RunVector("CLOUD-006");

    [Fact]
    public Task C10_CLOUD_007_CorruptionIsNotOverwritten() => RunVector("CLOUD-007");

    [Fact]
    public Task C10_CLOUD_008_StaleWriterIsDurablyFenced() => RunVector("CLOUD-008");

    [Fact]
    public async Task C10_DurableHubLogSurvivesLostResponseAndStoreRecreation()
    {
        using var temporary = new TempDatabase();
        var storeId = Guid.NewGuid();
        var writer = new HubWriterIdentity(Guid.NewGuid(), 7);
        var clock = new ManualClock();
        _factory.Store.Seed(new CloudSeed
        {
            StoreId = storeId,
            ActiveWriterDeviceId = writer.DeviceId,
            ActiveWriterEpoch = writer.Epoch,
            AcceptedThroughSeq = 0,
            EntryCount = 0,
            ProjectionVersion = 0
        });

        string firstHead;
        using (var hub = new SqliteHubStore(temporary.ConnectionString, writer, clock))
        {
            hub.Execute(Request(storeId, "op-durable-1"), _ => Response());
            hub.Execute(Request(storeId, "op-durable-2"), _ => Response());
            var durable = hub.ReadBatch(storeId, 100, 65_536)!;
            Assert.Equal(2, durable.Entries!.Count);
            Assert.Equal(HubReplicationConstants.ZeroHash, durable.Entries[0].PreviousEntryHash);
            Assert.Equal(durable.Entries[0].EntryHash, durable.Entries[1].PreviousEntryHash);
            foreach (var entry in durable.Entries)
            {
                Assert.Equal(
                    CanonicalJson.EntryHash(JsonSerializer.SerializeToElement(entry, HubJson.Options)),
                    entry.EntryHash);
            }

            firstHead = durable.Entries[^1].EntryHash;
            var lostTransport = new LostFirstResponseTransport(new HttpCloudReplicationTransport(_http));
            var client = new HubCloudReplicationClient(hub, lostTransport, Options(1));
            var lost = await client.ReplicateAsync(storeId);
            Assert.Equal("RETRY_REQUIRED", lost.Status);
            Assert.Equal(2, lost.OutboxDepth);
            Assert.Equal(2, _factory.Store.GetSnapshot(storeId)!.OperationIdCount);
        }

        using (var reopened = new SqliteHubStore(temporary.ConnectionString, writer, clock))
        {
            var client = new HubCloudReplicationClient(reopened, new HttpCloudReplicationTransport(_http), Options(1));
            var replay = await client.ReplicateAsync(storeId);
            Assert.Equal("DUPLICATE", replay.Status);
            Assert.Equal(2L, replay.AcceptedThroughSeq);
            Assert.Equal(0, replay.OutboxDepth);
            Assert.Equal(2, _factory.Store.GetSnapshot(storeId)!.OperationIdCount);
            Assert.Equal(firstHead, _factory.Store.GetSnapshot(storeId)!.HeadEntryHash);
            Assert.Equal("IDLE", (await client.ReplicateAsync(storeId)).Status);
            reopened.MarkWriterFenced(storeId, writer, "WRITER_FENCED");
        }

        using (var fenced = new SqliteHubStore(temporary.ConnectionString, writer, clock))
        {
            Assert.True(fenced.IsWriterFenced(storeId));
            Assert.Throws<HubWriterFencedException>(() => fenced.Execute(Request(storeId, "op-after-fence"), _ => Response()));
        }
    }

    private async Task RunVector(string vectorId)
    {
        var vector = VectorFile.Get(vectorId);
        var batchElement = VectorFile.FirstBatch(vector);
        var batch = JsonSerializer.Deserialize<ReplicationBatch>(batchElement.GetRawText(), HubJson.Options)!;
        var seed = VectorFile.Seed(vector, batchElement);
        _factory.Store.Seed(seed);
        var depth = vectorId == "CLOUD-006" ? 5 : vectorId == "CLOUD-008" ? 2 : batch.Entries?.Count ?? 0;
        var source = new VectorHubSource(batch, depth);
        ICloudReplicationTransport transport = new HttpCloudReplicationTransport(_http);
        if (vectorId == "CLOUD-005")
        {
            transport = new LostFirstResponseTransport(transport);
        }

        var client = new HubCloudReplicationClient(source, transport, Options(vectorId is "CLOUD-005" or "CLOUD-006" ? 2 : 1));
        var result = await client.ReplicateAsync(seed.StoreId);
        var snapshot = _factory.Store.GetSnapshot(seed.StoreId)!;
        switch (vectorId)
        {
            case "CLOUD-001":
                Assert.Equal("ACCEPTED", result.Status);
                Assert.Equal(42L, result.AcceptedThroughSeq);
                Assert.Equal(0, result.OutboxDepth);
                Assert.Equal(42L, snapshot.EntryCount);
                break;
            case "CLOUD-002":
                Assert.Equal("DUPLICATE", result.Status);
                Assert.Equal(42L, result.AcceptedThroughSeq);
                Assert.Equal(0, result.OutboxDepth);
                Assert.Equal(2, snapshot.OperationIdCount);
                Assert.Equal(42L, snapshot.ProjectionVersion);
                break;
            case "CLOUD-003":
                Assert.Equal("GAP_DETECTED", result.Status);
                Assert.Equal(41L, result.RetryFromSequence);
                Assert.Equal(40L, result.AcceptedThroughSeq);
                Assert.Equal(1, result.OutboxDepth);
                Assert.Equal(40L, snapshot.AcceptedThroughSeq);
                break;
            case "CLOUD-004":
                Assert.Equal("SEQUENCE_ORDER_VIOLATION", result.Status);
                Assert.Equal(2, result.OutboxDepth);
                Assert.Equal(40L, snapshot.ProjectionVersion);
                break;
            case "CLOUD-005":
                Assert.Equal("DUPLICATE", result.Status);
                Assert.Equal(2, result.Attempts);
                Assert.Equal(0, result.OutboxDepth);
                Assert.Equal(2, snapshot.OperationIdCount);
                Assert.Equal(42L, snapshot.EntryCount);
                break;
            case "CLOUD-006":
                Assert.Equal("THROTTLED", result.Status);
                Assert.Equal(2, result.Attempts);
                Assert.Equal(2_000, result.RetryAfterMs);
                Assert.Equal(2, result.MaxBatchEntries);
                Assert.Equal(65_536, result.MaxBatchBytes);
                Assert.Equal(5, result.OutboxDepth);
                Assert.Equal(40L, snapshot.AcceptedThroughSeq);
                break;
            case "CLOUD-007":
                Assert.Equal("ENTRY_HASH_MISMATCH", result.Status);
                Assert.Equal(1, result.OutboxDepth);
                Assert.Equal(40L, snapshot.EntryCount);
                break;
            case "CLOUD-008":
                Assert.Equal("WRITER_FENCED", result.Status);
                Assert.Equal("FENCED_READ_ONLY", result.Mode);
                Assert.True(source.IsWriterFenced(seed.StoreId));
                Assert.Equal(2, result.OutboxDepth);
                Assert.Equal(4L, snapshot.ActiveWriterEpoch);
                break;
            default:
                throw new InvalidOperationException(vectorId);
        }
    }

    private static HubReplicationOptions Options(int attempts) => new()
    {
        MaxAttempts = attempts,
        DelayAsync = (_, _) => Task.CompletedTask
    };

    private static OperationRequest Request(Guid storeId, string operationId) => new(
        storeId,
        Guid.Parse("33333333-3333-4333-8333-333333333333"),
        operationId,
        "product.test",
        null,
        JsonSerializer.SerializeToElement(new { }));

    private static string Response() => JsonSerializer.Serialize(new
    {
        status = "SUCCEEDED",
        result_status = "SUCCEEDED",
        response = new { }
    });

    public void Dispose()
    {
        _http.Dispose();
        _factory.Dispose();
    }

    private sealed class VectorHubSource : IHubReplicationSource
    {
        private readonly ReplicationBatch _batch;
        private int _depth;
        private bool _acknowledged;
        private bool _fenced;

        public VectorHubSource(ReplicationBatch batch, int depth)
        {
            _batch = batch;
            _depth = depth;
        }

        public ReplicationBatch? ReadBatch(Guid storeId, int maxEntries, int maxBytes) => _acknowledged ? null : _batch;
        public int GetOutboxDepth(Guid storeId) => _depth;
        public bool IsWriterFenced(Guid storeId) => _fenced;
        public void MarkWriterFenced(Guid storeId, HubWriterIdentity writer, string reason) => _fenced = true;

        public void Acknowledge(Guid storeId, long throughSequence, string headEntryHash, DateTimeOffset acknowledgedAt)
        {
            var last = _batch.Entries![^1];
            Assert.Equal(last.OperationSeq, throughSequence);
            Assert.Equal(last.EntryHash, headEntryHash);
            _acknowledged = true;
            _depth = 0;
        }
    }

    private sealed class LostFirstResponseTransport : ICloudReplicationTransport
    {
        private readonly ICloudReplicationTransport _inner;
        private int _calls;

        public LostFirstResponseTransport(ICloudReplicationTransport inner) => _inner = inner;

        public async Task<CloudTransportResponse> SendAsync(ReplicationBatch batch, CancellationToken cancellationToken)
        {
            var response = await _inner.SendAsync(batch, cancellationToken);
            _calls++;
            if (_calls == 1)
            {
                throw new HttpRequestException("Simulated lost response after Cloud commit.");
            }

            return response;
        }
    }

    private sealed class ManualClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 10, 25, 9, 0, 0, TimeSpan.Zero);
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
                services.RemoveAll<IPublicInvoiceStore>();
                services.AddSingleton<ICloudStore>(Store);
                services.AddSingleton<IPublicInvoiceStore>(Store);
            });
        }
    }

    private sealed class TempDatabase : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "kandoo-c10-tests", Guid.NewGuid().ToString("N"));
        public TempDatabase()
        {
            Directory.CreateDirectory(_directory);
            ConnectionString = $"Data Source={Path.Combine(_directory, "hub.db")};Pooling=False";
        }

        public string ConnectionString { get; }
        public void Dispose() => Directory.Delete(_directory, true);
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

            throw new FileNotFoundException("Could not locate protocol vectors.");
        }
    }
}
