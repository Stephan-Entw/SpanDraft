using SpanDraft.Desktop.State;

namespace SpanDraft.Desktop.Persistence;

public enum WorkspaceRecoveryState { None, CreateDraftRecovery, ProjectRecovery }

/// <summary>
/// The recovery of exactly one workspace: <see cref="WorkspaceRecoveryState.None"/>, the Create draft slot or the project
/// slot. The view model only reports domain events; slot names and <see cref="RecoveryId"/>s live here. CreateSetup and the
/// project created from it share one id. Slot writes stay best-effort: a failed debounce never blocks editing.
/// </summary>
/// <remarks>
/// A workspace never owns a project slot and a draft slot at the same time. Until New gets its own workspace (Phase 9),
/// it can still be started over an open project: while that project has a recovery slot of its own, the unconfirmed
/// Create draft is not written, so the unsaved project stays the single recovery of the workspace. Over a project without
/// recovery (clean) the draft owns the context as usual.
///
/// Slots this workspace could not delete are not part of the active context: they are cleanup remnants, kept separately
/// (<see cref="Remnants"/>) and retried at the next cleanup. A remnant draft whose id is the project's is that project's
/// companion: the project slot is never deleted while it exists, or the draft would reappear as a valid old draft.
/// </remarks>
public sealed class WorkspaceRecoveryContext
{
    private readonly IRecoveryStorage _storage;
    private readonly Func<TimeSpan, CancellationToken, Task>? _delay;
    private readonly Func<DateTimeOffset>? _utcNow;
    private ProjectRecovery? _project;
    private ProjectSetupRecovery? _setup;
    // Draft slots that are no longer the workspace's recovery but could not be deleted yet.
    private readonly List<ProjectSetupRecovery> _remnants = [];

    public WorkspaceRecoveryContext(IRecoveryStorage storage, Func<TimeSpan, CancellationToken, Task>? delay = null,
        Func<DateTimeOffset>? utcNow = null)
    {
        _storage = storage;
        _delay = delay;
        _utcNow = utcNow;
    }

    /// <summary>The single failure channel of normal recovery writes; it never faults the workspace.</summary>
    public event Action<Exception>? Failed;

    public WorkspaceRecoveryState State => _project is not null ? WorkspaceRecoveryState.ProjectRecovery
        : _setup is not null ? WorkspaceRecoveryState.CreateDraftRecovery : WorkspaceRecoveryState.None;

    public RecoveryId? ProjectRecoveryId => _project?.Slot.Id;
    public RecoveryId? SetupRecoveryId => _setup?.Slot.Id;
    /// <summary>The id of the workspace's current recovery: the project's, otherwise the draft's.</summary>
    public RecoveryId? CurrentRecoveryId => ProjectRecoveryId ?? SetupRecoveryId;
    /// <summary>Slots still on disk that this context has given up as its recovery but could not remove yet.</summary>
    public IReadOnlyList<RecoverySlot> Remnants => _remnants.Select(static r => r.Slot).ToArray();

    // ---- Create draft -----------------------------------------------------------------------------------------

    /// <summary>The confirmed values of a Create draft changed.</summary>
    public void SetupDraftChanged(ProjectSetupDraft draft)
    {
        // One recovery per workspace: unsaved project work (New over the open project) keeps the only slot.
        if (_project is not null) return;
        _setup ??= NewSetupWriter(SlotOf(RecoverySlotKind.Setup, RecoveryId.New()));
        _setup.Schedule(draft);
    }

    /// <summary>
    /// The Create draft was cancelled or replaced by its project: its slot is removed. The context is None afterwards in
    /// every case; a slot that cannot be deleted becomes a cleanup remnant (and the failure is thrown), never an active draft.
    /// </summary>
    public async Task SetupAbandonedAsync()
    {
        await RetryRemnantsAsync(except: null);
        if (_setup is not { } setup) return;
        _setup = null;
        try { await setup.DeleteAsync(); }
        catch (Exception e) when (IsStorageError(e)) { _remnants.Add(setup); throw; }
    }

    // ---- Project ----------------------------------------------------------------------------------------------

    /// <summary>
    /// A project was created from a confirmed CreateSetup. It takes over the draft's id: the complete project slot is
    /// published first (no debounce), the draft slot is only removed after that succeeded. If publishing fails the project
    /// still exists, the draft slot stays as the crash fallback (the catalog prefers a valid project of the same id), the
    /// snapshot is scheduled for a later publish and the failure is reported.
    /// </summary>
    public async Task ProjectCreatedAsync(ProjectSession session)
    {
        var setup = _setup;
        _setup = null;
        var id = setup?.Slot.Id ?? RecoveryId.New();
        var project = _project = NewProjectWriter(SlotOf(RecoverySlotKind.Project, id));
        bool published = false;
        try
        {
            await project.FlushAsync(session.CurrentRevision.State, session.FilePath ?? session.RecoveryOriginPath);
            published = true;
        }
        catch (Exception e) when (IsStorageError(e))
        {
            project.Schedule(session);
            Failed?.Invoke(e);
        }
        if (setup is null) return;
        if (published)
        {
            try { await setup.DeleteAsync(); }
            catch (Exception e) when (IsStorageError(e)) { _remnants.Add(setup); Failed?.Invoke(e); }
        }
        else _remnants.Add(setup);
    }

