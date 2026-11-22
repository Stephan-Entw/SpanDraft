using SpanDraft.Desktop.Libraries;
using SpanDraft.Desktop.Persistence;

namespace SpanDraft.Desktop.Services;

/// <summary>The one authoritative user section library; same fail-closed and transaction rules as the material library.</summary>
public sealed class SectionLibraryService(SectionLibraryStore? store)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _sync = new();
    private Task? _loading;
    private volatile LibraryState<UserSectionLibrary> _state = new(null, 0, LibraryFailure.NotLoaded);

    /// <summary>A composition without persistence: the library is empty and cannot be changed.</summary>
    public static SectionLibraryService Transient() => new(null) { _state = new(new UserSectionLibrary(), 0, null) };

    public LibraryState<UserSectionLibrary> State => _state;
    public long Revision => _state.Revision;
    public bool CanMutate => _state.IsAvailable && store is not null;
    public event Action<LibraryChangedEventArgs>? Changed;

    public Task LoadAsync()
    {
        lock (_sync) return _loading ??= LoadCoreAsync();
    }

    private async Task LoadCoreAsync()
    {
        if (store is null) _state = new(new UserSectionLibrary(), 0, null);
        else
        {
            try { _state = new(await store.LoadAsync(), 0, null); }
            catch (Exception e) when (LibraryFailure.IsFailure(e)) { _state = new(null, 0, LibraryFailure.From(e)); }
        }
        Changed?.Invoke(new(_state.Revision));
    }

    public Task<LibraryMutationResult> AddAsync(SectionLibraryEntry entry) => MutateAsync(current =>
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (current.Find(entry.Name) is not null) return (LibraryConflictKind.AlreadyExists, null);
        return (null, next => next.Add(entry));
    });

    public Task<LibraryMutationResult> ReplaceAsync(SectionLibraryEntry expectedOriginal, SectionLibraryEntry replacement) => MutateAsync(current =>
    {
        ArgumentNullException.ThrowIfNull(expectedOriginal);
        ArgumentNullException.ThrowIfNull(replacement);
        if (Check(current, expectedOriginal, out var found) is { } conflict) return (conflict, null);
        if (current.Find(replacement.Name) is { } other && !ReferenceEquals(other, found)) return (LibraryConflictKind.TargetNameTaken, null);
        return (null, next => next.Replace(found!.Name, replacement));
    });

    public Task<LibraryMutationResult> RenameAsync(SectionLibraryEntry expectedOriginal, string newName)
    {
        ArgumentNullException.ThrowIfNull(expectedOriginal);
        SectionLibraryEntry renamed;
        try { renamed = new(newName, expectedOriginal.Section); }
        catch (ArgumentException e) { return Task.FromResult(LibraryMutationResult.Invalid(e)); }
        return ReplaceAsync(expectedOriginal, renamed);
    }

    public Task<LibraryMutationResult> DeleteAsync(SectionLibraryEntry expectedOriginal) => MutateAsync(current =>
    {
        ArgumentNullException.ThrowIfNull(expectedOriginal);
        if (Check(current, expectedOriginal, out var found) is { } conflict) return (conflict, null);
        return (null, next => next.Remove(found!.Name));
    });

    private static LibraryConflictKind? Check(UserSectionLibrary current, SectionLibraryEntry expected, out SectionLibraryEntry? found)
    {
        found = current.Find(expected.Name);
        if (found is null) return LibraryConflictKind.OriginalMissing;
        return SameContent(found, expected) ? null : LibraryConflictKind.OriginalChanged;
    }

    // Entries have no identity or equality of their own: they are the same when they persist as the same name and definition.
    private static bool SameContent(SectionLibraryEntry a, SectionLibraryEntry b) =>
        ReferenceEquals(a, b) || SectionLibraryCodec.Serialize(new([a])).AsSpan().SequenceEqual(SectionLibraryCodec.Serialize(new([b])));

    private async Task<LibraryMutationResult> MutateAsync(
        Func<UserSectionLibrary, (LibraryConflictKind? Conflict, Action<UserSectionLibrary>? Apply)> plan)
    {
        LibraryMutationResult result;
        await _gate.WaitAsync();
        try
        {
            var state = _state;
            if (!CanMutate) return LibraryMutationResult.Unavailable();
            var current = state.Snapshot!;
            UserSectionLibrary next;
            try
            {
                var (conflict, apply) = plan(current);
                if (conflict is { } kind) return LibraryMutationResult.Conflicted(kind);
                // The published snapshot is never touched: the change is made on a copy and published after persisting.
                next = new UserSectionLibrary(current.All);
                apply!(next);
            }
            catch (Exception e) when (e is ArgumentException or KeyNotFoundException) { return LibraryMutationResult.Invalid(e); }
            try { await store!.SaveAsync(next); }
            catch (Exception e) when (LibraryFailure.IsFailure(e)) { return LibraryMutationResult.PersistFailed(e); }
            _state = new(next, state.Revision + 1, null);
            result = LibraryMutationResult.Committed(_state.Revision);
        }
        finally { _gate.Release(); }
        Changed?.Invoke(new(result.Revision));
        return result;
    }
}
