using Kandoo.Core;
using Xunit;

namespace Kandoo.Core.Tests;

public sealed class QuantityMillisTests
{
    [Fact]
    public void QuantityMillis_AddsScaledIntegerThousandths()
    {
        var oneUnit = QuantityMillis.FromUnits(1);
        var halfUnit = new QuantityMillis(500);

        var result = oneUnit.Add(halfUnit);

        Assert.Equal(1500L, result.Value);
    }
}
