using System.Reflection;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop;
using SpanDraft.Desktop.Activation;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.ViewModels;
using SpanDraft.Desktop.Views;
using SpanDraft.Desktop.Workspaces;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FileMenuAndShortcutsOpenBesideADirtyProject(bool keyboard)
    {
        using var environment = new DesktopControlEnvironment();
        var files = new Files();
        var storage = new TestRecoveryStorage();
        var host = new DesktopApplicationHost(new DesktopStartupContext(files, new LocalSettingsStore(files, "settings"), UserSettings.Default)
        { ProjectIdentities = Identities, StateStores = DesktopStateStores.Local(files) with { Recovery = storage } });
        host.RecoveryDelay = new Delay().Wait;
        var a = Start(host);
        string original = Project(files, "command-source.spandraft"), path = Project(files, "command-target.spandraft");
        Assert.True(Run(() => a.Model!.OpenAsync(original)));
        var session = a.Model!.Session!;
        Assert.True(session.Commit(State(7)));
        var revision = session.CurrentRevision;
        Assert.Null(Run(() => a.Model.CheckpointRecoveryAsync()));
        var slot = Assert.Single(storage.Slots());
        byte[] checkpoint = storage.Data[slot];
        var dialogs = SetPicker(a, path);

        Invoke(Key.N, "NewMenuItem");
        var draft = Assert.Single(host.Workspaces.Handles, h => h != a);
        Assert.Equal(MainViewMode.ProjectSetup, draft.Model!.Mode);
        Assert.Null(draft.Window.Owner);
        Assert.True(Run(() => draft.Model.CancelSetupAsync()));
        Assert.Same(a, Assert.Single(host.Workspaces.Handles));

        Invoke(Key.O, "OpenMenuItem");
        var opened = Assert.Single(host.Workspaces.Handles, h => h != a);
        Assert.Equal(path, opened.Model!.Session!.FilePath);
        Assert.Null(opened.Window.Owner);
        Assert.Equal(WorkspaceCreationReason.None, opened.Reason);
        Assert.Equal(0, host.Workspaces.StartSlotCount);
        Assert.Same(session, a.Model.Session);
        Assert.Same(revision, session.CurrentRevision);
        Assert.Equal(original, session.FilePath);
        Assert.Equal(MainViewMode.Editor, a.Model.Mode);
        Assert.True(session.IsDirty);
        Assert.Equal(0, dialogs.LeaveQuestions);
        Assert.Equal(0, dialogs.SaveQuestions);
        Assert.Empty(a.Window.OwnedWindows);
        Assert.Empty(storage.Discarded);
        Assert.Equal(slot, Assert.Single(storage.Writes));
        Assert.Equal(checkpoint, storage.Data[slot]);

        opened.Window.Close(); Flush();
        Assert.Same(a, Assert.Single(host.Workspaces.Handles));
        Assert.Equal(DesktopApplicationState.Ready, host.State);

        void Invoke(Key key, string menuName)
        {
            Run(() =>
            {
                var primary = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
                var item = a.Window.FindControl<MenuItem>(menuName)!;
                Assert.Equal(new KeyGesture(key, primary), item.InputGesture);
                if (keyboard)
                {
                    var press = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = primary };
                    a.Window.RaiseEvent(press);
                    Assert.True(press.Handled);
                }
                else
                {
                    Assert.True(item.Command!.CanExecute(null));
                    item.Command.Execute(null);
                }
                return Task.FromResult(true);
            });
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StartPageActionsAndRecentsReuseTheInitialWindow(bool recent)
    {
        using var environment = new DesktopControlEnvironment();
        var files = new Files();
        var host = Host(files);
        var initial = Start(host);
        string path = Project(files, "start-action.spandraft");
        SetPicker(initial, path);
        Assert.True(Run(async () => { await host.Services.Recents.RecordUseAsync(path); return true; }));
        DesktopControlEnvironment.ArrangeSection(initial.Window);
        var start = Assert.Single(initial.Window.GetVisualDescendants().OfType<StartPageView>());
        start.FindControl<Button>("NewProjectButton")!.Command!.Execute(null);
        Flush();
        Assert.Same(initial, Assert.Single(host.Workspaces.Handles));
        Assert.Equal(MainViewMode.ProjectSetup, initial.Model!.Mode);
        Assert.True(Run(() => initial.Model.CancelSetupAsync()));
        DesktopControlEnvironment.ArrangeSection(initial.Window);
        start = Assert.Single(initial.Window.GetVisualDescendants().OfType<StartPageView>());
        var open = recent ? Assert.Single(start.GetVisualDescendants().OfType<Button>(), b => b.Classes.Contains("recentProject"))
            : start.FindControl<Button>("OpenProjectButton")!;
        open.Command!.Execute(null); Flush();
        Assert.Same(initial, Assert.Single(host.Workspaces.Handles));
        Assert.Equal(path, initial.Model.Session!.FilePath);
        Assert.Equal(0, host.Workspaces.StartSlotCount);
    }

    private static Dialogs SetPicker(WorkspaceWindowHandle handle, string path)
    {
        var dialogs = new Dialogs { OpenPath = path };
        typeof(MainWindowViewModel).GetField("_dialogs", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(handle.Model, dialogs);
        return dialogs;
    }

    [Fact]
    public void ReservedStartKeepsItsSlotButCannotBeSelectedByAnotherNewOrOpen()
    {
        using var environment = new DesktopControlEnvironment();
        var files = new Files();
        var host = Host(files);
        var a = Start(host);
        Assert.Equal(ProjectOpenStatus.Opened, Run(() => host.Workspaces.RequestOpenProjectAsync(a, Project(files, "source.spandraft"))).Status);
        var startId = Run(() => host.Workspaces.RequestOpenProjectAsync(a, Project(files, "will-be-start.spandraft"))).Owner!.Value;
        var start = host.Workspaces.Find(startId)!;
        Assert.True(Run(() => start.Model!.CloseProjectAsync()));

        var arbitration = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.ProjectPaths.RecoverySettler = () => arbitration.Task;
        string firstPath = Project(files, "reserved.spandraft");
        var first = BeginOpen(host, a, firstPath);
        Assert.False(first.IsCompleted);
        Assert.True(start.IsOpenTargetReserved);
        Assert.False(start.IsCloseRequested);
        Assert.False(start.Model!.IsWorkspaceBusy);
        Assert.Equal(WorkspaceCreationReason.None, start.Reason);
        Assert.Equal(1, host.Workspaces.StartSlotCount);

        host.ProjectPaths.RecoverySettler = null;
        var draft = Run(() => host.Workspaces.RequestNewProjectAsync(a));
        Assert.NotNull(draft);
        Assert.NotSame(start, draft);
        var second = Run(() => host.Workspaces.RequestOpenProjectAsync(a, Project(files, "parallel.spandraft")));
        Assert.Equal(ProjectOpenStatus.Opened, second.Status);
        Assert.NotEqual(startId, second.Owner);
        Assert.Equal(MainViewMode.Start, start.Model.Mode);
        Assert.Equal(1, host.Workspaces.StartSlotCount);

        arbitration.SetResult();
        Assert.Equal(startId, Run(() => first).Owner);
        Assert.False(start.IsOpenTargetReserved);
        Assert.Equal(firstPath, start.Model.Session!.FilePath);
        Assert.Equal(0, host.Workspaces.StartSlotCount);
    }

    [Theory]
    [InlineData("draft")]
    [InlineData("recovery")]
    [InlineData("report")]
    public void ExternalOpenPreservesOccupiedWorkspace(string mode)
    {
        using var environment = new DesktopControlEnvironment();
        var files = new Files();
        var host = Host(files);
        var source = Start(host);
        var model = source.Model!;
        switch (mode)
        {
            case "draft": Assert.Same(source, Run(() => host.Workspaces.RequestNewProjectAsync(source))); break;
            case "recovery": Assert.Same(source, Run(() => host.Workspaces.ShowRecoveryCenterAsync(source, null))); break;
            default:
                Assert.True(Run(() => model.OpenAsync(Project(files, "report-project.spandraft"))));
                Assert.True(Run(() => model.ReportAsync()));
                break;
        }
        var content = model.CurrentViewModel;
        var session = model.Session;
        var sourceMode = model.Mode;
        string path = Project(files, "beside-" + mode + ".spandraft");
        Assert.Equal(BrokerStatus.Accepted, host.HandleBrokerRequest(LaunchRequest.OpenProject(path)));
        Flush();
        Assert.Equal(0, host.Activation.Count);
        Assert.Equal(sourceMode, model.Mode);
        Assert.Same(content, model.CurrentViewModel);
        Assert.Same(session, model.Session);
        var opened = Assert.Single(host.Workspaces.Handles, h => h != source);
        Assert.Equal(path, opened.Model!.Session!.FilePath);
        Assert.Equal(source.HoldsStartSlot ? 1 : 0, host.Workspaces.StartSlotCount);
    }

    [Theory]
    [InlineData("missing", false, false)]
    [InlineData("corrupt", false, false)]
    [InlineData("recent", false, false)]
    [InlineData("missing", true, false)]
    [InlineData("missing", false, true)]
    public void OpenFailureFinishesItsDialogBeforeRetiringTheTarget(string failure, bool last, bool reuseStart)
    {
        using var environment = new DesktopControlEnvironment();
        var files = new Files();
        var host = Host(files);
        var a = Start(host);
        if (!reuseStart) Assert.True(Run(() => a.Model!.OpenAsync(Project(files, "open-error-source.spandraft"))));
        string path = TestPath("failed-open.spandraft");
        if (failure == "corrupt") files.Data[path] = [0, 1, 2];
        bool fromRecent = failure == "recent";
        if (fromRecent) Run(async () => { await host.Services.Recents.RecordUseAsync(path); return true; });
        var pending = BeginOpen(host, a, path, fromRecent);
        var target = reuseStart ? a : Assert.Single(host.Workspaces.Handles, h => h != a);
        Assert.Equal(reuseStart ? WorkspaceCreationReason.None : WorkspaceCreationReason.OpenProject, target.Reason);
        Assert.Equal(reuseStart ? 1 : 0, host.Workspaces.StartSlotCount);
        Assert.False(pending.IsCompleted);
        Assert.True(target.IsOpenTargetReserved);
        var error = Assert.Single(target.Window.OwnedWindows);
        if (last)
        {
            a.Window.Close(); Flush();
            Assert.Same(target, Assert.Single(host.Workspaces.Handles));
        }
        Assert.True(target.Window.IsVisible);
        if (fromRecent)
        {
            var persist = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            string recentsFile = RecentProjectsStore.Local(files).FilePath;
            files.BeforeWrite = (file, _, _) => file == recentsFile ? persist.Task : Task.CompletedTask;
            Click(error, Strings.RemoveFromRecent);
            Assert.False(pending.IsCompleted);
            Assert.True(target.Window.IsVisible);
            persist.SetResult();
        }
        else Click(error, Strings.OK);
        Assert.Equal(failure == "corrupt" ? ProjectOpenStatus.Failed : ProjectOpenStatus.Missing, Run(() => pending).Status);
        if (fromRecent) Assert.DoesNotContain(host.Services.Recents.Entries, entry => entry.Path == path);
        Assert.False(target.IsOpenTargetReserved);
        if (last || reuseStart)
        {
            Assert.Same(target, Assert.Single(host.Workspaces.Handles));
            Assert.True(target.Window.IsVisible);
            Assert.Equal(WorkspaceCreationReason.None, target.Reason);
            Assert.Equal(MainViewMode.Start, target.Model!.Mode);
            Assert.True(target.Model.OpenCommand.CanExecute(null));
            Assert.Equal(1, host.Workspaces.StartSlotCount);
        }
        else
        {
            Assert.Same(a, Assert.Single(host.Workspaces.Handles));
            Assert.False(target.Window.IsVisible);
            Assert.Equal(0, host.Workspaces.StartSlotCount);
        }
    }

    [Fact]
    public void CancelledAndUnavailableLocalOpensLeaveNoTemporaryWorkspaceOrQueueEntry()
    {
        using var environment = new DesktopControlEnvironment();
        var files = new Files();
        var host = Host(files);
        var a = Start(host);
        Assert.True(Run(() => a.Model!.OpenAsync(Project(files, "cancel-open-source.spandraft"))));
        string path = Project(files, "cancel-open-target.spandraft");
        files.BeforeRead = _ => throw new OperationCanceledException();
        Assert.Equal(ProjectOpenStatus.Cancelled, Run(() => host.Workspaces.RequestOpenProjectAsync(a, path)).Status);
        Assert.Same(a, Assert.Single(host.Workspaces.Handles));

        var arbitration = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.ProjectPaths.RecoverySettler = () => arbitration.Task;
        var pending = BeginOpen(host, a, path);
        var target = Assert.Single(host.Workspaces.Handles, h => h != a);
        Assert.Equal(WorkspaceCreationReason.OpenProject, target.Reason);
        Assert.False(pending.IsCompleted);
        target.Window.Close(); Flush();
        arbitration.SetResult();
        Assert.Equal(ProjectOpenStatus.Unavailable, Run(() => pending).Status);
        Assert.Same(a, Assert.Single(host.Workspaces.Handles));
        Assert.Equal(0, host.Activation.Count);
        Assert.Equal(0, host.Workspaces.StartSlotCount);
        Assert.Equal(ProjectPathState.Free, host.ProjectPaths.StateOf(Identities.Resolve(path)));
    }

    [Fact]
    public void UnavailableOpenDefersItsCleanupUntilAnInProgressQuitIsCancelled()
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            var files = new Files();
            var host = Host(files);
            var a = Start(host);
            Assert.True(Run(() => a.Model!.OpenAsync(Project(files, "quit-open-source.spandraft"))));
            var session = a.Model!.Session!;
            Assert.True(session.Commit(State(8)));
            var arbitration = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            host.ProjectPaths.RecoverySettler = () => arbitration.Task;
            var pending = BeginOpen(host, a, Project(files, "quit-open-target.spandraft"));
            var target = Assert.Single(host.Workspaces.Handles, h => h != a);

            Assert.True(host.Workspaces.Activate(a.WindowId));
            Assert.True(host.Quit.RequestQuit()); Flush();
            var quit = host.Quit.UserQuit!;
            var leave = Assert.Single(a.Window.OwnedWindows);
            arbitration.SetResult();
            Assert.Equal(ProjectOpenStatus.Unavailable, Run(() => pending).Status);
            Assert.Contains(target, host.Workspaces.Handles);
            Assert.True(target.Model!.IsQuitFrozen);

            Click(leave, Strings.Cancel);
            Run(async () => { await quit.Completion; return true; });
            Assert.Same(a, Assert.Single(host.Workspaces.Handles));
            Assert.True(target.IsFinalized);
            Assert.Same(session, a.Model.Session);
            Assert.Equal(DesktopApplicationState.Ready, host.State);
            Assert.Equal(0, host.Workspaces.StartSlotCount);
        });
    }

    private static Task<ProjectOpenResult> BeginOpen(DesktopApplicationHost host, WorkspaceWindowHandle source, string path, bool fromRecent = false)
    {
        Task<ProjectOpenResult>? open = null;
        DesktopControlEnvironment.RunOnDispatcher(() =>
        {
            open = host.Workspaces.RequestOpenProjectAsync(source, path, fromRecent);
            return Task.CompletedTask;
        });
        Flush();
        return open!;
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
        Assert.Equal(1, host.Workspaces.StartSlotCount);
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
        Assert.Equal(1, host.Workspaces.StartSlotCount);
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
        Assert.Equal(0, host.Workspaces.StartSlotCount);
        var b = Run(() => host.Workspaces.RequestNewProjectAsync(a))!;
        Assert.NotSame(a, b);
        Assert.Equal(WorkspaceCreationReason.NewProject, b.Reason);
        Assert.Equal(WorkspaceKind.CreateSetup, b.Kind);
        Assert.Equal(MainViewMode.ProjectSetup, b.Model!.Mode);
        Assert.Equal(0, host.Workspaces.StartSlotCount);
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
        Assert.Equal(0, host.Workspaces.StartSlotCount);
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
        Assert.Equal(0, host.Workspaces.StartSlotCount);
        var b = Run(() => host.Workspaces.RequestNewProjectAsync(start))!;
        Assert.Equal(WorkspaceCreationReason.NewProject, b.Reason);
        // Closing the project of `start` while b holds no start slot makes `start` the start page again (§14).
        Assert.True(Run(() => start.Model!.CloseProjectAsync()));
        Assert.Equal(WorkspaceKind.StartOnly, start.Kind);
        Assert.Equal(1, host.Workspaces.StartSlotCount);
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
        Assert.Equal(1, host.Workspaces.StartSlotCount);
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
        Assert.Equal(0, host.Workspaces.StartSlotCount);
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

    private static void WithUiContext(Action action)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new AvaloniaSynchronizationContext());
        try { action(); } finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    /// <summary>A real window close of a workspace with unsaved work, answered with Don't Save.</summary>
    private static void CloseDiscarding(WorkspaceWindowHandle handle) => WithUiContext(() =>
    {
        handle.Window.Close(); Flush();
        Click(Assert.Single(handle.Window.OwnedWindows), Strings.DontSave); Flush(); Flush();
    });

    private static void Click(Avalonia.Controls.Window dialog, string label) => dialog.GetVisualDescendants()
        .OfType<Avalonia.Controls.Button>().Single(b => Equals(b.Content, label))
        .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));

    /// <summary>Two workspaces; <c>a</c> owns a dirty project, so closing it really opens the leave dialog.</summary>
    private static (DesktopApplicationHost Host, WorkspaceWindowHandle A, WorkspaceWindowHandle B) TwoWorkspaces(Files files)
    {
        var host = Host(files);
        var a = Start(host);
        CreateProject(a);
        var b = Run(() => host.Workspaces.RequestNewProjectAsync(a))!;
        return (host, a, b);
    }

    [Fact]
    public void ARealWindowCloseTakesTheWorkspaceOutOfRoutingUntilItIsCancelled()
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            var (host, a, b) = TwoWorkspaces(new Files());
            Assert.True(host.Workspaces.Activate(a.WindowId));
            Assert.Same(a, host.Workspaces.Active);

            a.Window.Close(); Flush();
            // The leave dialog is open: the close attempt is running and may still be cancelled.
            var dialog = Assert.Single(a.Window.OwnedWindows);
            Assert.True(a.IsCloseRequested);
            Assert.False(a.CanRoute);
            Assert.NotSame(a, host.Workspaces.Active);
            Assert.False(host.Workspaces.Activate(a.WindowId));

            Click(dialog, Strings.Cancel); Flush();
            Assert.False(a.IsCloseRequested);
            Assert.True(a.CanRoute);
            Assert.True(host.Workspaces.Activate(a.WindowId));
            Assert.Same(a, host.Workspaces.Active);
            Assert.Equal(DesktopApplicationState.Ready, host.State);
        });
    }

    [Fact]
    public void ARequestedCloseDoesNotReleaseTheStartSlot()
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            var files = new Files();
            var host = Host(files);
            var a = Start(host);
            // A clean (opened) project closes without a leave dialog.
            Assert.True(Run(() => a.Model!.OpenAsync(Project(files, "clean.spandraft"))));
            var b = Run(() => host.Workspaces.RequestNewProjectAsync(a))!;
            Assert.True(Run(() => a.Model!.CloseProjectAsync()));
            Flush();
            Assert.Equal(WorkspaceKind.StartOnly, a.Kind);
            Assert.Equal(1, host.Workspaces.StartSlotCount);

            // A project-less close needs no dialog, so observe the slot from inside the running close attempt.
            var seen = new List<(bool Requested, bool Holds, int Slots)>();
            a.Model!.PropertyChanged += (_, _) => seen.Add((a.IsCloseRequested, a.HoldsStartSlot, host.Workspaces.StartSlotCount));
            a.Window.Close(); Flush();
            Assert.Contains(seen, x => x.Requested);
            Assert.All(seen.Where(x => x.Requested), x => { Assert.True(x.Holds); Assert.Equal(1, x.Slots); });
            Assert.DoesNotContain(a, host.Workspaces.Handles);
            Assert.Equal(0, host.Workspaces.StartSlotCount);
            Assert.Contains(b, host.Workspaces.Handles);
        });
    }

    [Fact]
    public void AnOpenRequestWaitsWhileItsOwnerCloses_AndIsProcessedAfterTheFinalCleanup()
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            var files = new Files();
            var host = Host(files);
            var a = Start(host);
            string path = Project(files, "owned-by-a.spandraft");
            Assert.True(Run(() => a.Model!.OpenAsync(path)));
            Assert.True(a.Model!.Session!.Commit(State(7)));
            Assert.True(a.Model.Session.IsDirty);
            Assert.NotNull(host.ProjectPaths.IdentityOwnedBy(a.WindowId));
            var b = Run(() => host.Workspaces.RequestNewProjectAsync(a))!;
            string other = Project(files, "other.spandraft");
            Assert.True(Run(() => b.Model!.OpenAsync(other)));
            var session = b.Model!.Session;

            a.Window.Close(); Flush();
            var dialog = Assert.Single(a.Window.OwnedWindows);
            host.Activation.Enqueue(LaunchRequest.OpenProject(path));
            string later = Project(files, "waiting-behind-owner.spandraft");
            host.Activation.Enqueue(LaunchRequest.OpenProject(later));
            Flush();
            // The first target waits for the owner. The next request stays behind it in the existing FIFO.
            Assert.Equal(2, host.Activation.Count);
            var target = Assert.Single(host.Workspaces.Handles, h => h != a && h != b);
            Assert.Equal(WorkspaceCreationReason.OpenProject, target.Reason);
            Assert.Null(target.Model!.Session);
            Assert.Same(session, b.Model.Session);

            Click(dialog, Strings.DontSave); Flush(); Flush();
            // Final cleanup released the ownership, a deregistered, the queue was pumped again.
            Assert.DoesNotContain(a, host.Workspaces.Handles);
            Assert.Null(host.ProjectPaths.IdentityOwnedBy(a.WindowId));
            Assert.Equal(0, host.Activation.Count);
            Assert.Equal(path, target.Model.Session!.FilePath);
            Assert.Equal(later, Assert.Single(host.Workspaces.Handles, h => h != b && h != target).Model!.Session!.FilePath);
            Assert.Equal(other, b.Model.Session!.FilePath);
        });
    }

    [Fact]
    public void InitialWorkspaceCanBeCreatedOnlyOnceEvenAfterItWasClosed()
    {
        using var environment = new DesktopControlEnvironment();
        var host = Host(new Files());
        var a = Start(host);
        CreateProject(a);
        var b = Run(() => host.Workspaces.RequestNewProjectAsync(a))!;
        // a has an unsaved project: its window close runs the close transaction, here answered with Don't Save.
        CloseDiscarding(a); Flush();
        Assert.DoesNotContain(a, host.Workspaces.Handles);
        Assert.Null(host.Workspaces.Initial);
        Assert.Throws<InvalidOperationException>(() => host.Workspaces.CreateInitial());
        Assert.False(b.IsInitial);
        Assert.Single(host.Workspaces.Handles);
    }

    [Fact]
    public void TheLastWorkspaceEndsHostAndActivation()
    {
        using var environment = new DesktopControlEnvironment();
        var host = Host(new Files());
        var a = Start(host);
        a.Window.Close(); Flush();
        Assert.Empty(host.Workspaces.Handles);
        Assert.Equal(DesktopApplicationState.ShuttingDown, host.State);
    }

    [Fact]
    public void EveryWorkspaceWritesItsOwnIdentifiedRecoverySlotAndNeverTheLegacyOne()
    {
        using var environment = new DesktopControlEnvironment();
        var host = Host(new Files());
        var a = Start(host);
        CreateProject(a);
        var b = Run(() => host.Workspaces.RequestNewProjectAsync(a))!;
        Assert.True(a.IsInitial);
        Assert.False(b.IsInitial);
        CreateProject(b);
        Flush();
        var slots = Run(() => host.StateStores.Recovery.EnumerateAsync());
        Assert.Equal(2, slots.Count(s => s.Kind == RecoverySlotKind.Project));
        Assert.All(slots, slot => Assert.False(slot.IsLegacy));
        Assert.Equal(slots.Count, slots.Distinct().Count());
        Assert.Equal(DesktopApplicationState.Ready, host.State);
    }
}
