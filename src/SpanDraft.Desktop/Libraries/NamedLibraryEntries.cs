namespace SpanDraft.Desktop.Libraries;

internal static class LibraryNames
{
    internal static void Validate(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name != name.Trim()) throw new ArgumentException("Library names must be trimmed.", nameof(name));
    }
}

/// <summary>Ordered local keys, without persistent or historical entry identity.</summary>
internal sealed class NamedLibraryEntries<T> where T : class
{
    private readonly List<T> entries = [];
    private readonly Func<T, string> name;

    internal NamedLibraryEntries(IEnumerable<T> initial, Func<T, string> name)
    {
        this.name = name;
        All = entries.AsReadOnly();
        foreach (var entry in initial) Add(entry);
    }

    internal IReadOnlyList<T> All { get; }
    internal T? Find(string key)
    {
        int index = Index(key);
        return index < 0 ? null : entries[index];
    }

    internal void Add(T entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (Index(name(entry)) >= 0) throw new ArgumentException("Library names must be unique.", nameof(entry));
        entries.Add(entry);
    }

    internal void Replace(string oldName, T entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        int index = Index(oldName);
        if (index < 0) throw new KeyNotFoundException("Library entry not found: " + oldName);
        int collision = Index(name(entry));
        if (collision >= 0 && collision != index)
            throw new ArgumentException("Library names must be unique.", nameof(entry));
        entries[index] = entry;
    }

    internal bool Remove(string key)
    {
        int index = Index(key);
        if (index < 0) return false;
        entries.RemoveAt(index);
        return true;
    }

    private int Index(string key)
    {
        LibraryNames.Validate(key);
        return entries.FindIndex(entry => StringComparer.OrdinalIgnoreCase.Equals(name(entry), key));
    }
}
