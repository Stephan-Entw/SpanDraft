using Avalonia.Threading;
using SpanDraft.Desktop;
using SpanDraft.Desktop.Activation;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.ViewModels;
using SpanDraft.Desktop.Workspaces;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

/// <summary>Phase 5: internal workspace ownership. Multiwindow stays hidden; these tests use the manager's internal seams.</summary>
[Collection("Avalonia")]
public sealed class WorkspaceWindowManagerTests
{
    private static DesktopApplicationHost Host(Files files) =>
        new(new DesktopStartupContext(files, new LocalSettingsStore(files, "settings"), UserSettings.Default)
        { ProjectIdentities = Identities });

    private static WorkspaceWindowHandle Start(DesktopApplicationHost host)
    {
        var handle = host.Workspaces.CreateInitial();
        DesktopControlEnvironment.ShowInitialized(handle.Window);
        return handle;
    }

    private static T Run<T>(Func<Task<T>> action)
    {
        T result = default!;
        DesktopControlEnvironment.RunOnDispatcher(async () => result = await action());
        Dispatcher.UIThread.RunJobs();
        return result;
    }

    private static void Flush() => Dispatcher.UIThread.RunJobs();

    private static string Project(Files files, string name)
    {
        string path = TestPath(name);
        files.Data[path] = SpanDraft.Desktop.Persistence.ProjectFileCodec.Serialize(State());
        return path;
    }

    private static void CreateProject(WorkspaceWindowHandle handle)
    {
        var model = handle.Model!;
        if (model.Mode == MainViewMode.Start) Assert.True(Run(() => model.NewAsync()));
        CompleteSetup(model);
        Assert.True(Run(() => model.Setup.ConfirmAsync()));
        Assert.NotNull(model.Session);
    }

    [Fact]
    public void NormalStartHasExactlyOneHandleWhichIsTheDesktopMainWindow()
    {
        using var environment = new DesktopControlEnvironment();
        var host = Host(new Files());
        var handle = Start(host);
        Assert.Same(handle, Assert.Single(host.Workspaces.Handles));
        Assert.Same(handle, host.Workspaces.Initial);
        Assert.Same(handle, host.Workspaces.Active);
        Assert.Equal(1, host.Workspaces.StartOnlyWorkspaceCount);
        Assert.Equal(WorkspaceKind.StartOnly, handle.Kind);
        Assert.Equal(handle.WindowId, handle.Window.WindowId);
        Assert.Equal(handle.WindowId, handle.Model!.WindowId);
        Assert.Same(handle.Model, handle.Window.DataContext);
        Assert.Throws<InvalidOperationException>(() => host.Workspaces.CreateInitial());
    }

    [Fact]
    public void HandlesHaveStableUniqueIdsLookupAndSeparateModels()
    {
        using var environment = new DesktopControlEnvironment();
        var host = Host(new Files());
        var a = Start(host);
        CreateProject(a);
        var b = Run(() => host.Workspaces.RequestNewProjectAsync(a))!;
        Assert.NotEqual(a.WindowId, b.WindowId);
        Assert.Same(a, host.Workspaces.Find(a.WindowId));
        Assert.Same(b, host.Workspaces.Find(b.WindowId));
        Assert.Null(host.Workspaces.Find(WindowId.Create()));
        Assert.NotSame(a.Model, b.Model);
        Assert.NotNull(a.Model!.Session);
        Assert.Null(b.Model!.Session);
        // Global state is shared, not copied.
        Assert.Same(a.Model.Services, b.Model.Services);
        Assert.Same(a.Model.Services.Recents, b.Model.Services.Recents);
        Assert.Same(host.Services, b.Model.Services);
    }

    [Fact]
    public void ActivatingByWindowIdSurfacesAndTracksTheRightWorkspace()
    {
        using var environment = new DesktopControlEnvironment();
        var host = Host(new Files());
        var a = Start(host);
        CreateProject(a);
        var b = Run(() => host.Workspaces.RequestNewProjectAsync(a))!;
        var surfaced = new List<WindowId>();
        a.Window.Surface = _ => surfaced.Add(a.WindowId);
        b.Window.Surface = _ => surfaced.Add(b.WindowId);
        Assert.True(host.Workspaces.Activate(a.WindowId));
        Assert.Equal([a.WindowId], surfaced);
        Assert.Same(a, host.Workspaces.Active);
        Assert.True(host.Workspaces.Activate(b.WindowId));
        Assert.Same(b, host.Workspaces.Active);
        Assert.False(host.Workspaces.Activate(WindowId.Create()));
        Assert.Equal([a.WindowId, b.WindowId], surfaced);
    }

