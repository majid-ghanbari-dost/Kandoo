using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Kandoo.Core;
using Kandoo.Hub;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Kandoo.Hub.Tests;

public sealed class ManualHubClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 10, 22, 8, 0, 0, TimeSpan.Zero);
}

public sealed class ManualHubTimeProvider : TimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 10, 22, 8, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => UtcNow;
}

public sealed class HubApiFactory : WebApplicationFactory<Program>
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "kandoo-hub-api", Guid.NewGuid().ToString("N"));

    public HubApiFactory()
    {
        Directory.CreateDirectory(_directory);
        ConnectionString = $"Data Source={Path.Combine(_directory, "hub.db")};Pooling=False";
        TestStore = new SqliteHubStore(ConnectionString);
    }

    public string ConnectionString { get; }
    public ManualHubClock Clock { get; } = new();
    public ManualHubTimeProvider StatsTime { get; } = new();
    public SqliteHubStore TestStore { get; }
    public SqliteHubStore Store => TestStore;
    public DeterministicHubSmsGateway TestSms { get; } = new();
    public IHubStatusProbe? TestStatus { get; set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:HubSqlite"] = ConnectionString,
                ["Hub:OwnerBootstrapProof"] = "test-only-owner-bootstrap"
            }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IClock>();
            services.AddSingleton<IClock>(Clock);
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(StatsTime);
            // Replace both interfaces and the operation service together. The
            // top-level default store must never be shared by parallel vectors.
            services.RemoveAll<IHubStore>();
            services.RemoveAll<IHubAccessStore>();
            services.RemoveAll<HubOperationService>();
            services.RemoveAll<HubProductCatalogService>();
            services.RemoveAll<ModeBReviewService>();
            services.RemoveAll<IHubSmsDeliveryGateway>();
            if (TestStatus is not null)
            {
                services.RemoveAll<IHubStatusProbe>();
                services.AddSingleton(TestStatus);
            }

            services.AddSingleton<IHubStore>(TestStore);
            services.AddSingleton<IHubAccessStore>(TestStore);
            services.AddSingleton<IHubSmsDeliveryGateway>(TestSms);
            services.AddSingleton<HubOperationService>();
            services.AddSingleton(new HubProductCatalogService(TestStore));
            services.AddSingleton(provider => new ModeBReviewService(
                TestStore,
                provider.GetRequiredService<HubOperationService>(),
                provider.GetRequiredService<IClock>()));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            TestStore.Dispose();
            try
            {
                Directory.Delete(_directory, recursive: true);
            }
            catch (DirectoryNotFoundException)
            {
                // Best-effort test cleanup only.
            }
        }
    }
}

public sealed class DeterministicHubSmsGateway : IHubSmsDeliveryGateway
{
    private readonly object _gate = new();
    private readonly Dictionary<(Guid StoreId, string OperationId), Entry> _entries = new();

    public int CaptureCount { get; private set; }

    public void Seed(Guid storeId, Guid invoiceId, string operationId)
    {
        lock (_gate)
        {
            _entries[(storeId, operationId)] = new Entry(invoiceId, operationId, "stub-original", DateTimeOffset.UnixEpoch);
        }
    }

    public Task<HubSmsGatewayResult> ResendAsync(
        Guid storeId,
        Guid invoiceId,
        string operationId,
        string originalOperationId,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue((storeId, originalOperationId), out var original) || original.InvoiceId != invoiceId)
            {
                return Task.FromResult(new HubSmsGatewayResult("SMS_DELIVERY_NOT_FOUND"));
            }

            if (_entries.TryGetValue((storeId, operationId), out var existing))
            {
                return Task.FromResult(existing.InvoiceId == invoiceId && existing.OriginalOperationId == originalOperationId
                    ? Result(storeId, operationId, existing, "DUPLICATE", true)
                    : new HubSmsGatewayResult("OPERATION_ID_CONFLICT"));
            }

