using Kandoo.Core;
using Xunit;

namespace Kandoo.Core.Tests;

public sealed class InventoryLedgerTests
{
    [Fact]
    public void TC_D10_R1_R3_E09_SignedLedgerDerivesStockAndAllowsNegativeStock()
    {
        var ledger = new InventoryLedger();
        ledger.Append(Event(TestData.S1, 10000, "op-1"));
        ledger.Append(Event(TestData.S1, -3000, "op-2"));

        Assert.Equal(7000, ledger.StockMillis(TestData.S1, "prod-1"));
        ledger.Append(Event(TestData.S1, -10000, "op-3"));
        Assert.Equal(-3000, ledger.StockMillis(TestData.S1, "prod-1"));
        Assert.Equal(3, ledger.Events.Count);
    }

    [Fact]
    public void TC_D10_R2_CorrectionAppendsAndOriginalBytesRemainUnchanged()
    {
        var ledger = new InventoryLedger();
        var original = ledger.Append(Event(TestData.S1, 10000, "op-1"));
        var before = original with { };

        Assert.Throws<InvalidOperationException>(() => ledger.Update(original with { QuantityDeltaMillis = 6000 }));
        Assert.Throws<InvalidOperationException>(() => ledger.Delete(original.InventoryEventId));
        ledger.AppendCorrection(original, Guid.NewGuid(), -2000, "op-2", TestData.D1, "correction");

        Assert.Equal(before, ledger.Events[0]);
        Assert.Equal(8000, ledger.StockMillis(TestData.S1, "prod-1"));
        Assert.Equal(InventoryEventType.Correction, ledger.Events[1].EventType);
        Assert.Equal(original.InventoryEventId, ledger.Events[1].ReversesEventId);
    }

    [Fact]
    public void TC_SI_R6_StockAndEventListsAreStrictlyStoreScoped()
    {
        var ledger = new InventoryLedger();
        ledger.Append(Event(TestData.S1, 5000, "op-s1"));
        ledger.Append(Event(TestData.S2, 9000, "op-s2"));
        ledger.Append(Event(TestData.S1, -2000, "op-s1-adjust"));

        Assert.Equal(3000, ledger.StockMillis(TestData.S1, "prod-1"));
        Assert.Equal(9000, ledger.StockMillis(TestData.S2, "prod-1"));
        Assert.Equal(2, ledger.Events.Count(e => e.StoreId == TestData.S1));
        Assert.Single(ledger.Events.Where(e => e.StoreId == TestData.S2));
    }

    [Fact]
    public void TC_MD_R8_ConcurrentLedgerCommandsBothAppend()
    {
        var ledger = new InventoryLedger();
        ledger.Append(Event(TestData.S1, 10000, "op-initial"));
        Parallel.For(0, 2, index =>
        {
            var delta = index == 0 ? 5000 : -2000;
            ledger.Append(Event(TestData.S1, delta, $"op-concurrent-{index}"));
        });

        Assert.Equal(13000, ledger.StockMillis(TestData.S1, "prod-1"));
        Assert.Equal(3, ledger.Events.Count);
    }

    private static InventoryEvent Event(Guid storeId, long delta, string operationId)
    {
        return new(Guid.NewGuid(), storeId, "prod-1", delta, delta < 0 ? InventoryEventType.Sale : InventoryEventType.Receipt, operationId, TestData.D1);
    }
}
