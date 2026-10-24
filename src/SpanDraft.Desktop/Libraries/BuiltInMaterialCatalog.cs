using SpanDraft.Core.Materials;

namespace SpanDraft.Desktop.Libraries;

/// <summary>A read-only source, separate from user material names and user persistence.</summary>
public sealed class BuiltInMaterialCatalog
{
    private readonly NamedLibraryEntries<Material> entries;

    public BuiltInMaterialCatalog(IEnumerable<Material> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        this.entries = new(entries, entry => entry.Name);
    }

    public IReadOnlyList<Material> All => entries.All;
    public Material? Find(string name) => entries.Find(name);
}
