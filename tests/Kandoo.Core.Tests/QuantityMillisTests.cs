using Kandoo.Core;
using Xunit;

namespace Kandoo.Core.Tests;

public sealed class QuantityMillisTests
{
    [Fact]
    public void ASM_22_QuantityAndMoneyUseSignedIntegerUnits()
    {
        var oneUnit = QuantityMillis.FromUnits(1);
        var halfUnit = new QuantityMillis(500);
        var negativeTwoUnits = new QuantityMillis(-2000);
        var rials = new Rials(1000).Add(new Rials(250));

        Assert.Equal(1500L, oneUnit.Add(halfUnit).Value);
        Assert.Equal(-2000L, negativeTwoUnits.Value);
        Assert.Equal(1250L, rials.Value);
        Assert.Equal(-1500L, oneUnit.Add(negativeTwoUnits).Value);
    }
}
