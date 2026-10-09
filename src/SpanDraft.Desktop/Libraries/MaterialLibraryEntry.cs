using SpanDraft.Core.Materials;

namespace SpanDraft.Desktop.Libraries;

/// <summary>A material template with metadata that is never copied into a project.</summary>
public sealed class MaterialLibraryEntry
{
    public MaterialLibraryEntry(Material material, MaterialCategory category,
        string? materialNumber = null, IEnumerable<string>? otherStandards = null)
    {
        ArgumentNullException.ThrowIfNull(material);
        LibraryNames.Validate(material.Name);
        if (!Enum.IsDefined(category)) throw new ArgumentOutOfRangeException(nameof(category));
        Material = material;
        Category = category;
        MaterialNumber = string.IsNullOrWhiteSpace(materialNumber) ? null : materialNumber.Trim();
        OtherStandards = Array.AsReadOnly((otherStandards ?? []).Select(value =>
        {
            ArgumentNullException.ThrowIfNull(value);
            return value.Trim();
        }).Where(value => value.Length > 0).ToArray());
    }

    public Material Material { get; }
    public MaterialCategory Category { get; }
    public string? MaterialNumber { get; }
    public IReadOnlyList<string> OtherStandards { get; }
}