    /// <summary>The committed state of the project changed (or returned to its savepoint).</summary>
    public void ProjectChanged(ProjectSession session)
    {
        // A Create draft shows no editable session; the project cannot change while it owns the context.
        if (_setup is not null) return;
        if (_project is null)
        {
            // A cleanly opened project has no recovery; the first real change creates it.
            if (!session.IsDirty) return;
            _project = NewProjectWriter(SlotOf(RecoverySlotKind.Project, RecoveryId.New()));
        }
        _project.Schedule(session);
    }

    /// <summary>Publishes the state now, e.g. to protect a save in progress.</summary>
    public Task FlushAsync(ProjectState state, string? originalFilePath)
    {
        _project ??= NewProjectWriter(SlotOf(RecoverySlotKind.Project, RecoveryId.New()));
        return _project.FlushAsync(state, originalFilePath);
    }

    /// <summary>Re-schedules the current snapshot after a protective flush could not be rolled back.</summary>
    public void ScheduleProject(ProjectSession session) => ProjectChanged(session);

    /// <summary>
    /// Removes the project slot; the context returns to None. A draft slot with the project's id goes first: the project
    /// slot is the one that keeps that draft superseded, so it must outlive it. A failed delete throws and keeps the
    /// project slot owned by this context so that a later attempt can repeat it. Other remnants are retried best effort.
    /// </summary>
    public async Task DeleteAsync()
    {
        if (_project is { } project)
        {
            foreach (var companion in _remnants.Where(r => r.Slot.Id == project.Slot.Id).ToList())
            {
                await companion.DeleteAsync();
                _remnants.Remove(companion);
            }
            await project.DeleteAsync();
            _project = null;
        }
        await RetryRemnantsAsync(except: null);
    }

    /// <summary>
    /// New over a project that became clean again (undone back to its savepoint): its slot is obsolete. It is removed first
    /// (awaited, so it cannot reappear) and the context returns to None, which lets the Create draft own it as over any
    /// clean project. If the slot cannot be removed the failure is thrown and the context stays with that project.
    /// </summary>
    public async Task ReleaseCleanProjectAsync(ProjectSession session)
    {
        if (_project is null || session.IsDirty) return;
        await DeleteAsync();
    }

    private async Task RetryRemnantsAsync(ProjectSetupRecovery? except)
    {
        foreach (var remnant in _remnants.Where(r => !ReferenceEquals(r, except) && r.Slot.Id != _project?.Slot.Id).ToList())
        {
            try { await remnant.DeleteAsync(); _remnants.Remove(remnant); }
            catch (Exception e) when (IsStorageError(e)) { /* stays a remnant; it was reported when it first failed */ }
        }
    }

    /// <summary>Takes over an existing project slot (restore); the slot keeps its id.</summary>
    public void AdoptProject(RecoverySlot slot)
    {
        if (_project is not null || _setup is not null) throw new InvalidOperationException("The workspace already has a recovery.");
        _project = NewProjectWriter(slot);
    }

    /// <summary>Gives a just adopted slot back without touching it (the restore failed before it was committed).</summary>
    public void Unadopt(RecoverySlot slot)
    {
        if (_project?.Slot == slot) _project = null;
        else if (_setup?.Slot == slot) _setup = null;
    }

    /// <summary>Takes over an existing Create draft slot (restore); the slot keeps its id.</summary>
    public void AdoptCreateDraft(RecoverySlot slot)
    {
        if (_setup is not null || _project is not null) throw new InvalidOperationException("The workspace already has a recovery.");
        _setup = NewSetupWriter(slot);
    }

    public Task DrainAsync() => Task.WhenAll(_project?.DrainAsync() ?? Task.CompletedTask, _setup?.DrainAsync() ?? Task.CompletedTask);

    /// <summary>The current content of the project slot, or null.</summary>
    public Task<RecoverySnapshot?> ReadAsync() => _project?.ReadAsync() ?? Task.FromResult<RecoverySnapshot?>(null);

    /// <summary>The current content of the draft slot, or null.</summary>
    public Task<SetupRecoverySnapshot?> ReadSetupAsync() => _setup?.ReadAsync() ?? Task.FromResult<SetupRecoverySnapshot?>(null);

    // ---- helpers ----------------------------------------------------------------------------------------------

    private static RecoverySlot SlotOf(RecoverySlotKind kind, RecoveryId id) => new(kind, id);

    private ProjectRecovery NewProjectWriter(RecoverySlot slot)
    {
        var writer = new ProjectRecovery(_storage, slot, _delay, _utcNow);
        writer.Failed += e => Failed?.Invoke(e);
        return writer;
    }

    private ProjectSetupRecovery NewSetupWriter(RecoverySlot slot)
    {
        var writer = new ProjectSetupRecovery(_storage, slot, _delay, _utcNow);
        writer.Failed += e => Failed?.Invoke(e);
        return writer;
    }

    private static bool IsStorageError(Exception e) => RecoveryErrors.IsStorage(e);
}
