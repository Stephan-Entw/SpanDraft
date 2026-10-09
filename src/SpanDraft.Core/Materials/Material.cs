using SpanDraft.Core.Units;

namespace SpanDraft.Core.Materials;

/// <summary>A material snapshot. Density and Poisson ratio are currently unused by analysis.</summary>
public sealed class Material
{
    public Material(string name, Pressure youngsModulus, Pressure yieldStrength)
        : this(name, youngsModulus, yieldStrength, null, null)
    {
    }

    public Material(string name, Pressure youngsModulus, Pressure yieldStrength,
        MassDensity? density, PoissonRatio? poissonRatio)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        DomainGuard.Positive(youngsModulus.Pascals, nameof(youngsModulus));
        DomainGuard.Positive(yieldStrength.Pascals, nameof(yieldStrength));
        if (density is { } rho) DomainGuard.Positive(rho.KilogramsPerCubicMeter, nameof(density));

        Name = name;
        YoungsModulus = youngsModulus;
        YieldStrength = yieldStrength;
        Density = density;
        PoissonRatio = poissonRatio;
    }

    public string Name { get; }
    public Pressure YoungsModulus { get; }
    public Pressure YieldStrength { get; }
    public MassDensity? Density { get; }
    public PoissonRatio? PoissonRatio { get; }
}
