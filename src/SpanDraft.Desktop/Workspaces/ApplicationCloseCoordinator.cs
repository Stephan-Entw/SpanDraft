using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Threading;
using SpanDraft.Desktop.Activation;

namespace SpanDraft.Desktop.Workspaces;

/// <summary>What a platform close of a window means for the application (§74, §76).</summary>
internal enum PlatformClose { None, UserQuit, SystemShutdown }

/// <summary>
/// The window close requests that come from the platform's shutdown rather than from a window itself. Avalonia's lifetime
/// asks every top-level window non-programmatically with <see cref="WindowCloseReason.ApplicationShutdown"/> or
/// <see cref="WindowCloseReason.OSShutdown"/>; its own <c>TryShutdown</c>/<c>Shutdown</c> ask programmatically.
/// </summary>
internal static class PlatformCloseRequest
{
    public static bool IsPlatform(WindowClosingEventArgs e) => IsPlatform(e.CloseReason, e.IsProgrammatic);

    public static bool IsPlatform(WindowCloseReason reason, bool isProgrammatic) =>
        !isProgrammatic && reason is WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown;
}

/// <summary>One running user quit: its order, the open requests confirmed for dropping and whether a system shutdown took over.</summary>
internal sealed class QuitRun(IReadOnlyList<WorkspaceWindowHandle> order)
{
    /// <summary>The active workspace first, then the others in registration order; captured when the quit was reserved.</summary>
    public IReadOnlyList<WorkspaceWindowHandle> Order { get; } = order;
    public HashSet<LaunchRequest> Confirmed { get; } = new(ReferenceEqualityComparer.Instance);
    /// <summary>A system shutdown took over: the run ends without any further effect.</summary>
    public bool Superseded { get; set; }
    /// <summary>The workspace whose close is running (the one to surface for further quit signals).</summary>
    public WorkspaceWindowHandle? Current { get; set; }
    /// <summary>The asynchronous part of the run, for tests.</summary>
    public Task Completion { get; set; } = Task.CompletedTask;
}

/// <summary>
/// The application quit (§18, §74–§76): the one owner of the host's quit and shutdown commit. A user quit is preflighted and
/// reserved synchronously, then closes workspace after workspace with their own <see cref="WorkspaceCloseTransaction"/>,
/// the active one first; a cancel or failure ends it and keeps everything not closed yet. The last-window close of
/// Fenster-X arbitrates its quit here as well. A system shutdown is never refused: it stops mutations, asks nothing and
/// gives up no recovery.
/// </summary>
/// <remarks>
/// Platform quit signals are recognized in <see cref="Window.Closing"/>: Avalonia's <c>ShutdownRequestedEventArgs</c> does
/// not tell a user quit from an OS shutdown publicly, but the close reason of the windows it asks does. Cancelling those
/// closes cancels the platform signal synchronously (the lifetime finds windows left and reports the cancel).
/// </remarks>
internal sealed class ApplicationCloseCoordinator
{
    private readonly DesktopApplicationHost _host;
    // The quit owner: a running user quit or the last-window close transaction that arbitrates its quit; never both.
    private object? _owner;

    internal ApplicationCloseCoordinator(DesktopApplicationHost host, bool? windowsPlatform = null)
    {
        _host = host;
        WindowsPlatform = windowsPlatform ?? OperatingSystem.IsWindows();
    }

    /// <summary>The platform whose shutdown semantics apply (see <see cref="Classify"/>); settable for tests.</summary>
    internal bool WindowsPlatform { get; set; }

    private WorkspaceWindowManager Workspaces => _host.Workspaces;

    /// <summary>The running user quit, if any.</summary>
    internal QuitRun? UserQuit => _owner as QuitRun;
    internal bool IsUserQuitRunning => _owner is QuitRun;
    /// <summary>A system shutdown was recognized: every window may close, nothing is decided any more.</summary>
    internal bool IsSystemShutdown { get; private set; }

    // ---- platform -----------------------------------------------------------------------------------------------

    /// <summary>
    /// Classifies a window close: a platform close with <see cref="WindowCloseReason.OSShutdown"/> is a system shutdown; with
    /// <see cref="WindowCloseReason.ApplicationShutdown"/> it is the user's quit on macOS (app menu, Dock, Apple Event) but the
    /// end of the session on Windows, whose only such source is <c>WM_QUERYENDSESSION</c> (log off). Programmatic closes are
    /// never platform closes.
    /// </summary>
    internal PlatformClose Classify(WindowCloseReason reason, bool isProgrammatic) =>
        !PlatformCloseRequest.IsPlatform(reason, isProgrammatic) ? PlatformClose.None
        : reason == WindowCloseReason.OSShutdown || WindowsPlatform ? PlatformClose.SystemShutdown
        : PlatformClose.UserQuit;

