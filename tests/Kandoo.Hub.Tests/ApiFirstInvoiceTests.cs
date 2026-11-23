using System.Net;
using Kandoo.Core;
using Xunit;

namespace Kandoo.Hub.Tests;

public sealed class ApiFirstInvoiceTests : IDisposable
{
    private readonly HubApiFactory _factory = new();
    private readonly HubApiClient _api;

    public ApiFirstInvoiceTests() => _api = new HubApiClient(_factory);

    [Fact]
    public async Task C23_SaleDraftItemIssueAndOptionalPrintReplayThroughNativeOperations()
    {
        var storeId = Guid.NewGuid();
        var owner = await _api.Owner(storeId, Guid.NewGuid(), Guid.NewGuid());
        var paired = await _api.Pair(owner, storeId, Guid.NewGuid());

        var serviceTypes = await _api.Get($"/v1/stores/{storeId:D}/service-types", paired.Session.Token);
        Assert.Equal(HttpStatusCode.OK, serviceTypes.StatusCode);
        var types = serviceTypes.Body.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(3, types.Length);
        Assert.True(types.Single(value => value.GetProperty("service_type").GetString() == "SALE").GetProperty("enabled").GetBoolean());
        Assert.False(types.Single(value => value.GetProperty("service_type").GetString() == "RENTAL").GetProperty("enabled").GetBoolean());
        Assert.False(types.Single(value => value.GetProperty("service_type").GetString() == "OTHER").GetProperty("enabled").GetBoolean());

        var operationCount = _factory.Store.LoadOperationLog().Count(storeId);
        foreach (var disabledType in new[] { "RENTAL", "OTHER" })
        {
            var disabled = await _api.Post($"/v1/stores/{storeId:D}/invoices/drafts", new
            {
                operation_id = $"disabled-{disabledType}",
                service_type = disabledType,
                customer_phone = "+989120000101"
            }, paired.Session.Token);
            Assert.Equal(HttpStatusCode.NotImplemented, disabled.StatusCode);
            Assert.Equal("SERVICE_TYPE_DISABLED", disabled.Body.GetProperty("code").GetString());
        }

        Assert.Equal(operationCount, _factory.Store.LoadOperationLog().Count(storeId));
        var draftBody = new
        {
            operation_id = "c23-draft-1",
            service_type = "SALE",
            customer_phone = "+989120000101",
            external_invoice_number = "native-c23-1"
        };
        var draft = await _api.Post($"/v1/stores/{storeId:D}/invoices/drafts", draftBody, paired.Session.Token);
        Assert.Equal(HttpStatusCode.OK, draft.StatusCode);
        Assert.Equal("SUCCEEDED", draft.Body.GetProperty("status").GetString());
        Assert.Equal("IRR", draft.Body.GetProperty("currency").GetProperty("code").GetString());
        var invoiceId = draft.Body.GetProperty("response").GetProperty("invoice").GetProperty("invoice_id").GetGuid();
        var draftReplay = await _api.Post($"/v1/stores/{storeId:D}/invoices/drafts", draftBody, paired.Session.Token);
        Assert.True(draftReplay.Body.GetProperty("idempotent_replay").GetBoolean());
        Assert.Equal(invoiceId, draftReplay.Body.GetProperty("response").GetProperty("invoice").GetProperty("invoice_id").GetGuid());

        var item = await _api.Post($"/v1/stores/{storeId:D}/invoices/{invoiceId:D}/items", new
        {
            operation_id = "c23-item-1",
            service_type = "SALE",
            description = "Two units",
            quantity_millis = 2_000,
            unit_price_rials = 1_500
        }, paired.Session.Token);
        Assert.Equal(HttpStatusCode.OK, item.StatusCode);
        Assert.Equal(3_000, item.Body.GetProperty("response").GetProperty("invoice").GetProperty("items")[0]
            .GetProperty("line_total_rials").GetInt64());

        var issueBody = new { operation_id = "c23-issue-1", service_type = "SALE", print = true };
        var issued = await _api.Post($"/v1/stores/{storeId:D}/invoices/{invoiceId:D}/issue", issueBody, paired.Session.Token);
        Assert.Equal(HttpStatusCode.OK, issued.StatusCode);
        Assert.Equal("SUCCEEDED", issued.Body.GetProperty("status").GetString());
        Assert.Equal(1, issued.Body.GetProperty("response").GetProperty("invoice").GetProperty("display_number").GetInt64());
        Assert.True(issued.Body.GetProperty("print").GetProperty("requested").GetBoolean());
        Assert.Equal("PRINT_ADAPTER_DISABLED", issued.Body.GetProperty("print").GetProperty("status").GetString());
        Assert.Equal(
            "src/Kandoo.Core/Invoice.cs:InvoiceItem.UnitPriceRials/LineTotalRials",
            issued.Body.GetProperty("currency").GetProperty("invoice_source").GetString());
        var issueReplay = await _api.Post($"/v1/stores/{storeId:D}/invoices/{invoiceId:D}/issue", issueBody, paired.Session.Token);
        Assert.True(issueReplay.Body.GetProperty("idempotent_replay").GetBoolean());
        Assert.Equal(1, issueReplay.Body.GetProperty("response").GetProperty("invoice").GetProperty("display_number").GetInt64());

        var secondDraft = await _api.Post($"/v1/stores/{storeId:D}/invoices/drafts", new
        {
            operation_id = "c23-draft-2",
            service_type = "SALE",
            customer_phone = "+989120000101"
        }, paired.Session.Token);
        var secondInvoiceId = secondDraft.Body.GetProperty("response").GetProperty("invoice").GetProperty("invoice_id").GetGuid();
        var secondItem = await _api.Post($"/v1/stores/{storeId:D}/invoices/{secondInvoiceId:D}/items", new
        {
            operation_id = "c23-item-2",
            service_type = "SALE",
            description = "One unit",
            quantity_millis = 1_000,
            unit_price_rials = 2_000
        }, paired.Session.Token);
        Assert.Equal(HttpStatusCode.OK, secondItem.StatusCode);
        var secondIssue = await _api.Post($"/v1/stores/{storeId:D}/invoices/{secondInvoiceId:D}/issue", new
        {
            operation_id = "c23-issue-2",
            service_type = "SALE"
        }, paired.Session.Token);
        Assert.Equal(HttpStatusCode.OK, secondIssue.StatusCode);
        Assert.False(secondIssue.Body.GetProperty("print").GetProperty("requested").GetBoolean());
        Assert.Equal("NOT_REQUESTED", secondIssue.Body.GetProperty("print").GetProperty("status").GetString());
        Assert.Equal(2, secondIssue.Body.GetProperty("response").GetProperty("invoice").GetProperty("display_number").GetInt64());
    }

