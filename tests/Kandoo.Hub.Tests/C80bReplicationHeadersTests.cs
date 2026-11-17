using System.Net;
using System.Text.Json;
using Kandoo.Hub;
using Xunit;

namespace Kandoo.Hub.Tests;

/// <summary>
/// C-80b: Replication request headers align with the frozen Cloud contract.
///
/// The Cloud contract requires DeviceBearer auth on ingestOperationBatch,
/// which is Authorization: Bearer <device-secret>. The Hub transport must
/// send this header on every replication request. Cloud-side auth is untouched.
/// v1.1 fields (payload_version, currency, invoice_payloads) are included
/// when present on the batch.
/// </summary>
public sealed class C80bReplicationHeadersTests : IDisposable
{
    private readonly string _directory;
    private readonly SqliteHubStore _store;
    private readonly Guid _storeId = Guid.Parse("01010101-0101-0101-0101-010101010101");
    private readonly Guid _deviceId = Guid.Parse("02020202-0202-0202-0202-020202020202");

    public C80bReplicationHeadersTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "kandoo-c80b-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        var connectionString = $"Data Source={Path.Combine(_directory, "hub.db")};Pooling=False";
        _store = new SqliteHubStore(connectionString);
    }

    public void Dispose()
    {
        _store.Dispose();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (DirectoryNotFoundException) { }
        catch (IOException) { }
    }

    [Fact]
    public async Task C80b_RealTransportSendsAuthorizationBearerWithDeviceSecret()
    {
        var captured = new CapturingHandler();
        using var client = new HttpClient(captured);
        var transport = new HttpCloudReplicationTransport(client);

        var batch = new ReplicationBatch
        {
            BatchId = "batch-001",
            StoreId = _storeId,
            WriterDeviceId = _deviceId,
            WriterEpoch = 1,
            DeviceSecret = "dev-secret-abc123",
            Entries = new List<ReplicationLogEntry>
            {
                new()
                {
                    StoreId = _storeId,
                    WriterDeviceId = _deviceId,
                    WriterEpoch = 1,
                    OperationSeq = 1,
                    PreviousEntryHash = new string('0', 64),
                    EntryHash = new string('a', 64),
                    OperationId = "op-1",
                    DeviceId = _deviceId,
                    OperationType = "invoice.issue",
                    Payload = HubJson.Element(new { invoice_id = Guid.NewGuid(), service_type = "SALE" }),
                    Outcome = HubJson.Element(new {}),
                    ResponseSnapshot = HubJson.Element(new {}),
                    HubRecordedAt = "2026-11-16T00:00:00Z"
                }
            }
        };

        _ = await transport.SendAsync(batch, CancellationToken.None);

        var request = Assert.NotNull(captured.LastRequest);
        Assert.True(request.Headers.Contains("Authorization"),
            "Expected Authorization header on replication request.");
        var auth = request.Headers.GetValues("Authorization").First();
        Assert.Equal("Bearer dev-secret-abc123", auth);
        Assert.True(request.Headers.Contains("Idempotency-Key"));
        Assert.Equal("batch-001", request.Headers.GetValues("Idempotency-Key").First());
    }

    [Fact]
    public async Task C80b_RealTransportOmitsAuthorizationWhenDeviceSecretMissing()
    {
        var captured = new CapturingHandler();
        using var client = new HttpClient(captured);
        var transport = new HttpCloudReplicationTransport(client);

        var batch = new ReplicationBatch
        {
            BatchId = "batch-002",
            StoreId = _storeId,
            WriterDeviceId = _deviceId,
            WriterEpoch = 1,
            DeviceSecret = null,
            Entries = new List<ReplicationLogEntry>
            {
                new()
                {
                    StoreId = _storeId,
                    WriterDeviceId = _deviceId,
                    WriterEpoch = 1,
                    OperationSeq = 1,
                    PreviousEntryHash = new string('0', 64),
                    EntryHash = new string('a', 64),
                    OperationId = "op-2",
                    DeviceId = _deviceId,
                    OperationType = "invoice.issue",
                    Payload = HubJson.Element(new { invoice_id = Guid.NewGuid(), service_type = "SALE" }),
                    Outcome = HubJson.Element(new {}),
                    ResponseSnapshot = HubJson.Element(new {}),
                    HubRecordedAt = "2026-11-16T00:00:00Z"
                }
            }
        };

        _ = await transport.SendAsync(batch, CancellationToken.None);

        var request = Assert.NotNull(captured.LastRequest);
        Assert.False(request.Headers.Contains("Authorization"),
            "Authorization header must not be sent when device secret is absent.");
        Assert.True(request.Headers.Contains("Idempotency-Key"));
    }

    [Fact]
    public async Task C80b_RealTransportSendsV11FieldsWhenPresent()
    {
        var captured = new CapturingHandler();
        using var client = new HttpClient(captured);
        var transport = new HttpCloudReplicationTransport(client);

        var batch = new ReplicationBatch
        {
            BatchId = "batch-v11",
            StoreId = _storeId,
            WriterDeviceId = _deviceId,
            WriterEpoch = 1,
            DeviceSecret = "dev-secret-v11",
            PayloadVersion = "1.1",
            Currency = "toman",
            InvoicePayloads = new List<HubInvoiceReplicationPayloadV11>
            {
                new("op-v11")
                {
                    CustomerName = "مشتری نمونه",
                    Payments = new List<HubReplicationPaymentV11>
                    {
                        new("card-reader", 1000L)
                    },
                    RemainingBalance = 5000L
                }
            },
            Entries = new List<ReplicationLogEntry>
            {
                new()
                {
                    StoreId = _storeId,
                    WriterDeviceId = _deviceId,
                    WriterEpoch = 1,
                    OperationSeq = 1,
                    PreviousEntryHash = new string('0', 64),
                    EntryHash = new string('a', 64),
                    OperationId = "op-v11",
                    DeviceId = _deviceId,
                    OperationType = "invoice.issue",
                    Payload = HubJson.Element(new { invoice_id = Guid.NewGuid(), service_type = "SALE" }),
                    Outcome = HubJson.Element(new {}),
                    ResponseSnapshot = HubJson.Element(new {}),
                    HubRecordedAt = "2026-11-16T00:00:00Z"
                }
            }
        };

        _ = await transport.SendAsync(batch, CancellationToken.None);

        var request = Assert.NotNull(captured.LastRequest);
        var content = await request.Content!.ReadAsStringAsync();
        using var document = JsonDocument.Parse(content);
        var root = document.RootElement;

        Assert.Equal("1.1", root.GetProperty("payload_version").GetString());
        Assert.Equal("toman", root.GetProperty("currency").GetString());
        var invoices = root.GetProperty("invoice_payloads").EnumerateArray().ToArray();
        Assert.Single(invoices);
        Assert.Equal("مشتری نمونه", invoices[0].GetProperty("customer_name").GetString());
        Assert.Equal(5000L, invoices[0].GetProperty("remaining_balance").GetInt64());
    }

    private sealed class CapturingHandler : DelegatingHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"status\":\"ACCEPTED\",\"accepted_through_seq\":1,\"head_entry_hash\":\"x\"}",
                    System.Text.Encoding.UTF8,
                    "application/json")
            });
        }
    }
}
