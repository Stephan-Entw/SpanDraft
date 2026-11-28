using Avalonia.Threading;
using SpanDraft.Desktop.Activation;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;

namespace SpanDraft.Desktop.Workspaces;

/// <summary>Why a workspace is closed: its window (Fenster-X, Quit command) or Close Project (§14).</summary>
internal enum WorkspaceCloseReason { Window, CloseProject }

internal enum WorkspaceCloseOutcome { Cancelled, Closed, ShowsStart }

/// <summary>
/// The central workspace/window registry: creates, registers, looks up and activates workspaces and enforces
/// <c>StartSlotCount &lt;= 1</c>. It reads no project files, decides no identity and saves nothing.
/// </summary>
internal sealed class WorkspaceWindowManager
{
    private readonly DesktopApplicationHost _host;
    private readonly List<WorkspaceWindowHandle> _handles = [];
    private WorkspaceWindowHandle? _initial, _active;
    private bool _activationEnabled, _initialCreated;
    private RecoveryRestoreCoordinator? _restore;

    internal WorkspaceWindowManager(DesktopApplicationHost host)
    {
        _host = host;
        host.RetryServed += () => Dispatcher.UIThread.Post(SurfaceForRetry);
    }

    public IReadOnlyList<WorkspaceWindowHandle> Handles => _handles;
    public WorkspaceWindowHandle? Initial => _initial;
    public WorkspaceWindowHandle? Active => _active is { } active && IsReady(active) ? active : _handles.FirstOrDefault(IsReady);
    /// <summary>Workspaces reserving the single start slot (not necessarily showing the start page).</summary>
    public int StartSlotCount => _handles.Count(static h => h.HoldsStartSlot);

    /// <summary>
    /// A workspace that can take launch requests: registered, model bound, not closing or about to close, and no application
    /// quit runs (it holds every workspace, also for the <see cref="Active"/> fallback).
    /// </summary>
    private bool IsReady(WorkspaceWindowHandle handle) =>
        CanCreate && handle.CanRoute && handle.Window.IsUiReady && handle.Window.IsVisible
        && handle.Model is { IsDisposed: false } && _handles.Contains(handle);

    /// <summary>
    /// The order of an application quit (§75): the active workspace first, then the others in registration order. Read from
    /// the registry itself, since <see cref="Active"/> no longer answers once the quit holds the workspaces.
    /// </summary>
    internal IReadOnlyList<WorkspaceWindowHandle> QuitOrder()
    {
        var open = _handles.Where(static h => !h.IsClosing).ToList();
        if (_active is { } active && open.Remove(active)) open.Insert(0, active);
        return open;
    }

    /// <summary>
    /// True when no other workspace survives, i.e. closing this one ends the application. Workspaces whose close is
    /// committed already count as gone, so two concurrent closes cannot both miss being the last one.
    /// </summary>
    internal bool IsLast(WorkspaceWindowHandle handle) =>
        _handles.Contains(handle) && _handles.All(h => ReferenceEquals(h, handle) || h.IsClosing);

    public WorkspaceWindowHandle? Find(WindowId id) => _handles.FirstOrDefault(h => h.WindowId == id);

    /// <summary>Creates the initial workspace; the only one that makes the host Ready.</summary>
    public WorkspaceWindowHandle CreateInitial()
    {
        // One-shot for the whole process lifetime: the legacy startup/recovery owner must never be created twice.
        if (_initialCreated) throw new InvalidOperationException("The initial workspace was already created.");
        var handle = TryCreate(WorkspaceCreationReason.None, isInitial: true)
            ?? throw new InvalidOperationException("The initial workspace cannot register for settings.");
        _initialCreated = true;
        return _initial = handle;
    }

    /// <summary>The one regular creation path of workspace windows.</summary>
    private WorkspaceWindowHandle? TryCreate(WorkspaceCreationReason reason, bool isInitial = false)
    {
        if (!CanCreate) return null;
        if (reason == WorkspaceCreationReason.None && !isInitial && StartSlotCount >= 1)
            throw new InvalidOperationException("At most one workspace may hold the start slot.");
        if (_host.SettingsCoordinator.TryReserveRegistration() is not { } registration) return null;
        var handle = new WorkspaceWindowHandle(WindowId.Create(), reason, isInitial) { SettingsRegistration = registration };
        try
        {
            handle.AttachWindow(new MainWindow(_host, handle));
            _handles.Add(handle);
            _active ??= handle;
            handle.Window.Activated += (_, _) => _active = handle;
            return handle;
        }
        catch
        {
            handle.ReleaseSettingsRegistration();
            throw;
        }
    }

