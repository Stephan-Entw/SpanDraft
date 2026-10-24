using SpanDraft.Core.Materials;

namespace SpanDraft.Desktop.Persistence;

internal static class MaterialCategoryWire
{
    internal static string Write(MaterialCategory category) => category switch
    {
        MaterialCategory.StructuralSteel => "structuralSteel",
        MaterialCategory.StainlessSteel => "stainlessSteel",
        MaterialCategory.AlloySteel => "alloySteel",
        MaterialCategory.Aluminium => "aluminium",
        MaterialCategory.Other => "other",
        _ => throw new ArgumentOutOfRangeException(nameof(category))
    };

    internal static MaterialCategory Read(string category) => category switch
    {
        "structuralSteel" => MaterialCategory.StructuralSteel,
        "stainlessSteel" => MaterialCategory.StainlessSteel,
        "alloySteel" => MaterialCategory.AlloySteel,
        "aluminium" => MaterialCategory.Aluminium,
        "other" => MaterialCategory.Other,
        _ => throw new ArgumentException("Unknown material category.", nameof(category))
    };
}
