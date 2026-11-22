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
        { StateStores = DesktopStateStores.Local(files) with { Recovery = recovery } });
    }

    private static void WithUiContext(Action action)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new AvaloniaSynchronizationContext());
        try { action(); } finally { SynchronizationContext.SetSynchronizationContext(previous); }
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
            var window = new MainWindow(host);
            window.Show(); Dispatcher.UIThread.RunJobs();
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
        window = new MainWindow(host);
        window.Show(); Dispatcher.UIThread.RunJobs();
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
            var window = new MainWindow(host);
            window.Show(); Dispatcher.UIThread.RunJobs();
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
    public void QuitWithEmptyQueueOrOnlyActivateAsksNothingExtra()
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            var host = Host(new Files());
            var window = new MainWindow(host);
            window.Show(); Dispatcher.UIThread.RunJobs();
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
            var window = new MainWindow(host);
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
        var window = new MainWindow(host);
        window.Show(); Dispatcher.UIThread.RunJobs();
        var request = LaunchRequest.OpenProject(TestPath("lost.spandraft"));
        Assert.Equal(BrokerStatus.Retry, host.HandleBrokerRequest(request));
        Click(Assert.Single(window.OwnedWindows), Strings.Cancel); Dispatcher.UIThread.RunJobs();
        Assert.Equal(DesktopApplicationState.ShuttingDown, host.State);
        Assert.Equal(0, host.Activation.Count);
        // Nothing was acknowledged, so the Secondary still owns the request.
        Assert.Equal(BrokerStatus.Retry, host.HandleBrokerRequest(request));
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
            var window = new MainWindow(host);
            window.Show(); Dispatcher.UIThread.RunJobs();
            var request = LaunchRequest.Activate;
            var retry = BrokerClient.SendAsync(primary.Metadata, request, TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            while (!retry.IsCompleted) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10, TestContext.Current.CancellationToken); }
            Assert.Equal(BrokerStatus.Retry, await retry);
            Assert.Equal(0, host.Activation.Count);
            Assert.NotEqual(DesktopApplicationState.Ready, host.State);

            Click(Assert.Single(window.OwnedWindows), Strings.Restore); Dispatcher.UIThread.RunJobs();
            var accepted = BrokerClient.SendAsync(primary.Metadata, request, TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            while (!accepted.IsCompleted) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10, TestContext.Current.CancellationToken); }
            Assert.Equal(BrokerStatus.Accepted, await accepted);
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

            var dispatcher = new ActivationDispatcher();
            foreach (var request in requests) dispatcher.Enqueue(request);
            Assert.Equal(2, dispatcher.Count);
            Assert.Empty(MacActivationSource.ToRequests(new FileActivatedEventArgs([items[1]])));
        }
        finally { Directory.Delete(root, true); }
    }
}