    internal void ReleasePendingSettingsRegistrations()
    {
        foreach (var handle in _handles) handle.ReleaseSettingsRegistration();
    }

    /// <summary>Called when the window has created its view model: binds it once and wires the workspace hooks.</summary>
    internal void BindModel(WorkspaceWindowHandle handle, MainWindowViewModel model)
    {
        handle.BindModel(model);
        model.SessionActivated += handle.Neutralize;
        model.SetupCancelled += () => OnSetupCancelled(handle);
        model.RecoveryCenterLeft += () => OnRecoveryCenterLeft(handle);
        model.NewProjectHandler = async () => await RequestNewProjectAsync(handle) is not null;
        model.OpenProjectHandler = (path, fromRecent) => RequestOpenProjectAsync(handle, path, fromRecent);
        model.CanRequestProjectHandler = () => CanCreate && IsReady(handle) && !handle.IsOpenTargetReserved
            && handle.Reason != WorkspaceCreationReason.OpenProject;
        model.CloseWorkspaceHandler = async () => await CloseAsync(handle, WorkspaceCloseReason.CloseProject) != WorkspaceCloseOutcome.Cancelled;
        model.CanRouteHandler = () => IsReady(handle) && _host.State != DesktopApplicationState.ShuttingDown;
    }

    /// <summary>
    /// The one final cleanup of a workspace, from its window's Closed event: owned windows, report, session, subscriptions and
    /// path ownership are released exactly once, then it is deregistered; the last one ends activation and the host.
    /// </summary>
    internal void FinalizeClosed(WorkspaceWindowHandle handle)
    {
        if (handle.IsFinalized) return;
        handle.IsFinalized = true;
        handle.IsClosing = true;
        handle.ReleaseSettingsRegistration();
        handle.CloseOwnedWindows();
        handle.Model?.ReleaseReport();
        handle.Model?.Dispose();
        // Only now is the ownership gone: opens that waited for this close decide anew. A close that had not committed yet
        // (the platform closed the window in a system shutdown) is orphaned by this.
        handle.CloseTransaction?.FinishClosed();
        bool last = Deregister(handle);
        handle.CompleteFinalized();
        if (last)
        {
            DisableActivation();
            _host.BeginShutdown();
            return;
        }
        if (handle.IsInitial && _host.State == DesktopApplicationState.Starting)
        {
            // The initial workspace left before it made the host Ready; another workspace must not stay unreachable.
            _host.MarkReady();
            EnableActivation();
        }
        // Requests held back for it are evaluated anew.
        if (handle.IsInitial || handle.Window.IsUiReady) PostPump();
    }

    /// <summary>Removes a closed workspace from the registry. True when this was the last workspace.</summary>
    internal bool Deregister(WorkspaceWindowHandle handle)
    {
        if (!_handles.Remove(handle)) return false;
        if (ReferenceEquals(_initial, handle)) _initial = null;
        if (ReferenceEquals(_active, handle)) _active = null;
        return _handles.Count == 0;
    }

    /// <summary>Brings the workspace with this id to the front; false for an unknown id.</summary>
    public bool Activate(WindowId id)
    {
        if (Find(id) is not { } handle || !IsReady(handle)) return false;
        _active = handle;
        handle.Window.Surface(handle.Window.RetryTarget);
        return true;
    }

