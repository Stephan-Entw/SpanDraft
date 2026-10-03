namespace SpanDraft.Core.Units;

/// <summary>A finite, signed quantity in pascals. The default value represents zero.</summary>
public readonly record struct Pressure
{
    private Pressure(double value) => Pascals = DomainGuard.Finite(value, nameof(value));

    public double Pascals { get; }

    public static Pressure FromPascals(double value) => new(value);

    public double Megapascals => Pascals / 1e6;

    public static Pressure FromMegapascals(double value) =>
        new(DomainGuard.Finite(value, nameof(value)) * 1e6);
}
