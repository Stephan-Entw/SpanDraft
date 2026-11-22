using SpanDraft.Core.Materials;
using SpanDraft.Desktop.Libraries;
using SpanDraft.Desktop.Persistence;

namespace SpanDraft.Desktop.Services;

/// <summary>
/// The one authoritative user material library and the built-in catalog of the application. A mutation is visible only
/// after it was persisted; a library that failed to load stays closed instead of becoming an empty writable one.
/// </summary>
public sealed class MaterialLibraryService
{
    private readonly MaterialLibraryStore? _store;
    private readonly Func<BuiltInMaterialCatalog> _builtInLoader;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _sync = new();
    private Task? _loading;
    private volatile LibraryState<UserMaterialLibrary> _state = new(null, 0, LibraryFailure.NotLoaded);
    private BuiltInMaterialCatalog? _builtIns;
    private LibraryFailure? _builtInFailure;

    public MaterialLibraryService(MaterialLibraryStore? store, Func<BuiltInMaterialCatalog>? builtInLoader = null)
    {
        _store = store;
        _builtInLoader = builtInLoader ?? MaterialCatalogCodec.LoadBuiltIn;
        // The built-ins are an embedded resource, independent of any user file: they exist once per service, from the start.
        LoadBuiltIns();
    }

    /// <summary>A composition without persistence: built-ins are loaded, the user library is empty and cannot be changed.</summary>
    public static MaterialLibraryService Transient(Func<BuiltInMaterialCatalog>? builtInLoader = null)
    {
        return new MaterialLibraryService(null, builtInLoader) { _state = new(new UserMaterialLibrary(), 0, null) };
    }

    public LibraryState<UserMaterialLibrary> State => _state;
    public BuiltInMaterialCatalog? BuiltIns => _builtIns;
    public LibraryFailure? BuiltInFailure => _builtInFailure;
    public long Revision => _state.Revision;
    /// <summary>Mutations are possible only for an available library that has a persistence.</summary>
    public bool CanMutate => _state.IsAvailable && _store is not null;
    public event Action<LibraryChangedEventArgs>? Changed;

    /// <summary>Loads the user library once; later calls return the same load.</summary>
    public Task LoadAsync()
    {
        lock (_sync) return _loading ??= LoadCoreAsync();
    }

    private async Task LoadCoreAsync()
    {
        if (_store is null) _state = new(new UserMaterialLibrary(), 0, null);
        else
        {
            try { _state = new(await _store.LoadAsync(), 0, null); }
            catch (Exception e) when (LibraryFailure.IsFailure(e)) { _state = new(null, 0, LibraryFailure.From(e)); }
        }
        Changed?.Invoke(new(_state.Revision));
    }

    private void LoadBuiltIns()
    {
        try
        {
            var catalog = _builtInLoader();
            if (catalog.Find("S235JR") is null) throw new LibraryFormatException("Missing S235JR.");
            _builtIns = catalog; _builtInFailure = null;
        }
        catch (Exception e) when (LibraryFailure.IsFailure(e)) { _builtIns = null; _builtInFailure = LibraryFailure.From(e); }
    }

    public Task<LibraryMutationResult> AddAsync(Material entry) => MutateAsync(current =>
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (current.Find(entry.Name) is not null) return (LibraryConflictKind.AlreadyExists, null);
        return (null, next => next.Add(entry));
    });

    /// <summary>Replaces the entry the editor started from; the replacement may carry a new name.</summary>
    public Task<LibraryMutationResult> ReplaceAsync(Material expectedOriginal, Material replacement) => MutateAsync(current =>
    {
        ArgumentNullException.ThrowIfNull(expectedOriginal);
        ArgumentNullException.ThrowIfNull(replacement);
        if (Check(current, expectedOriginal, out var found) is { } conflict) return (conflict, null);
        if (current.Find(replacement.Name) is { } other && !ReferenceEquals(other, found)) return (LibraryConflictKind.TargetNameTaken, null);
        return (null, next => next.Replace(found!.Name, replacement));
    });

    public Task<LibraryMutationResult> RenameAsync(Material expectedOriginal, string newName)
    {
        ArgumentNullException.ThrowIfNull(expectedOriginal);
        Material renamed;
        try
        {
            if (expectedOriginal.StrengthType is not { } strength) throw new ArgumentException("A strength type is required.", nameof(expectedOriginal));
            renamed = new(newName, expectedOriginal.YoungsModulus, expectedOriginal.YieldStrength, strength, expectedOriginal.Density,
                expectedOriginal.PoissonRatio, expectedOriginal.Category, expectedOriginal.MaterialNumber, expectedOriginal.OtherStandards, expectedOriginal.Note);
        }
        catch (ArgumentException e) { return Task.FromResult(LibraryMutationResult.Invalid(e)); }
        return ReplaceAsync(expectedOriginal, renamed);
    }

    public Task<LibraryMutationResult> DeleteAsync(Material expectedOriginal) => MutateAsync(current =>
    {
        ArgumentNullException.ThrowIfNull(expectedOriginal);
        if (Check(current, expectedOriginal, out var found) is { } conflict) return (conflict, null);
        return (null, next => next.Remove(found!.Name));
    });

    // Only the entry the caller started from matters; changes to other entries are no conflict.
    private static LibraryConflictKind? Check(UserMaterialLibrary current, Material expected, out Material? found)
    {
        found = current.Find(expected.Name);
        if (found is null) return LibraryConflictKind.OriginalMissing;
        return MaterialSnapshotComparison.Equals(found, expected) ? null : LibraryConflictKind.OriginalChanged;
    }

    private async Task<LibraryMutationResult> MutateAsync(
        Func<UserMaterialLibrary, (LibraryConflictKind? Conflict, Action<UserMaterialLibrary>? Apply)> plan)
    {
        LibraryMutationResult result;
        await _gate.WaitAsync();
        try
        {
            var state = _state;
            if (!CanMutate) return LibraryMutationResult.Unavailable();
            var current = state.Snapshot!;
            UserMaterialLibrary next;
            try
            {
                var (conflict, apply) = plan(current);
                if (conflict is { } kind) return LibraryMutationResult.Conflicted(kind);
                // The published snapshot is never touched: the change is made on a copy and published after persisting.
                next = new UserMaterialLibrary(current.All);
                apply!(next);
            }
            catch (Exception e) when (e is ArgumentException or KeyNotFoundException) { return LibraryMutationResult.Invalid(e); }
            try { await _store!.SaveAsync(next); }
            catch (Exception e) when (LibraryFailure.IsFailure(e)) { return LibraryMutationResult.PersistFailed(e); }
            _state = new(next, state.Revision + 1, null);
            result = LibraryMutationResult.Committed(_state.Revision);
        }
        finally { _gate.Release(); }
        Changed?.Invoke(new(result.Revision));
        return result;
    }
}
