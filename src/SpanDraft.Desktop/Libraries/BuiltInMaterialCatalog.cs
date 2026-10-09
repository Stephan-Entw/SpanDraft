namespace SpanDraft.Desktop.Libraries;

/// <summary>A read-only source, separate from user material names and user persistence.</summary>
public sealed class BuiltInMaterialCatalog
{
    private readonly NamedLibraryEntries<MaterialLibraryEntry> entries;

    public BuiltInMaterialCatalog(IEnumerable<MaterialLibraryEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        this.entries = new(entries, entry => entry.Material.Name);
    }

    public IReadOnlyList<MaterialLibraryEntry> All => entries.All;
    public MaterialLibraryEntry? Find(string name) => entries.Find(name);
}