            CaptureCount++;
            var entry = new Entry(invoiceId, originalOperationId, $"stub-resend-{CaptureCount}", DateTimeOffset.UnixEpoch.AddSeconds(CaptureCount));
            _entries[(storeId, operationId)] = entry;
            return Task.FromResult(Result(storeId, operationId, entry, "SENT"));
        }
    }

    public Task<HubSmsGatewayResult?> GetStatusAsync(
        Guid storeId,
        Guid invoiceId,
        string operationId,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult<HubSmsGatewayResult?>(
                _entries.TryGetValue((storeId, operationId), out var entry) && entry.InvoiceId == invoiceId
                    ? Result(storeId, operationId, entry, "SENT")
                    : null);
        }
    }

    private static HubSmsGatewayResult Result(Guid storeId, string operationId, Entry entry, string status, bool replay = false) =>
        new(status, operationId, entry.InvoiceId, replay, entry.ProviderMessageId, entry.CreatedAt, entry.CreatedAt);

    private sealed record Entry(Guid InvoiceId, string OriginalOperationId, string ProviderMessageId, DateTimeOffset CreatedAt);
}

public sealed record ApiResponse(HttpStatusCode StatusCode, JsonElement Body);
public sealed record TestSession(string Token, string SessionId, Guid DeviceId, Guid? OwnerUserId);
public sealed record PairedDevice(
    TestSession Session,
    string DeviceCredential,
    string PairingId,
    string Nonce,
    JsonElement Grant,
    JsonElement Consume);

public sealed class HubApiClient : IDisposable
{
    private readonly HttpClient _client;

    public HubApiClient(HubApiFactory factory)
    {
        Factory = factory;
        _client = factory.CreateClient();
    }

    public HubApiFactory Factory { get; }
    public ManualHubClock Clock => Factory.Clock;
    public SqliteHubStore Store => Factory.Store;

    public async Task<TestSession> Owner(Guid storeId, Guid hubDeviceId, Guid ownerUserId)
    {
        var response = await Post($"/v1/stores/{store_id(storeId)}/owner-sessions", new
        {
            hub_device_id = hubDeviceId,
            owner_user_id = ownerUserId,
            bootstrap_proof = "test-only-owner-bootstrap"
        });
        Assert.Equal("SESSION_ACTIVE", response.Body.GetProperty("status").GetString());
        return new TestSession(
            response.Body.GetProperty("session_token").GetString()!,
            response.Body.GetProperty("session").GetProperty("session_id").GetString()!,
            hubDeviceId,
            ownerUserId);
    }

    public Task<ApiResponse> IssueGrant(TestSession owner, Guid storeId, string role = "COMPANION", string? level = null) =>
        Post($"/v1/stores/{store_id(storeId)}/pairing-grants", new
        {
            hub_device_id = owner.DeviceId,
            hub_endpoint = "https://hub.lan",
            requested_device_role = role,
            requested_level = level
        }, owner.Token);

    public async Task<PairedDevice> Pair(TestSession owner, Guid storeId, Guid deviceId, string label = "Counter")
    {
        var grant = await IssueGrant(owner, storeId);
        Assert.Equal("PAIRING_QR_ISSUED", grant.Body.GetProperty("status").GetString());
        var qr = grant.Body.GetProperty("qr");
        var pairingId = qr.GetProperty("pairing_id").GetString()!;
        var nonce = qr.GetProperty("nonce").GetString()!;
        var consume = await Consume(pairingId, nonce, deviceId, label);
        Assert.Equal("DEVICE_PAIRED", consume.Body.GetProperty("status").GetString());
        var session = new TestSession(
            consume.Body.GetProperty("session_token").GetString()!,
            consume.Body.GetProperty("session").GetProperty("session_id").GetString()!,
            deviceId,
            null);
        return new PairedDevice(session, consume.Body.GetProperty("device_credential").GetString()!, pairingId, nonce, qr, consume.Body);
    }