    [Fact]
    public void StartOnlyCreateSetupReusesTheSameHandleAndCancelReturnsToTheStartPage()
    {
        using var environment = new DesktopControlEnvironment();
        var host = Host(new Files());
        var start = Start(host);
        var result = Run(() => host.Workspaces.RequestNewProjectAsync(start));
        Assert.Same(start, result);
        Assert.Single(host.Workspaces.Handles);
        Assert.Equal(WorkspaceKind.CreateSetup, start.Kind);
        // The start slot stays reserved while its CreateSetup is open.
        Assert.Equal(1, host.Workspaces.StartOnlyWorkspaceCount);
        Assert.True(Run(() => start.Model!.CancelSetupAsync())); Flush();
        Assert.Single(host.Workspaces.Handles);
        Assert.True(start.Window.IsVisible);
        Assert.Equal(MainViewMode.Start, start.Model!.Mode);
        Assert.Equal(WorkspaceKind.StartOnly, start.Kind);
    }

    [Fact]
    public void NewFromAProjectCreatesASeparateNewProjectWorkspaceWithoutASecondStartOnly()
    {
        using var environment = new DesktopControlEnvironment();
        var host = Host(new Files());
        var a = Start(host);
        CreateProject(a);
        Assert.Equal(0, host.Workspaces.StartOnlyWorkspaceCount);
        var b = Run(() => host.Workspaces.RequestNewProjectAsync(a))!;
        Assert.NotSame(a, b);
        Assert.Equal(WorkspaceCreationReason.NewProject, b.Reason);
        Assert.Equal(WorkspaceKind.CreateSetup, b.Kind);
        Assert.Equal(MainViewMode.ProjectSetup, b.Model!.Mode);
        Assert.Equal(0, host.Workspaces.StartOnlyWorkspaceCount);
        Assert.Equal(2, host.Workspaces.Handles.Count);
        Assert.Equal(WorkspaceKind.Project, a.Kind);
    }

    [Fact]
    public void CancellingANewProjectWorkspaceRemovesOnlyThatWorkspaceAndCleansUpOnce()
    {
        using var environment = new DesktopControlEnvironment();
        var host = Host(new Files());
        var a = Start(host);
        CreateProject(a);
        var b = Run(() => host.Workspaces.RequestNewProjectAsync(a))!;
        int closed = 0;
        b.Window.Closed += (_, _) => closed++;
        Assert.True(Run(() => b.Model!.CancelSetupAsync())); Flush();
        Assert.Equal(1, closed);
        Assert.False(b.Window.IsVisible);
        Assert.Same(a, Assert.Single(host.Workspaces.Handles));
        Assert.Null(host.Workspaces.Find(b.WindowId));
        Assert.True(a.Window.IsVisible);
        Assert.NotNull(a.Model!.Session);
        Assert.Equal(DesktopApplicationState.Ready, host.State);
    }

    [Fact]
    public void ProjectlessInternalCloseRefusesAWorkspaceWithAProject()
    {
        using var environment = new DesktopControlEnvironment();
        var host = Host(new Files());
        var a = Start(host);
        CreateProject(a);
        Assert.Throws<InvalidOperationException>(() => host.Workspaces.CloseInternal(a));
    }

    [Fact]
    public void SuccessfulCreateNeutralizesTheNewProjectReason()
    {
        using var environment = new DesktopControlEnvironment();
        var host = Host(new Files());
        var a = Start(host);
        CreateProject(a);
        var b = Run(() => host.Workspaces.RequestNewProjectAsync(a))!;
        Assert.Equal(WorkspaceCreationReason.NewProject, b.Reason);
        CreateProject(b);
        Assert.Equal(WorkspaceCreationReason.None, b.Reason);
        Assert.Equal(WorkspaceKind.Project, b.Kind);
        Assert.Equal(2, host.Workspaces.Handles.Count);
        Assert.Equal(0, host.Workspaces.StartOnlyWorkspaceCount);
    }

