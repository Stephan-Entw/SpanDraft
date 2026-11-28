using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SpanDraft.Desktop;
using SpanDraft.Desktop.Activation;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.ViewModels;
using SpanDraft.Desktop.Workspaces;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

[Collection("Avalonia")]
public sealed class DesktopActivationWindowTests
{
    private static void Click(Window dialog, string label) => dialog.GetVisualDescendants().OfType<Button>()
        .Single(b => Equals(b.Content, label)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static DesktopApplicationHost Host(Files files, TestRecoveryStorage? recovery = null) =>
        new(new DesktopStartupContext(files, new LocalSettingsStore(files, "settings"), UserSettings.Default)
        { StateStores = DesktopStateStores.Local(files) with { Recovery = recovery ?? new TestRecoveryStorage() }, ProjectIdentities = Identities });

    private static TestRecoveryStorage WithRecovery(string? originalPath = null)
    {
        var storage = new TestRecoveryStorage();
        storage.Put(RecoverySlot.Project(RecoveryId.New()), ProjectRecovery.Encode(new(State(), originalPath, Now)));
        return storage;
    }

    private static void WithUiContext(Action action)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new AvaloniaSynchronizationContext());
        try { action(); } finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    [Fact]
    public void LaunchOpenForTheCurrentIdentitySurfacesTheSameWindowAndSession()
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            var files = new Files();
            var host = Host(files);
            var window = host.Workspaces.CreateInitial().Window;
            string path = TestPath("activate-owned.spandraft");
            files.Data[path] = ProjectFileCodec.Serialize(State(3));
            int reads = 0, surfaced = 0;
            files.BeforeRead = p => { if (p == path) reads++; return Task.CompletedTask; };
            window.Surface = target => { Assert.Same(window, target); surfaced++; };
            DesktopControlEnvironment.ShowInitialized(window);
            var model = Assert.IsType<MainWindowViewModel>(window.DataContext);
            Assert.Equal(window.WindowId, model.WindowId);
            Assert.True(model.OpenAsync(path).GetAwaiter().GetResult());
            var session = model.Session;

