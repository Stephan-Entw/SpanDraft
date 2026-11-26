using Avalonia.Threading;
using SpanDraft.Desktop.Activation;
using SpanDraft.Desktop.Persistence;
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
    // The close transaction that put the host into the temporary quit state; only it may cancel or commit it.
    private WorkspaceCloseTransaction? _quitOwner;

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

    /// <summary>A workspace that can take launch requests: registered, model bound and not closing or about to close.</summary>
    private bool IsReady(WorkspaceWindowHandle handle) => handle.CanRoute && handle.Model is not null && _handles.Contains(handle);

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
        var handle = Create(WorkspaceCreationReason.None, isInitial: true);
        _initialCreated = true;
        return _initial = handle;
    }

    /// <summary>The one regular creation path of workspace windows.</summary>
    private WorkspaceWindowHandle Create(WorkspaceCreationReason reason, bool isInitial)
    {
        if (_host.State == DesktopApplicationState.ShuttingDown) throw new InvalidOperationException("The application is shutting down.");
        if (reason == WorkspaceCreationReason.None && !isInitial && StartSlotCount >= 1)
            throw new InvalidOperationException("At most one workspace may hold the start slot.");
        var handle = new WorkspaceWindowHandle(WindowId.Create(), reason, isInitial);
        handle.AttachWindow(new MainWindow(_host, handle));
        _handles.Add(handle);
        _active ??= handle;
        handle.Window.Activated += (_, _) => _active = handle;
        return handle;
    }

    /// <summary>Called when the window has created its view model: binds it once and wires the workspace hooks.</summary>
    internal void BindModel(WorkspaceWindowHandle handle, MainWindowViewModel model)
    {
        handle.BindModel(model);
        model.SessionActivated += handle.Neutralize;
        model.SetupCancelled += () => OnSetupCancelled(handle);
        model.RecoveryCenterLeft += () => OnRecoveryCenterLeft(handle);
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
        handle.CloseOwnedWindows();
        handle.Model?.ReleaseReport();
        handle.Model?.Dispose();
        // Only now is the ownership gone: opens that waited for this close decide anew.
        handle.CloseTransaction?.FinishClosed();
        if (Deregister(handle))
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
        PostPump();
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
    /// Internal New request (not wired to any user path before Phase 9): a StartOnly workspace is reused, otherwise a
    /// separate NewProject workspace is created. Returns the workspace now showing the CreateSetup, or null.
    /// </summary>
    internal async Task<WorkspaceWindowHandle?> RequestNewProjectAsync(WorkspaceWindowHandle source)
    {
        if (!_handles.Contains(source) || source.Model is not { } model) return null;
        var startOnly = source.Kind == WorkspaceKind.StartOnly ? source
            : _handles.FirstOrDefault(h => h.Kind == WorkspaceKind.StartOnly && h.Model is { Mode: MainViewMode.Start });
        if (startOnly?.Model is { } startModel)
        {
            if (!ReferenceEquals(startOnly, source)) Activate(startOnly.WindowId);
            return await startModel.NewAsync() ? startOnly : null;
        }
        if (!model.CanNewProject) return null;
        var handle = Create(WorkspaceCreationReason.NewProject, isInitial: false);
        handle.Window.Show();
        try
        {
            await handle.Window.Initialization;
            if (await handle.Model!.NewAsync()) { Activate(handle.WindowId); return handle; }
        }
        catch (OperationCanceledException) { /* the window went away; clean up below */ }
        catch (Exception error)
        {
            CloseInternal(handle);
            // Initialization failures stay visible instead of silently dropping the New request.
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
        }
        CloseInternal(handle);
        return null;
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
                return;
            }
        }
        handle.IsClosing = true;
        handle.Window.CloseCommitted();
    }

    // ---- Close ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Closes a workspace through its <see cref="WorkspaceCloseTransaction"/>. The reservation is taken synchronously before
    /// the first await. A window close of the last surviving workspace also arbitrates the application quit: waiting open
    /// requests are asked about before the decision, and again atomically with the shutdown commit; whether it is the last
    /// one is decided anew after every await. Close Project decides at the commit boundary between start page and close (§14).
    /// </summary>
    internal async Task<WorkspaceCloseOutcome> CloseAsync(WorkspaceWindowHandle handle, WorkspaceCloseReason reason)
    {
        if (!_handles.Contains(handle) || handle.Model is not { } model || handle.IsClosing) return WorkspaceCloseOutcome.Cancelled;
        if (!handle.Window.CanBeginClose) return WorkspaceCloseOutcome.Cancelled;
        if (WorkspaceCloseTransaction.TryBegin(model, handle, recordRecent: reason == WorkspaceCloseReason.Window) is not { } transaction)
            return WorkspaceCloseOutcome.Cancelled;
        var confirmed = new HashSet<LaunchRequest>(ReferenceEqualityComparer.Instance);
        try
        {
            if (reason == WorkspaceCloseReason.Window && !await ConfirmQuitAsync(transaction, handle, confirmed))
                return await AbortAsync(transaction);
            if (!await transaction.PrepareAsync()) return await AbortAsync(transaction);
            // ---- commit boundary: only the last-window arbitration can still abort, and it rolls back.
            bool closeWindow = reason == WorkspaceCloseReason.Window
                || _handles.Any(h => !ReferenceEquals(h, handle) && h.HoldsStartSlot);
            if (closeWindow && !await CommitQuitAsync(transaction, handle, confirmed)) return await AbortAsync(transaction);
            var report = transaction.Commit(closeWindow);
            if (closeWindow)
            {
                handle.CloseTransaction = transaction;
                handle.Window.CloseCommitted();
                return WorkspaceCloseOutcome.Closed;
            }
            PostPump();
            await report;
            return WorkspaceCloseOutcome.ShowsStart;
        }
        catch
        {
            await AbortAsync(transaction);
            throw;
        }
    }

    private async Task<WorkspaceCloseOutcome> AbortAsync(WorkspaceCloseTransaction transaction)
    {
        await transaction.AbortAsync(() => EndQuit(transaction));
        // Requests that waited while the close was pending are evaluated again.
        PostPump();
        return WorkspaceCloseOutcome.Cancelled;
    }

    /// <summary>Before the decision: as the last workspace, the waiting open requests are asked about. False keeps one.</summary>
    private async Task<bool> ConfirmQuitAsync(WorkspaceCloseTransaction transaction, WorkspaceWindowHandle handle, HashSet<LaunchRequest> confirmed)
    {
        while (true)
        {
            if (!IsLast(handle) || !BeginQuit(transaction)) { EndQuit(transaction); return true; }
            if (!await handle.Window.ConfirmDiscardPendingRequestsAsync(confirmed)) return false;
            // The set of workspaces may have changed meanwhile (e.g. a restore): decide again.
            if (IsLast(handle)) return true;
        }
    }

    /// <summary>
    /// At the commit boundary: if this is (still or now) the last workspace, the shutdown is committed atomically against new
    /// activations; a request that arrived meanwhile is asked about first. False when the user keeps one.
    /// </summary>
    private async Task<bool> CommitQuitAsync(WorkspaceCloseTransaction transaction, WorkspaceWindowHandle handle, HashSet<LaunchRequest> confirmed)
    {
        while (true)
        {
            if (!IsLast(handle)) { EndQuit(transaction); return true; }
            // Another transaction owns a quit only once it committed the shutdown: nothing is left to decide here.
            if (!BeginQuit(transaction)) return true;
            if (_host.TryCommitShutdown(confirmed)) return true;
            if (!await handle.Window.ConfirmDiscardPendingRequestsAsync(confirmed)) return false;
        }
    }

    /// <summary>Takes the quit for <paramref name="transaction"/>; false if another transaction owns it.</summary>
    private bool BeginQuit(WorkspaceCloseTransaction transaction)
    {
        if (ReferenceEquals(_quitOwner, transaction)) return true;
        if (_quitOwner is not null) return false;
        _host.BeginQuit();
        if (_host.State != DesktopApplicationState.QuitInProgress) return _host.State != DesktopApplicationState.ShuttingDown;
        _quitOwner = transaction;
        return true;
    }

    /// <summary>Only the owner of the quit cancels it; other transactions leave it alone.</summary>
    private void EndQuit(WorkspaceCloseTransaction transaction)
    {
        if (!ReferenceEquals(_quitOwner, transaction)) return;
        _quitOwner = null;
        _host.CancelQuit();
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
        IsReady(handle) && handle.HoldsStartSlot && handle.Model is { Mode: MainViewMode.Start, IsWorkspaceBusy: false };

    /// <summary>
    /// Shows the Recovery Center from a UI hook (menu, start page, a refused open): fire and forget, failures surface on the
    /// dispatcher like any other unhandled UI error.
    /// </summary>
    internal void RequestRecoveryCenter(WorkspaceWindowHandle source, Guid? focus) => Dispatcher.UIThread.Post(async () =>
    {
        try { await ShowRecoveryCenterAsync(source, focus); }
        catch (Exception error) { Dispatcher.UIThread.Post(() => System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(error)); }
    });

    /// <summary>
    /// The Recovery Center is a workspace mode, not a dialog: an existing one is shown again; otherwise an idle start
    /// workspace navigates to it and keeps its start slot; otherwise a workspace of its own is created, which holds no slot.
    /// The requesting workspace is never changed (it may show an editor or a setup).
    /// </summary>
    internal async Task<WorkspaceWindowHandle?> ShowRecoveryCenterAsync(WorkspaceWindowHandle? source, Guid? focus)
    {
        if (_host.State == DesktopApplicationState.ShuttingDown) return null;
        var existing = _handles.FirstOrDefault(h => IsReady(h) && h.Model!.Mode == MainViewMode.RecoveryCenter);
        if (existing is not null)
        {
            existing.Model!.EnterRecoveryCenter(focus);
            Activate(existing.WindowId);
            return existing;
        }
        var start = source is not null && IsIdleStart(source) ? source : _handles.FirstOrDefault(IsIdleStart);
        if (start is not null && start.Model!.EnterRecoveryCenter(focus))
        {
            Activate(start.WindowId);
            return start;
        }
        var handle = Create(WorkspaceCreationReason.RecoveryCenter, isInitial: false);
        handle.Window.Show();
        try
        {
            await handle.Window.Initialization;
            if (handle.Model!.EnterRecoveryCenter(focus)) { Activate(handle.WindowId); return handle; }
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            CloseInternal(handle);
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
        }
        CloseInternal(handle);
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
    internal async Task<(WorkspaceWindowHandle Handle, bool IsNew)> AcquireRestoreTargetAsync(WorkspaceWindowHandle source, bool lastUndecided)
    {
        var idle = _handles.FirstOrDefault(h => !ReferenceEquals(h, source) && IsIdleStart(h));
        if (idle is not null) return (idle, false);
        if (lastUndecided && source.HoldsStartSlot && source.Model is { Mode: MainViewMode.RecoveryCenter })
            return (source, false);
        var handle = Create(WorkspaceCreationReason.Restore, isInitial: false);
        handle.Window.Show();
        try { await handle.Window.Initialization; }
        catch
        {
            CloseInternal(handle);
            throw;
        }
        return (handle, true);
    }

    // ---- Activation ------------------------------------------------------------------------------------------

    /// <summary>After MarkReady: one consumer for the whole process, routed to a workspace.</summary>
    internal void EnableActivation()
    {
        if (_activationEnabled) return;
        _activationEnabled = true;
        _host.Activation.SetConsumer(HandleLaunchRequestAsync);
        _host.Activation.Enqueued += PostPump;
    }

    internal void DisableActivation()
    {
        if (!_activationEnabled) return;
        _activationEnabled = false;
        _host.Activation.SetConsumer(null);
        _host.Activation.Enqueued -= PostPump;
    }

    internal void PostPump() => Dispatcher.UIThread.Post(async () =>
    {
        try { await _host.Activation.PumpAsync(); }
        catch (Exception error) { Dispatcher.UIThread.Post(() => System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(error)); }
    });

    private Task<bool> HandleLaunchRequestAsync(LaunchRequest request)
    {
        // A path owned by a closing workspace is not decided here: the open arbitration waits for that close and decides
        // anew, so requests for other paths are never held back by it.
        // Open requests prefer the start workspace; everything else the active one.
        var target = request.Type == LaunchRequestType.OpenProject
            ? _handles.FirstOrDefault(h => IsReady(h) && h.HoldsStartSlot) ?? Active
            : Active;
        return target is null ? Task.FromResult(false) : target.Window.HandleLaunchRequestAsync(request);
    }

    private void SurfaceForRetry()
    {
        // While the host still starts, the initial workspace owns the (rare) blocking startup dialog.
        var target = _host.State == DesktopApplicationState.Starting ? _initial ?? Active : Active;
        target?.Window.Surface(target.Window.RetryTarget);
    }
}
