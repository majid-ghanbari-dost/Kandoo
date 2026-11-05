using System.Net;
using Xunit;

namespace Kandoo.Hub.Tests;

public sealed class ReplicationPayloadRecordingTests : IDisposable
{
    private readonly HubApiFactory _factory = new();
    private readonly HubApiClient _api;

    public ReplicationPayloadRecordingTests() => _api = new HubApiClient(_factory);

    [Fact]
    public async Task C45_IssuePersistsOnlyRecordedOptionalFacts()
    {
        var storeId = Guid.NewGuid();
        var owner = await _api.Owner(storeId, Guid.NewGuid(), Guid.NewGuid());
        var paired = await _api.Pair(owner, storeId, Guid.NewGuid());
        var draft = await _api.Post($"/v1/stores/{storeId:D}/invoices/drafts", new
        {
            operation_id = "c45-draft",
            service_type = "SALE",
            customer_phone = "+989120000101"
        }, paired.Session.Token);
        var invoiceId = draft.Body.GetProperty("response").GetProperty("invoice").GetProperty("invoice_id").GetGuid();
        var item = await _api.Post($"/v1/stores/{storeId:D}/invoices/{invoiceId:D}/items", new
        {
            operation_id = "c45-item",
            service_type = "SALE",
            description = "داده نمونه",
            quantity_millis = 1_000,
            unit_price_rials = 2_000
        }, paired.Session.Token);
        Assert.Equal(HttpStatusCode.OK, item.StatusCode);

        var issued = await _api.Post($"/v1/stores/{storeId:D}/invoices/{invoiceId:D}/issue", new
        {
            operation_id = "c45-issue",
            service_type = "SALE",
            customer_name = "مشتری نمونه",
            payments = new[] { new { type = "card-to-card", amount = 2_000L } },
            remaining_balance = 123L
        }, paired.Session.Token);
        Assert.Equal(HttpStatusCode.OK, issued.StatusCode);

        var entry = _factory.Store.ReadBatch(storeId, 100, 1_048_576)!.Entries!
            .Single(value => value.OperationId == "c45-issue");
        Assert.Equal("مشتری نمونه", entry.Payload.GetProperty("customer_name").GetString());
        var payment = Assert.Single(entry.Payload.GetProperty("payments").EnumerateArray());
        Assert.Equal("card-to-card", payment.GetProperty("type").GetString());
        Assert.Equal(2_000, payment.GetProperty("amount").GetInt64());
        Assert.Equal(123, entry.Payload.GetProperty("remaining_balance").GetInt64());
        Assert.False(entry.Payload.TryGetProperty("currency", out _));
        Assert.False(entry.Payload.TryGetProperty("total", out _));
    }

    public void Dispose()
    {
        _api.Dispose();
        _factory.Dispose();
    }
}
