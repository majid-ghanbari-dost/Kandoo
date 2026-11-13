using System.Net;
using Xunit;

namespace Kandoo.Hub.Tests;

public sealed class ReplicationPayloadV11IssueTests : IDisposable
{
    private const string Store = "11111111-1111-1111-1111-111111111111";
    private readonly HubApiFactory _factory;
    private readonly HubApiClient _api;

    public ReplicationPayloadV11IssueTests()
    {
        _factory = new HubApiFactory(new HubReplicationOptions
        {
            PayloadV11Enabled = true,
            PayloadV11StoreCurrencies = new Dictionary<Guid, string> { [Guid.Parse(Store)] = "toman" }
        });
        _api = new HubApiClient(_factory);
    }

    [Fact]
    public async Task C45b_IssueStampsV11MarkerAndRecordedValues()
    {
        var storeId = Guid.Parse(Store);
        var owner = await _api.Owner(storeId, Guid.NewGuid(), Guid.NewGuid());
        var paired = await _api.Pair(owner, storeId, Guid.NewGuid());
        var draft = await _api.Post($"/v1/stores/{storeId:D}/invoices/drafts", new
        {
            operation_id = "c45b-draft",
            service_type = "SALE",
            customer_phone = "+989120000101"
        }, paired.Session.Token);
        var invoiceId = draft.Body.GetProperty("response").GetProperty("invoice").GetProperty("invoice_id").GetGuid();
        var item = await _api.Post($"/v1/stores/{storeId:D}/invoices/{invoiceId:D}/items", new
        {
            operation_id = "c45b-item",
            service_type = "SALE",
            description = "داده نمونه",
            quantity_millis = 1_000,
            unit_price_rials = 2_000
        }, paired.Session.Token);
        Assert.Equal(HttpStatusCode.OK, item.StatusCode);

        var issued = await _api.Post($"/v1/stores/{storeId:D}/invoices/{invoiceId:D}/issue", new
        {
            operation_id = "c45b-issue",
            service_type = "SALE",
            customer_name = "مشتری نمونه",
            payments = new[] { new { type = "card-to-card", amount = 2_000L } },
            remaining_balance = 123L
        }, paired.Session.Token);
        Assert.Equal(HttpStatusCode.OK, issued.StatusCode);

        var invoice = issued.Body.GetProperty("response").GetProperty("invoice");
        Assert.Equal("1.1", invoice.GetProperty("invoice_schema_version").GetString());
        Assert.Equal("مشتری نمونه", invoice.GetProperty("customer_name").GetString());
        Assert.Equal("toman", invoice.GetProperty("currency_unit").GetString());
        Assert.Equal(123, invoice.GetProperty("remaining_balance").GetInt64());
        var payments = invoice.GetProperty("payments").EnumerateArray().ToArray();
        Assert.Single(payments);
        Assert.Equal("card-to-card", payments[0].GetProperty("type").GetString());
        Assert.Equal(2_000L, payments[0].GetProperty("amount").GetInt64());
        Assert.False(invoice.TryGetProperty("total", out _));
    }

    [Fact]
    public async Task C45b_IssueOmitsAbsentV11ValuesButStampsMarker()
    {
        // No per-store currency configured, so currency_unit must be absent too.
        using var absentFactory = new HubApiFactory(new HubReplicationOptions { PayloadV11Enabled = true });
        using var absentApi = new HubApiClient(absentFactory);
        var storeId = Guid.Parse(Store);
        var owner = await absentApi.Owner(storeId, Guid.NewGuid(), Guid.NewGuid());
        var paired = await absentApi.Pair(owner, storeId, Guid.NewGuid());
        var draft = await absentApi.Post($"/v1/stores/{storeId:D}/invoices/drafts", new
        {
            operation_id = "c45b-absent-draft",
            service_type = "SALE",
            customer_phone = "+989120000101"
        }, paired.Session.Token);
        var invoiceId = draft.Body.GetProperty("response").GetProperty("invoice").GetProperty("invoice_id").GetGuid();
        await absentApi.Post($"/v1/stores/{storeId:D}/invoices/{invoiceId:D}/items", new
        {
            operation_id = "c45b-absent-item",
            service_type = "SALE",
            description = "dessert",
            quantity_millis = 1_000,
            unit_price_rials = 2_000
        }, paired.Session.Token);

        var issued = await absentApi.Post($"/v1/stores/{storeId:D}/invoices/{invoiceId:D}/issue", new
        {
            operation_id = "c45b-absent-issue",
            service_type = "SALE"
        }, paired.Session.Token);
        Assert.Equal(HttpStatusCode.OK, issued.StatusCode);

        var invoice = issued.Body.GetProperty("response").GetProperty("invoice");
        Assert.Equal("1.1", invoice.GetProperty("invoice_schema_version").GetString());
        Assert.False(invoice.TryGetProperty("customer_name", out _));
        Assert.False(invoice.TryGetProperty("payments", out _));
        Assert.False(invoice.TryGetProperty("currency_unit", out _));
        Assert.False(invoice.TryGetProperty("remaining_balance", out _));
    }

