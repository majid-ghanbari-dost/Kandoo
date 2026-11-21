using Kandoo.Core;
using Xunit;

namespace Kandoo.Core.Tests;

public sealed class InvoiceTests
{
    [Fact]
    public void TC_D12_R2_R4_IssueOnlyNumberingIsGaplessAndSurvivesRestore()
    {
        var service = new InvoiceService(new Dictionary<Guid, long> { [TestData.S1] = 41 });
        var cancelled = TestData.IssuableDraft(service, TestData.S1, "op-cancel");
        var first = TestData.IssuableDraft(service, TestData.S1, "op-first");
        var second = TestData.IssuableDraft(service, TestData.S1, "op-second");
        var otherStore = TestData.IssuableDraft(service, TestData.S2, "op-other-store", TestData.D3);

        Assert.All(new[] { cancelled, first, second, otherStore }, invoice => Assert.Null(invoice.DisplayNumber));
        service.Cancel(cancelled, TestData.T0);
        Assert.Equal(41, service.GetHighWatermark(TestData.S1));

        service.Issue(first, TestData.T0);
        service.Issue(second, TestData.T0);
        service.Issue(otherStore, TestData.T0);
        Assert.Equal(42, first.DisplayNumber);
        Assert.Equal(43, second.DisplayNumber);
        Assert.Equal(1, otherStore.DisplayNumber);

        var restored = new InvoiceService(new Dictionary<Guid, long> { [TestData.S1] = service.GetHighWatermark(TestData.S1) });
        var afterRestore = TestData.IssuableDraft(restored, TestData.S1, "op-after-restore");
        restored.Issue(afterRestore, TestData.T0);
        Assert.Equal(44, afterRestore.DisplayNumber);
    }

    [Fact]
    public void TC_D12_R4_FailedIssueAndTimeoutConsumeNoNumber()
    {
        var service = new InvoiceService(new Dictionary<Guid, long> { [TestData.S1] = 41 });
        var empty = service.CreateDraft(Guid.NewGuid(), TestData.S1, "op-empty", TestData.D1, TestData.T0);
        var timedOut = TestData.IssuableDraft(service, TestData.S1, "op-timeout");

        Assert.Throws<InvalidOperationException>(() => service.Issue(empty, TestData.T0));
        service.Timeout(timedOut, TestData.T0);
        Assert.Equal(41, service.GetHighWatermark(TestData.S1));
        Assert.Null(timedOut.DisplayNumber);
        Assert.Equal(RetirementReason.Timeout, timedOut.RetirementReason);

        var valid = TestData.IssuableDraft(service, TestData.S1, "op-valid");
        service.Issue(valid, TestData.T0);
        Assert.Equal(42, valid.DisplayNumber);
    }

    [Fact]
    public void TC_D12_R3_MD_R9_ConcurrentIssuesProduceExactlyOneToTwenty()
    {
        var service = new InvoiceService();
        var invoices = Enumerable.Range(0, 20).Select(i => TestData.IssuableDraft(service, TestData.S1, $"op-{i}", i % 2 == 0 ? TestData.D1 : TestData.D2)).ToArray();

        Parallel.ForEach(invoices, invoice => service.Issue(invoice, TestData.T0));

        Assert.Equal(20, service.Count);
        Assert.Equal(Enumerable.Range(1, 20).Select(i => (long)i).OrderBy(i => i), invoices.Select(i => i.DisplayNumber!.Value).OrderBy(i => i));
        Assert.Equal(20, service.GetHighWatermark(TestData.S1));
    }

    [Fact]
    public void TC_D12_R5_ExternalPosNumberNeverDeterminesPlatformNumbering()
    {
        var service = new InvoiceService(new Dictionary<Guid, long> { [TestData.S2] = 41 });
        var first = TestData.IssuableDraft(service, TestData.S2, "op-b-1", TestData.D3, "0007");
        var second = TestData.IssuableDraft(service, TestData.S2, "op-b-2", TestData.D3, "0007");

        Assert.NotEqual(first.InvoiceId, second.InvoiceId);
        Assert.Null(first.DisplayNumber);
        Assert.Null(second.DisplayNumber);
        service.Issue(first, TestData.T0);
        service.Issue(second, TestData.T0);
        Assert.Equal(42, first.DisplayNumber);
        Assert.Equal(43, second.DisplayNumber);
        Assert.Equal("0007", first.ExternalInvoiceNumber);
    }

    [Fact]
    public void TC_D10_R5_R6_IssuedSnapshotAndItemsAreImmutable()
    {
        var service = new InvoiceService();
        var invoice = TestData.IssuableDraft(service, TestData.S1);
        service.Issue(invoice, TestData.T0);
        var payload = invoice.IssuedPayload;
        var digest = invoice.IssuedContentSha256;
        var items = invoice.Items;

        Assert.Throws<InvalidOperationException>(() => invoice.AddItem(TestData.Item(TestData.S1, 2)));
        Assert.Throws<InvalidOperationException>(() => invoice.ChangeCustomer(Guid.NewGuid()));
        Assert.Throws<InvalidOperationException>(() => invoice.ChangeDisplayNumber(99));
        Assert.Throws<InvalidOperationException>(() => invoice.ReplaceIssuedPayload("replacement", "hash"));
        Assert.Equal(payload, invoice.IssuedPayload);
        Assert.Equal(digest, invoice.IssuedContentSha256);
        Assert.Equal(items, invoice.Items);

        invoice.Supersede();
        Assert.Equal(InvoiceStatus.Superseded, invoice.Status);
        Assert.Equal(payload, invoice.IssuedPayload);
    }
}