    /// <summary>
    /// Opens CreateSetup in a free start workspace or a separate NewProject workspace.
    /// </summary>
    internal async Task<WorkspaceWindowHandle?> RequestNewProjectAsync(WorkspaceWindowHandle source)
    {
        if (!CanAcquireWorkspace || !IsReady(source) || source.Model?.CanNewProject != true) return null;
        if (FindIdleStart(source) is { } start)
        {
            if (!ReferenceEquals(start, source)) Activate(start.WindowId);
            return await start.Model!.NewAsync() ? start : null;
        }
        if (TryCreate(WorkspaceCreationReason.NewProject) is not { } handle) return null;
        try
        {
            handle.Window.Show();
            if (await WaitForNewTargetAsync(handle) && await handle.Model!.NewAsync())
            {
                Activate(handle.WindowId);
                return handle;
            }
        }
        catch (OperationCanceledException) { /* the window went away; clean up below */ }
        catch (Exception error)
        {
            CleanupUnusedWorkspace(handle);
            // Initialization failures stay visible instead of silently dropping the New request.
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
        }
        CleanupUnusedWorkspace(handle);
        return null;
    }

    /// <summary>A local action belongs to its source. A request from the activation queue has no window owner.</summary>
    internal async Task<ProjectOpenResult> RequestOpenProjectAsync(WorkspaceWindowHandle? source, string path, bool fromRecent = false)
    {
        var request = new WindowOpenRequest(this, source, path, fromRecent);
        ProjectOpenResult result;
        try { result = await _host.ProjectOpen.OpenAsync(path, request); }
        catch (Exception error) when (MainWindowViewModel.IsFileError(error))
        {
            return await request.HandleOutcomeAsync(MainWindowViewModel.ClassifyOpenRequestError(error));
        }
        if (result.Status == ProjectOpenStatus.AlreadyOpen && result.Owner is { } owner && Find(owner)?.Model is { } model)
            await model.RecordOpenedProjectAsync(result);
        return result;
    }

    private sealed class WindowOpenRequest(WorkspaceWindowManager manager, WorkspaceWindowHandle? source, string path, bool fromRecent)
        : IProjectOpenRequest
    {
        public bool CanContinue => manager.CanCreate && (source is not null ? manager.IsReady(source)
            : manager._host.State == DesktopApplicationState.Ready);

        public async Task<ProjectOpenResult> RunOpenAsync(Func<Task<ProjectOpenResult>> transaction, bool afterSave) =>
            await HandleOutcomeAsync(await transaction());

        internal async Task<ProjectOpenResult> HandleOutcomeAsync(ProjectOpenResult result)
        {
            if (result.Status == ProjectOpenStatus.Opened && result.Owner is { } owner && manager.Find(owner)?.Model is { } model)
                await model.RecordOpenedProjectAsync(result);
            if (result.Status is not (ProjectOpenStatus.Missing or ProjectOpenStatus.Failed)) return result;
            if (!CanContinue || !await manager.WaitForSettingsAsync(source) || !CanContinue) return ProjectOpenResult.Unavailable;
            var preferred = source ?? manager.Active;
            var presenter = preferred is not null && manager.CanPresentOpenFailure(preferred) ? preferred
                : manager._handles.FirstOrDefault(manager.CanPresentOpenFailure);
            return presenter is not null && await presenter.Model!.TryPresentOpenFailureAsync(result, path, fromRecent)
                ? result : ProjectOpenResult.Unavailable;
        }

        public async Task<ProjectOpenStatus> ActivateProjectAsync(ProjectState state, string canonicalPath, Action<WindowId, Action> commit) =>
            CanContinue && await manager.WaitForSettingsAsync(source) && CanContinue
                ? await manager.OpenInWorkspaceAsync(source ?? manager.Active, state, canonicalPath, commit)
                : ProjectOpenStatus.Unavailable;

        public bool ActivateExisting(WindowId owner) => CanContinue && manager.CanAcquireWorkspace && manager.Activate(owner);

        public async Task<bool> ActivateRecoveryAsync(Guid candidate)
        {
            if (!CanContinue) return false;
            try { return await manager.ShowRecoveryCenterAsync(source ?? manager.Active, candidate) is not null; }
            catch (Exception error) when (MainWindowViewModel.IsFileError(error))
            {
                System.Diagnostics.Trace.TraceWarning("Recovery Center activation failed: {0}", error);
                return false;
            }
        }
    }

    private bool CanPresentOpenFailure(WorkspaceWindowHandle handle) => IsReady(handle) && handle.Window.CanBeginClose
        && !handle.IsOpenTargetReserved && handle.Model is { IsWorkspaceBusy: false };

