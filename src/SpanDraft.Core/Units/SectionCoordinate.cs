namespace SpanDraft.Core.Units;

/// <summary>A finite, signed cross-section coordinate in metres. The default value is zero.</summary>
public readonly record struct SectionCoordinate
{
    private SectionCoordinate(double value) => Meters = DomainGuard.Finite(value, nameof(value));

    public double Meters { get; }
    public double Millimeters => Meters * 1000;

    public static SectionCoordinate FromMeters(double value) => new(value);
    public static SectionCoordinate FromMillimeters(double value) =>
        new(DomainGuard.Finite(value, nameof(value)) / 1000);
}