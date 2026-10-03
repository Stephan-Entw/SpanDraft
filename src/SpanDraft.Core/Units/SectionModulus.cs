namespace SpanDraft.Core.Units;

/// <summary>A finite, non-negative quantity in cubic metres. The default value represents zero.</summary>
public readonly record struct SectionModulus
{
    private SectionModulus(double value) => CubicMeters = DomainGuard.NonNegative(value, nameof(value));

    public double CubicMeters { get; }

    public static SectionModulus FromCubicMeters(double value) => new(value);

    public double CubicMillimeters => CubicMeters * 1000000000;

    public static SectionModulus FromCubicMillimeters(double value) =>
        new(DomainGuard.NonNegative(value, nameof(value)) / 1000000000);
}