    [Fact]
    public void NewFromAProjectReusesAnExistingStartOnlyWorkspace()
    {
        using var environment = new DesktopControlEnvironment();
        var files = new Files();
        var host = Host(files);
        var start = Start(host);
        var a = Run(() => host.Workspaces.RequestNewProjectAsync(start))!;
        Assert.Same(start, a);
        Assert.True(Run(() => start.Model!.CancelSetupAsync())); Flush();
        // Project B in a separate workspace: first open one in the start workspace, then create a start workspace again.
        string path = Project(files, "reuse.spandraft");
        Assert.True(Run(() => start.Model!.OpenAsync(path)));
        Assert.Equal(0, host.Workspaces.StartOnlyWorkspaceCount);
        var b = Run(() => host.Workspaces.RequestNewProjectAsync(start))!;
        Assert.Equal(WorkspaceCreationReason.NewProject, b.Reason);
        // Closing the project of `start` while b holds no start slot makes `start` the start page again (§14).
        Assert.True(Run(() => start.Model!.CloseProjectAsync()));
        Assert.Equal(WorkspaceKind.StartOnly, start.Kind);
        Assert.Equal(1, host.Workspaces.StartOnlyWorkspaceCount);
        Assert.Equal(2, host.Workspaces.Handles.Count);
        // A New request from the other workspace reuses that start workspace instead of creating a third one.
        CreateProject(b);
        Assert.Equal(WorkspaceCreationReason.None, b.Reason);
        var reused = Run(() => host.Workspaces.RequestNewProjectAsync(b))!;
        Assert.Same(start, reused);
        Assert.Equal(WorkspaceKind.CreateSetup, start.Kind);
        Assert.Equal(2, host.Workspaces.Handles.Count);
    }

    [Fact]
    public void ClosingTheProjectClosesTheWorkspaceWhenAnotherStartOnlyExists()
    {
        using var environment = new DesktopControlEnvironment();
        var files = new Files();
        var host = Host(files);
        var a = Start(host);
        Assert.True(Run(() => a.Model!.OpenAsync(Project(files, "close-a.spandraft"))));
        var b = Run(() => host.Workspaces.RequestNewProjectAsync(a))!;
        Assert.True(Run(() => b.Model!.OpenAsync(Project(files, "close-b.spandraft"))));
        Assert.Equal(WorkspaceCreationReason.None, b.Reason);
        // Make `a` the start page; b keeps its project.
        Assert.True(Run(() => a.Model!.CloseProjectAsync()));
        Assert.Equal(WorkspaceKind.StartOnly, a.Kind);
        // Now b closes its project: a is already StartOnly, so b's workspace is closed instead.
        Assert.True(Run(() => b.Model!.CloseProjectAsync())); Flush();
        Assert.Same(a, Assert.Single(host.Workspaces.Handles));
        Assert.False(b.Window.IsVisible);
        Assert.Equal(1, host.Workspaces.StartOnlyWorkspaceCount);
    }

    [Fact]
    public void EditSetupKeepsHandleWindowIdAndSession()
    {
        using var environment = new DesktopControlEnvironment();
        var host = Host(new Files());
        var a = Start(host);
        CreateProject(a);
        var model = a.Model!;
        var session = model.Session;
        var id = a.WindowId;
        model.EditProject();
        Assert.Equal(MainViewMode.ProjectSetup, model.Mode);
        Assert.Same(a, Assert.Single(host.Workspaces.Handles));
        Assert.Equal(id, a.WindowId);
        Assert.Same(model, a.Model);
        Assert.Same(session, model.Session);
        Assert.Equal(WorkspaceKind.Project, a.Kind);
        Assert.Equal(0, host.Workspaces.StartOnlyWorkspaceCount);
        Assert.True(Run(() => model.CancelSetupAsync())); Flush();
        Assert.Same(session, model.Session);
        Assert.Same(a, Assert.Single(host.Workspaces.Handles));
    }

