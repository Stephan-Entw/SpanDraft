using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Activation;
using Xunit;

namespace SpanDraft.Desktop.Tests;

public sealed class DesktopApplicationHostTests
{
    [Fact]
    public void LifecycleStartsStartingAndFollowsTransitions()
    {
        var host = new DesktopApplicationHost();
        Assert.Equal(DesktopApplicationState.Starting, host.State);
        host.MarkReady();
        Assert.Equal(DesktopApplicationState.Ready, host.State);
        host.BeginQuit();
        Assert.Equal(DesktopApplicationState.QuitInProgress, host.State);
        host.MarkReady();
        Assert.Equal(DesktopApplicationState.QuitInProgress, host.State);
        host.CancelQuit();
        Assert.Equal(DesktopApplicationState.Ready, host.State);
        host.BeginShutdown();
        host.BeginQuit();
        Assert.Equal(DesktopApplicationState.ShuttingDown, host.State);
    }

    [Fact]
    public void CancelQuitRestoresStartingAndKeepsMarkReadyDuringQuit()
    {
        var host = new DesktopApplicationHost();
        host.BeginQuit();
        host.CancelQuit();
        Assert.Equal(DesktopApplicationState.Starting, host.State);

        host.BeginQuit();
        host.MarkReady();
        Assert.Equal(DesktopApplicationState.QuitInProgress, host.State);
        host.CancelQuit();
        Assert.Equal(DesktopApplicationState.Ready, host.State);
    }

    [Fact]
    public void HostExposesInjectedStartupServices()
    {
        var files = new ProjectFileStore();
        var settings = new LocalSettingsStore(files, "settings");
        var stores = DesktopStateStores.Local(files);
        var host = new DesktopApplicationHost(new DesktopStartupContext(files, settings, UserSettings.Default) { StateStores = stores });
        Assert.Same(files, host.Files);
        Assert.Same(settings, host.SettingsStore);
        Assert.Same(stores, host.StateStores);
    }

    private static LaunchRequest Open(string name) => LaunchRequest.OpenProject(Path.Combine(Path.GetTempPath(), name));

    [Fact]
    public void CommittedShutdownDropsConfirmedRequestsAndAcceptsNothingAfterwards()
    {
        var host = new DesktopApplicationHost();
        var confirmed = Open("confirmed.spandraft");
        Assert.True(host.SubmitPlatformActivation(confirmed));
        Assert.True(host.SubmitPlatformActivation(LaunchRequest.Activate));
        bool consumed = false;
        host.Activation.SetConsumer(_ => { consumed = true; return Task.FromResult(true); });

        Assert.True(host.TryCommitShutdown(new HashSet<LaunchRequest>(ReferenceEqualityComparer.Instance) { confirmed }));
        Assert.Equal(DesktopApplicationState.ShuttingDown, host.State);
        Assert.Equal(0, host.Activation.Count);
        Assert.False(host.SubmitPlatformActivation(Open("late.spandraft")));
        Assert.Equal(BrokerStatus.Retry, host.HandleBrokerRequest(Open("late.spandraft")));
        Assert.Equal(0, host.Activation.Count);
        Assert.True(host.Activation.PumpAsync().IsCompletedSuccessfully);
        Assert.False(consumed);
    }

    [Fact]
    public void ShutdownCommitFailsWithoutSideEffectsWhileAnUnconfirmedOpenRequestWaits()
    {
        var host = new DesktopApplicationHost();
        var confirmed = Open("a.spandraft");
        host.SubmitPlatformActivation(confirmed);
        host.SubmitPlatformActivation(LaunchRequest.OpenUri("spandraft://open/late"));
        Assert.False(host.TryCommitShutdown(new HashSet<LaunchRequest>(ReferenceEqualityComparer.Instance) { confirmed }));
        Assert.Equal(DesktopApplicationState.Starting, host.State);
        Assert.Equal(2, host.Activation.Count);
        Assert.True(host.SubmitPlatformActivation(Open("b.spandraft")));
    }

    [Fact]
    public void InitialRequestJoinsTheQueueFirstOnlyWhenReadyIsReallyEntered()
    {
        var host = new DesktopApplicationHost();
        var initial = Open("initial.spandraft");
        host.SetInitialRequest(initial);
        var platform = Open("platform.spandraft");
        host.SubmitPlatformActivation(platform);
        Assert.Equal(1, host.Activation.Count);

        // MarkReady during a pending quit only remembers Ready; the initial request stays held.
        host.BeginQuit();
        host.MarkReady();
        Assert.Equal(1, host.Activation.Count);
        host.CancelQuit();
        Assert.Equal(DesktopApplicationState.Ready, host.State);
        Assert.Equal([initial, platform], host.Activation.PendingOpenRequests());

        // A startup that ends before Ready drops its own request.
        var cancelled = new DesktopApplicationHost();
        cancelled.SetInitialRequest(initial);
        Assert.True(cancelled.TryCommitShutdown(new HashSet<LaunchRequest>()));
        cancelled.MarkReady();
        Assert.Equal(0, cancelled.Activation.Count);
    }

    [Fact]
    public void EnqueuedIsNeverRaisedWhileTheLifecycleGateIsHeld()
    {
        var host = new DesktopApplicationHost();
        host.SetInitialRequest(Open("initial.spandraft"));
        var gate = typeof(DesktopApplicationHost).GetField("_gate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(host)!;
        var heldDuringNotification = new List<bool>();
        host.Activation.Enqueued += () =>
        {
            // Another thread can take the gate only if the notifying thread does not hold it.
            heldDuringNotification.Add(!Task.Run(() =>
            {
                bool taken = Monitor.TryEnter(gate, TimeSpan.FromSeconds(1));
                if (taken) Monitor.Exit(gate);
                return taken;
            }).GetAwaiter().GetResult());
        };
        host.SubmitPlatformActivation(Open("platform.spandraft"));
        host.MarkReady();
        host.HandleBrokerRequest(Open("broker.spandraft"));
        host.BeginQuit();
        host.CancelQuit();
        Assert.Equal([false, false, false], heldDuringNotification);
    }
}
