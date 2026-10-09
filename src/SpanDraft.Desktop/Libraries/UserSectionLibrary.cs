namespace SpanDraft.Desktop.Libraries;

public sealed class UserSectionLibrary
{
    private readonly NamedLibraryEntries<SectionLibraryEntry> entries;

    public UserSectionLibrary(IEnumerable<SectionLibraryEntry>? entries = null) =>
        this.entries = new(entries ?? [], entry => entry.Name);

    public IReadOnlyList<SectionLibraryEntry> All => entries.All;
    public SectionLibraryEntry? Find(string name) => entries.Find(name);
    public void Add(SectionLibraryEntry entry) => entries.Add(entry);
    public void Replace(string oldName, SectionLibraryEntry entry) => entries.Replace(oldName, entry);
    public bool Remove(string name) => entries.Remove(name);
}
