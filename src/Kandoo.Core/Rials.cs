namespace Kandoo.Core;

/// <summary>Integer Iranian Rials. Fractional and binary floating-point money are not represented.</summary>
public readonly record struct Rials(long Value)
{
    public static Rials Zero => new(0);

    public Rials Add(Rials other) => new(checked(Value + other.Value));

    public Rials Subtract(Rials other) => new(checked(Value - other.Value));

    public void RequireNonNegative()
    {
        if (Value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Value), "Rial amounts cannot be negative in this context.");
        }
    }
}
