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
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

[Collection("Avalonia")]
public sealed class DesktopActivationWindowTests
{
    private static void Click(Window dialog, string label) => dialog.GetVisualDescendants().OfType<Button>()
        .Single(b => Equals(b.Content, label)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static DesktopApplicationHost Host(Files files, bool withRecovery = false)
    {
        var recovery = new ProjectRecovery(files, ProjectRecovery.Local(files).SlotPath, new Delay().Wait, () => Now);
        if (withRecovery) files.Data[recovery.SlotPath] = ProjectRecovery.Encode(new(State(), null, Now));
        return new(new DesktopStartupContext(files, new LocalSettingsStore(files, "settings"), UserSettings.Default)
        { StateStores = DesktopStateStores.Local(files) with { Recovery = recovery }, ProjectIdentities = Identities });
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
            Assert.Equal(window.WindowId, host.ProjectPaths.ArbitrateOpen(Identities.Resolve(path)).Owner);

            window.Close(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(ProjectPathState.Free, host.ProjectPaths.StateOf(Identities.Resolve(path)));
        });
    }

    [Fact]
    public void RequestArrivingWhileOpenIsUnavailableStaysQueuedAndOpensAfterReturningToTheEditor()
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            var files = new Files();
            string a = TestPath("queue-a.spandraft"), b = TestPath("queue-b.spandraft");
            files.Data[a] = ProjectFileCodec.Serialize(State(1));
            files.Data[b] = ProjectFileCodec.Serialize(State(4));
            var host = Host(files);
            var window = host.Workspaces.CreateInitial().Window;
            DesktopControlEnvironment.ShowInitialized(window);
            var main = Assert.IsType<MainWindowViewModel>(window.DataContext);

            Assert.Equal(BrokerStatus.Accepted, host.HandleBrokerRequest(LaunchRequest.OpenProject(a)));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(a, main.Session!.FilePath);