    [Fact]
    public async Task C45b_IssueOffDoesNotStampV11()
    {
        using var offFactory = new HubApiFactory();
        using var offApi = new HubApiClient(offFactory);
        var storeId = Guid.Parse(Store);
        var owner = await offApi.Owner(storeId, Guid.NewGuid(), Guid.NewGuid());
        var paired = await offApi.Pair(owner, storeId, Guid.NewGuid());
        var draft = await offApi.Post($"/v1/stores/{storeId:D}/invoices/drafts", new
        {
            operation_id = "c45b-off-draft",
            service_type = "SALE",
            customer_phone = "+989120000101"
        }, paired.Session.Token);
        var invoiceId = draft.Body.GetProperty("response").GetProperty("invoice").GetProperty("invoice_id").GetGuid();
        await offApi.Post($"/v1/stores/{storeId:D}/invoices/{invoiceId:D}/items", new
        {
            operation_id = "c45b-off-item",
            service_type = "SALE",
            description = "legacy",
            quantity_millis = 1_000,
            unit_price_rials = 2_000
        }, paired.Session.Token);

        var issued = await offApi.Post($"/v1/stores/{storeId:D}/invoices/{invoiceId:D}/issue", new
        {
            operation_id = "c45b-off-issue",
            service_type = "SALE",
            customer_name = "should-not-appear",
            payments = new[] { new { type = "cash", amount = 1L } },
            remaining_balance = 5L
        }, paired.Session.Token);
        Assert.Equal(HttpStatusCode.OK, issued.StatusCode);

        var invoice = issued.Body.GetProperty("response").GetProperty("invoice");
        Assert.False(invoice.TryGetProperty("invoice_schema_version", out _));
        Assert.False(invoice.TryGetProperty("customer_name", out _));
        Assert.False(invoice.TryGetProperty("payments", out _));
        Assert.False(invoice.TryGetProperty("currency_unit", out _));
        Assert.False(invoice.TryGetProperty("remaining_balance", out _));
    }

    [Fact]
    public async Task C45b_IssueInvalidV11Returns400ValidationError()
    {
        var storeId = Guid.Parse(Store);
        var owner = await _api.Owner(storeId, Guid.NewGuid(), Guid.NewGuid());
        var paired = await _api.Pair(owner, storeId, Guid.NewGuid());
        var draft = await _api.Post($"/v1/stores/{storeId:D}/invoices/drafts", new
        {
            operation_id = "c45b-bad-draft",
            service_type = "SALE",
            customer_phone = "+989120000101"
        }, paired.Session.Token);
        var invoiceId = draft.Body.GetProperty("response").GetProperty("invoice").GetProperty("invoice_id").GetGuid();
        await _api.Post($"/v1/stores/{storeId:D}/invoices/{invoiceId:D}/items", new
        {
            operation_id = "c45b-bad-item",
            service_type = "SALE",
            description = "bad",
            quantity_millis = 1_000,
            unit_price_rials = 2_000
        }, paired.Session.Token);

        var issued = await _api.Post($"/v1/stores/{storeId:D}/invoices/{invoiceId:D}/issue", new
        {
            operation_id = "c45b-bad-issue",
            service_type = "SALE",
            customer_name = ""
        }, paired.Session.Token);
        Assert.Equal(HttpStatusCode.BadRequest, issued.StatusCode);
        Assert.Equal("VALIDATION_ERROR", issued.Body.GetProperty("code").GetString());
    }

    public void Dispose()
    {
        _api.Dispose();
        _factory.Dispose();
    }
}
