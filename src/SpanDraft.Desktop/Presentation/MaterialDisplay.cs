using SpanDraft.Core.Materials;

namespace SpanDraft.Desktop.Presentation;

internal static class MaterialDisplay
{
    internal static string Format(Material material)
    {
        ArgumentNullException.ThrowIfNull(material);
        string name = material.MaterialNumber is { } number ? $"{material.Name} ({number})" : material.Name;
        return material.OtherStandards.Count == 0 ? name : name + " · " + string.Join(" · ", material.OtherStandards);
    }
}