            Assert.Equal(BrokerStatus.Accepted, host.HandleBrokerRequest(LaunchRequest.OpenProject(path)));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, host.Activation.Count);
            Assert.Equal(1, reads);
            Assert.Equal(1, surfaced);
            Assert.Same(session, model.Session);
            Assert.Same(window, Assert.Single(host.Workspaces.Handles).Window);
            Assert.Equal(0, host.Workspaces.StartSlotCount);
            Assert.Equal(window.WindowId, host.ProjectPaths.ArbitrateOpen(Identities.Resolve(path)).Owner);

            window.Close(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(ProjectPathState.Free, host.ProjectPaths.StateOf(Identities.Resolve(path)));
        });
    }

    [Fact]
    public void RapidExternalOpensKeepTheirOrderAndProjectsAndDuplicatesOnlyActivateTheOwner()
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            var files = new Files();
            string a = TestPath("queue-a.spandraft"), b = TestPath("queue-b.spandraft"), c = TestPath("queue-c.spandraft");
            files.Data[a] = ProjectFileCodec.Serialize(State(1));
            files.Data[b] = ProjectFileCodec.Serialize(State(4));
            files.Data[c] = ProjectFileCodec.Serialize(State(7));
            var host = Host(files);
            var window = host.Workspaces.CreateInitial().Window;
            DesktopControlEnvironment.ShowInitialized(window);
            var main = Assert.IsType<MainWindowViewModel>(window.DataContext);

            Assert.Equal(BrokerStatus.Accepted, host.HandleBrokerRequest(LaunchRequest.OpenProject(a)));
            Dispatcher.UIThread.RunJobs();
            var original = main.Session;
            Assert.Equal(a, original!.FilePath);
            var reads = new List<string>();
            files.BeforeRead = path => { if (path == b || path == c) reads.Add(path); return Task.CompletedTask; };
            Assert.Equal(BrokerStatus.Accepted, host.HandleBrokerRequest(LaunchRequest.OpenProject(b)));
            Assert.True(host.SubmitPlatformActivation(LaunchRequest.OpenProject(c)));
            Assert.True(host.SubmitPlatformActivation(LaunchRequest.OpenProject(b)));
            Dispatcher.UIThread.RunJobs(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, host.Activation.Count);
            Assert.Equal([b, c], reads);
            Assert.Equal([a, b, c], host.Workspaces.Handles.Select(handle => handle.Model!.Session!.FilePath));
            Assert.Equal(b, host.Workspaces.Active!.Model!.Session!.FilePath);
            Assert.Same(original, main.Session);
            Assert.Equal(0, host.Workspaces.StartSlotCount);
            foreach (var handle in host.Workspaces.Handles.ToArray()) ApproveClose(handle.Window);
        });
    }

    private static void SetupWithQueuedOpens(out DesktopApplicationHost host, out MainWindow window, out string target)
    {
        var files = new Files();
        string a = TestPath("quit-a.spandraft"); target = TestPath("quit-b.spandraft");
        files.Data[a] = ProjectFileCodec.Serialize(State(1));
        files.Data[target] = ProjectFileCodec.Serialize(State(4));
        files.Data[TestPath("quit-late.spandraft")] = ProjectFileCodec.Serialize(State(7));
        host = Host(files);
        window = host.Workspaces.CreateInitial().Window;
        DesktopControlEnvironment.ShowInitialized(window);
        var main = Assert.IsType<MainWindowViewModel>(window.DataContext);
        host.HandleBrokerRequest(LaunchRequest.OpenProject(a)); Dispatcher.UIThread.RunJobs();
        Assert.Equal(a, main.Session!.FilePath);
        Assert.Equal(BrokerStatus.Accepted, host.HandleBrokerRequest(LaunchRequest.OpenProject(target)));
        // Start the last-window close before the posted activation pump runs.
        Assert.Equal(1, host.Activation.Count);
    }

    [Fact]
    public void AnUnavailableLaunchTargetLeavesTheRequestQueuedUntilItCanBeRoutedAgain()
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            var files = new Files();
            string a = TestPath("retry-a.spandraft"), b = TestPath("retry-b.spandraft");
            files.Data[a] = ProjectFileCodec.Serialize(State(1));
            files.Data[b] = ProjectFileCodec.Serialize(State(4));
            var host = Host(files);
            var window = host.Workspaces.CreateInitial().Window;
            DesktopControlEnvironment.ShowInitialized(window);
            var main = Assert.IsType<MainWindowViewModel>(window.DataContext);
            host.HandleBrokerRequest(LaunchRequest.OpenProject(a)); Dispatcher.UIThread.RunJobs();
            var session = main.Session;
            var arbitration = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            host.ProjectPaths.RecoverySettler = () => arbitration.Task;
            host.HandleBrokerRequest(LaunchRequest.OpenProject(b)); Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, host.Activation.Count);
            var unavailable = Assert.Single(host.Workspaces.Handles, handle => handle.Window != window);
            unavailable.Window.Close(); Dispatcher.UIThread.RunJobs();
            Assert.Same(window, Assert.Single(host.Workspaces.Handles).Window);
            Assert.Equal(1, host.Activation.Count);
            host.ProjectPaths.RecoverySettler = null;
            arbitration.SetResult();
            DesktopControlEnvironment.RunOnDispatcher(host.Activation.PumpAsync);
            Dispatcher.UIThread.RunJobs(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, host.Activation.Count);
            var opened = Assert.Single(host.Workspaces.Handles, handle => handle.Window != window);
            Assert.Equal(b, opened.Model!.Session!.FilePath);
            Assert.Same(session, main.Session);
            ApproveClose(opened.Window); ApproveClose(window);
        });
    }

    [Fact]
    public void QuitWithQueuedOpenRequestAsksAndConfirmingQuits()
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            SetupWithQueuedOpens(out var host, out var window, out _);
            bool closed = false; window.Closed += (_, _) => closed = true;
            window.Close(); Dispatcher.UIThread.RunJobs();
            Assert.False(closed);
            Click(Assert.Single(window.OwnedWindows), Strings.Discard); Dispatcher.UIThread.RunJobs();
            Dispatcher.UIThread.RunJobs();
            Assert.True(closed);
            Assert.Equal(0, host.Activation.Count);
        });
    }

    [Fact]
    public void QuitWithQueuedOpenRequestCancelledReturnsToReadyAndOpensTheRequest()
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            SetupWithQueuedOpens(out var host, out var window, out var target);
            var main = Assert.IsType<MainWindowViewModel>(window.DataContext);
            var original = main.Session;
            window.Close(); Dispatcher.UIThread.RunJobs();
            Click(Assert.Single(window.OwnedWindows), Strings.Cancel); Dispatcher.UIThread.RunJobs();
            Assert.Equal(DesktopApplicationState.Ready, host.State);
            Assert.Equal(0, host.Activation.Count);
            Assert.Same(original, main.Session);
            var opened = Assert.Single(host.Workspaces.Handles, handle => handle.Window != window);
            Assert.Equal(target, opened.Model!.Session!.FilePath);
            ApproveClose(opened.Window); ApproveClose(window);
        });
    }

    [Fact]
    public void RequestArrivingWhileQuitIsPendingIsAskedAboutBeforeClosingAndDeclinedReturnsToReady()
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            SetupWithQueuedOpens(out var host, out var window, out var target);
            bool closed = false; window.Closed += (_, _) => closed = true;
            window.Close(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(DesktopApplicationState.QuitInProgress, host.State);
            var before = Assert.Single(window.OwnedWindows);
            // Simulates a macOS file activation that bypasses the broker while the quit is still pending.
            Assert.True(host.SubmitPlatformActivation(LaunchRequest.OpenProject(TestPath("quit-late.spandraft"))));
            Click(before, Strings.Discard);
            Dispatcher.UIThread.RunJobs();
            Assert.False(closed);
            // Only the late request is asked about; the already confirmed one is not asked again.
            var late = Assert.Single(window.OwnedWindows);
            Assert.Contains(late.GetVisualDescendants().OfType<TextBlock>(), text => text.Text?.Contains("quit-late.spandraft") == true);
            Click(late, Strings.Cancel); Dispatcher.UIThread.RunJobs();
            Assert.False(closed);
            Assert.Equal(DesktopApplicationState.Ready, host.State);
            Assert.Equal(0, host.Activation.Count);
            Assert.Equal([TestPath("quit-a.spandraft"), target, TestPath("quit-late.spandraft")],
                host.Workspaces.Handles.Select(handle => handle.Model!.Session!.FilePath));
            foreach (var handle in host.Workspaces.Handles.ToArray()) ApproveClose(handle.Window);
        });
    }

    [Fact]
    public void QuitWithEmptyQueueOrOnlyActivateAsksNothingExtra()
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            var host = Host(new Files());
            var window = host.Workspaces.CreateInitial().Window;
            DesktopControlEnvironment.ShowInitialized(window);
            Assert.Empty(host.Activation.PendingOpenRequests());
            host.Activation.Enqueue(LaunchRequest.Activate);
            Assert.Empty(host.Activation.PendingOpenRequests());
            host.Activation.SetConsumer(_ => Task.FromResult(false));
            bool closed = false; window.Closed += (_, _) => closed = true;
            window.Close(); Dispatcher.UIThread.RunJobs();
            Assert.Empty(window.OwnedWindows);
            Assert.True(closed);
        });
    }

    [Fact]
    public void PendingRecoveryNeverHoldsTheHostBackAndRequestsAreAccepted()
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            var files = new Files();
            string target = TestPath("during-recovery.spandraft");
            files.Data[target] = ProjectFileCodec.Serialize(State(4));
            var storage = WithRecovery();
            var host = Host(files, storage);
            var window = host.Workspaces.CreateInitial().Window;
            DesktopControlEnvironment.ShowInitialized(window);

            // No question, no dialog: the host is Ready although an undecided recovery exists.
            Assert.Empty(window.OwnedWindows);
            Assert.Equal(DesktopApplicationState.Ready, host.State);
            Assert.True(host.Recovery.HasUndecided);
            var model = Assert.IsType<MainWindowViewModel>(window.DataContext);
            Assert.True(model.HasRecovery);

            Assert.Equal(BrokerStatus.Accepted, host.HandleBrokerRequest(LaunchRequest.OpenProject(target)));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, host.Activation.Count);
            Assert.Equal(target, model.Session!.FilePath);
            Assert.True(host.Recovery.HasUndecided);
            Assert.Single(storage.Slots());
            ApproveClose(window);
        });
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void OpeningAPathWithPendingRecoveryShowsTheRecoveryCenterAndNeverReadsTheDisk(bool occupied)
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            var files = new Files();
            string target = TestPath("recovery-pending.spandraft");
            files.Data[target] = ProjectFileCodec.Serialize(State(4));
            int reads = 0;
            files.BeforeRead = p => { if (p == target) reads++; return Task.CompletedTask; };
            var host = Host(files, WithRecovery(target));
            var window = host.Workspaces.CreateInitial().Window;
            DesktopControlEnvironment.ShowInitialized(window);
            var model = Assert.IsType<MainWindowViewModel>(window.DataContext);
            if (occupied)
            {
                string existing = TestPath("already-in-editor.spandraft");
                files.Data[existing] = ProjectFileCodec.Serialize(State(3));
                Assert.True(model.OpenAsync(existing).GetAwaiter().GetResult());
            }
            var original = model.Session;
            Assert.Equal(ProjectPathState.RecoveryPending, host.ProjectPaths.StateOf(Identities.Resolve(target)));
            // The catalog compared the original with the recovery while loading; from here on the disk must stay untouched.
            reads = 0;

            Assert.Equal(BrokerStatus.Accepted, host.HandleBrokerRequest(LaunchRequest.OpenProject(target)));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, host.Activation.Count);
            var center = Assert.Single(host.Workspaces.Handles, handle => handle.Model!.Mode == MainViewMode.RecoveryCenter);
            Assert.Null(center.Model!.Session);
            Assert.Same(original, model.Session);
            if (occupied) Assert.NotSame(window, center.Window);
            else Assert.Same(window, center.Window);
            Assert.DoesNotContain(host.Workspaces.Handles, handle => handle.Reason == WorkspaceCreationReason.OpenProject);
            Assert.Equal(occupied ? 0 : 1, host.Workspaces.StartSlotCount);
            Assert.Equal(0, reads);
            foreach (var handle in host.Workspaces.Handles.ToArray()) ApproveClose(handle.Window);
        });
    }

    [Fact]
    public void UnreadableRecoveryAreaHoldsTheStartupBackUntilRetrySucceeds()
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            var storage = new TestRecoveryStorage { FailEnumerate = new IOException("Injected enumeration failure.") };
            var host = Host(new Files(), storage);
            var window = host.Workspaces.CreateInitial().Window;
            window.Show(); Dispatcher.UIThread.RunJobs();
            var dialog = Assert.Single(window.OwnedWindows);
            var request = LaunchRequest.OpenProject(TestPath("waiting.spandraft"));

            Assert.Equal(BrokerStatus.Retry, host.HandleBrokerRequest(request));
            Assert.Equal(DesktopApplicationState.Starting, host.State);
            Assert.Same(dialog, window.RetryTarget);
            Assert.Equal(0, host.Activation.Count);

            // Still unreadable: the question comes back.
            Click(dialog, Strings.Retry); Dispatcher.UIThread.RunJobs();
            Assert.Equal(DesktopApplicationState.Starting, host.State);
            dialog = Assert.Single(window.OwnedWindows);

            storage.FailEnumerate = null;
            Click(dialog, Strings.Retry); Dispatcher.UIThread.RunJobs();
            Assert.Equal(DesktopApplicationState.Ready, host.State);
            Assert.Empty(window.OwnedWindows);
            Assert.Equal(BrokerStatus.Accepted, host.HandleBrokerRequest(request));
            ApproveClose(window);
        });
    }

    [Fact]
    public void QuittingTheUnreadableRecoveryQuestionNeverConfirmedARequest()
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            var host = Host(new Files(), new TestRecoveryStorage { FailEnumerate = new IOException("Injected enumeration failure.") });
            var window = host.Workspaces.CreateInitial().Window;
            bool closed = false; window.Closed += (_, _) => closed = true;
            window.Show(); Dispatcher.UIThread.RunJobs();
            var request = LaunchRequest.OpenProject(TestPath("lost.spandraft"));
            Assert.Equal(BrokerStatus.Retry, host.HandleBrokerRequest(request));
            Click(Assert.Single(window.OwnedWindows), Strings.Quit); Dispatcher.UIThread.RunJobs();
            Assert.True(closed);
            Assert.NotEqual(DesktopApplicationState.Ready, host.State);
            Assert.Equal(0, host.Activation.Count);
            // Nothing was acknowledged, so the Secondary still owns the request.
            Assert.Equal(BrokerStatus.Retry, host.HandleBrokerRequest(request));
        });
    }

    [Fact]
    public void RealBrokerEndpointAnswersAcceptedAlthoughRecoveryIsPending()
    {
        using var environment = new DesktopControlEnvironment();
        string root = Path.Combine(Path.GetTempPath(), "spandraft-activation-win-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var primary = PrimaryInstance.TryAcquire(new DataScope(root))!;
            using var broker = BrokerServer.Start(primary.Metadata);
            var host = Host(new Files(), WithRecovery());
            host.AttachBroker(broker);
            var window = host.Workspaces.CreateInitial().Window;
            DesktopControlEnvironment.ShowInitialized(window);
            try
            {
                Assert.True(host.Recovery.HasUndecided);
                Assert.Equal(DesktopApplicationState.Ready, host.State);
                DesktopControlEnvironment.RunOnDispatcher(async () => Assert.Equal(BrokerStatus.Accepted,
                    await BrokerClient.SendAsync(primary.Metadata, LaunchRequest.Activate, TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)));
            }
            finally { ApproveClose(window); }
        }
        finally { try { Directory.Delete(root, true); } catch (IOException) { } }
    }

    [Fact]
    public async Task MacFileActivationFeedsTheDispatcherAndFiltersForeignExtensions()
    {
        using var environment = new DesktopControlEnvironment();
        string root = Path.Combine(Path.GetTempPath(), "spandraft-activation-mac-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string good = Path.Combine(root, "beam.spandraft"), upper = Path.Combine(root, "Other.SPANDRAFT"), foreign = Path.Combine(root, "notes.txt");
            foreach (var path in new[] { good, upper, foreign }) File.WriteAllText(path, "x");
            var window = new Window();
            var items = new List<IStorageItem>();
            foreach (var path in new[] { good, foreign, upper })
                items.Add((await window.StorageProvider.TryGetFileFromPathAsync(new Uri(path)))!);
            var requests = MacActivationSource.ToRequests(new FileActivatedEventArgs(items));
            Assert.Equal([LaunchRequest.OpenProject(good), LaunchRequest.OpenProject(upper)], requests);

            var host = new DesktopApplicationHost();
            foreach (var request in requests) Assert.True(host.SubmitPlatformActivation(request));
            Assert.Equal(2, host.Activation.Count);
            Assert.Empty(MacActivationSource.ToRequests(new FileActivatedEventArgs([items[1]])));
        }
        finally { Directory.Delete(root, true); }
    }

    private static void ApproveClose(MainWindow window)
    {
        typeof(MainWindow).GetField("_closeApproved", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(window, true);
        window.Close(); Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public void PlatformActivationsArrivingBeforeReadyAreQueuedAndRunWhenTheHostIsReady()
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            var files = new Files();
            string target = TestPath("mac-before-ready.spandraft");
            files.Data[target] = ProjectFileCodec.Serialize(State(2));
            var host = Host(files, WithRecovery());
            var window = host.Workspaces.CreateInitial().Window;
            Assert.True(host.SubmitPlatformActivation(LaunchRequest.OpenProject(target)));
            Assert.Equal(DesktopApplicationState.Starting, host.State);
            Assert.Equal(1, host.Activation.Count);

            DesktopControlEnvironment.ShowInitialized(window);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(DesktopApplicationState.Ready, host.State);
            Assert.Equal(0, host.Activation.Count);
            Assert.Equal(target, Assert.IsType<MainWindowViewModel>(window.DataContext).Session!.FilePath);
            ApproveClose(window);
        });
    }

    [Fact]
    public void InitialRequestPrecedesPlatformRequestsReceivedMeanwhile()
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            var files = new Files();
            string initial = TestPath("initial-a.spandraft"), later = TestPath("initial-b.spandraft");
            files.Data[initial] = ProjectFileCodec.Serialize(State(2));
            files.Data[later] = ProjectFileCodec.Serialize(State(3));
            var host = Host(files, WithRecovery());
            var window = host.Workspaces.CreateInitial().Window;
            host.SetInitialRequest(LaunchRequest.OpenProject(initial));
            host.SubmitPlatformActivation(LaunchRequest.OpenProject(later));
            DesktopControlEnvironment.ShowInitialized(window);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, host.Activation.Count);
            Assert.Equal(initial, Assert.IsType<MainWindowViewModel>(window.DataContext).Session!.FilePath);
            var next = Assert.Single(host.Workspaces.Handles, handle => handle.Window != window);
            Assert.Equal(later, next.Model!.Session!.FilePath);
            Assert.Equal(0, host.Workspaces.StartSlotCount);
            ApproveClose(next.Window); ApproveClose(window);
        });
    }
}
