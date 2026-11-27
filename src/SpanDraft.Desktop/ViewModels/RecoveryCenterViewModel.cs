using System.Globalization;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Resources;

namespace SpanDraft.Desktop.ViewModels;

/// <summary>
/// The Recovery Center workspace: every undecided recovery candidate with restore and discard, plus the bulk actions.
/// It decides nothing on its own; restoring is the workspace manager's transaction, discarding the catalog's. Leaving it
/// never quits the application, and it leaves by itself when nothing undecided remains.
/// </summary>
public sealed class RecoveryCenterViewModel : ObservableObject, IDisposable
{
    private readonly RecoveryCatalog _catalog;
    private readonly Func<Guid, Task<bool>> _restore;
    private readonly Action _leave;
    private readonly Func<Exception, Task> _reportError;
    private bool _busy, _disposed;
    private Guid? _focused;

    internal RecoveryCenterViewModel(RecoveryCatalog catalog, Func<Guid, Task<bool>> restore, Action leave,
        Func<Exception, Task> reportError, Guid? focus = null)
    {
        _catalog = catalog;
        _restore = restore;
        _leave = leave;
        _reportError = reportError;
        _focused = focus;
        RestoreAllCommand = new(RestoreAllAsync, () => !_busy && Items.Any(static i => i.CanRestore));
        DiscardUnreadableCommand = new(DiscardUnreadableAsync, () => !_busy && HasUnreadable);
        BackCommand = new(() => { _leave(); return Task.FromResult(true); }, () => !_busy);
        _catalog.Changed += Refresh;
        Rebuild();
    }

    public string Title => Strings.RecoveryCenter;
    public IReadOnlyList<RecoveryItemViewModel> Items { get; private set; } = [];
    public bool IsEmpty => Items.Count == 0;
    public bool HasUnreadable => Items.Any(static i => i.IsUnreadable);
    public bool IsBusy => _busy;
    public AsyncActionCommand RestoreAllCommand { get; }
    public AsyncActionCommand DiscardUnreadableCommand { get; }
    public AsyncActionCommand BackCommand { get; }

    /// <summary>Marks the candidate a refused open of its file pointed at, so it is found at once.</summary>
    internal void Focus(Guid candidateId)
    {
        _focused = candidateId;
        foreach (var item in Items) item.IsFocused = item.CandidateId == candidateId;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _catalog.Changed -= Refresh;
    }

    private void Refresh()
    {
        if (_disposed) return;
        Rebuild();
        if (!_catalog.HasUndecided && !_busy) _leave();
    }

    private void Rebuild()
    {
        var undecided = _catalog.Undecided;
        // Valid projects first (a file's newest variant before its older ones), then drafts, then what cannot be read.
        Items = undecided
            .OrderBy(static c => c.IsDamaged ? 2 : c.Kind == RecoverySlotKind.Project ? 0 : 1)
            .ThenBy(static c => c.OriginalFilePath, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(static c => c.IsPreferred)
            .ThenByDescending(static c => c.WrittenAtUtc)
            .Select(c => new RecoveryItemViewModel(c, this) { IsFocused = c.CandidateId == _focused })
            .ToArray();
        Notify(nameof(Items)); Notify(nameof(IsEmpty)); Notify(nameof(HasUnreadable));
        RefreshCommands();
    }

    private void RefreshCommands()
    {
        Notify(nameof(IsBusy));
        RestoreAllCommand.Refresh(); DiscardUnreadableCommand.Refresh(); BackCommand.Refresh();
        foreach (var item in Items) item.RefreshCommands();
    }

    internal bool CanAct => !_busy && !_disposed;

    private async Task<bool> RunAsync(Func<Task<bool>> action)
    {
        if (_busy || _disposed) return false;
        _busy = true;
        RefreshCommands();
        bool result = false;
        try { result = await action(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or ProjectFormatException)
        { await _reportError(e); }
        finally
        {
            _busy = false;
            if (!_disposed) { Rebuild(); }
        }
        // Nothing undecided is left: there is nothing more to do here.
        if (!_disposed && !_catalog.HasUndecided) _leave();
        return result;
    }

    internal Task<bool> RestoreAsync(Guid candidateId) => RunAsync(() => _restore(candidateId));

    internal Task<bool> DiscardAsync(Guid candidateId) => RunAsync(() => _catalog.DiscardAsync(candidateId));

    private Task<bool> RestoreAllAsync() => RunAsync(async () =>
    {
        bool any = false;
        // One after the other: each restore opens (or reuses) its own workspace.
        foreach (var id in Items.Where(static i => i.CanRestore).Select(static i => i.CandidateId).ToList())
        {
            if (_disposed) break;
            try { any |= await _restore(id); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or ProjectFormatException)
            { await _reportError(e); }
        }
        return any;
    });

    private Task<bool> DiscardUnreadableAsync() => RunAsync(async () =>
    {
        bool any = false;
        foreach (var id in Items.Where(static i => i.IsUnreadable).Select(static i => i.CandidateId).ToList())
        {
            if (_disposed) break;
            try { any |= await _catalog.DiscardAsync(id); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            { await _reportError(e); }
        }
        return any;
    });
}

/// <summary>One undecided candidate as a row of the Recovery Center.</summary>
public sealed class RecoveryItemViewModel : ObservableObject
{
    private readonly RecoveryCenterViewModel _owner;
    private bool _focused;

    internal RecoveryItemViewModel(RecoveryCandidate candidate, RecoveryCenterViewModel owner)
    {
        _owner = owner;
        Candidate = candidate;
        RestoreCommand = new(() => _owner.RestoreAsync(CandidateId), () => CanRestore && _owner.CanAct);
        DiscardCommand = new(() => _owner.DiscardAsync(CandidateId), () => _owner.CanAct);
    }

    internal RecoveryCandidate Candidate { get; }
    internal Guid CandidateId => Candidate.CandidateId;
    public bool IsUnreadable => Candidate.IsDamaged;
    public bool CanRestore => !Candidate.IsDamaged;
    public bool IsOlderVariant => Candidate is { Kind: RecoverySlotKind.Project, IsDamaged: false, Identity: not null, IsPreferred: false };

    public string Title => Candidate switch
    {
        { IsDamaged: true } => Candidate.Slot.FileName,
        { Kind: RecoverySlotKind.Setup } => Strings.NewProject,
        { OriginalFilePath: { } path } => Path.GetFileNameWithoutExtension(path),
        { Project.State.Info.CalculationTitle: { Length: > 0 } title } => title,
        _ => Strings.Untitled
    };

    /// <summary>What the entry is: the kind, and for an unreadable one why it cannot be restored.</summary>
    public string Kind => Candidate switch
    {
        { IsDamaged: true } => Strings.RecoveryKindUnreadable,
        { Kind: RecoverySlotKind.Setup } => Strings.RecoveryKindDraft,
        _ when IsOlderVariant => Strings.RecoveryKindOlder,
        _ => Strings.RecoveryKindProject
    };

    public string? Detail => Candidate.IsDamaged ? Candidate.ValidationError : Candidate.OriginalFilePath;

    public string SavedLabel => Candidate.WrittenAtUtc is { } written
        ? string.Format(CultureInfo.CurrentUICulture, Strings.RecoverySavedAt, written.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)) : "";

    public string AutomationName => Title + ", " + Kind;

    public bool IsFocused
    {
        get => _focused;
        set => Set(ref _focused, value);
    }

    public AsyncActionCommand RestoreCommand { get; }
    public AsyncActionCommand DiscardCommand { get; }

    internal void RefreshCommands() { RestoreCommand.Refresh(); DiscardCommand.Refresh(); }
}
