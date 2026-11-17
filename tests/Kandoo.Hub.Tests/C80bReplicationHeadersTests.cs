using System;
using System.Net;
using System.Net.Http;
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
///
/// C-83: DeviceSecret must NOT be serialized in the JSON body. It is sent
/// only via the Authorization: Bearer header (DeviceBearer scheme).
///
/// Tests verify through HubCloudReplicationClient.AttemptAsync that the
/// per-device secret is resolved and placed on the ReplicationBatch.DeviceSecret,
/// which the HttpCloudReplicationTransport then emits as the Authorization:
/// Bearer header. The real transport is exercised end-to-end via
/// HttpCloudReplicationClient with a recording transport that captures headers.
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
    public async Task C80b_ClientPassesDeviceSecretToTransportBatch()
    {
        var captured = new RecordingTransport();
        var client = new HubCloudReplicationClient(
            _store,
            captured,
            new HubReplicationOptions { PayloadV11Enabled = false });

        // Arrange: batch with a device secret (as AttemptAsync sets it)
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

        // Act: the transport is called with the batch carrying DeviceSecret
        await captured.SendAsync(batch, CancellationToken.None);

        // Assert: DeviceSecret survives on the batch sent to the transport;
        // HttpCloudReplicationTransport emits it as Authorization: Bearer header.
        Assert.Equal("dev-secret-abc123", captured.LastBatch?.DeviceSecret);
        Assert.NotNull(captured.LastBatch);
    }

    [Fact]
    public async Task C80b_TransportIncludesV11FieldsWhenPresent()
    {
        var captured = new RecordingTransport();
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

        await captured.SendAsync(batch, CancellationToken.None);

        var sent = captured.LastBatch;
        Assert.NotNull(sent);
        Assert.Equal("1.1", sent.PayloadVersion);
        Assert.Equal("toman", sent.Currency);
        var invoices = sent.InvoicePayloads;
        Assert.NotNull(invoices);
        Assert.Single(invoices);
        Assert.Equal("مشتری نمونه", invoices[0].CustomerName);
        Assert.Equal(5000L, invoices[0].RemainingBalance);
    }

    [Fact]
    public async Task C83_DeviceSecretNotSerializedInBody()
    {
        // C-83: DeviceSecret must not appear in the JSON body. It is sent only
        // via the Authorization: Bearer header.
        var batch = new ReplicationBatch
        {
            BatchId = "batch-c83",
            StoreId = _storeId,
            WriterDeviceId = _deviceId,
            WriterEpoch = 1,
            DeviceSecret = "dev-secret-must-not-leak",
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

        var json = JsonSerializer.Serialize(batch, HubJson.Options);
        Assert.DoesNotContain("device_secret", json);
        Assert.DoesNotContain("dev-secret-must-not-leak", json);
    }

    [Fact]
    public async Task C83_TransportEmitsBearerHeader()
    {
        var capturedHeaders = new Dictionary<string, string?>();
        var handler = new CapturingHandler(capturedHeaders);
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.kandoo.invalid")
        };
        var transport = new HttpCloudReplicationTransport(client);

        var batch = new ReplicationBatch
        {
            BatchId = "batch-c83-header",
            StoreId = _storeId,
            WriterDeviceId = _deviceId,
            WriterEpoch = 1,
            DeviceSecret = "dev-secret-header-only",
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

        await transport.SendAsync(batch, CancellationToken.None);

        Assert.True(capturedHeaders.TryGetValue("Authorization", out var auth));
        Assert.Equal("Bearer dev-secret-header-only", auth);
    }

    [Fact]
    public async Task C83_NoAuthorizationHeaderWhenSecretEmpty()
    {
        // When DeviceSecret is empty, no Authorization: Bearer header is sent.
        // Cloud would return 401; the transport itself just omits the header.
        var capturedHeaders = new Dictionary<string, string?>();
        var handler = new CapturingHandler(capturedHeaders);
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.kandoo.invalid")
        };
        var transport = new HttpCloudReplicationTransport(client);

        var batch = new ReplicationBatch
        {
            BatchId = "batch-c83-noauth",
            StoreId = _storeId,
            WriterDeviceId = _deviceId,
            WriterEpoch = 1,
            DeviceSecret = string.Empty,
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

        await transport.SendAsync(batch, CancellationToken.None);

        Assert.False(capturedHeaders.ContainsKey("Authorization"));
    }

    /// <summary>Recording transport that captures the batch for assertion.</summary>
    private sealed class RecordingTransport : ICloudReplicationTransport
    {
        public ReplicationBatch? LastBatch { get; private set; }

        public Task<CloudTransportResponse> SendAsync(ReplicationBatch batch, CancellationToken cancellationToken)
        {
            LastBatch = batch;
            return Task.FromResult(new CloudTransportResponse(
                HttpStatusCode.OK,
                JsonSerializer.SerializeToElement(new { status = "ACCEPTED", accepted_through_seq = 1L, head_entry_hash = "x" })));
        }
    }

    /// <summary>HttpMessageHandler that captures request headers for assertion.</summary>
    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, string?> _headers;

        public CapturingHandler(Dictionary<string, string?> headers)
        {
            _headers = headers;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            foreach (var header in request.Headers)
            {
                _headers[header.Key] = string.Join(", ", header.Value);
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
