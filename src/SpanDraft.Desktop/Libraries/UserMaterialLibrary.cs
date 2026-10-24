using SpanDraft.Core.Materials;

namespace SpanDraft.Desktop.Libraries;

public sealed class UserMaterialLibrary
{
    private readonly NamedLibraryEntries<Material> entries;

    public UserMaterialLibrary(IEnumerable<Material>? entries = null) =>
        this.entries = new(entries ?? [], entry => entry.Name);

    public IReadOnlyList<Material> All => entries.All;
    public Material? Find(string name) => entries.Find(name);
    public void Add(Material entry) { RequireStrengthType(entry); entries.Add(entry); }
    public void Replace(string oldName, Material entry) { RequireStrengthType(entry); entries.Replace(oldName, entry); }

    private static void RequireStrengthType(Material entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.StrengthType is null)
            throw new ArgumentException("A new or replaced material requires a strength type.", nameof(entry));
    }
    public bool Remove(string name) => entries.Remove(name);
}
