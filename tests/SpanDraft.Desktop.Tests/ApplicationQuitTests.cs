using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SpanDraft.Desktop;
using SpanDraft.Desktop.Activation;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using SpanDraft.Desktop.Workspaces;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

/// <summary>
/// Phase 8 through the real production path: the application quit over several workspace windows, its recovery
/// checkpoint, the shutdown commit, the application dialogs, the platform quit signals and the system shutdown. Debounces
/// are held and storage gates injected; nothing is timed.
/// </summary>
[Collection("Avalonia")]
public sealed class ApplicationQuitTests
{
    private sealed class App
    {
        public Files Files { get; } = new();
        public TestRecoveryStorage Storage { get; } = new();
        /// <summary>The workspaces' recovery debounce: held until released, so a change stays unpublished.</summary>
        public Delay Debounce { get; } = new();
        public DesktopApplicationHost Host { get; }

        public App(bool windowsPlatform = false)
        {
            Host = new(new DesktopStartupContext(Files, new LocalSettingsStore(Files, "settings"), UserSettings.Default)
            { StateStores = DesktopStateStores.Local(Files) with { Recovery = Storage }, ProjectIdentities = Identities });
            Host.RecoveryDelay = Debounce.Wait;
            Host.Quit.WindowsPlatform = windowsPlatform;
        }

        public WorkspaceWindowHandle Start()
        {
            var handle = Host.Workspaces.CreateInitial();
            DesktopControlEnvironment.ShowInitialized(handle.Window);
            return handle;
        }

        public string Project(string name)
        {
            string path = TestPath(name + ".spandraft");
            Files.Data[path] = ProjectFileCodec.Serialize(State());
            return path;
        }

        public WorkspaceWindowHandle StartWithProject(string name)
        {
            var handle = Start();
            Assert.True(Run(() => handle.Model!.OpenAsync(Project(name))));
            return handle;
        }

        public WorkspaceWindowHandle Second(WorkspaceWindowHandle source, string name)
        {
            var handle = Run(() => Host.Workspaces.RequestNewProjectAsync(source))!;
            Assert.NotSame(source, handle);
            Assert.True(Run(() => handle.Model!.OpenAsync(Project(name))));
            return handle;
        }

        /// <summary>An unsaved change whose recovery write still waits in its debounce.</summary>
        public void Change(WorkspaceWindowHandle handle, int revision = 2) => Assert.True(handle.Model!.Session!.Commit(State(revision)));

        /// <summary>Opens a project, changes it and publishes its recovery slot.</summary>
        public (string Path, RecoverySlot Slot) OpenDirty(WorkspaceWindowHandle handle, string name)
        {
            string path = Project(name);
            Assert.True(Run(() => handle.Model!.OpenAsync(path)));
            var before = Storage.Slots(RecoverySlotKind.Project).ToHashSet();
            Change(handle);
            Debounce.ReleaseAll(); Flush();
            return (path, Assert.Single(Storage.Slots(RecoverySlotKind.Project), s => !before.Contains(s)));
        }

        public void SystemShutdown(params WorkspaceWindowHandle[] closing)
        {
            Assert.Equal(PlatformClose.SystemShutdown, Host.Quit.OnPlatformClosing(WindowCloseReason.OSShutdown, isProgrammatic: false));
            // What the platform's lifetime does next: it closes every window, and none of them refuses.
            foreach (var handle in closing) handle.Window.Close();
            Flush();
        }
    }

    /// <summary>The platform's lifetime events, proxied like the environment's other platform services.</summary>
    public class PlatformLifetimeEvents : System.Reflection.DispatchProxy
    {
        private EventHandler<ShutdownRequestedEventArgs>? _shutdownRequested;
        public void Raise(ShutdownRequestedEventArgs e) => _shutdownRequested?.Invoke(this, e);

        protected override object? Invoke(System.Reflection.MethodInfo? method, object?[]? args)
        {
            var handler = (EventHandler<ShutdownRequestedEventArgs>?)args?[0];
            if (method!.Name == "add_ShutdownRequested") _shutdownRequested += handler;
            else if (method.Name == "remove_ShutdownRequested") _shutdownRequested -= handler;
            return null;
        }
    }

    private static T Run<T>(Func<Task<T>> action)
    {
        T result = default!;
        DesktopControlEnvironment.RunOnDispatcher(async () => result = await action());
        Dispatcher.UIThread.RunJobs();
        return result;
    }

