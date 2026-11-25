using System.Net;
using System.Text.Json;
using Kandoo.Core;
using Xunit;

namespace Kandoo.Hub.Tests;

/// <summary>
/// C-98 / M-010 DoD-A point 3: issuing an invoice must leave a demo SMS line in the local outbox.
/// </summary>
/// <remarks>
/// Before this test the demo SMS did not exist outside Kandoo.Core: <see cref="DemoSmsProvider"/>
/// was implemented and unit-tested, but nothing in Kandoo.Hub constructed it, so the outbox file
/// was never written and the demo chain stopped at "issue the invoice". These tests pin the wiring
/// and, just as importantly, pin that a broken outbox cannot break a real sale.
/// </remarks>
public sealed class C98DemoSmsTests : IDisposable
{
    private readonly string _outboxDirectory = Path.Combine(
        Path.GetTempPath(), "kandoo-c98-outbox", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_outboxDirectory, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
            // Best-effort cleanup only.
        }
    }

    [Fact]
    public void C98_SendDemoSmsWritesOneLineWithDisplayNumber()
    {
        var directory = Path.Combine(_outboxDirectory, "store");
        Directory.CreateDirectory(directory);
        using var store = new SqliteHubStore(
            $"Data Source={Path.Combine(directory, "hub.db")};Pooling=False",
            demoSmsOutboxDirectory: directory,
            demoStoreName: "فروشگاه نمونه");

        var message = store.SendDemoSms(Guid.NewGuid(), "+989120000101", "body", 42);

        Assert.Equal("+989120000101", message.RecipientPhone);
        Assert.Equal(42, message.DisplayNumber);

        var path = Path.Combine(directory, "demo-sms.log");
        Assert.True(File.Exists(path));
        var line = Assert.Single(File.ReadAllLines(path));
        Assert.Contains("+989120000101", line, StringComparison.Ordinal);
        Assert.Contains("display_number=42", line, StringComparison.Ordinal);
        Assert.Contains("store=فروشگاه نمونه", line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task C98_IssuingAnInvoiceWritesTheDemoSmsLine()
    {
        using var factory = new HubApiFactory(_outboxDirectory);
        using var api = new HubApiClient(factory);
        var storeId = Guid.NewGuid();
        var owner = await api.Owner(storeId, Guid.NewGuid(), Guid.NewGuid());
        var paired = await api.Pair(owner, storeId, Guid.NewGuid());

        _ = await DraftAndIssueAsync(api, storeId, paired.Session.Token);

        var outboxFile = Path.Combine(_outboxDirectory, "demo-sms.log");
        Assert.True(File.Exists(outboxFile), "expected a demo SMS outbox line after issuing the invoice");
        var lines = File.ReadAllLines(outboxFile);
        var line = Assert.Single(lines);
        // The outbox line is the operator-facing record of the demo SMS: recipient, the display
        // number, and no real customer data beyond the phone the operator typed.
        Assert.Contains("+989120000101", line, StringComparison.Ordinal);
        Assert.Contains("display_number=1", line, StringComparison.Ordinal);
        Assert.Contains("فاکتور دیجیتال کندو", line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task C98_IssueStillSucceedsWhenTheOutboxCannotBeWritten()
    {
        // Point the outbox at a path that cannot be created: a regular file stands where a
        // directory is required. The invoice must still issue — a demo-only side effect may
        // never break a real sale.
        var blocked = Path.Combine(_outboxDirectory, "blocked");
        Directory.CreateDirectory(_outboxDirectory);
        await File.WriteAllTextAsync(blocked, "not a directory");

        using var factory = new HubApiFactory(blocked);
        using var api = new HubApiClient(factory);
        var storeId = Guid.NewGuid();
        var owner = await api.Owner(storeId, Guid.NewGuid(), Guid.NewGuid());
        var paired = await api.Pair(owner, storeId, Guid.NewGuid());

        var invoiceId = await DraftAndIssueAsync(api, storeId, paired.Session.Token);

        var invoice = factory.Store.GetInvoice(storeId, invoiceId);
        Assert.NotNull(invoice);
        Assert.Equal(InvoiceStatus.Issued, invoice!.Status);
        // No outbox line was produced, and that is the point: the issue still committed.
        Assert.False(File.Exists(Path.Combine(blocked, "demo-sms.log")));
    }

    private static async Task<Guid> DraftAndIssueAsync(HubApiClient api, Guid storeId, string token)
    {
        var draft = await api.Post($"/v1/stores/{storeId:D}/invoices/drafts", new
        {
            operation_id = "c98-draft-1",
            service_type = "SALE",
            customer_phone = "+989120000101"
        }, token);
        Assert.Equal(HttpStatusCode.OK, draft.StatusCode);
        var invoiceId = ReadInvoiceId(draft.Body);

        var item = await api.Post($"/v1/stores/{storeId:D}/invoices/{invoiceId:D}/items", new
        {
            operation_id = "c98-item-1",
            service_type = "SALE",
            description = "C98 demo product",
            quantity_millis = 2000,
            unit_price_rials = 12500
        }, token);
        Assert.Equal(HttpStatusCode.OK, item.StatusCode);

        var issued = await api.Post($"/v1/stores/{storeId:D}/invoices/{invoiceId:D}/issue", new
        {
            operation_id = "c98-issue-1",
            service_type = "SALE"
        }, token);
        Assert.Equal(HttpStatusCode.OK, issued.StatusCode);
        Assert.Equal("SUCCEEDED", issued.Body.GetProperty("status").GetString());
        return ReadInvoiceId(issued.Body);
    }

    private static Guid ReadInvoiceId(JsonElement body) =>
        body.GetProperty("response").GetProperty("invoice").GetProperty("invoice_id").GetGuid();
}
