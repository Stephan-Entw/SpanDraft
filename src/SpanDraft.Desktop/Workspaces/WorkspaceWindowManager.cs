using Avalonia.Threading;
using SpanDraft.Desktop.Activation;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.ViewModels;

namespace SpanDraft.Desktop.Workspaces;

/// <summary>
/// The central workspace/window registry: creates, registers, looks up and activates workspaces and enforces
/// <c>StartOnlyWorkspaceCount &lt;= 1</c>. It reads no project files, decides no identity and saves nothing.
/// </summary>
internal sealed class WorkspaceWindowManager
{
    private readonly DesktopApplicationHost _host;
    private readonly List<WorkspaceWindowHandle> _handles = [];
    private WorkspaceWindowHandle? _initial, _active;
    private bool _activationEnabled;

    internal WorkspaceWindowManager(DesktopApplicationHost host)
    {
        _host = host;
        host.RetryServed += () => Dispatcher.UIThread.Post(SurfaceForRetry);
    }

    public IReadOnlyList<WorkspaceWindowHandle> Handles => _handles;
    public WorkspaceWindowHandle? Initial => _initial;
    public WorkspaceWindowHandle? Active => _active is { } active && IsReady(active) ? active : _handles.FirstOrDefault(IsReady);
    public int StartOnlyWorkspaceCount => _handles.Count(static h => h.HoldsStartSlot);

    /// <summary>A workspace that can take launch requests: registered, model bound and not about to close.</summary>
    private bool IsReady(WorkspaceWindowHandle handle) => !handle.IsClosing && handle.Model is not null && _handles.Contains(handle);

    /// <summary>True when this is the only registered workspace, i.e. closing it ends the application.</summary>
    internal bool IsLast(WorkspaceWindowHandle handle) => _handles.Count == 1 && _handles[0] == handle;

    public WorkspaceWindowHandle? Find(WindowId id) => _handles.FirstOrDefault(h => h.WindowId == id);

    /// <summary>Creates the initial workspace; the only one that runs legacy startup and recovery.</summary>
    public WorkspaceWindowHandle CreateInitial()
    {
        if (_initial is not null) throw new InvalidOperationException("The initial workspace already exists.");
        return _initial = Create(WorkspaceCreationReason.None, isInitial: true);
    }

    /// <summary>The one regular creation path of workspace windows.</summary>
    private WorkspaceWindowHandle Create(WorkspaceCreationReason reason, bool isInitial)
    {
        if (reason == WorkspaceCreationReason.None && !isInitial && StartOnlyWorkspaceCount >= 1)
            throw new InvalidOperationException("At most one StartOnly workspace may exist.");
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
        if (Find(id) is not { } handle) return false;
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
        // A workspace created for New disappears; one that came from the start page just shows it again.
        if (handle.Reason != WorkspaceCreationReason.NewProject) return;
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
        // Open requests prefer the start workspace; everything else the active one.
        var target = request.Type == LaunchRequestType.OpenProject
            ? _handles.FirstOrDefault(h => IsReady(h) && h.HoldsStartSlot) ?? Active
            : Active;
        return target is null ? Task.FromResult(false) : target.Window.HandleLaunchRequestAsync(request);
    }

    private void SurfaceForRetry()
    {
        // While the host still starts, the initial workspace owns the blocking recovery dialog.
        var target = _host.State == DesktopApplicationState.Starting ? _initial ?? Active : Active;
        target?.Window.Surface(target.Window.RetryTarget);
    }
}
