namespace SpanDraft.Core.Units;

/// <summary>A finite, non-negative quantity in square metres. The default value represents zero.</summary>
public readonly record struct Area
{
    private Area(double value) => SquareMeters = DomainGuard.NonNegative(value, nameof(value));

    public double SquareMeters { get; }

    public static Area FromSquareMeters(double value) => new(value);

    public double SquareMillimeters => SquareMeters * 1000000;

    public static Area FromSquareMillimeters(double value) =>
        new(DomainGuard.NonNegative(value, nameof(value)) / 1000000);
}
