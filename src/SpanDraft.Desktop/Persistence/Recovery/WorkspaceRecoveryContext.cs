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
/// </remarks>
public sealed class WorkspaceRecoveryContext
{
    private readonly IRecoveryStorage _storage;
    private readonly Func<TimeSpan, CancellationToken, Task>? _delay;
    private readonly Func<DateTimeOffset>? _utcNow;
    private ProjectRecovery? _project;
    private ProjectSetupRecovery? _setup;

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

    // ---- Create draft -----------------------------------------------------------------------------------------

    /// <summary>The confirmed values of a Create draft changed.</summary>
    public void SetupDraftChanged(ProjectSetupDraft draft)
    {
        // One recovery per workspace: unsaved project work (New over the open project) keeps the only slot.
        if (_project is not null) return;
        _setup ??= NewSetupWriter(SlotOf(RecoverySlotKind.Setup, RecoveryId.New()));
        _setup.Schedule(draft);
    }

    /// <summary>The Create draft was cancelled or replaced by its project: its slot is removed.</summary>
    public async Task SetupAbandonedAsync()
    {
        // A failed delete keeps the writer: the stale slot stays this workspace's and the next attempt removes or overwrites it.
        if (_setup is not { } setup) return;
        await setup.DeleteAsync();
        _setup = null;
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
            catch (Exception e) when (IsStorageError(e)) { _leftoverSetup = setup; Failed?.Invoke(e); }
        }
        else _leftoverSetup = setup;
    }

    private ProjectSetupRecovery? _leftoverSetup;

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
    /// Removes the project slot (and a draft slot a failed hand-off left behind); the context returns to None. A failed
    /// delete keeps the slot owned by this context so that a later attempt can repeat it.
    /// </summary>
    public async Task DeleteAsync()
    {
        if (_project is { } project)
        {
            await project.DeleteAsync();
            _project = null;
        }
        if (_leftoverSetup is { } leftover)
        {
            await leftover.DeleteAsync();
            _leftoverSetup = null;
        }
    }

    /// <summary>Takes over an existing project slot (restore); the slot keeps its id.</summary>
    public void AdoptProject(RecoverySlot slot)
    {
        if (_project is not null || _setup is not null) throw new InvalidOperationException("The workspace already has a recovery.");
        _project = NewProjectWriter(slot);
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

    private static bool IsStorageError(Exception e) => e is IOException or UnauthorizedAccessException;
}