    /// <summary>
    /// Called from a workspace window's synchronous <c>Closing</c>: only classifies, reserves and schedules. A user quit is
    /// requested (at most one runs) and the caller cancels its close; a system shutdown is begun and the window may close.
    /// </summary>
    internal PlatformClose OnPlatformClosing(WindowCloseReason reason, bool isProgrammatic)
    {
        var kind = Classify(reason, isProgrammatic);
        if (kind == PlatformClose.SystemShutdown) BeginSystemShutdown();
        else if (kind == PlatformClose.UserQuit) RequestQuit();
        return kind;
    }

    // ---- user quit ----------------------------------------------------------------------------------------------

    /// <summary>
    /// The one entry of every user quit source. Preflight and the global reservation run synchronously: the host enters
    /// QuitInProgress, every workspace is frozen and leaves request routing. The rest is posted, so a caller inside a
    /// platform callback closes nothing. False when no new quit was started: one already runs (its question is surfaced) or
    /// something cannot be interrupted safely (it is surfaced).
    /// </summary>
    internal bool RequestQuit()
    {
        if (IsSystemShutdown) return false;
        if (_owner is not null) { SurfaceQuit(); return false; }
        if (_host.State != DesktopApplicationState.Ready) return false;
        var handles = Workspaces.Handles.Where(static h => !h.IsClosing).ToList();
        if (handles.Count == 0) return false;
        foreach (var handle in handles)
            if (handle.Model is not { } model || !handle.Window.IsReadyForQuit || handle.IsCloseRequested || model.IsWorkspaceBusy)
            {
                handle.Window.Surface(handle.Window.RetryTarget);
                return false;
            }
        if (_host.AppDialogs.BlocksQuit(out var blocker))
        {
            blocker.Activate();
            return false;
        }
        // The order is read before the reservation takes every workspace out of routing.
        var run = new QuitRun(Workspaces.QuitOrder());
        _host.BeginQuit();
        if (_host.State != DesktopApplicationState.QuitInProgress) return false;
        _owner = run;
        foreach (var handle in handles) handle.Model!.FreezeForQuit();
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        run.Completion = finished.Task;
        Dispatcher.UIThread.Post(async () =>
        {
            try { await RunAsync(run); }
            catch (Exception error) { Dispatcher.UIThread.Post(() => System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(error)); }
            finally { finished.TrySetResult(); }
        });
        return true;
    }

    private bool IsOpen(WorkspaceWindowHandle handle) => Workspaces.Handles.Contains(handle) && !handle.IsClosing;

    private async Task RunAsync(QuitRun run)
    {
        try
        {
            // Safely closable application dialogs go first; the others kept the quit from starting.
            await _host.AppDialogs.CloseForQuitAsync();
            if (run.Superseded) return;
            await CheckpointAsync(run);
            if (run.Superseded) return;
            // Waiting open requests are asked about once, before the first decision.
            if (run.Order.FirstOrDefault(IsOpen) is { } first)
            {
                run.Current = first;
                bool drop = await first.Window.ConfirmDiscardPendingRequestsAsync(run.Confirmed);
                if (run.Superseded) return;
                if (!drop) { EndUserQuit(run); return; }
            }
            foreach (var handle in run.Order)
            {
                if (!IsOpen(handle)) continue;
                run.Current = handle;
                var outcome = await Workspaces.CloseAsync(handle, WorkspaceCloseReason.Window, run);
                if (run.Superseded) return;
                // A cancel or failure already ended the quit through the transaction's rollback; only a refusal did not.
                if (outcome != WorkspaceCloseOutcome.Closed) { EndUserQuit(run); return; }
                // Completely closed and cleaned up before the next one is asked.
                await handle.Finalized;
                if (run.Superseded) return;
            }
            // Every workspace is closed; the last one committed the shutdown before its own commit.
            if (ReferenceEquals(_owner, run) && _host.State == DesktopApplicationState.ShuttingDown) _owner = null;
            else EndUserQuit(run);
        }
        catch
        {
            if (!run.Superseded) EndUserQuit(run);
            throw;
        }
    }

    /// <summary>
    /// The recovery checkpoint before the first question (§74): every workspace publishes its confirmed state now. A failure
    /// of one keeps neither the others from their checkpoint nor the quit from going on; it is shown once and traced.
    /// </summary>
    private async Task CheckpointAsync(QuitRun run)
    {
        var models = run.Order.Where(IsOpen).Select(static h => h.Model!).ToList();
        var failures = await Task.WhenAll(models.Select(static m => m.CheckpointRecoveryAsync()));
        for (int i = 0; i < models.Count; i++)
        {
            if (failures[i] is not { } error) continue;
            Trace.TraceWarning("Recovery checkpoint before quitting failed: {0}", error);
            if (run.Superseded) return;
            await models[i].ReportCheckpointFailureAsync(error);
        }
    }

    /// <summary>Ends a user quit that did not shut down: every remaining workspace is released, the host is Ready again.</summary>
    internal void EndUserQuit(QuitRun run)
    {
        if (!ReferenceEquals(_owner, run)) return;
        _owner = null;
        run.Current = null;
        foreach (var handle in Workspaces.Handles.ToList()) handle.Model?.ReleaseQuitFreeze();
        _host.CancelQuit();
        Workspaces.PostPump();
    }