    private async Task<ProjectOpenStatus> OpenInWorkspaceAsync(WorkspaceWindowHandle? source, ProjectState state, string path,
        Action<WindowId, Action> commit)
    {
        if (source is null || !CanAcquireWorkspace) return ProjectOpenStatus.Unavailable;
        var start = FindIdleStart(source);
        if ((start ?? TryCreate(WorkspaceCreationReason.OpenProject)) is not { } target) return ProjectOpenStatus.Unavailable;
        target.IsOpenTargetReserved = true;
        try
        {
            if (start is null)
            {
                target.Window.Show();
                if (!await WaitForNewTargetAsync(target)) return ProjectOpenStatus.Unavailable;
            }
            if (!CanCommit()) return ProjectOpenStatus.Unavailable;
            target.Model!.RefreshWorkspaceState();
            var status = await target.Model.OpenIntoEmptyWorkspaceAsync(state, path, CanCommit, commit);
            if (status == ProjectOpenStatus.Opened) Activate(target.WindowId);
            return status;
        }
        catch (OperationCanceledException)
        {
            return CanCommit() ? ProjectOpenStatus.Cancelled : ProjectOpenStatus.Unavailable;
        }
        finally
        {
            target.IsOpenTargetReserved = false;
            if (target.Reason == WorkspaceCreationReason.OpenProject) CleanupUnusedWorkspace(target);
            else if (!IsShuttingDown && target.Model is { IsDisposed: false } model) model.RefreshWorkspaceState();
        }

        bool CanCommit() => CanUseTarget(target) && target.IsOpenTargetReserved && target.Model is { Mode: MainViewMode.Start, HasProject: false };
    }

    internal void CleanupUnusedWorkspace(WorkspaceWindowHandle handle)
    {
        if (handle.IsOpenTargetReserved || !handle.CanRoute || handle.IsFinalized || IsShuttingDown) return;
        if (_host.Quit.UserQuit is { } quit)
        {
            ObserveAsync(CleanupAfterQuitAsync(quit));
            return;
        }
        if (handle.Model is null || handle.Model.IsSafelyEmpty) CloseInternal(handle);
        else ScheduleInternalClose(handle);

        async Task CleanupAfterQuitAsync(QuitRun run)
        {
            await run.Completion;
            CleanupUnusedWorkspace(handle);
        }
    }

    /// <summary>The shutdown is committed or the system ends the session: no workspace is created or changed any more.</summary>
    internal bool IsShuttingDown => _host.State == DesktopApplicationState.ShuttingDown;

    /// <summary>No workspace is created while the application quits or shuts down.</summary>
    private bool CanCreate => _host.State != DesktopApplicationState.ShuttingDown && !_host.Quit.IsUserQuitRunning;

    internal bool CanAcquireWorkspace => CanCreate && _host.SettingsCoordinator.ActiveApply is null;

    internal bool CanUseTarget(WorkspaceWindowHandle handle) => CanAcquireWorkspace && IsReady(handle) && !handle.Model!.IsSettingsFrozen;

    private async Task<bool> WaitForNewTargetAsync(WorkspaceWindowHandle handle)
    {
        var initialization = handle.Window.Initialization;
        if (!initialization.IsCompleted && await Task.WhenAny(initialization, _host.Shutdown) != initialization) return false;
        await initialization;
        return await WaitForSettingsAsync(handle) && CanUseTarget(handle);
    }

    private async Task<bool> WaitForSettingsAsync(WorkspaceWindowHandle? handle)
    {
        while (CanCreate && handle?.CanRoute != false)
        {
            if (_host.SettingsCoordinator.ActiveApply is not { } apply) return true;
            if (handle is null) await Task.WhenAny(apply, _host.Shutdown);
            else await Task.WhenAny(apply, _host.Shutdown, handle.Finalized);
        }
        return false;
    }

    /// <summary>
    /// Closes a workspace without its close transaction: only one that is provably empty and safe (no project, no draft, no
    /// recovery slot, remnant or fault, nothing running) or not initialized at all. The cleanup happens once in the window's
    /// Closed path. An internal close never ends the application: the last surviving workspace becomes the start page instead.
    /// </summary>
    internal void CloseInternal(WorkspaceWindowHandle handle)
    {
        if (!_handles.Contains(handle) || handle.IsFinalized) return;
        if (handle.Model is { } model)
        {
            if (!model.IsSafelyEmpty)
                throw new InvalidOperationException("Only an empty, safe workspace can be closed without its close transaction.");
            if (IsLast(handle) && _host.State != DesktopApplicationState.ShuttingDown)
            {
                handle.IsClosing = false;
                handle.Neutralize();
                model.RefreshWorkspaceState();
                return;
            }
        }
        handle.IsClosing = true;
        handle.Window.CloseCommitted();
    }

