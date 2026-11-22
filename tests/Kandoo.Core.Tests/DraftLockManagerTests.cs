using Kandoo.Core;
using Xunit;

namespace Kandoo.Core.Tests;

public sealed class DraftLockManagerTests
{
    [Fact]
    public void TC_D11_R1_R2_R4_R11_LocksAreExclusiveAndStoreScoped()
    {
        var manager = new DraftLockManager(new ManualClock(TestData.T0));
        var first = manager.Acquire(TestData.S1, "+98 912 000 0001", Guid.NewGuid(), TestData.D1, Guid.NewGuid());
        var otherStore = manager.Acquire(TestData.S2, "+989120000001", Guid.NewGuid(), TestData.D3, Guid.NewGuid());

        var conflict = Assert.Throws<DraftLockException>(() => manager.Acquire(TestData.S1, "+989120000001", Guid.NewGuid(), TestData.D2, Guid.NewGuid()));
        var sameDevice = Assert.Throws<DraftLockException>(() => manager.Acquire(TestData.S1, "+989120000001", Guid.NewGuid(), TestData.D1, Guid.NewGuid()));

        Assert.Equal("+989120000001", first.CustomerPhone);
        Assert.Equal("DRAFT_LOCK_CONFLICT", conflict.Code);
        Assert.Equal("DRAFT_ALREADY_ACTIVE", sameDevice.Code);
        Assert.Equal(DraftLockState.Active, first.State);
        Assert.Equal(DraftLockState.Active, otherStore.State);
    }

    [Fact]
    public void TC_D11_R8_OnlyAcceptedDraftChangesRestartInactivity()
    {
        var clock = new ManualClock(TestData.T0);
        var manager = new DraftLockManager(clock);
        var first = manager.Acquire(TestData.S1, "+989120000001", Guid.NewGuid(), TestData.D1, Guid.NewGuid());
        var second = manager.Acquire(TestData.S2, "+989120000001", Guid.NewGuid(), TestData.D3, Guid.NewGuid(), 600);

        clock.Advance(TimeSpan.FromSeconds(299));
        Assert.True(manager.AcceptChange(TestData.S1, "+989120000001", TestData.D1, DraftChangeKind.Quantity));
        Assert.True(manager.PassiveHeartbeat(TestData.S2, "+989120000001", TestData.D3));
        Assert.Equal(TestData.T0.AddSeconds(299), first.LastActivityAt);
        Assert.Equal(TestData.T0, second.LastActivityAt);

        clock.Advance(TimeSpan.FromSeconds(299)); // T0+598: first is still active.
        Assert.True(manager.Evaluate(TestData.S1, "+989120000001"));
        clock.Advance(TimeSpan.FromSeconds(1)); // T0+599: first expires at the boundary.
        Assert.False(manager.Evaluate(TestData.S1, "+989120000001"));
        Assert.Equal(LockReleaseReason.Timeout, first.ReleaseReason);
        Assert.True(manager.Evaluate(TestData.S2, "+989120000001"));
        clock.Advance(TimeSpan.FromSeconds(1)); // T0+600: passive activity never renewed second.
        Assert.False(manager.Evaluate(TestData.S2, "+989120000001"));
    }

    [Fact]
    public void TC_D11_R7_DisconnectReleasesAndLateOwnerWriteCannotReactivate()
    {
        var manager = new DraftLockManager(new ManualClock(TestData.T0));
        var invoiceId = Guid.NewGuid();
        var first = manager.Acquire(TestData.S1, "+989120000001", invoiceId, TestData.D1, Guid.NewGuid());

        Assert.Equal(1, manager.Disconnect(TestData.D1));
        Assert.Equal(LockReleaseReason.Disconnect, first.ReleaseReason);
        Assert.False(manager.AcceptChange(TestData.S1, "+989120000001", TestData.D1, DraftChangeKind.Item));

        var second = manager.Acquire(TestData.S1, "+989120000001", Guid.NewGuid(), TestData.D2, Guid.NewGuid());
        Assert.Equal(TestData.D2, second.DeviceId);
        Assert.False(manager.AcceptChange(TestData.S1, "+989120000001", TestData.D1, DraftChangeKind.Item));
    }

    [Fact]
    public void TC_D11_R9_TimeoutRetiresDraftWithoutAdvancingNumbering()
    {
        var clock = new ManualClock(TestData.T0);
        var service = new InvoiceService(new Dictionary<Guid, long> { [TestData.S1] = 41 });
        var invoice = TestData.IssuableDraft(service, TestData.S1);
        var manager = new DraftLockManager(clock, id => service.Timeout(invoice, clock.UtcNow));
        manager.Acquire(TestData.S1, "+989120000001", invoice.InvoiceId, TestData.D1, Guid.NewGuid());

        clock.Advance(TimeSpan.FromSeconds(300));
        Assert.False(manager.Evaluate(TestData.S1, "+989120000001"));
        Assert.Equal(RetirementReason.Timeout, invoice.RetirementReason);
        Assert.Null(invoice.DisplayNumber);
        Assert.Equal(41, service.GetHighWatermark(TestData.S1));
        Assert.False(manager.AcceptChange(TestData.S1, "+989120000001", TestData.D1, DraftChangeKind.Item));
    }

    [Fact]
    public void TC_D11_R5_R6_R10_ReleaseReasonsAllowSequentialPurchases()
    {
        var manager = new DraftLockManager(new ManualClock(TestData.T0));
        var first = manager.Acquire(TestData.S1, "+989120000001", Guid.NewGuid(), TestData.D1, Guid.NewGuid());
        Assert.True(manager.Release(TestData.S1, "+989120000001", TestData.D1, LockReleaseReason.Issue));
        var second = manager.Acquire(TestData.S1, "+989120000001", Guid.NewGuid(), TestData.D2, Guid.NewGuid());
        Assert.True(manager.Release(TestData.S1, "+989120000001", TestData.D2, LockReleaseReason.Cancel));
        var third = manager.Acquire(TestData.S1, "+989120000001", Guid.NewGuid(), TestData.D1, Guid.NewGuid());

        Assert.Equal(LockReleaseReason.Issue, first.ReleaseReason);
        Assert.Equal(LockReleaseReason.Cancel, second.ReleaseReason);
        Assert.Equal(DraftLockState.Active, third.State);
    }
}
