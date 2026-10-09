namespace SpanDraft.Core.Units;

/// <summary>A finite, strictly positive mass density in kg/m³. Material rejects the zero default.</summary>
public readonly record struct MassDensity
{
    private MassDensity(double value) => KilogramsPerCubicMeter = DomainGuard.Positive(value, nameof(value));

    public double KilogramsPerCubicMeter { get; }

    public static MassDensity FromKilogramsPerCubicMeter(double value) => new(value);
}