            // A new-project setup on top of an open session makes opening unavailable.
            Assert.True(main.NewAsync().IsCompletedSuccessfully); Dispatcher.UIThread.RunJobs();
            Assert.False(main.CanOpenProject);
            Assert.Equal(BrokerStatus.Accepted, host.HandleBrokerRequest(LaunchRequest.OpenProject(b)));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, host.Activation.Count);
            Assert.Equal(a, main.Session!.FilePath);

            Assert.True(main.CancelSetupAsync().IsCompletedSuccessfully); Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, host.Activation.Count);
            Assert.Equal(b, main.Session!.FilePath);
            typeof(MainWindow).GetField("_closeApproved", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(window, true);
            window.Close(); Dispatcher.UIThread.RunJobs();
        });
    }

    private static void SetupBlockedWithQueuedOpen(out DesktopApplicationHost host, out MainWindow window, out string target)
    {
        var files = new Files();
        string a = TestPath("quit-a.spandraft"); target = TestPath("quit-b.spandraft");
        files.Data[a] = ProjectFileCodec.Serialize(State(1));
        files.Data[target] = ProjectFileCodec.Serialize(State(4));
        host = Host(files);
        window = host.Workspaces.CreateInitial().Window;
        DesktopControlEnvironment.ShowInitialized(window);
        var main = Assert.IsType<MainWindowViewModel>(window.DataContext);
        host.HandleBrokerRequest(LaunchRequest.OpenProject(a)); Dispatcher.UIThread.RunJobs();
        Assert.True(main.NewAsync().IsCompletedSuccessfully); Dispatcher.UIThread.RunJobs();
        Assert.Equal(BrokerStatus.Accepted, host.HandleBrokerRequest(LaunchRequest.OpenProject(target)));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, host.Activation.Count);
    }

    [Fact]
    public void BlockedOpenRequestSurfacesTheBlockingTargetOnceAndStaysQueued()
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            var files = new Files();
            string a = TestPath("surf-a.spandraft"), b = TestPath("surf-b.spandraft");
            files.Data[a] = ProjectFileCodec.Serialize(State(1));
            files.Data[b] = ProjectFileCodec.Serialize(State(4));
            var host = Host(files);
            var window = host.Workspaces.CreateInitial().Window;
            DesktopControlEnvironment.ShowInitialized(window);
            var main = Assert.IsType<MainWindowViewModel>(window.DataContext);
            host.HandleBrokerRequest(LaunchRequest.OpenProject(a)); Dispatcher.UIThread.RunJobs();
            Assert.True(main.NewAsync().IsCompletedSuccessfully); Dispatcher.UIThread.RunJobs();
            var surfaced = new List<Window>();
            window.Surface = surfaced.Add;

            host.HandleBrokerRequest(LaunchRequest.OpenProject(b)); Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, host.Activation.Count);
            Assert.Same(window.RetryTarget, Assert.Single(surfaced));
            Assert.Equal(a, main.Session!.FilePath);
            window.Surface = static _ => { };
            typeof(MainWindow).GetField("_closeApproved", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(window, true);
            window.Close(); Dispatcher.UIThread.RunJobs();
        });
    }

    [Fact]
    public void QuitWithQueuedOpenRequestAsksAndConfirmingQuits()
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            SetupBlockedWithQueuedOpen(out var host, out var window, out _);
            bool closed = false; window.Closed += (_, _) => closed = true;
            window.Close(); Dispatcher.UIThread.RunJobs();
            Assert.False(closed);
            Click(Assert.Single(window.OwnedWindows), Strings.Discard); Dispatcher.UIThread.RunJobs();
            // The existing leave question for the setup draft may follow; answer it if present.
            if (window.OwnedWindows.Count > 0) Click(window.OwnedWindows[0], Strings.Discard);
            Dispatcher.UIThread.RunJobs();
            Assert.True(closed);
        });
    }

    [Fact]
    public void QuitWithQueuedOpenRequestCancelledReturnsToReadyAndOpensTheRequest()
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            SetupBlockedWithQueuedOpen(out var host, out var window, out var target);
            var main = Assert.IsType<MainWindowViewModel>(window.DataContext);
            window.Close(); Dispatcher.UIThread.RunJobs();
            Click(Assert.Single(window.OwnedWindows), Strings.Cancel); Dispatcher.UIThread.RunJobs();
            Assert.Equal(DesktopApplicationState.Ready, host.State);
            Assert.Equal(1, host.Activation.Count);

            Assert.True(main.CancelSetupAsync().IsCompletedSuccessfully); Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, host.Activation.Count);
            Assert.Equal(target, main.Session!.FilePath);
            typeof(MainWindow).GetField("_closeApproved", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(window, true);
            window.Close(); Dispatcher.UIThread.RunJobs();
        });
    }

    [Fact]
    public void RequestArrivingWhileQuitIsPendingIsAskedAboutBeforeClosingAndDeclinedReturnsToReady()
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            SetupBlockedWithQueuedOpen(out var host, out var window, out _);
            bool closed = false; window.Closed += (_, _) => closed = true;
            window.Close(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(DesktopApplicationState.QuitInProgress, host.State);
            Click(Assert.Single(window.OwnedWindows), Strings.Discard);
            // Simulates a macOS file activation that bypasses the broker while the quit is still pending.
            Assert.True(host.SubmitPlatformActivation(LaunchRequest.OpenProject(TestPath("quit-late.spandraft"))));
            Dispatcher.UIThread.RunJobs();
            Assert.False(closed);
            // Only the late request is asked about; the already confirmed one is not asked again.
            Click(Assert.Single(window.OwnedWindows), Strings.Cancel); Dispatcher.UIThread.RunJobs();
            Assert.False(closed);
            Assert.Equal(DesktopApplicationState.Ready, host.State);
            Assert.Equal(2, host.Activation.Count);
            typeof(MainWindow).GetField("_closeApproved", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(window, true);
            window.Close(); Dispatcher.UIThread.RunJobs();
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
    public void OpenLegacyRecoveryAnswersRetryKeepsTheQueueEmptyAndActivatesTheDialog()
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            var files = new Files();
            string target = TestPath("during-recovery.spandraft");
            files.Data[target] = ProjectFileCodec.Serialize(State(4));
            var host = Host(files, withRecovery: true);
            var window = host.Workspaces.CreateInitial().Window;
            window.Show(); Dispatcher.UIThread.RunJobs();
            var dialog = Assert.Single(window.OwnedWindows);
            var request = LaunchRequest.OpenProject(target);

            Assert.Equal(BrokerStatus.Retry, host.HandleBrokerRequest(request));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, host.Activation.Count);
            Assert.Equal(DesktopApplicationState.Starting, host.State);
            Assert.Same(dialog, window.RetryTarget);

            Click(dialog, Strings.Restore); Dispatcher.UIThread.RunJobs();
            Assert.Equal(DesktopApplicationState.Ready, host.State);
            Assert.Same(window, window.RetryTarget);
            Assert.Equal(BrokerStatus.Accepted, host.HandleBrokerRequest(request));
            Dispatcher.UIThread.RunJobs();
            // The restored session is dirty, so the existing Save/Discard/Cancel question comes first.
            Click(Assert.Single(window.OwnedWindows), Strings.DontSave); Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, host.Activation.Count);
            Assert.Equal(target, Assert.IsType<MainWindowViewModel>(window.DataContext).Session!.FilePath);
            typeof(MainWindow).GetField("_closeApproved", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(window, true);
            window.Close(); Dispatcher.UIThread.RunJobs();
        });
    }

    [Fact]
    public void CancelledLegacyRecoveryNeverConfirmedARequest()
    {
        using var environment = new DesktopControlEnvironment();
        var host = Host(new Files(), withRecovery: true);
        var window = host.Workspaces.CreateInitial().Window;
        window.Show(); Dispatcher.UIThread.RunJobs();
        var request = LaunchRequest.OpenProject(TestPath("lost.spandraft"));
        Assert.Equal(BrokerStatus.Retry, host.HandleBrokerRequest(request));
        Click(Assert.Single(window.OwnedWindows), Strings.Cancel); Dispatcher.UIThread.RunJobs();
        Assert.Equal(DesktopApplicationState.ShuttingDown, host.State);
        Assert.Equal(0, host.Activation.Count);
        // Nothing was acknowledged, so the Secondary still owns the request.
        Assert.Equal(BrokerStatus.Retry, host.HandleBrokerRequest(request));
    }

    // Pumps the UI dispatcher on this thread until the task is done; awaiting it then continues inline, without a thread switch.
    private static async Task<T> PumpUntilAsync<T>(Task<T> task)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (!task.IsCompleted && DateTime.UtcNow < deadline) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(10); }
        Assert.True(task.IsCompleted);
        return await task;
    }

    [Fact]
    public async Task RealBrokerEndpointAnswersRetryDuringRecoveryAndAcceptedAfterRestore()
    {
        using var environment = new DesktopControlEnvironment();
        string root = Path.Combine(Path.GetTempPath(), "spandraft-activation-win-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var primary = PrimaryInstance.TryAcquire(new DataScope(root))!;
            using var broker = BrokerServer.Start(primary.Metadata);
            var host = Host(new Files(), withRecovery: true);
            host.AttachBroker(broker);
            var window = host.Workspaces.CreateInitial().Window;
            window.Show(); Dispatcher.UIThread.RunJobs();
            var request = LaunchRequest.Activate;
            var retry = BrokerClient.SendAsync(primary.Metadata, request, TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            // Pump before awaiting: the window and its dispatcher must stay on this thread.
            Assert.Equal(BrokerStatus.Retry, await PumpUntilAsync(retry));
            Assert.Equal(0, host.Activation.Count);
            Assert.NotEqual(DesktopApplicationState.Ready, host.State);

            Click(Assert.Single(window.OwnedWindows), Strings.Restore); Dispatcher.UIThread.RunJobs();
            var accepted = BrokerClient.SendAsync(primary.Metadata, request, TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Equal(BrokerStatus.Accepted, await PumpUntilAsync(accepted));
            typeof(MainWindow).GetField("_closeApproved", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(window, true);
            window.Close(); Dispatcher.UIThread.RunJobs();
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

    private static bool IsRecoveryDialog(Window dialog) =>
        dialog.GetVisualDescendants().OfType<Button>().Any(b => Equals(b.Content, Strings.Restore));

    private static void ApproveClose(MainWindow window)
    {
        typeof(MainWindow).GetField("_closeApproved", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(window, true);
        window.Close(); Dispatcher.UIThread.RunJobs();
    }

    private static (DesktopApplicationHost Host, MainWindow Window, Files Files, string Slot) ShowWithLegacyRecovery(params string[] projects)
    {
        var files = new Files();
        foreach (var (path, index) in projects.Select((p, i) => (p, i))) files.Data[path] = ProjectFileCodec.Serialize(State(index + 2));
        var host = Host(files, withRecovery: true);
        return (host, host.Workspaces.CreateInitial().Window, files, ProjectRecovery.Local(files).SlotPath);
    }

    [Fact]
    public void MacOpenDuringLegacyRecoveryIsNotSilentlyLostByCancel()
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            string target = TestPath("mac-during-recovery.spandraft");
            var (host, window, files, slot) = ShowWithLegacyRecovery(target);
            bool closed = false; window.Closed += (_, _) => closed = true;
            window.Show(); Dispatcher.UIThread.RunJobs();
            Assert.True(host.SubmitPlatformActivation(LaunchRequest.OpenProject(target)));
            Assert.Equal(DesktopApplicationState.Starting, host.State);

            Click(Assert.Single(window.OwnedWindows), Strings.Cancel); Dispatcher.UIThread.RunJobs();
            Assert.False(closed);
            Assert.False(IsRecoveryDialog(Assert.Single(window.OwnedWindows)));
            Assert.Equal(1, host.Activation.Count);
            Assert.Equal(DesktopApplicationState.Starting, host.State);
            Assert.True(files.Data.ContainsKey(slot));
            ApproveClose(window);
        });
    }

    [Fact]
    public void ConfirmedDiscardOfPlatformActivationsAfterRecoveryCancelExitsAndKeepsTheSlot()
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            string first = TestPath("mac-discard-a.spandraft"), late = TestPath("mac-discard-b.spandraft");
            var (host, window, files, slot) = ShowWithLegacyRecovery(first, late);
            bool closed = false; window.Closed += (_, _) => closed = true;
            window.Show(); Dispatcher.UIThread.RunJobs();
            host.SubmitPlatformActivation(LaunchRequest.OpenProject(first));
            Click(Assert.Single(window.OwnedWindows), Strings.Cancel); Dispatcher.UIThread.RunJobs();

            // An activation arriving while the discard question is open wins against the commit and is asked about too.
            var question = Assert.Single(window.OwnedWindows);
            Assert.True(host.SubmitPlatformActivation(LaunchRequest.OpenProject(late)));
            Click(question, Strings.Discard); Dispatcher.UIThread.RunJobs();
            Assert.False(closed);
            Assert.False(IsRecoveryDialog(Assert.Single(window.OwnedWindows)));

            Click(Assert.Single(window.OwnedWindows), Strings.Discard); Dispatcher.UIThread.RunJobs();
            Assert.True(closed);
            Assert.Equal(DesktopApplicationState.ShuttingDown, host.State);
            Assert.Equal(0, host.Activation.Count);
            Assert.True(files.Data.ContainsKey(slot));
            // A committed shutdown is final: nothing is accepted or processed afterwards.
            Assert.False(host.SubmitPlatformActivation(LaunchRequest.OpenProject(first)));
            Assert.Equal(BrokerStatus.Retry, host.HandleBrokerRequest(LaunchRequest.OpenProject(first)));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, host.Activation.Count);
        });
    }

    [Fact]
    public void DecliningTheDiscardKeepsSpanDraftRunningAndTheRecoveryCanBeDecidedAgain()
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            string target = TestPath("mac-keep.spandraft");
            var (host, window, files, slot) = ShowWithLegacyRecovery(target);
            bool closed = false; window.Closed += (_, _) => closed = true;
            window.Show(); Dispatcher.UIThread.RunJobs();
            host.SubmitPlatformActivation(LaunchRequest.OpenProject(target));
            Click(Assert.Single(window.OwnedWindows), Strings.Cancel); Dispatcher.UIThread.RunJobs();
            Click(Assert.Single(window.OwnedWindows), Strings.Cancel); Dispatcher.UIThread.RunJobs();

            Assert.False(closed);
            Assert.True(IsRecoveryDialog(Assert.Single(window.OwnedWindows)));
            Assert.Equal(DesktopApplicationState.Starting, host.State);
            Assert.True(files.Data.ContainsKey(slot));
            Assert.Equal(1, host.Activation.Count);
            Assert.Equal(BrokerStatus.Retry, host.HandleBrokerRequest(LaunchRequest.Activate));

            Click(Assert.Single(window.OwnedWindows), Strings.Restore); Dispatcher.UIThread.RunJobs();
            Assert.Equal(DesktopApplicationState.Ready, host.State);
            // The restored session is dirty, so the existing Save/Discard/Cancel question comes first.
            Click(Assert.Single(window.OwnedWindows), Strings.DontSave); Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, host.Activation.Count);
            Assert.Equal(target, Assert.IsType<MainWindowViewModel>(window.DataContext).Session!.FilePath);
            ApproveClose(window);
        });
    }

    [Fact]
    public void ActivateOnlyDuringLegacyRecoveryNeedsNoDiscardQuestion()
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            var (host, window, files, slot) = ShowWithLegacyRecovery();
            bool closed = false; window.Closed += (_, _) => closed = true;
            window.Show(); Dispatcher.UIThread.RunJobs();
            Assert.True(host.SubmitPlatformActivation(LaunchRequest.Activate));
            Click(Assert.Single(window.OwnedWindows), Strings.Cancel); Dispatcher.UIThread.RunJobs();
            Assert.True(closed);
            Assert.True(files.Data.ContainsKey(slot));
        });
    }

    [Fact]
    public void InitialRequestFollowsTheStartupCancelSilentlyAndOtherwiseRunsFirst()
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            string initial = TestPath("initial-a.spandraft"), later = TestPath("initial-b.spandraft");
            // Cancelling the startup drops its own request without an extra question (§70.1).
            var (host, window, _, _) = ShowWithLegacyRecovery(initial);
            host.SetInitialRequest(LaunchRequest.OpenProject(initial));
            bool closed = false; window.Closed += (_, _) => closed = true;
            window.Show(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, host.Activation.Count);
            Click(Assert.Single(window.OwnedWindows), Strings.Cancel); Dispatcher.UIThread.RunJobs();
            Assert.True(closed);

            // After a successful recovery decision the initial request precedes platform requests received meanwhile.
            (host, window, _, _) = ShowWithLegacyRecovery(initial, later);
            host.SetInitialRequest(LaunchRequest.OpenProject(initial));
            window.Show(); Dispatcher.UIThread.RunJobs();
            host.SubmitPlatformActivation(LaunchRequest.OpenProject(later));
            Click(Assert.Single(window.OwnedWindows), Strings.Restore); Dispatcher.UIThread.RunJobs();
            Click(Assert.Single(window.OwnedWindows), Strings.DontSave); Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, host.Activation.Count);
            // Had the later request run first, the initial one would have replaced it.
            Assert.Equal(later, Assert.IsType<MainWindowViewModel>(window.DataContext).Session!.FilePath);
            ApproveClose(window);
        });
    }
}