    // ---- Close ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Closes a workspace through its <see cref="WorkspaceCloseTransaction"/>. The reservation is taken synchronously before
    /// the first await. A window close of the last surviving workspace also arbitrates the application quit with the
    /// <see cref="ApplicationCloseCoordinator"/>: waiting open requests are asked about before the decision, and again
    /// atomically with the shutdown commit; whether it is the last one is decided anew after every await. Close Project decides
    /// at the commit boundary between start page and close (§14). While an application quit runs only that quit closes
    /// workspaces (<paramref name="quit"/>); it hands its freeze of the workspace to the close and asked about the requests
    /// already. A workspace cleaned up meanwhile by a system shutdown (orphaned transaction) decides nothing any more.
    /// </summary>
    internal async Task<WorkspaceCloseOutcome> CloseAsync(WorkspaceWindowHandle handle, WorkspaceCloseReason reason, QuitRun? quit = null)
    {
        var coordinator = _host.Quit;
        if (coordinator.IsSystemShutdown || !ReferenceEquals(coordinator.UserQuit, quit)) return WorkspaceCloseOutcome.Cancelled;
        if (!_handles.Contains(handle) || handle.Model is not { } model || handle.IsClosing) return WorkspaceCloseOutcome.Cancelled;
        if (!handle.Window.CanBeginClose) return WorkspaceCloseOutcome.Cancelled;
        if (WorkspaceCloseTransaction.TryBegin(model, handle, recordRecent: reason == WorkspaceCloseReason.Window,
                fromQuitFreeze: quit is not null) is not { } transaction)
            return WorkspaceCloseOutcome.Cancelled;
        object owner = (object?)quit ?? transaction;
        var confirmed = quit?.Confirmed ?? new HashSet<LaunchRequest>(ReferenceEqualityComparer.Instance);
        try
        {
            if (reason == WorkspaceCloseReason.Window && quit is null
                && !await coordinator.ConfirmLastWindowAsync(transaction, handle, confirmed))
                return await AbortAsync(transaction, quit);
            if (!await transaction.PrepareAsync()) return await AbortAsync(transaction, quit);
            // ---- commit boundary: only the shutdown arbitration can still abort, and it rolls back.
            bool closeWindow = reason == WorkspaceCloseReason.Window
                || _handles.Any(h => !ReferenceEquals(h, handle) && h.HoldsStartSlot);
            if (closeWindow && !await coordinator.CommitShutdownAsync(owner, handle, confirmed)) return await AbortAsync(transaction, quit);
            if (transaction.IsOrphaned) return WorkspaceCloseOutcome.Cancelled;
            var report = transaction.Commit(closeWindow);
            if (closeWindow)
            {
                handle.Window.CloseCommitted();
                return WorkspaceCloseOutcome.Closed;
            }
            PostPump();
            await report;
            return WorkspaceCloseOutcome.ShowsStart;
        }
        catch
        {
            await AbortAsync(transaction, quit);
            throw;
        }
    }

    private async Task<WorkspaceCloseOutcome> AbortAsync(WorkspaceCloseTransaction transaction, QuitRun? quit)
    {
        // An orphaned transaction is finished already: nothing is rolled back, released twice or reported.
        if (transaction.IsOrphaned) return WorkspaceCloseOutcome.Cancelled;
        await transaction.AbortAsync(() =>
        {
            if (quit is not null) _host.Quit.EndUserQuit(quit);
            else _host.Quit.EndWindowQuit(transaction);
        });
        if (Find(transaction.Model.WindowId) is { Reason: WorkspaceCreationReason.OpenProject } handle) CleanupUnusedWorkspace(handle);
        // Requests that waited while the close was pending are evaluated again.
        PostPump();
        return WorkspaceCloseOutcome.Cancelled;
    }