    [Fact]
    public void AnAlreadyOpenIdentityRoutesToTheOwningWorkspaceAndRecordsTheCanonicalTarget()
    {
        using var environment = new DesktopControlEnvironment();
        var files = new Files();
        var host = Host(files);
        var a = Start(host);
        string path = Project(files, "owned.spandraft");
        Assert.True(Run(() => a.Model!.OpenAsync(path)));
        var session = a.Model!.Session;
        var b = Run(() => host.Workspaces.RequestNewProjectAsync(a))!;
        var surfaced = new List<WindowId>();
        a.Window.Surface = _ => surfaced.Add(a.WindowId);
        b.Window.Surface = _ => surfaced.Add(b.WindowId);
        Assert.NotNull(host.ProjectPaths.IdentityOwnedBy(a.WindowId));

        Assert.True(Run(() => b.Model!.OpenAsync(path)));
        Assert.Equal([a.WindowId], surfaced);
        Assert.Same(a, host.Workspaces.Active);
        Assert.Same(session, a.Model.Session);
        Assert.Null(b.Model!.Session);
        Assert.Contains(host.Services.Recents.Entries, e => e.Path == Identities.Resolve(path).CanonicalPath);
    }

    [Fact]
    public void DeregisteringANonLastWorkspaceKeepsHostAndActivationAlive()
    {
        using var environment = new DesktopControlEnvironment();
        var files = new Files();
        var host = Host(files);
        var a = Start(host);
        CreateProject(a);
        var b = Run(() => host.Workspaces.RequestNewProjectAsync(a))!;
        Assert.True(Run(() => b.Model!.CancelSetupAsync())); Flush();
        Assert.Equal(DesktopApplicationState.Ready, host.State);
        // The shared consumer still serves requests: an Activate request is handled by the remaining workspace.
        bool activated = false;
        a.Window.Surface = _ => activated = true;
        host.Activation.Enqueue(LaunchRequest.Activate);
        Flush();
        Assert.Equal(0, host.Activation.Count);
        Assert.Equal(BrokerStatus.Accepted, host.HandleBrokerRequest(LaunchRequest.Activate));
        Assert.Same(a, host.Workspaces.Active);
        _ = activated;
    }

    [Fact]
    public void ClosingOneOfSeveralWindowsLeavesTheHostReady()
    {
        using var environment = new DesktopControlEnvironment();
        var host = Host(new Files());
        var a = Start(host);
        CreateProject(a);
        var b = Run(() => host.Workspaces.RequestNewProjectAsync(a))!;
        Assert.False(host.Workspaces.IsLast(b));
        b.Window.Close(); Flush();
        // Only the last window may enter the app-wide quit; the host and its queue stay usable for the others.
        Assert.NotEqual(DesktopApplicationState.ShuttingDown, host.State);
        Assert.NotEqual(DesktopApplicationState.QuitInProgress, host.State);
        Assert.Equal(BrokerStatus.Accepted, host.HandleBrokerRequest(LaunchRequest.Activate));
    }

    [Fact]
    public void OpenRequestsSkipAClosingWorkspace()
    {
        using var environment = new DesktopControlEnvironment();
        var files = new Files();
        var host = Host(files);
        var a = Start(host);
        a.IsClosing = true;
        host.Activation.Enqueue(LaunchRequest.OpenProject(Project(files, "queued.spandraft")));
        Flush();
        // No ready target: the request stays queued instead of being routed to a window that is going away.
        Assert.Equal(1, host.Activation.Count);
        Assert.Null(a.Model!.Session);
    }

    [Fact]
    public void TheLastWorkspaceEndsHostAndActivation()
    {
        using var environment = new DesktopControlEnvironment();
        var host = Host(new Files());
        var a = Start(host);
        a.Window.CloseApproved(); Flush();
        Assert.Empty(host.Workspaces.Handles);
        Assert.Equal(DesktopApplicationState.ShuttingDown, host.State);
    }

    [Fact]
    public void OnlyTheInitialWorkspaceUsesTheLegacyRecoverySlot()
    {
        using var environment = new DesktopControlEnvironment();
        var files = new Files();
        var host = Host(files);
        var a = Start(host);
        CreateProject(a);
        var b = Run(() => host.Workspaces.RequestNewProjectAsync(a))!;
        Assert.True(a.IsInitial);
        Assert.False(b.IsInitial);
        // The legacy slot is touched only by the initial workspace; remove it and let the second workspace work.
        string slot = host.StateStores.Recovery.SlotPath, setupSlot = host.StateStores.SetupRecovery.SlotPath;
        files.Data.TryRemove(slot, out _); files.Data.TryRemove(setupSlot, out _);
        CreateProject(b);
        Flush();
        Assert.False(files.Data.ContainsKey(slot));
        Assert.False(files.Data.ContainsKey(setupSlot));
        Assert.Equal(DesktopApplicationState.Ready, host.State);
    }
}
