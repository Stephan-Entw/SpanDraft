using SpanDraft.Desktop.State;

namespace SpanDraft.Desktop.Persistence;

public enum WorkspaceRecoveryState { None, CreateDraftRecovery, ProjectRecovery }

/// <summary>What a leave of the workspace gives up: the session's project slot, the Create draft, or everything (close).</summary>
public enum RecoveryLeaveScope { Project, Draft, Workspace }

/// <summary>
/// Which protection is faulted (§60): only set when a close or discard already lifted the recovery protection, could not
/// complete and the protection could not be restored. Normal recovery write failures never set it.
/// </summary>
public enum RecoveryFault { None, Project, Draft }

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
/// Detached draft slots awaiting cleanup are kept separately (<see cref="Remnants"/>) and retried at the next cleanup;
/// when the workspace closes they are moved to the Discarded area like any deliberately given up draft.
/// A remnant draft whose id is the project's is that project's companion: the project slot is never deleted while it
/// exists, or the draft would reappear as a valid old draft.
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
    // Set once the workspace committed its close: nothing is written for it any more.
    private bool _retired;

    public WorkspaceRecoveryContext(IRecoveryStorage storage, Func<TimeSpan, CancellationToken, Task>? delay = null,
        Func<DateTimeOffset>? utcNow = null)
    {
        _storage = storage;
        _delay = delay;
        _utcNow = utcNow;
    }

    /// <summary>The single failure channel of normal recovery writes; it never faults the workspace.</summary>
    public event Action<Exception>? Failed;

    /// <summary>Raised whenever <see cref="Fault"/> changes.</summary>
    public event Action? FaultChanged;

    /// <summary>The faulted protection, see <see cref="RecoveryFault"/>; <see cref="RecoveryFault.None"/> in normal operation.</summary>
    public RecoveryFault Fault { get; private set; }

    /// <summary>Why the protection could not be restored, while <see cref="Fault"/> is set.</summary>
    public Exception? FaultError { get; private set; }

    /// <summary>No recovery slot of any kind is held: neither project, draft nor a cleanup remnant, and nothing is faulted.</summary>
    public bool IsEmpty => _project is null && _setup is null && _remnants.Count == 0 && Fault == RecoveryFault.None;

    public WorkspaceRecoveryState State => _project is not null ? WorkspaceRecoveryState.ProjectRecovery
        : _setup is not null ? WorkspaceRecoveryState.CreateDraftRecovery : WorkspaceRecoveryState.None;

    public RecoveryId? ProjectRecoveryId => _project?.Slot.Id;
    public RecoveryId? SetupRecoveryId => _setup?.Slot.Id;
    /// <summary>The id of the workspace's current recovery: the project's, otherwise the draft's.</summary>
    public RecoveryId? CurrentRecoveryId => ProjectRecoveryId ?? SetupRecoveryId;
    /// <summary>Detached draft slots this context still needs to remove.</summary>
    public IReadOnlyList<RecoverySlot> Remnants => _remnants.Select(static r => r.Slot).ToArray();

    // ---- Create draft -----------------------------------------------------------------------------------------

    /// <summary>The confirmed values of a Create draft changed.</summary>
    public void SetupDraftChanged(ProjectSetupDraft draft)
    {
        // One recovery per workspace: unsaved project work (New over the open project) keeps the only slot.
        if (_retired || _project is not null) return;
        _setup ??= NewSetupWriter(SlotOf(RecoverySlotKind.Setup, RecoveryId.New()));
        _setup.Schedule(draft);
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
        if (setup is not null) _remnants.Add(setup);
        var project = _project = NewProjectWriter(SlotOf(RecoverySlotKind.Project, id), setup);
        try
        {
            await project.FlushAsync(session.CurrentRevision.State, session.FilePath ?? session.RecoveryOriginPath,
                setup is null ? null : () => TryRemovePublishedCompanionAsync(setup));
        }
        catch (Exception e) when (IsStorageError(e))
        {
            project.Schedule(session);
            Failed?.Invoke(e);
        }
    }

    /// <summary>The committed state of the project changed (or returned to its savepoint).</summary>
    public void ProjectChanged(ProjectSession session)
    {
        // A Create draft shows no editable session; the project cannot change while it owns the context.
        if (_retired || _setup is not null) return;
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
    /// Removes the project slot and releases the context if the delete has not been superseded by newer work. A draft
    /// slot with the project's id goes first because the project slot keeps that draft superseded. A failed delete
    /// throws and keeps the project slot owned so that a later attempt can repeat it. Other remnants are retried best effort.
    /// </summary>
    public async Task DeleteAsync()
    {
        if (_project is { } project)
            await project.DeleteAsync(() => { if (ReferenceEquals(_project, project)) _project = null; });
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
            try { await RemoveRemnantAsync(remnant); }
            catch (Exception e) when (IsStorageError(e)) { /* stays a remnant; it was reported when it first failed */ }
        }
    }

    private async Task TryRemovePublishedCompanionAsync(ProjectSetupRecovery companion)
    {
        try { await RemoveRemnantAsync(companion); }
        catch (Exception e) when (IsStorageError(e)) { Failed?.Invoke(e); }
    }

    private async Task RemoveRemnantAsync(ProjectSetupRecovery remnant)
    {
        if (!_remnants.Contains(remnant)) return;
        await remnant.DeleteAsync();
        _remnants.Remove(remnant);
    }

    /// <summary>Takes over an existing project slot (restore); the slot keeps its id.</summary>
    public void AdoptProject(RecoverySlot slot)
    {
        if (_project is not null || _setup is not null) throw new InvalidOperationException("The workspace already has a recovery.");
        _project = NewProjectWriter(slot, published: true);
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
        _setup = NewSetupWriter(slot, published: true);
    }

    public Task DrainAsync() => Task.WhenAll(_project?.DrainAsync() ?? Task.CompletedTask, _setup?.DrainAsync() ?? Task.CompletedTask);

    /// <summary>The current content of the project slot, or null.</summary>
    public Task<RecoverySnapshot?> ReadAsync() => _project?.ReadAsync() ?? Task.FromResult<RecoverySnapshot?>(null);

    /// <summary>The current content of the draft slot, or null.</summary>
    public Task<SetupRecoverySnapshot?> ReadSetupAsync() => _setup?.ReadAsync() ?? Task.FromResult<SetupRecoverySnapshot?>(null);

    // ---- Leave (close, discard) --------------------------------------------------------------------------------

    /// <summary>
    /// Prepares leaving the recovery of <paramref name="scope"/> without making anything visible: the project slot is moved to
    /// the Discarded area when <paramref name="discardProject"/> (a deliberate Discard), otherwise removed as technical
    /// cleanup (it is obsolete after Save or at the savepoint); the Create draft and, for a close, every cleanup remnant are
    /// always moved, since they were given up deliberately. A companion draft goes before its project slot. Pending debounced
    /// writes can never publish afterwards. The context keeps its writers until <see cref="RecoveryLeave.Commit"/>.
    /// </summary>
    /// <remarks>
    /// On failure the storage transitions that really happened decide: if the previous protection was lifted (the active slot
    /// was moved, or the companion that was the only crash fallback of a never published project slot) the current confirmed
    /// state is published again, and if that fails too the context becomes faulted (§60). Otherwise the current state is only
    /// scheduled again. The failure is thrown in either case.
    /// </remarks>
    public async Task<RecoveryLeave> PrepareLeaveAsync(RecoveryLeaveScope scope, bool discardProject, ProjectSession? session,
        ProjectSetupDraft? draft)
    {
        if (_retired) throw new InvalidOperationException("The workspace recovery was already retired.");
        var leave = new RecoveryLeave(this, scope, scope == RecoveryLeaveScope.Draft ? null : _project,
            scope == RecoveryLeaveScope.Project ? null : _setup, session, draft, discardProject);
        try { await leave.PrepareAsync(); }
        catch (Exception e) when (IsStorageError(e))
        {
            await leave.RestoreAsync();
            throw;
        }
        return leave;
    }

    /// <summary>
    /// Remediation of a faulted context: publishes the current confirmed state of the faulted kind again and clears the fault
    /// once that succeeded. A storage failure keeps the fault and is thrown. Without a fault it does nothing.
    /// </summary>
    public async Task RestoreProtectionAsync(ProjectSession? session, ProjectSetupDraft? draft)
    {
        switch (Fault)
        {
            case RecoveryFault.Project when _project is { } project && session is not null:
                await project.FlushAsync(session.CurrentRevision.State, session.FilePath ?? session.RecoveryOriginPath);
                break;
            case RecoveryFault.Draft when _setup is { } setup && draft is not null:
                await setup.FlushAsync(draft);
                break;
            default: return;
        }
        SetFault(RecoveryFault.None, null);
    }

    /// <summary>
    /// A save completed. Only a project fault is cleared, and only when the session is clean now: the saved file is the
    /// protection then. A draft fault is never touched by a save.
    /// </summary>
    public void ProjectSaved(ProjectSession session)
    {
        if (Fault == RecoveryFault.Project && !session.IsDirty) SetFault(RecoveryFault.None, null);
    }

    private void SetFault(RecoveryFault fault, Exception? error)
    {
        if (Fault == fault && ReferenceEquals(FaultError, error)) return;
        Fault = fault;
        FaultError = error;
        FaultChanged?.Invoke();
    }

    /// <summary>
    /// One prepared leave of a <see cref="WorkspaceRecoveryContext"/>: it records which storage transitions happened, so that a
    /// rollback knows whether the previous protection was lifted. Created only by <see cref="PrepareLeaveAsync"/>.
    /// </summary>
    public sealed class RecoveryLeave
    {
        private readonly WorkspaceRecoveryContext _context;
        private readonly RecoveryLeaveScope _scope;
        private readonly ProjectRecovery? _project;
        private readonly ProjectSetupRecovery? _setup;
        private readonly ProjectSession? _session;
        private readonly ProjectSetupDraft? _draft;
        private readonly bool _discardProject;
        private bool _activeMoved, _companionMoved, _projectFallbackRemains, _finished;

        internal RecoveryLeave(WorkspaceRecoveryContext context, RecoveryLeaveScope scope, ProjectRecovery? project,
            ProjectSetupRecovery? setup, ProjectSession? session, ProjectSetupDraft? draft, bool discardProject)
        {
            _context = context;
            _scope = scope;
            _project = project;
            _setup = setup;
            _session = session;
            _draft = draft;
            _discardProject = discardProject;
        }

        /// <summary>
        /// True when the protection that existed before the leave is gone: the active slot was moved, or the companion draft
        /// was moved while no published project slot remains (the draft was its only crash fallback then). Both are read
        /// from the storage transitions that really happened, not from a state captured before them.
        /// </summary>
        public bool ProtectionLifted => _activeMoved || _companionMoved && !_projectFallbackRemains;

        internal async Task PrepareAsync()
        {
            var remnants = _context._remnants;
            if (_scope == RecoveryLeaveScope.Workspace)
                foreach (var remnant in remnants.Where(r => r.Slot.Id != _project?.Slot.Id).ToList())
                {
                    await remnant.DiscardAsync();
                    remnants.Remove(remnant);
                }
            if (_project is not null)
            {
                if (_discardProject)
                {
                    if (remnants.FirstOrDefault(r => r.Slot.Id == _project.Slot.Id) is { } companion)
                    {
                        _companionMoved = await companion.DiscardAsync() == SlotDiscardOutcome.Moved;
                        remnants.Remove(companion);
                    }
                    try { _activeMoved = await _project.DiscardAsync() == SlotDiscardOutcome.Moved; }
                    // Read after the attempt, under the slot's own ordering: a publication that was still running has
                    // completed by now, and a failed move left the slot exactly as it was.
                    finally { _projectFallbackRemains = _project.IsPublished; }
                }
                // Technical cleanup: the slot is obsolete (saved or at its savepoint). Its companion goes first, as always.
                else await _project.DeleteAsync();
            }
            if (_setup is not null) _activeMoved = await _setup.DiscardAsync() == SlotDiscardOutcome.Moved;
        }

        /// <summary>
        /// Gives the leave up: if the previous protection was lifted, the current confirmed state is published again (a fault
        /// if that fails); otherwise it is only scheduled again. True unless the context became faulted.
        /// </summary>
        public async Task<bool> RollbackAsync()
        {
            if (_finished) throw new InvalidOperationException("The recovery leave is already finished.");
            _finished = true;
            return await RestoreAsync();
        }

        internal async Task<bool> RestoreAsync()
        {
            bool lifted = ProtectionLifted;
            var context = _context;
            if (_project is not null && _session is { IsDirty: true } session)
            {
                if (!lifted) { _project.Schedule(session); return true; }
                try { await _project.FlushAsync(session.CurrentRevision.State, session.FilePath ?? session.RecoveryOriginPath); }
                catch (Exception e) when (IsStorageError(e)) { context.SetFault(RecoveryFault.Project, e); return false; }
                return true;
            }
            if (_setup is not null && _draft is not null)
            {
                if (!lifted) { _setup.Schedule(_draft); return true; }
                try { await _setup.FlushAsync(_draft); }
                catch (Exception e) when (IsStorageError(e)) { context.SetFault(RecoveryFault.Draft, e); return false; }
            }
            return true;
        }

        /// <summary>
        /// Makes the leave final: the writers of the scope are dropped (a fault ends with them). <paramref name="retire"/> ends
        /// the context for good, used when the workspace itself closes. Synchronous: nothing can fail any more.
        /// </summary>
        public void Commit(bool retire)
        {
            if (_finished) throw new InvalidOperationException("The recovery leave is already finished.");
            _finished = true;
            if (_scope != RecoveryLeaveScope.Draft && ReferenceEquals(_context._project, _project)) _context._project = null;
            if (_scope != RecoveryLeaveScope.Project && ReferenceEquals(_context._setup, _setup)) _context._setup = null;
            if (retire) _context._retired = true;
            _context.SetFault(RecoveryFault.None, null);
        }
    }

    // ---- helpers ----------------------------------------------------------------------------------------------

    private static RecoverySlot SlotOf(RecoverySlotKind kind, RecoveryId id) => new(kind, id);

    private ProjectRecovery NewProjectWriter(RecoverySlot slot, ProjectSetupRecovery? companion = null, bool published = false)
    {
        var writer = new ProjectRecovery(_storage, slot, _delay, _utcNow,
            companion is null ? null : () => RemoveRemnantAsync(companion), published);
        writer.Failed += e => Failed?.Invoke(e);
        return writer;
    }

    private ProjectSetupRecovery NewSetupWriter(RecoverySlot slot, bool published = false)
    {
        var writer = new ProjectSetupRecovery(_storage, slot, _delay, _utcNow, published);
        writer.Failed += e => Failed?.Invoke(e);
        return writer;
    }

    private static bool IsStorageError(Exception e) => RecoveryErrors.IsStorage(e);
}