    private void OnSetupCancelled(WorkspaceWindowHandle handle)
    {
        // A workspace created for New or for a restored draft disappears; one that came from the start page just shows it again.
        // Its draft was discarded before.
        if (handle.Reason is not (WorkspaceCreationReason.NewProject or WorkspaceCreationReason.Restore)) return;
        ScheduleInternalClose(handle);
    }

    /// <summary>
    /// Closes a dedicated workspace that has become empty. Reserved at once (it takes no request, waiting open or local
    /// command from here on), executed only once no operation of its own runs any more: then, in one step, the reservation
    /// is released and, if it is still empty and safe, it is closed (or, as the last workspace, becomes the start page);
    /// otherwise it simply stays and takes requests again.
    /// </summary>
    private void ScheduleInternalClose(WorkspaceWindowHandle handle)
    {
        if (!_handles.Contains(handle) || !handle.CanRoute || handle.Model is not { } model || !model.ReserveInternalClose()) return;
        handle.IsCloseRequested = true;
        // Never inside the operation or navigation that decided it.
        Dispatcher.UIThread.Post(() => RunWhenIdle(model, () =>
        {
            var report = model.ReleaseLeaveReservation();
            handle.IsCloseRequested = false;
            if (_handles.Contains(handle) && model.IsSafelyEmpty) CloseInternal(handle);
            if (!handle.IsClosing) PostPump();
            ObserveAsync(report);
        }));
    }

