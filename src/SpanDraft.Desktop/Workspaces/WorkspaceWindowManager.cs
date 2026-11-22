using Avalonia.Threading;
using SpanDraft.Desktop.Activation;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.ViewModels;

namespace SpanDraft.Desktop.Workspaces;

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

    /// <summary>A workspace that can take launch requests: registered, model bound and not closing or about to close.</summary>
    private bool IsReady(WorkspaceWindowHandle handle) => handle.CanRoute && handle.Model is not null && _handles.Contains(handle);

    /// <summary>True when this is the only registered workspace, i.e. closing it ends the application.</summary>
    internal bool IsLast(WorkspaceWindowHandle handle) => _handles.Count == 1 && _handles[0] == handle;

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
        model.ProjectClosing = () => OnProjectClosing(handle);
    }

    /// <summary>Final cleanup, called once from the window's Closed path. True when this was the last workspace.</summary>
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

    /// <summary>Internal close of a projectless workspace; the cleanup itself happens once in the window's Closed path.</summary>
    internal void CloseInternal(WorkspaceWindowHandle handle)
    {
        if (!_handles.Contains(handle)) return;
        if (handle.Model?.HasProject == true) throw new InvalidOperationException("Only a projectless workspace can be closed internally.");
        handle.IsClosing = true;
        handle.Window.CloseApproved();
    }

    private void OnSetupCancelled(WorkspaceWindowHandle handle)
    {
        // A workspace created for New or for a restored draft disappears; one that came from the start page just shows it again.
        if (handle.Reason is not (WorkspaceCreationReason.NewProject or WorkspaceCreationReason.Restore)) return;
        handle.IsClosing = true;
        Dispatcher.UIThread.Post(() => CloseInternal(handle));
    }

    /// <summary>Close Project (§14): becomes the start page unless another start workspace exists; then it closes.</summary>
    private void OnProjectClosing(WorkspaceWindowHandle handle)
    {
        if (!_handles.Any(h => !ReferenceEquals(h, handle) && h.HoldsStartSlot)) return;
        handle.IsClosing = true;
        Dispatcher.UIThread.Post(() => CloseInternal(handle));
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
        {
            handle.IsClosing = true;
            Dispatcher.UIThread.Post(() => CloseInternal(handle));
        }
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
        // Temporary conservative Phase-5 rule (finer handling is Phase 7): while a workspace that still owns a project
        // identity is closing or about to close, external opens wait in the queue. Its ownership is released by the final
        // cleanup, after which the request is evaluated normally again. The manager resolves no identity itself.
        if (request.Type == LaunchRequestType.OpenProject
            && _handles.Any(h => !h.CanRoute && _host.ProjectPaths.IdentityOwnedBy(h.WindowId) is not null))
            return Task.FromResult(false);
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
