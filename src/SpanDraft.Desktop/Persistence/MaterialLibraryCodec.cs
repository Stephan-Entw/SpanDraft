using System.Text.Json;
using SpanDraft.Core.Materials;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Libraries;
using static SpanDraft.Desktop.Persistence.LibraryJson;

namespace SpanDraft.Desktop.Persistence;

public static class MaterialLibraryCodec
{
    public const string Format = "SpanDraft.MaterialLibrary";
    public const int Version = 1;

    public static byte[] Serialize(UserMaterialLibrary library)
    {
        ArgumentNullException.ThrowIfNull(library);
        return JsonSerializer.SerializeToUtf8Bytes(new MaterialLibraryV1
        {
            Format = Format, FormatVersion = Version,
            Entries = library.All.Select(MaterialLibraryMapping.ToDto).ToArray()
        }, Options);
    }

    public static UserMaterialLibrary Deserialize(ReadOnlySpan<byte> bytes) =>
        Read<MaterialLibraryV1, UserMaterialLibrary>(bytes, Format, Version,
            file => new(Required(file.Entries, "entries").Select(MaterialLibraryMapping.FromDto)));
}

internal static class MaterialLibraryMapping
{
    internal static MaterialLibraryEntryV1 ToDto(MaterialLibraryEntry entry) => new()
    {
        Material = new()
        {
            Name = entry.Material.Name,
            YoungsModulus = entry.Material.YoungsModulus.Pascals,
            YieldStrength = entry.Material.YieldStrength.Pascals,
            Density = entry.Material.Density?.KilogramsPerCubicMeter,
            PoissonRatio = entry.Material.PoissonRatio?.Value
        },
        Category = entry.Category switch
        {
            MaterialCategory.StructuralSteel => "structuralSteel",
            MaterialCategory.StainlessSteel => "stainlessSteel",
            MaterialCategory.AlloySteel => "alloySteel",
            MaterialCategory.Aluminium => "aluminium",
            MaterialCategory.Other => "other",
            _ => throw new LibraryFormatException("Unknown material category.")
        },
        MaterialNumber = entry.MaterialNumber,
        OtherStandards = entry.OtherStandards.Count == 0 ? null : entry.OtherStandards.ToArray()
    };

    internal static MaterialLibraryEntry FromDto(MaterialLibraryEntryV1 entry)
    {
        Required(entry, "entry");
        var m = Required(entry.Material, "material");
        return new(new Material(Required(m.Name, "name"),
            Pressure.FromPascals(Number(m.YoungsModulus, "youngsModulus")),
            Pressure.FromPascals(Number(m.YieldStrength, "yieldStrength")),
            m.Density is { } rho ? MassDensity.FromKilogramsPerCubicMeter(Number(rho, "density")) : null,
            m.PoissonRatio is { } nu ? PoissonRatio.FromValue(Number(nu, "poissonRatio")) : null),
            entry.Category switch
            {
                "structuralSteel" => MaterialCategory.StructuralSteel,
                "stainlessSteel" => MaterialCategory.StainlessSteel,
                "alloySteel" => MaterialCategory.AlloySteel,
                "aluminium" => MaterialCategory.Aluminium,
                "other" => MaterialCategory.Other,
                _ => throw new LibraryFormatException("Unknown material category.")
            }, entry.MaterialNumber, entry.OtherStandards);
    }
}