    /// <summary>Runs <paramref name="action"/> once the model runs no operation besides its own reservation (event driven).</summary>
    private static void RunWhenIdle(MainWindowViewModel model, Action action)
    {
        if (model.IsOperationIdle) { action(); return; }
        void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (!model.IsOperationIdle) return;
            model.PropertyChanged -= Changed;
            Dispatcher.UIThread.Post(action);
        }
        model.PropertyChanged += Changed;
    }

    private static async void ObserveAsync(Task task)
    {
        try { await task; }
        catch (Exception error) { Dispatcher.UIThread.Post(() => System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(error)); }
    }

    // ---- Recovery ---------------------------------------------------------------------------------------------

    /// <summary>A workspace that shows the start page and nothing else: it takes a restore without further ado.</summary>
    private bool IsIdleStart(WorkspaceWindowHandle handle) =>
        CanUseTarget(handle) && handle.HoldsStartSlot && !handle.IsOpenTargetReserved && handle.Window.CanBeginClose
        && handle.Model is { Mode: MainViewMode.Start, IsWorkspaceBusy: false };

    private WorkspaceWindowHandle? FindIdleStart(WorkspaceWindowHandle source) =>
        IsIdleStart(source) && source.Model!.IsSafelyEmpty ? source
        : _handles.FirstOrDefault(handle => IsIdleStart(handle) && handle.Model!.IsSafelyEmpty);

    /// <summary>
    /// The Recovery Center is a workspace mode, not a dialog: an existing one is shown again; otherwise an idle start
    /// workspace navigates to it and keeps its start slot; otherwise a workspace of its own is created, which holds no slot.
    /// The requesting workspace is never changed (it may show an editor or a setup).
    /// </summary>
    internal async Task<WorkspaceWindowHandle?> ShowRecoveryCenterAsync(WorkspaceWindowHandle? source, Guid? focus)
    {
        if (!CanAcquireWorkspace) return null;
        if (focus is { } candidate && !_host.Recovery.Undecided.Any(c => c.CandidateId == candidate)) return null;
        var existing = _handles.FirstOrDefault(h => CanUseTarget(h) && h.Model!.Mode == MainViewMode.RecoveryCenter);
        if (existing is not null)
            return existing.Model!.EnterRecoveryCenter(focus) && Activate(existing.WindowId) ? existing : null;
        var start = source is not null && IsIdleStart(source) ? source : _handles.FirstOrDefault(IsIdleStart);
        if (start is not null && start.Model!.EnterRecoveryCenter(focus))
        {
            return Activate(start.WindowId) ? start : null;
        }
        if (TryCreate(WorkspaceCreationReason.RecoveryCenter) is not { } handle) return null;
        try
        {
            handle.Window.Show();
            if (await WaitForNewTargetAsync(handle) && handle.Model!.EnterRecoveryCenter(focus) && Activate(handle.WindowId)) return handle;
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            CleanupUnusedWorkspace(handle);
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
        }
        CleanupUnusedWorkspace(handle);
        return null;
    }

    private void OnRecoveryCenterLeft(WorkspaceWindowHandle handle)
    {
        // Only a workspace created for the Recovery Center is affected; a start workspace just shows its start page again.
        if (handle.Reason != WorkspaceCreationReason.RecoveryCenter || handle.Model?.HasProject == true) return;
        if (_handles.Any(h => !ReferenceEquals(h, handle) && h.HoldsStartSlot))
            ScheduleInternalClose(handle);
        else handle.Neutralize();   // it becomes the start workspace and takes over the start slot
    }

    /// <summary>Restores a candidate for the Recovery Center of <paramref name="source"/>; see <see cref="RecoveryRestoreCoordinator"/>.</summary>
    internal Task<bool> RestoreRecoveryAsync(Guid candidateId, WorkspaceWindowHandle source) =>
        (_restore ??= new(_host.Recovery, this)).RestoreAsync(candidateId, source);

    /// <summary>
    /// The workspace that receives a restore: another idle start workspace; the Recovery Center's own start workspace if this
    /// is the last undecided candidate; otherwise a new one that holds no start slot.
    /// </summary>
    internal async Task<(WorkspaceWindowHandle Handle, bool IsNew)?> AcquireRestoreTargetAsync(WorkspaceWindowHandle source, bool lastUndecided)
    {
        if (!CanAcquireWorkspace) return null;
        var idle = _handles.FirstOrDefault(h => !ReferenceEquals(h, source) && IsIdleStart(h));
        if (idle is not null) return (idle, false);
        if (lastUndecided && CanUseTarget(source) && source.HoldsStartSlot && source.Model is { Mode: MainViewMode.RecoveryCenter })
            return (source, false);
        if (TryCreate(WorkspaceCreationReason.Restore) is not { } handle) return null;
        try
        {
            handle.Window.Show();
            if (await WaitForNewTargetAsync(handle)) return (handle, true);
        }
        catch (OperationCanceledException) { }
        catch
        {
            CleanupUnusedWorkspace(handle);
            throw;
        }
        CleanupUnusedWorkspace(handle);
        return null;
    }

    // ---- Activation ------------------------------------------------------------------------------------------

    /// <summary>After MarkReady: one consumer for the whole process, routed to a workspace.</summary>
    internal void EnableActivation()
    {
        if (_activationEnabled) return;
        _activationEnabled = true;
        _host.Activation.SetConsumer(HandleLaunchRequestAsync);
        _host.Activation.Enqueued += PostPump;
        _host.SettingsCoordinator.ApplyCompleted += PostPump;
        _host.AppDialogs.Closed += PostPump;
    }

    internal void DisableActivation()
    {
        if (!_activationEnabled) return;
        _activationEnabled = false;
        _host.Activation.SetConsumer(null);
        _host.Activation.Enqueued -= PostPump;
        _host.SettingsCoordinator.ApplyCompleted -= PostPump;
        _host.AppDialogs.Closed -= PostPump;
    }

    internal void PostPump()
    {
        if (_host.SettingsCoordinator.ActiveApply is not null) return;
        Dispatcher.UIThread.Post(async () =>
        {
            try { await _host.Activation.PumpAsync(); }
            catch (Exception error) { Dispatcher.UIThread.Post(() => System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(error)); }
        });
    }

    private async Task<bool> HandleLaunchRequestAsync(LaunchRequest request)
    {
        // A last-window close or global quit still decides what happens to queued requests.
        if (_host.State != DesktopApplicationState.Ready || !CanAcquireWorkspace || Active is not { } source) return false;
        return request.Type == LaunchRequestType.OpenProject
            ? (await RequestOpenProjectAsync(null, request.Value!)).Status != ProjectOpenStatus.Unavailable
            : source.Window.HandleActivation();
    }

    private void SurfaceForRetry()
    {
        // While the host still starts, the initial workspace owns the (rare) blocking startup dialog.
        var target = _host.State == DesktopApplicationState.Starting ? _initial ?? Active : _host.Quit.QuitTarget ?? Active;
        target?.Window.Surface(target.Window.RetryTarget);
    }
}
