using SpanDraft.Core.Materials;
using SpanDraft.Core.Units;

namespace SpanDraft.Desktop.Persistence;

internal static class MaterialStrengthWire
{
    internal static string? Write(MaterialStrengthType? type) => type switch
    {
        MaterialStrengthType.Re => "re",
        MaterialStrengthType.Rp02 => "rp02",
        null => null,
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    internal static Material Read(string name, Pressure youngsModulus, Pressure strength, string? type,
        MassDensity? density, PoissonRatio? poissonRatio, MaterialCategory category = MaterialCategory.Other,
        string? materialNumber = null, IEnumerable<string>? otherStandards = null) => type switch
    {
        "re" => new(name, youngsModulus, strength, MaterialStrengthType.Re, density, poissonRatio, category, materialNumber, otherStandards),
        "rp02" => new(name, youngsModulus, strength, MaterialStrengthType.Rp02, density, poissonRatio, category, materialNumber, otherStandards),
        null => Material.FromLegacy(name, youngsModulus, strength, density, poissonRatio, category, materialNumber, otherStandards),
        _ => throw new ArgumentException("Unknown material strength type.", nameof(type))
    };
}
