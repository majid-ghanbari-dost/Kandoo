using Kandoo.Core;
using Xunit;

namespace Kandoo.Core.Tests;

public sealed class OperationLogTests
{
    [Fact]
    public void TC_D10_R8_R9_ExactReplayReturnsOriginalAndMismatchHasNoEffects()
    {
        var log = new OperationLog();
        var effects = 0;
        var request = Request(TestData.S1, TestData.D1, "op-1", "product.create", "1000");
        var first = log.Execute(request, () => { effects++; return "product-1"; });

        for (var i = 0; i < 10; i++)
        {
            var replay = log.Execute(request, () => { effects++; return "new-effect"; });
            Assert.Equal(OperationExecutionStatus.Replayed, replay.Status);
            Assert.Equal(first.Response, replay.Response);
            Assert.Equal(first.OperationSeq, replay.OperationSeq);
        }

        var differentDevice = log.Execute(Request(TestData.S1, TestData.D2, "op-1", "product.create", "1000"), () => { effects++; return "bad"; });
        var differentType = log.Execute(Request(TestData.S1, TestData.D1, "op-1", "product.update", "1000"), () => { effects++; return "bad"; });
        var differentPayload = log.Execute(Request(TestData.S1, TestData.D1, "op-1", "product.create", "1100"), () => { effects++; return "bad"; });

        Assert.Equal(OperationExecutionStatus.Succeeded, first.Status);
        Assert.Equal(1, effects);
        Assert.Equal(1, log.Count(TestData.S1));
        Assert.Equal("OPERATION_ID_CONFLICT", differentDevice.ErrorCode);
        Assert.Equal("OPERATION_ID_CONFLICT", differentType.ErrorCode);
        Assert.Equal("OPERATION_ID_CONFLICT", differentPayload.ErrorCode);
        Assert.Equal("product-1", log.Get(TestData.S1, "op-1")!.Response);
    }

    [Fact]
    public void TC_SI_R7_OperationIdentityIsNamespacedByStore()
    {
        var log = new OperationLog();
        var first = log.Execute(Request(TestData.S1, TestData.D1, "op-shared", "product.create", "1000"), () => "s1-product");
        var second = log.Execute(Request(TestData.S2, TestData.D3, "op-shared", "product.create", "1000"), () => "s2-product");

        Assert.Equal("s1-product", first.Response);
        Assert.Equal("s2-product", second.Response);
        Assert.Equal(1, first.OperationSeq);
        Assert.Equal(1, second.OperationSeq);
        Assert.Equal("s1-product", log.Get(TestData.S1, "op-shared")!.Response);
        Assert.Equal("s2-product", log.Get(TestData.S2, "op-shared")!.Response);
    }

    [Fact]
    public void TC_D12_R6_CreateAndIssueReplaysDoNotAllocateAgain()
    {
        var service = new InvoiceService(new Dictionary<Guid, long> { [TestData.S1] = 41 });
        var log = new OperationLog();
        Invoice? invoice = null;
        var create = Request(TestData.S1, TestData.D1, "op-create", "invoice.draft_create", "null");
        var created = log.Execute(create, () =>
        {
            invoice = service.CreateDraft(Guid.NewGuid(), TestData.S1, "op-create", TestData.D1, TestData.T0);
            invoice.AddItem(TestData.Item(TestData.S1));
            return invoice.InvoiceId.ToString();
        });
        var createReplay = log.Execute(create, () => throw new InvalidOperationException("Replay must not execute creation."));
        var issue = Request(TestData.S1, TestData.D1, "op-issue", "invoice.issue", "null");
        var issued = log.Execute(issue, () =>
        {
            service.Issue(invoice!, TestData.T0);
            return invoice!.DisplayNumber!.Value.ToString();
        });
        var issueReplay = log.Execute(issue, () => throw new InvalidOperationException("Replay must not execute issue."));

        Assert.Equal(created.Response, createReplay.Response);
        Assert.Equal(issued.Response, issueReplay.Response);
        Assert.Equal("42", issued.Response);
        Assert.Equal(42, service.GetHighWatermark(TestData.S1));
        Assert.Equal(1, service.Count);

        var second = TestData.IssuableDraft(service, TestData.S1, "op-second");
        service.Issue(second, TestData.T0);
        Assert.Equal(43, second.DisplayNumber);
        Assert.Equal(2, service.Count);
    }

    [Fact]
    public void TC_D10_R7_MD_R1_MissingIdentityFailsBeforeEffects()
    {
        var log = new OperationLog();
        var effects = 0;
        var invalid = new OperationRequest(TestData.S1, Guid.Empty, "op-missing-device", "product.create", null, TestData.Payload("{}"));

        Assert.Throws<ArgumentException>(() => log.Execute(invalid, () => { effects++; return "bad"; }));
        Assert.Equal(0, effects);
        Assert.Equal(0, log.Count(TestData.S1));
    }

    private static OperationRequest Request(Guid storeId, Guid deviceId, string operationId, string operationType, string price)
    {
        return new(storeId, deviceId, operationId, operationType, null, TestData.Payload($"{{\"price_rials\":{price}}}"));
    }
}