    [Fact]
    public async Task C23_ReviewApiIsFailClosedThenIssuesOnceWithOptionalPrint()
    {
        var storeId = Guid.NewGuid();
        var owner = await _api.Owner(storeId, Guid.NewGuid(), Guid.NewGuid());
        var paired = await _api.Pair(owner, storeId, Guid.NewGuid());
        var operationCount = _factory.Store.LoadOperationLog().Count(storeId);

        var accepted = await _api.Post($"/v1/stores/{storeId:D}/reviews", new
        {
            canonical_json = Golden()
        }, paired.Session.Token);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal("REVIEW", accepted.Body.GetProperty("status").GetString());
        var reviewId = accepted.Body.GetProperty("review").GetProperty("review_id").GetString()!;
        var attention = await _api.Get($"/v1/stores/{storeId:D}/reviews/{reviewId}/attention", paired.Session.Token);
        Assert.Equal("OPERATOR_PHONE_REQUIRED", Assert.Single(attention.Body.GetProperty("items").EnumerateArray()).GetProperty("reason").GetString());

        var missingPhone = await _api.Post($"/v1/stores/{storeId:D}/reviews/{reviewId}/issue", new
        {
            service_type = "SALE"
        }, paired.Session.Token);
        Assert.Equal(HttpStatusCode.Conflict, missingPhone.StatusCode);
        Assert.Equal("OPERATOR_PHONE_REQUIRED", missingPhone.Body.GetProperty("status").GetString());
        var rental = await _api.Post($"/v1/stores/{storeId:D}/reviews/{reviewId}/issue", new
        {
            service_type = "RENTAL",
            print = true
        }, paired.Session.Token);
        Assert.Equal(HttpStatusCode.NotImplemented, rental.StatusCode);
        Assert.Equal(operationCount, _factory.Store.LoadOperationLog().Count(storeId));

        var phone = await _api.Post($"/v1/stores/{storeId:D}/reviews/{reviewId}/customer-phone", new
        {
            phone = "+98 912 000 0101"
        }, paired.Session.Token);
        Assert.Equal(HttpStatusCode.OK, phone.StatusCode);
        Assert.Equal("+989120000101", phone.Body.GetProperty("review").GetProperty("operator_phone").GetString());
        Assert.Equal(operationCount, _factory.Store.LoadOperationLog().Count(storeId));

        var issueBody = new { service_type = "SALE", print = true };
        var issued = await _api.Post($"/v1/stores/{storeId:D}/reviews/{reviewId}/issue", issueBody, paired.Session.Token);
        Assert.Equal(HttpStatusCode.OK, issued.StatusCode);
        Assert.Equal("ISSUED", issued.Body.GetProperty("status").GetString());
        Assert.Equal(1, issued.Body.GetProperty("display_number").GetInt64());
        Assert.Equal("PRINT_ADAPTER_DISABLED", issued.Body.GetProperty("print").GetProperty("status").GetString());
        var invoiceId = issued.Body.GetProperty("invoice_id").GetGuid();
        Assert.Equal(InvoiceStatus.Issued, _factory.Store.GetInvoice(storeId, invoiceId)!.Status);
        Assert.Equal(operationCount + 3, _factory.Store.LoadOperationLog().Count(storeId));

        var replay = await _api.Post($"/v1/stores/{storeId:D}/reviews/{reviewId}/issue", issueBody, paired.Session.Token);
        Assert.True(replay.Body.GetProperty("idempotent_replay").GetBoolean());
        Assert.Equal(invoiceId, replay.Body.GetProperty("invoice_id").GetGuid());
        Assert.Equal(operationCount + 3, _factory.Store.LoadOperationLog().Count(storeId));
    }

