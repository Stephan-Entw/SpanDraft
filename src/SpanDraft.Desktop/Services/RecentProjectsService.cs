using SpanDraft.Desktop.Persistence;

namespace SpanDraft.Desktop.Services;

/// <summary>
/// The one recent-projects state of the application. All start pages read this list. Normalization, ordering, the limit and
/// the handling of an unreadable file stay with <see cref="RecentProjectsStore"/>; persistence remains best effort.
/// </summary>
public sealed class RecentProjectsService(RecentProjectsStore? store)
{
    private readonly object _sync = new();
    private IReadOnlyList<RecentProjectEntry> _entries = [];
    private long _revision;

    public IReadOnlyList<RecentProjectEntry> Entries { get { lock (_sync) return _entries; } }
    public long Revision { get { lock (_sync) return _revision; } }
    public event Action? Changed;

    /// <summary>Loads the stored list; the store makes this idempotent and retries after a transient read failure.</summary>
    public async Task LoadAsync()
    {
        if (store is null) return;
        await store.LoadAsync();
        Publish();
    }

    public async Task RecordUseAsync(string projectPath, string? replacedPath = null)
    {
        if (store is null) { PublishLocal(entries => Add(entries, projectPath, replacedPath)); return; }
        await store.RecordUseAsync(projectPath, replacedPath);
        Publish();
    }

    public async Task RemoveAsync(string projectPath)
    {
        if (store is null)
        {
            string full = Path.GetFullPath(projectPath);
            PublishLocal(entries => entries.Where(e => !RecentProjectsCodec.PathComparer.Equals(e.Path, full)).ToArray());
            return;
        }
        await store.RemoveAsync(projectPath);
        Publish();
    }

    // The store owns the list; the service publishes whatever it holds after each completed operation.
    private void Publish()
    {
        var latest = store!.Entries;
        lock (_sync)
        {
            if (ReferenceEquals(_entries, latest)) return;
            _entries = latest; _revision++;
        }
        Changed?.Invoke();
    }

    private void PublishLocal(Func<IReadOnlyList<RecentProjectEntry>, IReadOnlyList<RecentProjectEntry>> change)
    {
        lock (_sync) { _entries = change(_entries); _revision++; }
        Changed?.Invoke();
    }

    private static IReadOnlyList<RecentProjectEntry> Add(IReadOnlyList<RecentProjectEntry> entries, string projectPath, string? replacedPath)
    {
        string full = Path.GetFullPath(projectPath);
        string? previous = replacedPath is null ? null : Path.GetFullPath(replacedPath);
        return RecentProjectsCodec.Normalize(new[] { new RecentProjectEntry(full, DateTimeOffset.UtcNow) }.Concat(entries.Where(e =>
            !RecentProjectsCodec.PathComparer.Equals(e.Path, full) && !RecentProjectsCodec.PathComparer.Equals(e.Path, previous))));
    }
}
