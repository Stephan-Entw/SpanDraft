using Avalonia.Controls;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Services;
using SpanDraft.Desktop.ViewModels;

namespace SpanDraft.Desktop.Workspaces;

/// <summary>
/// Why a workspace exists. Only <see cref="None"/> workspaces reserve the single start slot; the others were created for
/// one purpose and lose that reason once it is fulfilled.
/// </summary>
internal enum WorkspaceCreationReason { None, NewProject, OpenProject, RecoveryCenter, Restore }

/// <summary>Explicit workspace classification; see <see cref="WorkspaceWindowHandle.Kind"/>.</summary>
internal enum WorkspaceKind { StartOnly, CreateSetup, Project, RecoveryCenter }

/// <summary>
/// The unique assignment of one workspace: its <see cref="WindowId"/>, window and view model. Session, editor, undo,
/// dirty state, analysis and report stay in the view model.
/// </summary>
internal sealed class WorkspaceWindowHandle
{
    private readonly List<Window> _ownedWindows = [];

    internal WorkspaceWindowHandle(WindowId windowId, WorkspaceCreationReason reason, bool isInitial)
    {
        WindowId = windowId;
        Reason = reason;
        IsInitial = isInitial;
    }

    public WindowId WindowId { get; }
    public MainWindow Window { get; private set; } = null!;
    /// <summary>Bound exactly once when the window is opened; null before that.</summary>
    public MainWindowViewModel? Model { get; private set; }
    public WorkspaceCreationReason Reason { get; private set; }
    internal SettingsCoordinator.RegistrationReservation? SettingsRegistration { get; set; }

    internal void ReleaseSettingsRegistration()
    {
        SettingsRegistration?.Dispose();
        SettingsRegistration = null;
    }
    /// <summary>The initial workspace is the only one that runs the Phase-2 legacy startup and recovery.</summary>
    public bool IsInitial { get; }
    public IReadOnlyList<Window> OwnedWindows => _ownedWindows;

    internal void AttachWindow(MainWindow window)
    {
        if (Window is not null) throw new InvalidOperationException("The handle already has a window.");
        Window = window;
    }

    internal void BindModel(MainWindowViewModel model)
    {
        if (Model is not null) throw new InvalidOperationException("The handle already has a view model.");
        if (model.WindowId != WindowId) throw new ArgumentException("The model belongs to another workspace.", nameof(model));
        Model = model;
    }

    /// <summary>After the project was created or opened the creation reason has no meaning any more.</summary>
    internal void Neutralize() => Reason = WorkspaceCreationReason.None;

    /// <summary>
    /// True while this ordinary workspace reserves the single start slot and has no project. This is
    /// independent of <see cref="Kind"/>: a start workspace that currently shows CreateSetup still holds the slot,
    /// but is classified as <see cref="WorkspaceKind.CreateSetup"/>. A merely requested (still cancellable) close keeps it.
    /// </summary>
    internal bool HoldsStartSlot => !IsClosing && Reason == WorkspaceCreationReason.None && Model?.HasProject != true;

    /// <summary>The workspace was selected for an open. Its start slot, if any, stays reserved by this workspace.</summary>
    internal bool IsOpenTargetReserved { get; set; }

    /// <summary>The close is decided and cannot be cancelled any more; the workspace gives up its start slot.</summary>
    internal bool IsClosing { get; set; }

    /// <summary>
    /// A window close attempt is running (e.g. leave dialogs are open) and may still be cancelled. The workspace takes
    /// no new activation or open requests meanwhile, but keeps its start slot.
    /// </summary>
    internal bool IsCloseRequested { get; set; }

    /// <summary>False while the workspace is closing or about to; such a workspace must not be chosen for requests.</summary>
    internal bool CanRoute => !IsClosing && !IsCloseRequested;

    /// <summary>
    /// The running or committed close transaction of this workspace; the final cleanup finishes it (or orphans it when the
    /// platform closed the window before the commit).
    /// </summary>
    internal WorkspaceCloseTransaction? CloseTransaction { get; set; }

    private readonly TaskCompletionSource _finalized = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>The one final cleanup of this workspace has begun (it runs exactly once).</summary>
    internal bool IsFinalized { get; set; }

    /// <summary>Completes once the final cleanup has run: window closed, owned windows, report, session and ownership released.</summary>
    internal Task Finalized => _finalized.Task;

    internal void CompleteFinalized() => _finalized.TrySetResult();

    /// <summary>
    /// What the workspace currently shows. Start-slot ownership and open-target availability are decided separately.
    /// </summary>
    internal WorkspaceKind Kind => Model?.HasProject == true ? WorkspaceKind.Project
        : Reason == WorkspaceCreationReason.RecoveryCenter || Model?.Mode == MainViewMode.RecoveryCenter ? WorkspaceKind.RecoveryCenter
        : Reason is WorkspaceCreationReason.NewProject or WorkspaceCreationReason.Restore || Model?.Mode == MainViewMode.ProjectSetup
            ? WorkspaceKind.CreateSetup
        : WorkspaceKind.StartOnly;

    /// <summary>Couples a workspace-owned modeless window to this workspace.</summary>
    internal void AttachOwnedWindow(Window window)
    {
        _ownedWindows.Add(window);
        window.Closed += (_, _) => _ownedWindows.Remove(window);
    }

    internal void CloseOwnedWindows()
    {
        foreach (var window in _ownedWindows.ToList()) window.Close();
        _ownedWindows.Clear();
    }
}
