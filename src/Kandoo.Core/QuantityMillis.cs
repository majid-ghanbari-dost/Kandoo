namespace Kandoo.Core;

/// <summary>
/// A quantity expressed as signed integer thousandths. No floating-point
/// representation is used by the domain core.
/// </summary>
public readonly record struct QuantityMillis(long Value)
{
    public static QuantityMillis Zero => new(0);

    public static QuantityMillis FromUnits(long units) => new(checked(units * 1000L));

    public QuantityMillis Add(QuantityMillis other) => new(checked(Value + other.Value));

    public QuantityMillis Subtract(QuantityMillis other) => new(checked(Value - other.Value));

    public QuantityMillis Negate() => new(checked(-Value));
}