    private static void Flush() { Dispatcher.UIThread.RunJobs(); Dispatcher.UIThread.RunJobs(); }

    private static void WithUiContext(Action action)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new AvaloniaSynchronizationContext());
        try { action(); } finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    private static Window Dialog(WorkspaceWindowHandle handle) => Assert.Single(handle.Window.OwnedWindows, w => w.IsVisible);
    private static Window? OpenDialog(WorkspaceWindowHandle handle) => handle.Window.OwnedWindows.SingleOrDefault(w => w.IsVisible);

    private static void Click(Window dialog, string label)
    {
        dialog.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, label))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Flush();
    }

    private static void ClickMenu(WorkspaceWindowHandle handle, string header)
    {
        handle.Window.GetLogicalDescendants().OfType<MenuItem>().Single(i => Equals(i.Header, header))
            .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Flush();
    }

    private static string DialogText(Window dialog) =>
        string.Join("\n", dialog.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));

    private static TaskCompletionSource Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    // ---- reservation, order, partial quit ---------------------------------------------------------------------

    [Fact]
    public void AQuitIsReservedSynchronouslyAndHoldsEveryWorkspaceUntilItEnds()
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            var app = new App(); var a = app.StartWithProject("hold-a"); var b = app.Second(a, "hold-b");
            var firstSession = a.Model!.Session;
            var secondSession = b.Model!.Session;
            string requested = app.Project("hold-request");
            Assert.True(app.Host.Quit.RequestQuit());
            // Synchronously, before anything of the quit ran.
            Assert.Equal(DesktopApplicationState.QuitInProgress, app.Host.State);
            foreach (var handle in new[] { a, b })
            {
                var model = handle.Model!;
                Assert.True(model.IsQuitFrozen); Assert.True(model.IsWorkspaceBusy); Assert.True(model.Session!.IsBusy);
                Assert.False(model.Session.Commit(State(5))); Assert.False(model.SetResultPresentation(model.Settings.Profile, 5));
                Assert.Equal(ProjectOpenStatus.Unavailable, model.OpenPathResultAsync(requested, fromRecent: false).Result.Status);
            }
            Assert.Null(app.Host.Workspaces.Active);
            Assert.Equal(BrokerStatus.Retry, app.Host.HandleBrokerRequest(LaunchRequest.OpenProject(requested)));
            Assert.True(app.Host.SubmitPlatformActivation(LaunchRequest.OpenProject(requested)));
            Assert.False(app.Host.Quit.RequestQuit());   // no second quit
            var run = app.Host.Quit.UserQuit!;
            Flush();
            // No workspace is created and no ordinary close or Close Project happens meanwhile.
            Assert.Null(Run(() => app.Host.Workspaces.RequestNewProjectAsync(a)));
            Assert.Null(Run(() => app.Host.Workspaces.ShowRecoveryCenterAsync(a, null)));
            Assert.Equal(2, app.Host.Workspaces.Handles.Count);
            var pending = Dialog(b);   // b was the active workspace
            Assert.Contains("hold-request", DialogText(pending)); Assert.Null(OpenDialog(a));
            a.Window.Close(); Flush();
            Assert.Contains(a, app.Host.Workspaces.Handles); Assert.Null(OpenDialog(a));
            Assert.False(Run(() => a.Model!.CloseProjectAsync()));
            Assert.Same(run, app.Host.Quit.UserQuit); Assert.Equal(1, app.Host.Activation.Count);
            // Keeping the request ends the quit before anything was closed; the request is processed afterwards.
            Click(pending, Strings.Cancel); Flush();
            Assert.True(run.Completion.IsCompleted); Assert.Null(app.Host.Quit.UserQuit);
            Assert.Equal(DesktopApplicationState.Ready, app.Host.State);
            Assert.False(a.Model!.IsQuitFrozen); Assert.False(b.Model!.IsQuitFrozen); Assert.False(a.Model.Session!.IsBusy);
            Assert.Equal(0, app.Host.Activation.Count);
            string canonical = Identities.Resolve(requested).CanonicalPath;
            var opened = Assert.Single(app.Host.Workspaces.Handles, h => h != a && h != b);
            Assert.Equal(canonical, opened.Model!.Session!.FilePath);
            Assert.Same(firstSession, a.Model.Session);
            Assert.Same(secondSession, b.Model.Session);
            Assert.Equal(BrokerStatus.Accepted, app.Host.HandleBrokerRequest(LaunchRequest.Activate));
        });
    }

    [Fact]
    public void TheActiveWorkspaceGoesFirstEachIsClosedCompletelyBeforeTheNextAndACancelKeepsTheRest()
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            var app = new App(); var a = app.Start(); var (_, slotA) = app.OpenDirty(a, "order-a");
            var b = app.Second(a, "order-b-clean"); var (_, slotB) = app.OpenDirty(b, "order-b");
            var c = app.Second(a, "order-c");
            var identityB = app.Host.ProjectPaths.IdentityOwnedBy(b.WindowId)!;
            Assert.True(app.Host.Workspaces.Activate(b.WindowId));
            Assert.True(app.Host.Quit.RequestQuit());
            // Whether b was cleaned up completely when a's close began (the quit handed its freeze of a to that close).
            var finalizedWhenABegan = new List<bool>();
            a.Model!.PropertyChanged += (_, _) =>
            {
                if (!a.Model.IsQuitFrozen && app.Host.Quit.IsUserQuitRunning && finalizedWhenABegan.Count == 0)
                    finalizedWhenABegan.Add(b.Finalized.IsCompleted);
            };
            Flush();
            var questionB = Dialog(b); Assert.Null(OpenDialog(a)); Assert.Null(OpenDialog(c));
            Click(questionB, Strings.DontSave);
            Assert.Equal([true], finalizedWhenABegan);
            // b is closed and cleaned up completely before a, the next one in registration order, is asked.
            Assert.DoesNotContain(b, app.Host.Workspaces.Handles); Assert.True(b.Finalized.IsCompleted);
            Assert.Null(app.Host.ProjectPaths.IdentityOwnedBy(b.WindowId));
            Assert.Equal(ProjectPathState.Free, app.Host.ProjectPaths.StateOf(identityB));
            Assert.Contains(slotB.FileName, app.Storage.Discarded);
            var questionA = Dialog(a); Assert.Null(OpenDialog(c));
            Click(questionA, Strings.Cancel);
            // Partial quit: b stays closed; a and the not yet reached c stay open, routable and released.
            Assert.Equal(DesktopApplicationState.Ready, app.Host.State); Assert.Null(app.Host.Quit.UserQuit);
            foreach (var handle in new[] { a, c })
            {
                Assert.Contains(handle, app.Host.Workspaces.Handles); Assert.True(handle.CanRoute);
                Assert.False(handle.Model!.IsQuitFrozen); Assert.False(handle.Model.IsWorkspaceBusy);
            }
            Assert.True(a.Model!.Session!.IsDirty); Assert.Contains(slotA, app.Storage.Slots());
            Assert.NotNull(app.Host.Workspaces.Active);
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ARequestArrivingDuringTheQuitIsAskedAboutAtTheShutdownCommit(bool drop)
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            var app = new App(); var a = app.Start(); var (_, slot) = app.OpenDirty(a, "commit-a");
            var b = app.Second(a, "commit-b");
            var remaining = b.Model!.Session;
            Assert.True(app.Host.Workspaces.Activate(a.WindowId));
            Assert.True(app.Host.Quit.RequestQuit()); Flush();
            var question = Dialog(a);
            string requested = app.Project("commit-request");
            Assert.True(app.Host.SubmitPlatformActivation(LaunchRequest.OpenProject(requested)));
            Click(question, Strings.DontSave);
            // a is closed; b (clean) closes without a question, but as the last one it asks about the request first.
            Assert.DoesNotContain(a, app.Host.Workspaces.Handles); Assert.Contains(slot.FileName, app.Storage.Discarded);
            var pending = Dialog(b); Assert.Contains("commit-request", DialogText(pending));
            Assert.Equal(DesktopApplicationState.QuitInProgress, app.Host.State);
            Click(pending, drop ? Strings.Discard : Strings.Cancel); Flush();
            if (drop)
            {
                Assert.Empty(app.Host.Workspaces.Handles); Assert.Equal(DesktopApplicationState.ShuttingDown, app.Host.State);
                Assert.Equal(0, app.Host.Activation.Count);
                return;
            }
            // Kept: the quit ends, a stays closed, b keeps its project and the request opens separately.
            Assert.DoesNotContain(a, app.Host.Workspaces.Handles);
            Assert.Contains(b, app.Host.Workspaces.Handles); Assert.Equal(DesktopApplicationState.Ready, app.Host.State);
            Assert.Equal(0, app.Host.Activation.Count);
            var opened = Assert.Single(app.Host.Workspaces.Handles, h => h != b);
            Assert.Equal(Identities.Resolve(requested).CanonicalPath, opened.Model!.Session!.FilePath);
            Assert.Same(remaining, b.Model.Session);
        });
    }

    // ---- recovery checkpoint ----------------------------------------------------------------------------------

    [Fact]
    public void EveryUnpublishedChangeIsCheckpointedBeforeTheFirstQuestionAndNothingElse()
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            var app = new App(); var a = app.StartWithProject("checkpoint-a"); app.Change(a);
            var b = Run(() => app.Host.Workspaces.RequestNewProjectAsync(a))!;
            CompleteProjectInfo(b.Model!.Setup);   // a confirmed Create draft, its write still waiting
            var c = app.Second(a, "checkpoint-c");   // clean
            Assert.Empty(app.Storage.Slots());
            Assert.True(app.Host.Workspaces.Activate(a.WindowId));
            Assert.True(app.Host.Quit.RequestQuit()); Flush();
            var question = Dialog(a);   // the first question
            var project = app.Storage.OnlySlot(RecoverySlotKind.Project);
            Assert.True(a.Model!.Session!.CurrentRevision.State.ContentEquals(ProjectRecovery.Decode(app.Storage.Data[project]).State));
            Assert.Equal("Test calculation", ProjectSetupRecovery.Decode(app.Storage.Data[app.Storage.OnlySlot(RecoverySlotKind.Setup)]).Draft.CalculationTitle);
            Assert.Equal(2, app.Storage.Slots().Count);   // nothing for the clean workspace
            Assert.Null(OpenDialog(b)); Assert.Null(OpenDialog(c));
            Click(question, Strings.Cancel);
            Assert.Equal(DesktopApplicationState.Ready, app.Host.State);
        });
    }

    [Fact]
    public void AFailedCheckpointIsShownOnceKeepsTheOthersAndTheQuitGoesOn()
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            var app = new App(); var a = app.StartWithProject("failed-a"); app.Change(a);
            var b = Run(() => app.Host.Workspaces.RequestNewProjectAsync(a))!; CompleteProjectInfo(b.Model!.Setup);
            app.Storage.FailWrite = s => s.Kind == RecoverySlotKind.Project;
            Assert.True(app.Host.Workspaces.Activate(a.WindowId));
            Assert.True(app.Host.Quit.RequestQuit()); Flush();
            var error = Dialog(a); Assert.Contains(Strings.RecoveryError, DialogText(error));
            Assert.Single(app.Storage.Slots(RecoverySlotKind.Setup));   // b's checkpoint was not kept from writing
            Assert.False(a.Model!.IsRecoveryFaulted);
            Click(error, Strings.OK);
            var question = Dialog(a);   // then the normal question: the user can still decide
            Assert.DoesNotContain(Strings.RecoveryError, DialogText(question));
            Click(question, Strings.Cancel);
            Assert.Null(OpenDialog(a)); Assert.Equal(DesktopApplicationState.Ready, app.Host.State);
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ADeferredRecoveryErrorAndTheCheckpointNeverShowTwoDialogs(bool checkpointFails)
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            var app = new App(); var a = app.StartWithProject("twice"); app.Change(a);
            var gate = Gate(); int writes = 0;
            app.Storage.BeforeWrite = (_, _, _) => ++writes == 1 ? gate.Task : Task.CompletedTask;
            // The superseded debounce write still reaches the storage and fails there: a real deferred error.
            app.Storage.HonorWriteCancellation = false;
            int deferred = 0;
            app.Storage.FailureFactory = _ => { deferred++; return new IOException("Injected write failure."); };
            app.Debounce.ReleaseAll(); Flush();   // the debounced write is publishing, held
            Assert.True(app.Host.Quit.RequestQuit()); Flush();   // the checkpoint waits behind it
            Assert.Null(OpenDialog(a));
            app.Storage.FailWrite = _ => writes == 1 || checkpointFails;
            gate.SetResult(); Flush();
            if (checkpointFails)
            {
                var error = Dialog(a); Assert.Contains(Strings.RecoveryError, DialogText(error)); Click(error, Strings.OK);
            }
            var question = Dialog(a); Assert.DoesNotContain(Strings.RecoveryError, DialogText(question));
            Click(question, Strings.Cancel);
            Assert.Null(OpenDialog(a));   // the deferred debounce error is superseded, never shown in addition
            Assert.Equal(checkpointFails ? 2 : 1, deferred);   // the debounce write really failed while frozen
            Assert.Equal(DesktopApplicationState.Ready, app.Host.State);
        });
    }

    // ---- application dialogs ----------------------------------------------------------------------------------

    [Fact]
    public void ApplicationDialogsAreSingletonsAndOnlyUnsafeOnesKeepTheQuitFromStarting()
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            var app = new App(); var a = app.StartWithProject("dialogs-a"); var b = app.Second(a, "dialogs-b");
            ClickMenu(a, Strings.Settings);
            var settings = app.Host.AppDialogs.Find(AppDialogKind.Settings)!;
            Assert.Same(a.Window, settings.Owner);
            ClickMenu(b, Strings.Settings);
            Assert.Same(settings, app.Host.AppDialogs.Find(AppDialogKind.Settings));
            Assert.DoesNotContain(b.Window.OwnedWindows, w => w.IsVisible); Assert.False(b.Model!.IsWorkspaceBusy);
            // An ordinary close of its owner waits for it.
            a.Window.Close(); Flush();
            Assert.Contains(a, app.Host.Workspaces.Handles); Assert.Same(settings, Dialog(a));
            // Unconfirmed changes keep the quit from starting and are not discarded.
            var model = (SettingsViewModel)settings.DataContext!;
            model.Select(model.IsUnitedStates ? UnitProfileKind.StructuralEngineering : UnitProfileKind.UnitedStates);
            Assert.True(model.HasChanges);
            Assert.False(app.Host.Quit.RequestQuit()); Flush();
            Assert.Equal(DesktopApplicationState.Ready, app.Host.State); Assert.True(model.HasChanges); Assert.True(settings.IsVisible);
            model.DiscardPending(); Assert.False(model.HasChanges);   // the user takes the change back
            // About is a singleton as well; safely closable dialogs are closed by the quit, which then goes on.
            ClickMenu(b, Strings.About);
            var about = app.Host.AppDialogs.Find(AppDialogKind.About)!;
            ClickMenu(a, Strings.About);
            Assert.Same(about, app.Host.AppDialogs.Find(AppDialogKind.About)); Assert.Same(b.Window, about.Owner);
            Assert.True(app.Host.Quit.RequestQuit()); Flush();
            Assert.False(settings.IsVisible); Assert.False(about.IsVisible);
            Assert.Null(app.Host.AppDialogs.Find(AppDialogKind.Settings)); Assert.Null(app.Host.AppDialogs.Find(AppDialogKind.About));
            Assert.Empty(app.Host.Workspaces.Handles); Assert.Equal(DesktopApplicationState.ShuttingDown, app.Host.State);
        });
    }

    [Fact]
    public void ABusyWorkspaceOrAPendingCloseKeepsTheQuitFromStarting()
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            var app = new App(); var a = app.StartWithProject("busy-a"); var (_, _) = app.OpenDirty(a, "busy-a2");
            var b = app.Second(a, "busy-b");
            // An operation of b runs (held in its read): no quit.
            string other = app.Project("busy-other"); var gate = Gate();
            app.Files.BeforeRead = path => path == other ? gate.Task : Task.CompletedTask;
            Dispatcher.UIThread.Post(async () => await b.Model!.OpenAsync(other)); Flush();
            Assert.True(b.Model!.IsWorkspaceBusy);
            Assert.False(app.Host.Quit.RequestQuit()); Assert.Equal(DesktopApplicationState.Ready, app.Host.State);
            Assert.False(a.Model!.IsQuitFrozen);
            gate.SetResult(); Flush(); app.Files.BeforeRead = null;
            // A's own close is asking: no quit either.
            a.Window.Close(); Flush(); var question = Dialog(a);
            Assert.False(app.Host.Quit.RequestQuit()); Assert.Null(app.Host.Quit.UserQuit);
            Click(question, Strings.Cancel);
            Assert.True(app.Host.Quit.RequestQuit());
        });
    }

    // ---- platform signals ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData(WindowCloseReason.OSShutdown, false, false, nameof(PlatformClose.SystemShutdown))]
    [InlineData(WindowCloseReason.OSShutdown, false, true, nameof(PlatformClose.SystemShutdown))]
    [InlineData(WindowCloseReason.ApplicationShutdown, false, false, nameof(PlatformClose.UserQuit))]
    [InlineData(WindowCloseReason.ApplicationShutdown, false, true, nameof(PlatformClose.SystemShutdown))]
    [InlineData(WindowCloseReason.ApplicationShutdown, true, false, nameof(PlatformClose.None))]
    [InlineData(WindowCloseReason.ApplicationShutdown, true, true, nameof(PlatformClose.None))]
    [InlineData(WindowCloseReason.OSShutdown, true, true, nameof(PlatformClose.None))]
    [InlineData(WindowCloseReason.WindowClosing, false, true, nameof(PlatformClose.None))]
    [InlineData(WindowCloseReason.OwnerWindowClosing, false, false, nameof(PlatformClose.None))]
    [InlineData(WindowCloseReason.Undefined, false, true, nameof(PlatformClose.None))]
    public void PlatformClosesAreClassifiedByReasonOriginAndPlatform(WindowCloseReason reason, bool programmatic, bool windows, string expected)
    {
        var coordinator = new ApplicationCloseCoordinator(new DesktopApplicationHost(), windows);
        Assert.Equal(Enum.Parse<PlatformClose>(expected), coordinator.Classify(reason, programmatic));
    }

    /// <summary>
    /// Avalonia's own lifetime on a platform quit signal. Its platform events are bound in the environment's locator scope,
    /// like the other platform services of the test environment, since only so its <c>DoShutdown</c> runs for real.
    /// </summary>
    private static void WithLifetime(Action<ClassicDesktopStyleApplicationLifetime, PlatformLifetimeEvents> test)
    {
        var platform = System.Reflection.DispatchProxy.Create<IPlatformLifetimeEventsImpl, PlatformLifetimeEvents>();
        DesktopControlEnvironment.BindService(platform);
        var events = (PlatformLifetimeEvents)(object)platform;
        // Explicit: closing the test's last window must not end the dispatcher.
        var lifetime = new ClassicDesktopStyleApplicationLifetime { ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown };
        var setup = lifetime.GetType().GetInterfaces().Single(i => i.Name == "ISetupApplicationLifetime");
        setup.GetMethod("BeforeAppInit")!.Invoke(lifetime, null);
        setup.GetMethod("AfterAppInit")!.Invoke(lifetime, null);
        try { test(lifetime, events); }
        finally { lifetime.Dispose(); }
    }

    [Fact]
    public void AMacOSPlatformQuitIsCancelledSynchronouslyAndStartsExactlyOneQuit()
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() => WithLifetime((lifetime, events) =>
        {
            var app = new App(windowsPlatform: false); var a = app.StartWithProject("platform-a"); var b = app.Second(a, "platform-b");
            var first = new ShutdownRequestedEventArgs(); events.Raise(first);
            Assert.True(first.Cancel);
            Assert.All(new[] { a, b }, h => Assert.True(h.Window.IsVisible));
            Assert.Equal(DesktopApplicationState.QuitInProgress, app.Host.State);
            var run = app.Host.Quit.UserQuit!;
            var second = new ShutdownRequestedEventArgs(); events.Raise(second);
            Assert.True(second.Cancel); Assert.Same(run, app.Host.Quit.UserQuit);
            Flush();
            Assert.True(run.Completion.IsCompleted);
            Assert.Empty(app.Host.Workspaces.Handles); Assert.Equal(DesktopApplicationState.ShuttingDown, app.Host.State);
        }));
    }

    [Fact]
    public void APlatformQuitOnlyAsksOwnedDialogsAndDiscardsNothing()
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() => WithLifetime((lifetime, events) =>
        {
            var app = new App(windowsPlatform: false); var a = app.StartWithProject("asked-a");
            ClickMenu(a, Strings.Settings);
            var settings = app.Host.AppDialogs.Find(AppDialogKind.Settings)!; var model = (SettingsViewModel)settings.DataContext!;
            model.Select(model.IsUnitedStates ? UnitProfileKind.StructuralEngineering : UnitProfileKind.UnitedStates);
            var request = new ShutdownRequestedEventArgs(); events.Raise(request);
            Assert.True(request.Cancel);
            Assert.True(settings.IsVisible); Assert.True(model.HasChanges);   // asked before its owner, not discarded
            Assert.Null(app.Host.Quit.UserQuit); Assert.Equal(DesktopApplicationState.Ready, app.Host.State);
        }));
    }

    [Fact]
    public void AProgrammaticShutdownOfTheLifetimeIsNeverTakenForASystemShutdown()
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() => WithLifetime((lifetime, _) =>
        {
            var app = new App(windowsPlatform: true); var a = app.Start(); app.OpenDirty(a, "programmatic");
            Assert.False(lifetime.TryShutdown());
            Flush();
            Assert.False(app.Host.Quit.IsSystemShutdown);
            Click(Dialog(a), Strings.Cancel);   // an ordinary close of the window, asked and cancelled
            Assert.Contains(a, app.Host.Workspaces.Handles); Assert.Equal(DesktopApplicationState.Ready, app.Host.State);
        }));
    }

    // ---- system shutdown -----------------------------------------------------------------------------------------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheLifetimeStartsAnIdleCheckpointBeforeDisposalWithoutWaitingForStorage(bool osShutdown)
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() => WithLifetime((lifetime, events) =>
        {
            var app = new App(windowsPlatform: true);
            var a = app.StartWithProject("shutdown-before-disposal");
            app.Change(a);
            var gate = Gate();
            int started = 0;
            app.Storage.BeforeWrite = (_, _, _) =>
            {
                Assert.False(a.Model!.IsDisposed);
                Assert.Equal(DesktopApplicationState.ShuttingDown, app.Host.State);
                started++;
                return gate.Task;
            };
            var request = new ShutdownRequestedEventArgs();
            typeof(ShutdownRequestedEventArgs).GetProperty("IsOSShutdown",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(request, osShutdown);

            events.Raise(request);

            Assert.False(request.Cancel);
            Assert.True(a.Model!.IsDisposed);
            Assert.Empty(lifetime.Windows);
            Assert.Empty(app.Host.Workspaces.Handles);
            Assert.Equal(1, started);
            Assert.Empty(app.Storage.Writes);
            Assert.Empty(app.Storage.Discarded);
            gate.SetResult();
        }));
    }

    [Fact]
    public void SystemShutdownLeavesAnInFlightDebouncedWriteAndItsCancellationAlone()
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            var app = new App();
            var a = app.StartWithProject("shutdown-running-write");
            var gate = Gate();
            int started = 0;
            CancellationToken running = default;
            app.Storage.BeforeWrite = (_, _, token) =>
            {
                if (++started == 1) running = token;
                return gate.Task;
            };
            app.Change(a);
            app.Debounce.ReleaseAll(); Flush();
            Assert.Equal(1, started);
            Assert.True(running.CanBeCanceled);

            // Keep the dispatcher running here so a checkpoint wrongly queued behind the write can be observed.
            app.Host.Quit.BeginSystemShutdown(); Flush();
            Assert.False(running.IsCancellationRequested);
            gate.SetResult(); Flush();

            Assert.Equal(1, started);
            Assert.Single(app.Storage.Writes);
            Assert.Empty(app.Storage.Discarded);
            a.Window.Close(); Flush();
        });
    }

    [Fact]
    public void ASystemShutdownAsksNothingRefusesNothingAndKeepsEveryPublishedRecovery()
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            var app = new App(); var a = app.Start(); var (_, slot) = app.OpenDirty(a, "system-a"); app.Change(a, 3);
            var b = app.Second(a, "system-b");
            ClickMenu(a, Strings.Settings);
            var settings = (SettingsWindow)app.Host.AppDialogs.Find(AppDialogKind.Settings)!;
            var model = (SettingsViewModel)settings.DataContext!;
            model.Select(model.IsUnitedStates ? UnitProfileKind.StructuralEngineering : UnitProfileKind.UnitedStates);
            var persist = Gate(); app.Files.BeforeWrite = (path, _, _) => path.Contains("settings") ? persist.Task : Task.CompletedTask;
            bool? applied = null;
            Dispatcher.UIThread.Post(async () => applied = await model.ApplyAsync()); Flush();
            Assert.True(model.IsSaving);
            Assert.Equal(PlatformClose.SystemShutdown, app.Host.Quit.OnPlatformClosing(WindowCloseReason.OSShutdown, isProgrammatic: false));
            // Synchronously: mutations stop, nothing is taken any more, nothing is asked.
            Assert.True(a.Model!.IsQuitFrozen); Assert.True(b.Model!.IsQuitFrozen);
            Assert.Equal(DesktopApplicationState.ShuttingDown, app.Host.State);
            Assert.Equal(BrokerStatus.Retry, app.Host.HandleBrokerRequest(LaunchRequest.Activate));
            Assert.False(app.Host.SubmitPlatformActivation(LaunchRequest.Activate));
            // The platform closes every window: the saving Settings dialog and the dirty workspace do not refuse.
            settings.Close(); Flush(); Assert.False(settings.IsVisible);
            a.Window.Close(); b.Window.Close(); Flush();
            Assert.Empty(app.Host.Workspaces.Handles);
            Assert.Contains(slot, app.Storage.Slots()); Assert.Empty(app.Storage.Discarded);
            // The held settings commit completes; the disposed workspaces take nothing from it and nothing is shown.
            persist.SetResult(); Flush();
            Assert.True(applied); Assert.DoesNotContain(a.Window.OwnedWindows, w => w.IsVisible);
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ADiscardHeldWhenTheSystemShutsDownCompletesButNothingIsCommittedOrRepublished(bool viaQuit)
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            var app = new App(); var a = app.Start(); var (_, slot) = app.OpenDirty(a, "held-discard");
            var identity = app.Host.ProjectPaths.IdentityOwnedBy(a.WindowId)!;
            if (viaQuit) Assert.True(app.Host.Quit.RequestQuit()); else a.Window.Close();
            Flush();
            var question = Dialog(a);
            var gate = Gate(); app.Storage.BeforeDiscard = _ => gate.Task;
            Click(question, Strings.DontSave);   // the user's discard is held in the storage
            Assert.Contains(a, app.Host.Workspaces.Handles);
            app.SystemShutdown(a);
            Assert.DoesNotContain(a, app.Host.Workspaces.Handles); Assert.Equal(DesktopApplicationState.ShuttingDown, app.Host.State);
            int writes = app.Storage.Writes.Count;
            gate.SetResult(); Flush();
            Assert.Equal(slot.FileName, Assert.Single(app.Storage.Discarded));   // the decision stays
            Assert.Empty(app.Storage.Slots(RecoverySlotKind.Project)); Assert.Equal(writes, app.Storage.Writes.Count);   // no republish
            Assert.Equal(ProjectPathState.Free, app.Host.ProjectPaths.StateOf(identity));   // the owner release ended
            Assert.DoesNotContain(a.Window.OwnedWindows, w => w.IsVisible);
        });
    }

    [Fact]
    public void ACheckpointHeldWhenTheSystemShutsDownStillPublishesAndAsksNothingMore()
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            var app = new App(); var a = app.StartWithProject("held-checkpoint"); app.Change(a);
            var gate = Gate(); int started = 0;
            app.Storage.BeforeWrite = (_, _, _) => { started++; return gate.Task; };
            Assert.True(app.Host.Quit.RequestQuit()); Flush();
            var run = app.Host.Quit.UserQuit!;
            Assert.Null(OpenDialog(a));
            app.SystemShutdown(a);
            Assert.True(run.Superseded);
            gate.SetResult(); Flush();
            Assert.True(run.Completion.IsCompleted);
            Assert.Equal(1, started);
            Assert.True(a.Model!.Session!.CurrentRevision.State.ContentEquals(
                ProjectRecovery.Decode(app.Storage.Data[app.Storage.OnlySlot(RecoverySlotKind.Project)]).State));
            Assert.DoesNotContain(a.Window.OwnedWindows, w => w.IsVisible);
        });
    }

    [Fact]
    public void ARestoreHeldWhenTheSystemShutsDownLeavesTheCandidateUndecided()
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            var app = new App(); var id = RecoveryId.New();
            var project = RecoverySlot.Project(id);
            app.Storage.Put(project, ProjectRecovery.Encode(new(State(), null, Now)));
            app.Storage.Put(RecoverySlot.Setup(id), ProjectSetupRecovery.Encode(new(new ProjectSetupDraft(), Now)));   // its companion
            var a = app.Start();
            var candidate = Assert.Single(app.Host.Recovery.Undecided).CandidateId;
            var gate = Gate(); app.Storage.BeforeDelete = s => s.Kind == RecoverySlotKind.Setup ? gate.Task : Task.CompletedTask;
            bool? restored = null;
            Dispatcher.UIThread.Post(async () => restored = await app.Host.Workspaces.RestoreRecoveryAsync(candidate, a)); Flush();
            Assert.Null(restored);
            app.SystemShutdown(a);
            gate.SetResult(); Flush();
            Assert.False(restored);
            Assert.Contains(project, app.Storage.Slots());
            Assert.Contains(app.Host.Recovery.Undecided, c => c.CandidateId == candidate);
            Assert.Empty(app.Host.Workspaces.Handles);
        });
    }
}
