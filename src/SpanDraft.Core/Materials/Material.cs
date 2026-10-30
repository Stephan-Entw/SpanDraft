using SpanDraft.Core.Units;

namespace SpanDraft.Core.Materials;

/// <summary>A material snapshot with optional density and Poisson ratio.</summary>
public sealed class Material
{
    public Material(string name, Pressure youngsModulus, Pressure yieldStrength, MaterialStrengthType strengthType,
        MassDensity? density = null, PoissonRatio? poissonRatio = null, MaterialCategory category = MaterialCategory.Other,
        string? materialNumber = null, IEnumerable<string>? otherStandards = null)
        : this(name, youngsModulus, yieldStrength, (MaterialStrengthType?)strengthType, density, poissonRatio, category, materialNumber, otherStandards)
    {
    }

    private Material(string name, Pressure youngsModulus, Pressure yieldStrength, MaterialStrengthType? strengthType,
        MassDensity? density, PoissonRatio? poissonRatio, MaterialCategory category, string? materialNumber, IEnumerable<string>? otherStandards)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        DomainGuard.Positive(youngsModulus.Pascals, nameof(youngsModulus));
        DomainGuard.Positive(yieldStrength.Pascals, nameof(yieldStrength));
        if (density is { } rho) DomainGuard.Positive(rho.KilogramsPerCubicMeter, nameof(density));
        if (strengthType is { } type && !Enum.IsDefined(type))
            throw new ArgumentOutOfRangeException(nameof(strengthType));
        if (!Enum.IsDefined(category)) throw new ArgumentOutOfRangeException(nameof(category));

        Name = name;
        YoungsModulus = youngsModulus;
        YieldStrength = yieldStrength;
        StrengthType = strengthType;
        Density = density;
        PoissonRatio = poissonRatio;
        Category = category;
        MaterialNumber = string.IsNullOrWhiteSpace(materialNumber) ? null : materialNumber.Trim();
        OtherStandards = Array.AsReadOnly((otherStandards ?? []).Select(value =>
        {
            ArgumentNullException.ThrowIfNull(value);
            return value.Trim();
        }).Where(value => value.Length > 0).ToArray());
    }

    public string Name { get; }
    public Pressure YoungsModulus { get; }
    public Pressure YieldStrength { get; }
    /// <summary>Null only for historical data whose strength type has not yet been confirmed.</summary>
    public MaterialStrengthType? StrengthType { get; }
    public MassDensity? Density { get; }
    public PoissonRatio? PoissonRatio { get; }
    public MaterialCategory Category { get; }
    public string? MaterialNumber { get; }
    public IReadOnlyList<string> OtherStandards { get; }

    internal static Material FromLegacy(string name, Pressure youngsModulus, Pressure yieldStrength,
        MassDensity? density, PoissonRatio? poissonRatio, MaterialCategory category = MaterialCategory.Other,
        string? materialNumber = null, IEnumerable<string>? otherStandards = null) =>
        new(name, youngsModulus, yieldStrength, null, density, poissonRatio, category, materialNumber, otherStandards);
}
