using SpanDraft.Core.Units;

namespace SpanDraft.Core.Materials;

/// <summary>Material properties for elastic beam analysis and a later yield-strength assessment.</summary>
public sealed class Material
{
    public Material(string name, Pressure youngsModulus, Pressure yieldStrength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        DomainGuard.Positive(youngsModulus.Pascals, nameof(youngsModulus));
        DomainGuard.Positive(yieldStrength.Pascals, nameof(yieldStrength));

        Name = name;
        YoungsModulus = youngsModulus;
        YieldStrength = yieldStrength;
    }

    public string Name { get; }
    public Pressure YoungsModulus { get; }
    public Pressure YieldStrength { get; }
}