    public Task<ApiResponse> Consume(
        string pairingId, string nonce, Guid deviceId, string label = "Counter", string platform = "WINDOWS") =>
        Post("/v1/pairing-grants/consume", new
        {
            pairing_id = pairingId,
            nonce,
            device_id = deviceId,
            label,
            platform,
            device_public_key = "test-only-public-key"
        });

    public Task<ApiResponse> SubmitRequest(JsonElement request, TestSession session)
    {
        var body = new Dictionary<string, object?>
        {
            ["protocol_version"] = "0",
            ["session_id"] = session.SessionId,
            ["store_id"] = request.GetProperty("store_id").Clone(),
            ["device_id"] = request.GetProperty("device_id").Clone(),
            ["operation_id"] = request.GetProperty("operation_id").Clone(),
            ["operation_type"] = request.GetProperty("operation_type").Clone(),
            ["base_version"] = request.GetProperty("base_version").Clone(),
            ["payload"] = request.GetProperty("payload").Clone(),
            ["request_hash"] = request.GetProperty("request_hash").Clone()
        };
        var storeId = request.GetProperty("store_id").GetGuid();
        return Post($"/v1/stores/{store_id(storeId)}/operations", body, session.Token);
    }

    public Task<ApiResponse> SubmitNew(
        Guid storeId,
        TestSession session,
        string operationId,
        string operationType,
        object payload)
    {
        var payloadElement = HubJson.Element(payload);
        var domain = new OperationRequest(storeId, session.DeviceId, operationId, operationType, null, payloadElement);
        return Post($"/v1/stores/{store_id(storeId)}/operations", new
        {
            protocol_version = "0",
            session_id = session.SessionId,
            store_id = storeId,
            device_id = session.DeviceId,
            operation_id = operationId,
            operation_type = operationType,
            base_version = (long?)null,
            payload = payloadElement,
            request_hash = domain.RequestHash
        }, session.Token);
    }

    public async Task<ApiResponse> Post(string path, object? body, string? token = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        request.Content = new StringContent(JsonSerializer.Serialize(body, HubJson.Options), Encoding.UTF8, "application/json");
        using var response = await _client.SendAsync(request);
        return await Read(response);
    }

    public async Task<ApiResponse> Patch(string path, object? body, string? token = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, path);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        request.Content = new StringContent(JsonSerializer.Serialize(body, HubJson.Options), Encoding.UTF8, "application/json");
        using var response = await _client.SendAsync(request);
        return await Read(response);
    }

    public async Task<ApiResponse> Get(string path, string? token = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        using var response = await _client.SendAsync(request);
        return await Read(response);
    }

    public void SeedOperations(Guid storeId, Guid deviceId, int count)
    {
        for (var index = 0; index < count; index++)
        {
            var payload = HubJson.Element(new { index });
            var request = new OperationRequest(storeId, deviceId, $"seed-{index:D4}", "test.seed", null, payload);
            var result = Store.Execute(request, _ => "{}");
            Assert.Equal(OperationExecutionStatus.Succeeded, result.Status);
        }
    }

    public void Dispose() => _client.Dispose();

    private static async Task<ApiResponse> Read(HttpResponseMessage response)
    {
        var raw = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(raw);
        return new ApiResponse(response.StatusCode, document.RootElement.Clone());
    }

    private static string store_id(Guid value) => value.ToString("D");
}

public static class VectorFile
{
    private static readonly Lazy<JsonDocument> Document = new(() => JsonDocument.Parse(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-protocol-test-vectors.json"))));

    public static JsonElement Get(string id) => Document.Value.RootElement.GetProperty("vectors")
        .EnumerateArray()
        .Single(vector => vector.GetProperty("id").GetString() == id)
        .Clone();

    public static JsonElement Step(JsonElement vector, int index = 0) =>
        vector.GetProperty("input").GetProperty("steps")[index];

    public static JsonElement Request(JsonElement vector, int index = 0) => Step(vector, index).GetProperty("request");
}