    private void SurfaceQuit()
    {
        var target = (_owner as QuitRun)?.Current ?? Workspaces.Handles.FirstOrDefault(h => h.CloseTransaction is not null);
        target?.Window.Surface(target.Window.RetryTarget);
    }

    /// <summary>The window to surface for an external request answered with Retry, while a quit runs.</summary>
    internal WorkspaceWindowHandle? QuitTarget => (_owner as QuitRun)?.Current;

    // ---- shutdown commit (user quit and last window) ------------------------------------------------------------------

    /// <summary>
    /// Before the decision of a window close: as the last workspace it takes the quit and asks about waiting open requests.
    /// False keeps one (the close is cancelled).
    /// </summary>
    internal async Task<bool> ConfirmLastWindowAsync(WorkspaceCloseTransaction transaction, WorkspaceWindowHandle handle,
        HashSet<LaunchRequest> confirmed)
    {
        while (true)
        {
            if (!Workspaces.IsLast(handle) || !TryBeginWindowQuit(transaction)) { EndWindowQuit(transaction); return true; }
            if (!await handle.Window.ConfirmDiscardPendingRequestsAsync(confirmed)) return false;
            // The set of workspaces may have changed meanwhile (e.g. a restore): decide again.
            if (Workspaces.IsLast(handle)) return true;
        }
    }

    /// <summary>
    /// At the commit boundary of the closing <paramref name="handle"/>: if it is (still or now) the last workspace, the shutdown
    /// is committed atomically against new activations; a request that arrived meanwhile is asked about first. False when the
    /// user keeps one, or when the workspace is gone (system shutdown). <paramref name="owner"/> is the user quit or the
    /// window close's transaction.
    /// </summary>
    internal async Task<bool> CommitShutdownAsync(object owner, WorkspaceWindowHandle handle, HashSet<LaunchRequest> confirmed)
    {
        while (true)
        {
            if (handle.IsFinalized || owner is QuitRun { Superseded: true }) return false;
            if (!Workspaces.IsLast(handle))
            {
                if (owner is WorkspaceCloseTransaction transaction) EndWindowQuit(transaction);
                return true;
            }
            // Another transaction owns a quit only once it committed the shutdown: nothing is left to decide here.
            if (owner is WorkspaceCloseTransaction own && !TryBeginWindowQuit(own)) return true;
            if (owner is QuitRun run && !ReferenceEquals(_owner, run)) return false;
            if (_host.TryCommitShutdown(confirmed)) return true;
            if (!await handle.Window.ConfirmDiscardPendingRequestsAsync(confirmed)) return false;
        }
    }

    /// <summary>Takes the quit for a last-window close; false if another owner holds it.</summary>
    private bool TryBeginWindowQuit(WorkspaceCloseTransaction transaction)
    {
        if (ReferenceEquals(_owner, transaction)) return true;
        if (_owner is not null) return false;
        _host.BeginQuit();
        if (_host.State != DesktopApplicationState.QuitInProgress) return _host.State != DesktopApplicationState.ShuttingDown;
        _owner = transaction;
        return true;
    }

    /// <summary>Only the window close that owns the quit cancels it; others leave it alone.</summary>
    internal void EndWindowQuit(WorkspaceCloseTransaction transaction)
    {
        if (!ReferenceEquals(_owner, transaction)) return;
        _owner = null;
        _host.CancelQuit();
    }

    // ---- system shutdown -----------------------------------------------------------------------------------------

    /// <summary>
    /// The OS ends the session (§76): never refused. Synchronously new mutations stop (every workspace is frozen), a running
    /// user quit is superseded and the host shuts down (no request is taken any more). No dialog is opened and no recovery
    /// is given up: the platform closes the windows itself. A best-effort checkpoint of the workspaces without a running close,
    /// leave or operation of their own is started before the platform closes the windows, without waiting for publication.
    /// </summary>
    internal void BeginSystemShutdown()
    {
        if (IsSystemShutdown) return;
        IsSystemShutdown = true;
        if (_owner is QuitRun run) run.Superseded = true;
        var handles = Workspaces.Handles.ToList();
        // Decided before the freeze: a workspace whose own close, discard or operation runs is left alone.
        var unreserved = handles.Where(static h => h.CanRoute && h.Model is { IsRecoveryFaulted: false } model && !model.HasOwnWork)
            .Select(static h => h.Model!).ToList();
        foreach (var handle in handles) handle.Model?.FreezeForQuit();
        _host.BeginShutdown();
        foreach (var model in unreserved) _ = CheckpointForShutdownAsync(model);
    }

    private static async Task CheckpointForShutdownAsync(ViewModels.MainWindowViewModel model)
    {
        try { await model.CheckpointForShutdownAsync().ConfigureAwait(false); }
        catch (Exception error) { Trace.TraceWarning("Recovery checkpoint during system shutdown failed: {0}", error); }
    }
}