    [Fact]
    public async Task C23_SmsResendAndStatusAreIdempotentAndStoreScoped()
    {
        var storeId = Guid.NewGuid();
        var invoiceId = Guid.NewGuid();
        var owner = await _api.Owner(storeId, Guid.NewGuid(), Guid.NewGuid());
        var paired = await _api.Pair(owner, storeId, Guid.NewGuid());
        _factory.TestSms.Seed(storeId, invoiceId, "sms-original");
        var body = new { operation_id = "sms-resend-1", original_operation_id = "sms-original" };

        var resent = await _api.Post($"/v1/stores/{storeId:D}/invoices/{invoiceId:D}/sms/resend", body, paired.Session.Token);
        Assert.Equal(HttpStatusCode.OK, resent.StatusCode);
        Assert.Equal("SENT", resent.Body.GetProperty("status").GetString());
        Assert.Equal("stub-resend-1", resent.Body.GetProperty("provider_message_id").GetString());
        Assert.Equal(1, _factory.TestSms.CaptureCount);
        var replay = await _api.Post($"/v1/stores/{storeId:D}/invoices/{invoiceId:D}/sms/resend", body, paired.Session.Token);
        Assert.Equal("DUPLICATE", replay.Body.GetProperty("status").GetString());
        Assert.True(replay.Body.GetProperty("idempotent_replay").GetBoolean());
        Assert.Equal(1, _factory.TestSms.CaptureCount);

        var status = await _api.Get($"/v1/stores/{storeId:D}/invoices/{invoiceId:D}/sms/sms-resend-1", paired.Session.Token);
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        Assert.Equal("SENT", status.Body.GetProperty("status").GetString());
        Assert.Equal(invoiceId, status.Body.GetProperty("invoice_id").GetGuid());
        var missing = await _api.Get($"/v1/stores/{storeId:D}/invoices/{Guid.NewGuid():D}/sms/sms-resend-1", paired.Session.Token);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        var wrongStore = await _api.Get($"/v1/stores/{Guid.NewGuid():D}/invoices/{invoiceId:D}/sms/sms-resend-1", paired.Session.Token);
        Assert.Equal(HttpStatusCode.Forbidden, wrongStore.StatusCode);
        Assert.Equal("STORE_ACCESS_DENIED", wrongStore.Body.GetProperty("code").GetString());
    }

    private static string Golden()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName, "tests", "Kandoo.Core.Tests", "Fixtures", "E11", "contracts", "goldens", "valid",
                "draft_mode_b_null_display_number.json");
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate the E-11 draft Mode-B golden.");
    }

    public void Dispose()
    {
        _api.Dispose();
        _factory.Dispose();
    }
}
