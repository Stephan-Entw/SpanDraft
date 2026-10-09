namespace SpanDraft.Desktop.Libraries;

public sealed class UserMaterialLibrary
{
    private readonly NamedLibraryEntries<MaterialLibraryEntry> entries;

    public UserMaterialLibrary(IEnumerable<MaterialLibraryEntry>? entries = null) =>
        this.entries = new(entries ?? [], entry => entry.Material.Name);

    public IReadOnlyList<MaterialLibraryEntry> All => entries.All;
    public MaterialLibraryEntry? Find(string name) => entries.Find(name);
    public void Add(MaterialLibraryEntry entry) => entries.Add(entry);
    public void Replace(string oldName, MaterialLibraryEntry entry) => entries.Replace(oldName, entry);
    public bool Remove(string name) => entries.Remove(name);
}
