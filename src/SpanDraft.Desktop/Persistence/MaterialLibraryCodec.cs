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
    internal static MaterialLibraryEntryV1 ToDto(Material entry) => new()
    {
        Material = new()
        {
            Name = entry.Name,
            YoungsModulus = entry.YoungsModulus.Pascals,
            YieldStrength = entry.YieldStrength.Pascals,
            StrengthType = MaterialStrengthWire.Write(entry.StrengthType),
            Density = entry.Density?.KilogramsPerCubicMeter,
            PoissonRatio = entry.PoissonRatio?.Value
        },
        Category = MaterialCategoryWire.Write(entry.Category),
        MaterialNumber = entry.MaterialNumber,
        OtherStandards = entry.OtherStandards.Count == 0 ? null : entry.OtherStandards.ToArray()
    };

    internal static Material FromDto(MaterialLibraryEntryV1 entry)
    {
        Required(entry, "entry");
        var m = Required(entry.Material, "material");
        return MaterialStrengthWire.Read(Required(m.Name, "name"),
            Pressure.FromPascals(Number(m.YoungsModulus, "youngsModulus")),
            Pressure.FromPascals(Number(m.YieldStrength, "yieldStrength")), m.StrengthType,
            m.Density is { } rho ? MassDensity.FromKilogramsPerCubicMeter(Number(rho, "density")) : null,
            m.PoissonRatio is { } nu ? PoissonRatio.FromValue(Number(nu, "poissonRatio")) : null,
            MaterialCategoryWire.Read(entry.Category), entry.MaterialNumber, entry.OtherStandards);
    }
}
