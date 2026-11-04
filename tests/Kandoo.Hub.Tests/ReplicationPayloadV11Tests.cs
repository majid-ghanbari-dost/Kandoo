using System.Text.Json;
using Xunit;

namespace Kandoo.Hub.Tests;

public sealed class ReplicationPayloadV11Tests
{
    private const string Store = "11111111-1111-1111-1111-111111111111";
    private const string Writer = "22222222-2222-2222-2222-222222222222";
    private const string Device = "33333333-3333-3333-3333-333333333333";
    private const string Invoice = "44444444-4444-4444-4444-444444444444";
    private const string OffGolden = "{\"batch_id\":\"batch-fixed\",\"store_id\":\"11111111-1111-1111-1111-111111111111\",\"writer_device_id\":\"22222222-2222-2222-2222-222222222222\",\"writer_epoch\":1,\"entries\":[{\"protocol_version\":\"0\",\"store_id\":\"11111111-1111-1111-1111-111111111111\",\"writer_device_id\":\"22222222-2222-2222-2222-222222222222\",\"writer_epoch\":1,\"operation_seq\":1,\"previous_entry_hash\":\"0000000000000000000000000000000000000000000000000000000000000000\",\"entry_hash\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"operation_id\":\"op-fixed\",\"device_id\":\"33333333-3333-3333-3333-333333333333\",\"operation_type\":\"invoice.issue\",\"base_version\":null,\"request_hash\":\"request-fixed\",\"payload\":{\"invoice_id\":\"44444444-4444-4444-4444-444444444444\",\"service_type\":\"SALE\"},\"outcome\":{},\"response_snapshot\":{},\"hub_recorded_at\":\"2026-11-03T00:00:00Z\"}]}";

    [Fact]
    public void C45_OffIsByteIdenticalToGolden()
    {
        var batch = FixedBatch();
        var json = JsonSerializer.Serialize(
            HubReplicationPayloadV11.Apply(batch, new HubReplicationOptions { PayloadV11Enabled = false }),
            HubJson.Options);
        Assert.Equal(OffGolden, json);
        Assert.DoesNotContain("payload_version", json);
        Assert.DoesNotContain("invoice_payloads", json);
    }

    [Fact]
    public void C45_OnAddsRecordedPresentValuesAndTomanWithoutChangingEntries()
    {
        var batch = FixedBatch();
        batch.Entries![0].Payload = HubJson.Element(new
        {
            invoice_id = Guid.Parse(Invoice),
            service_type = "SALE",
            customer_name = "مشتری نمونه",
            payments = new[]
            {
                new { type = "card-reader", amount = 1L },
                new { type = "card-to-card", amount = 2L },
                new { type = "cash", amount = 3L },
                new { type = "credit", amount = 4L }
            },
            remaining_balance = -5L
        });
        var entryBefore = JsonSerializer.Serialize(batch.Entries[0], HubJson.Options);
        var options = new HubReplicationOptions
        {
            PayloadV11Enabled = true,
            PayloadV11StoreCurrencies = new Dictionary<Guid, string> { [Guid.Parse(Store)] = "toman" }
        };

        var json = JsonSerializer.Serialize(HubReplicationPayloadV11.Apply(batch, options), HubJson.Options);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal("1.1", root.GetProperty("payload_version").GetString());
        Assert.Equal("toman", root.GetProperty("currency").GetString());
        Assert.Equal(entryBefore, JsonSerializer.Serialize(batch.Entries[0], HubJson.Options));
        var invoice = Assert.Single(root.GetProperty("invoice_payloads").EnumerateArray());
        Assert.Equal("op-fixed", invoice.GetProperty("operation_id").GetString());
        Assert.Equal("مشتری نمونه", invoice.GetProperty("customer_name").GetString());
        Assert.Equal(-5, invoice.GetProperty("remaining_balance").GetInt64());
        var payments = invoice.GetProperty("payments").EnumerateArray().ToArray();
        Assert.Equal(new[] { "card-reader", "card-to-card", "cash", "credit" },
            payments.Select(payment => payment.GetProperty("type").GetString()).ToArray());
        Assert.Equal(new[] { 1L, 2L, 3L, 4L },
            payments.Select(payment => payment.GetProperty("amount").GetInt64()).ToArray());
        Assert.False(invoice.TryGetProperty("total", out _));
    }

    [Fact]
    public void C45_OnOmitsAbsentValues()
    {
        var batch = FixedBatch();
        var json = JsonSerializer.Serialize(HubReplicationPayloadV11.Apply(
            batch, new HubReplicationOptions { PayloadV11Enabled = true }), HubJson.Options);
        using var document = JsonDocument.Parse(json);
        Assert.Equal("1.1", document.RootElement.GetProperty("payload_version").GetString());
        Assert.False(document.RootElement.TryGetProperty("currency", out _));
        Assert.False(document.RootElement.TryGetProperty("invoice_payloads", out _));
    }

    [Fact]
    public void C45_OffDoesNotValidateOrProjectV11Values()
    {
        var batch = FixedBatch();
        batch.Entries![0].Payload = HubJson.Element(new { invoice_id = Guid.Parse(Invoice), payments = "not-an-array" });
        var before = JsonSerializer.Serialize(batch, HubJson.Options);
        var options = new HubReplicationOptions
        {
            PayloadV11Enabled = false,
            PayloadV11StoreCurrencies = new Dictionary<Guid, string> { [Guid.Parse(Store)] = "unsupported" }
        };
        var after = JsonSerializer.Serialize(HubReplicationPayloadV11.Apply(batch, options), HubJson.Options);
        Assert.Equal(before, after);
    }

    private static ReplicationBatch FixedBatch() => new()
    {
        BatchId = "batch-fixed",
        StoreId = Guid.Parse(Store),
        WriterDeviceId = Guid.Parse(Writer),
        WriterEpoch = 1,
        Entries =
        [
            new ReplicationLogEntry
            {
                StoreId = Guid.Parse(Store),
                WriterDeviceId = Guid.Parse(Writer),
                WriterEpoch = 1,
                OperationSeq = 1,
                PreviousEntryHash = new string('0', 64),
                EntryHash = new string('a', 64),
                OperationId = "op-fixed",
                DeviceId = Guid.Parse(Device),
                OperationType = "invoice.issue",
                BaseVersion = null,
                RequestHash = "request-fixed",
                Payload = HubJson.Element(new { invoice_id = Guid.Parse(Invoice), service_type = "SALE" }),
                Outcome = HubJson.Element(new { }),
                ResponseSnapshot = HubJson.Element(new { }),
                HubRecordedAt = "2026-11-03T00:00:00Z"
            }
        ]
    };
}
