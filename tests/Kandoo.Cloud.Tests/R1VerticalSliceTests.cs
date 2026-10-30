extern alias Hub;

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Kandoo.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using Hub::Kandoo.Hub;

namespace Kandoo.Cloud.Tests;

[Collection("Postgres")]
public sealed class R1VerticalSliceTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 29, 8, 0, 0, TimeSpan.Zero);
    private readonly PostgresTestDatabase _database;
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "kandoo-r1", Guid.NewGuid().ToString("N"));
    private readonly R1Clock _clock = new() { UtcNow = T0 };
    private readonly Guid _writerDeviceId = Guid.NewGuid();
    private readonly SqliteHubStore _hubStore;
    private readonly string _connectionString;
    private readonly HubFactory _hubFactory;
    private readonly CloudFactory _cloudFactory;
    private readonly HttpClient _hub;
    private readonly HttpClient _cloud;

    public R1VerticalSliceTests(PostgresTestDatabase database)
    {
        _database = database;
        Directory.CreateDirectory(_directory);
        _connectionString = $"Data Source={Path.Combine(_directory, "hub.db")};Pooling=False";
        _hubStore = new SqliteHubStore(_connectionString, new HubWriterIdentity(_writerDeviceId, 7), _clock);
        _hubFactory = new HubFactory(_hubStore, _clock, _connectionString);
        _cloudFactory = new CloudFactory(database.Store);
        _hub = _hubFactory.CreateClient();
        _cloud = _cloudFactory.CreateClient();
    }

    [Fact]
    public async Task C15_R1_ProductDraftIssueLogReplicationSmsAndPublicInvoiceRunEndToEnd()
    {
        var slice = await CreateIssuedSlice();
        var batch = _hubStore.ReadBatch(slice.StoreId, 100, 1_048_576)!;
        Assert.Equal(4, batch.Entries!.Count);
        Assert.Equal(4, _hubStore.LoadOperationLog().Count(slice.StoreId));
        Assert.Equal(JsonValueKind.Null, slice.Draft.Body.Response.GetProperty("invoice").GetProperty("display_number").ValueKind);
        Assert.Equal(1, slice.Issue.Body.Response.GetProperty("invoice").GetProperty("display_number").GetInt64());

        _database.Store.Seed(CloudSeed(slice.StoreId, _writerDeviceId));
        var replication = new HubCloudReplicationClient(
            _hubStore,
            new HttpCloudReplicationTransport(_cloud),
            ReplicationOptions());
        var accepted = await replication.ReplicateAsync(slice.StoreId);
        Assert.Equal("ACCEPTED", accepted.Status);
        Assert.Equal(0, accepted.OutboxDepth);
        Assert.Equal(4, _database.Store.GetSnapshot(slice.StoreId)!.OperationIdCount);

        var token = _database.Store.GetPublicToken(slice.StoreId, slice.InvoiceId);
        Assert.NotNull(token);
        var provider = new DeterministicStubSmsProvider();
        var deliveries = new PostgresSmsDeliveryStore(_database.ConnectionString);
        var subscriptions = new R1Subscriptions(slice.StoreId);
        var sms = new SmsService(_database.Store, subscriptions, deliveries, provider, _clock);
        var request = new SmsSendRequest(
            slice.StoreId, "r1-sms-1", slice.InvoiceId, "+989120000101", $"https://cloud.test/i/{token}");
        var sent = await sms.SendAsync(request);
        Assert.Equal("SENT", sent.Status);
        Assert.Equal(1, provider.CaptureCount);
        Assert.Equal(1, deliveries.GetCounter(slice.StoreId)!.UsedCount);
        Assert.Equal(1, deliveries.GetCounter(slice.StoreId)!.SentCount);

        using var publicResponse = await _cloud.GetAsync($"/i/{token}?format=json");
        Assert.Equal(HttpStatusCode.OK, publicResponse.StatusCode);
        using var publicBody = JsonDocument.Parse(await publicResponse.Content.ReadAsStringAsync());
        Assert.Equal("R1 Store", publicBody.RootElement.GetProperty("store_name").GetString());
        Assert.Equal(1, publicBody.RootElement.GetProperty("display_number").GetInt64());
        Assert.Equal(slice.InvoiceId, publicBody.RootElement.GetProperty("digital_invoice").GetProperty("invoice_id").GetGuid());
        var persisted = _database.Store.GetPublicInvoice(token!);
        Assert.NotNull(persisted);
        var line = Assert.Single(persisted!.LineItems);
        Assert.Equal("R1 product", line.ProductName);
        Assert.Equal(25_000, persisted.PayableRials);
    }

    [Fact]
    public async Task C15_R1_RetriesCreateNoDuplicateOperationsCloudProjectionsOrSmsCharges()
    {
        var slice = await CreateIssuedSlice();
        var issueReplay = await Submit(
            slice.StoreId, slice.Device.Session, "r1-issue-1", "invoice.issue", new { invoice_id = slice.InvoiceId });
        Assert.Equal(HttpStatusCode.OK, issueReplay.StatusCode);
        Assert.True(issueReplay.Body.IdempotentReplay);
        Assert.Equal(slice.Issue.Body.OperationSeq, issueReplay.Body.OperationSeq);
        Assert.Equal(4, _hubStore.LoadOperationLog().Count(slice.StoreId));

        var batch = _hubStore.ReadBatch(slice.StoreId, 100, 1_048_576)!;
        _database.Store.Seed(CloudSeed(slice.StoreId, _writerDeviceId));
        var replication = new HubCloudReplicationClient(
            _hubStore, new HttpCloudReplicationTransport(_cloud), ReplicationOptions());
        Assert.Equal("ACCEPTED", (await replication.ReplicateAsync(slice.StoreId)).Status);
        var cloudBatch = JsonSerializer.Deserialize<OperationBatch>(
            JsonSerializer.Serialize(batch, HubJson.Options), CloudJson.Options)!;
        var duplicate = _database.Store.Ingest(slice.StoreId, cloudBatch, cloudBatch.BatchId);
        var ack = Assert.IsType<IngestAck>(duplicate.Body);
        Assert.Equal("DUPLICATE", ack.Status);
        Assert.Equal(0, ack.IngestedCount);
        Assert.Equal(4, _database.Store.GetSnapshot(slice.StoreId)!.OperationIdCount);

        var token = _database.Store.GetPublicToken(slice.StoreId, slice.InvoiceId);
        Assert.NotNull(token);
        var provider = new DeterministicStubSmsProvider();
        var deliveries = new PostgresSmsDeliveryStore(_database.ConnectionString);
        var sms = new SmsService(_database.Store, new R1Subscriptions(slice.StoreId), deliveries, provider, _clock);
        var request = new SmsSendRequest(
            slice.StoreId, "r1-sms-retry", slice.InvoiceId, "+989120000101", $"https://cloud.test/i/{token}");
        Assert.Equal("SENT", (await sms.SendAsync(request)).Status);
        var replay = await sms.SendAsync(request);
        Assert.Equal("DUPLICATE", replay.Status);
        Assert.True(replay.IdempotentReplay);
        Assert.Equal(1, provider.CaptureCount);
        Assert.Equal(1, deliveries.GetCounter(slice.StoreId)!.UsedCount);
        Assert.Equal(1, deliveries.GetCounter(slice.StoreId)!.SentCount);
        Assert.Equal(token, _database.Store.GetPublicToken(slice.StoreId, slice.InvoiceId));
    }

    [Fact]
    public async Task C24_OfflineIssueThenSyncReplayAndReissueStayDuplicateFree()
    {
        var slice = await CreateIssuedSlice();
        var issueReplay = await Submit(
            slice.StoreId, slice.Device.Session, "r1-issue-1", "invoice.issue", new { invoice_id = slice.InvoiceId });
        Assert.Equal(HttpStatusCode.OK, issueReplay.StatusCode);
        Assert.True(issueReplay.Body.IdempotentReplay);
        Assert.Equal(slice.Issue.Body.OperationSeq, issueReplay.Body.OperationSeq);
        Assert.Equal(1, issueReplay.Body.Response.GetProperty("invoice").GetProperty("display_number").GetInt64());
        Assert.Equal(4, _hubStore.LoadOperationLog().Count(slice.StoreId));
        Assert.Equal(4, _hubStore.GetOutboxDepth(slice.StoreId));

        var offline = new HubCloudReplicationClient(
            _hubStore, new OfflineTransport(), ReplicationOptions());
        var disconnected = await offline.ReplicateAsync(slice.StoreId);
        Assert.Equal("RETRY_REQUIRED", disconnected.Status);
        Assert.Equal("NETWORK_ERROR", disconnected.ErrorCode);
        Assert.Equal(4, disconnected.OutboxDepth);

        _database.Store.Seed(CloudSeed(slice.StoreId, _writerDeviceId));
        var recovered = new HubCloudReplicationClient(
            _hubStore,
            new LostFirstResponseTransport(new HttpCloudReplicationTransport(_cloud)),
            ReplicationOptions(attempts: 2));
        var replayed = await recovered.ReplicateAsync(slice.StoreId);
        Assert.Equal("DUPLICATE", replayed.Status);
        Assert.Equal(2, replayed.Attempts);
        Assert.Equal(0, replayed.OutboxDepth);
        Assert.Equal("IDLE", (await recovered.ReplicateAsync(slice.StoreId)).Status);

        var snapshot = _database.Store.GetSnapshot(slice.StoreId)!;
        Assert.Equal(4, snapshot.OperationIdCount);
        Assert.Equal(4, snapshot.EntryCount);
        var token = _database.Store.GetPublicToken(slice.StoreId, slice.InvoiceId);
        Assert.NotNull(token);
        var publicInvoice = _database.Store.GetPublicInvoice(token!);
        Assert.NotNull(publicInvoice);
        Assert.Equal(1, publicInvoice!.View.DisplayNumber);
        Assert.Single(publicInvoice.LineItems);
    }

    [Fact]
    public async Task C15_R1_StoreACannotReadStoreB()
    {
        var storeA = Guid.NewGuid();
        var storeB = Guid.NewGuid();
        var ownerA = await Owner(storeA, _writerDeviceId);
        _hubStore.IssueOwnerSession(storeB, Guid.NewGuid(), Guid.NewGuid(), "r1-store-b-owner", _clock.UtcNow, _clock.UtcNow.AddHours(1));

        var response = await Get(_hub, $"/v1/stores/{storeB:D}/devices", ownerA.Token);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("STORE_ACCESS_DENIED", response.Body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task C15_R1_SecondDeviceCannotOpenTheSamePhoneDraftWhileTheFirstIsActive()
    {
        var storeId = Guid.NewGuid();
        var owner = await Owner(storeId, _writerDeviceId);
        var first = await Pair(owner, storeId, Guid.NewGuid(), "First");
        var second = await Pair(owner, storeId, Guid.NewGuid(), "Second");
        var phone = "+989120000102";
        var draft = await Submit(storeId, first.Session, "r1-lock-draft-1", "invoice.draft_create", new { customer_phone = phone });
        Assert.Equal(HttpStatusCode.OK, draft.StatusCode);

        var conflict = await Submit(storeId, second.Session, "r1-lock-draft-2", "invoice.draft_create", new { customer_phone = phone });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal("DRAFT_LOCK_CONFLICT", conflict.Body.Status);
        Assert.Equal("REJECTED", conflict.Body.ResultStatus);
    }

    private async Task<IssuedSlice> CreateIssuedSlice()
    {
        var storeId = Guid.NewGuid();
        var owner = await Owner(storeId, _writerDeviceId);
        var device = await Pair(owner, storeId, Guid.NewGuid());
        var product = await Post(_hub, $"/v1/stores/{storeId:D}/products", new
        {
            operation_id = "r1-product-1",
            name = "R1 product",
            unit_price_rials = 12_500,
            initial_stock_millis = 10_000
        }, device.Session.Token);
        Assert.Equal(HttpStatusCode.OK, product.StatusCode);
        Assert.Equal("CREATED", product.Body.GetProperty("status").GetString());
        var productId = product.Body.GetProperty("product").GetProperty("product_id").GetString()!;

        var draft = await Submit(storeId, device.Session, "r1-draft-1", "invoice.draft_create", new
        {
            customer_phone = "+989120000101",
            store_name = "R1 Store"
        });
        Assert.Equal(HttpStatusCode.OK, draft.StatusCode);
        var invoiceId = draft.Body.Response.GetProperty("invoice").GetProperty("invoice_id").GetGuid();
        var added = await Submit(storeId, device.Session, "r1-item-1", "invoice.item_add", new
        {
            invoice_id = invoiceId,
            description = "R1 product",
            product_id = productId,
            quantity_millis = 2_000,
            unit_price_rials = 12_500,
            line_total_rials = 25_000
        });
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        var issue = await Submit(storeId, device.Session, "r1-issue-1", "invoice.issue", new { invoice_id = invoiceId });
        Assert.Equal(HttpStatusCode.OK, issue.StatusCode);
        return new IssuedSlice(storeId, device, invoiceId, draft, issue);
    }

    private async Task<TestSession> Owner(Guid storeId, Guid hubDeviceId)
    {
        var response = await Post(_hub, $"/v1/stores/{storeId:D}/owner-sessions", new
        {
            hub_device_id = hubDeviceId,
            owner_user_id = Guid.NewGuid(),
            bootstrap_proof = "r1-owner-proof"
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return new TestSession(
            response.Body.GetProperty("session_token").GetString()!,
            response.Body.GetProperty("session").GetProperty("session_id").GetString()!,
            hubDeviceId);
    }

    private async Task<PairedDevice> Pair(TestSession owner, Guid storeId, Guid deviceId, string label = "R1 Counter")
    {
        var grant = await Post(_hub, $"/v1/stores/{storeId:D}/pairing-grants", new
        {
            hub_device_id = owner.DeviceId,
            hub_endpoint = "https://hub.lan",
            requested_device_role = "COMPANION"
        }, owner.Token);
        Assert.Equal(HttpStatusCode.OK, grant.StatusCode);
        var qr = grant.Body.GetProperty("qr");
        var consumed = await Post(_hub, "/v1/pairing-grants/consume", new
        {
            pairing_id = qr.GetProperty("pairing_id").GetString(),
            nonce = qr.GetProperty("nonce").GetString(),
            device_id = deviceId,
            label,
            platform = "WINDOWS",
            device_public_key = "r1-test-public-key"
        });
        Assert.Equal(HttpStatusCode.OK, consumed.StatusCode);
        return new PairedDevice(new TestSession(
            consumed.Body.GetProperty("session_token").GetString()!,
            consumed.Body.GetProperty("session").GetProperty("session_id").GetString()!,
            deviceId));
    }

    private async Task<HubReply> Submit(
        Guid storeId,
        TestSession session,
        string operationId,
        string operationType,
        object payload)
    {
        var payloadElement = HubJson.Element(payload);
        var operation = new OperationRequest(storeId, session.DeviceId, operationId, operationType, null, payloadElement);
        var response = await Post(_hub, $"/v1/stores/{storeId:D}/operations", new
        {
            protocol_version = "0",
            session_id = session.SessionId,
            store_id = storeId,
            device_id = session.DeviceId,
            operation_id = operationId,
            operation_type = operationType,
            base_version = (long?)null,
            payload = payloadElement,
            request_hash = operation.RequestHash
        }, session.Token);
        var body = JsonSerializer.Deserialize<HubOperationReply>(response.Body.GetRawText(), HubJson.Options)!;
        return new HubReply(response.StatusCode, body);
    }

    private static CloudSeed CloudSeed(Guid storeId, Guid writerDeviceId) => new()
    {
        StoreId = storeId,
        ActiveWriterDeviceId = writerDeviceId,
        ActiveWriterEpoch = 7,
        AcceptedThroughSeq = 0,
        EntryCount = 0,
        ProjectionVersion = 0
    };

    private static HubReplicationOptions ReplicationOptions(int attempts = 1) => new()
    {
        MaxAttempts = attempts,
        DelayAsync = (_, _) => Task.CompletedTask
    };

    private static async Task<ApiResponse> Post(HttpClient client, string path, object body, string? token = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        request.Content = new StringContent(JsonSerializer.Serialize(body, HubJson.Options), Encoding.UTF8, "application/json");
        using var response = await client.SendAsync(request);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return new ApiResponse(response.StatusCode, document.RootElement.Clone());
    }

    private static async Task<ApiResponse> Get(HttpClient client, string path, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return new ApiResponse(response.StatusCode, document.RootElement.Clone());
    }

    public void Dispose()
    {
        _hub.Dispose();
        _cloud.Dispose();
        _hubFactory.Dispose();
        _cloudFactory.Dispose();
        _hubStore.Dispose();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    private sealed record TestSession(string Token, string SessionId, Guid DeviceId);
    private sealed record PairedDevice(TestSession Session);
    private sealed record ApiResponse(HttpStatusCode StatusCode, JsonElement Body);
    private sealed record HubReply(HttpStatusCode StatusCode, HubOperationReply Body);
    private sealed record IssuedSlice(Guid StoreId, PairedDevice Device, Guid InvoiceId, HubReply Draft, HubReply Issue);

    private sealed class R1Clock : IClock
    {
        public DateTimeOffset UtcNow { get; set; }
    }

    private sealed class OfflineTransport : ICloudReplicationTransport
    {
        public Task<CloudTransportResponse> SendAsync(ReplicationBatch batch, CancellationToken cancellationToken) =>
            throw new HttpRequestException("C-24 simulated WAN outage before Cloud receipt.");
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
                throw new HttpRequestException("C-24 simulated lost response after Cloud commit.");
            }

            return response;
        }
    }

    private sealed class R1Subscriptions : ISmsSubscriptionMetadata
    {
        private readonly SmsSubscriptionMetadata _value;
        public R1Subscriptions(Guid storeId) => _value = new SmsSubscriptionMetadata(storeId, true, 10, T0.AddDays(-1));
        public SmsSubscriptionMetadata? Get(Guid storeId) => storeId == _value.StoreId ? _value : null;
    }

    private sealed class HubFactory : WebApplicationFactory<Hub::Program>
    {
        private readonly SqliteHubStore _store;
        private readonly IClock _clock;
        private readonly string _connectionString;

        public HubFactory(SqliteHubStore store, IClock clock, string connectionString)
        {
            _store = store;
            _clock = clock;
            _connectionString = connectionString;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:HubSqlite"] = _connectionString,
                    ["Hub:OwnerBootstrapProof"] = "r1-owner-proof"
                }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IClock>();
                services.RemoveAll<IHubStore>();
                services.RemoveAll<IHubAccessStore>();
                services.RemoveAll<HubOperationService>();
                services.RemoveAll<HubProductCatalogService>();
                services.AddSingleton<IClock>(_clock);
                services.AddSingleton<IHubStore>(_store);
                services.AddSingleton<IHubAccessStore>(_store);
                services.AddSingleton<HubOperationService>();
                services.AddSingleton(new HubProductCatalogService(_store));
            });
        }
    }

    private sealed class CloudFactory : WebApplicationFactory<Program>
    {
        private readonly PostgresCloudStore _store;
        public CloudFactory(PostgresCloudStore store) => _store = store;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ICloudStore>();
                services.RemoveAll<IPublicInvoiceStore>();
                services.AddSingleton<ICloudStore>(_store);
                services.AddSingleton<IPublicInvoiceStore>(_store);
            });
        }
    }
}
