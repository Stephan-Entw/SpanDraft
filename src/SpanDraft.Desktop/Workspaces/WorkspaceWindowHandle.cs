using Avalonia.Controls;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.ViewModels;

namespace SpanDraft.Desktop.Workspaces;

/// <summary>Why a workspace exists; only the states Phase 5 needs.</summary>
internal enum WorkspaceCreationReason { None, NewProject }

/// <summary>Explicit workspace classification; see <see cref="WorkspaceWindowHandle.Kind"/>.</summary>
internal enum WorkspaceKind { StartOnly, CreateSetup, Project }

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

    /// <summary>True while this workspace occupies the single start slot: no project and not a dedicated New workspace.</summary>
    internal bool HoldsStartSlot => !IsClosing && Reason == WorkspaceCreationReason.None && Model?.HasProject != true;

    /// <summary>Set synchronously when the owner has decided to close the window, so it no longer counts as a start workspace.</summary>
    internal bool IsClosing { get; set; }

    /// <summary>Explicit classification; a New workspace is never counted as StartOnly, not even before its model exists.</summary>
    internal WorkspaceKind Kind => Model?.HasProject == true ? WorkspaceKind.Project
        : Reason == WorkspaceCreationReason.NewProject || Model?.Mode == MainViewMode.ProjectSetup ? WorkspaceKind.CreateSetup
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
