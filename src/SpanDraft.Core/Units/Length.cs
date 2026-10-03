namespace SpanDraft.Core.Units;

/// <summary>A finite, non-negative quantity in metres. The default value represents zero.</summary>
public readonly record struct Length
{
    private Length(double value) => Meters = DomainGuard.NonNegative(value, nameof(value));

    public double Meters { get; }

    public static Length FromMeters(double value) => new(value);

    public double Millimeters => Meters * 1000;

    public static Length FromMillimeters(double value) =>
        new(DomainGuard.NonNegative(value, nameof(value)) / 1000);
}
