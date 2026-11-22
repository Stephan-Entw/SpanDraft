using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SpanDraft.Desktop;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Services;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

[Collection("Avalonia")]
public sealed class DesktopWindowLifecycleTests
{
    private static void Click(Window dialog, string label) => dialog.GetVisualDescendants().OfType<Button>()
        .Single(b => Equals(b.Content, label)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static DesktopApplicationHost Host(Files files, TestRecoveryStorage? recovery = null) =>
        new(new DesktopStartupContext(files, new LocalSettingsStore(files, "settings"), UserSettings.Default)
        { StateStores = DesktopStateStores.Local(files) with { Recovery = recovery ?? new TestRecoveryStorage() } });

    private static TestRecoveryStorage WithRecovery()
    {
        var storage = new TestRecoveryStorage();
        storage.Put(RecoverySlot.Project(RecoveryId.New()), ProjectRecovery.Encode(new(State(), null, Now)));
        return storage;
    }

    [Fact]
    public void WithoutRecoveryTheHostIsReadyAfterInitializationAndShutsDownOnClose()
    {
        using var environment = new DesktopControlEnvironment();
        var host = Host(new Files());
        var window = host.Workspaces.CreateInitial().Window;
        Assert.Equal(DesktopApplicationState.Starting, host.State);
        DesktopControlEnvironment.ShowInitialized(window);
        Assert.Equal(DesktopApplicationState.Ready, host.State);
        window.Close(); Dispatcher.UIThread.RunJobs();
        Assert.Equal(DesktopApplicationState.ShuttingDown, host.State);
    }

    [Fact]
    public void PendingRecoveryNeverKeepsTheHostStarting()
    {
        using var environment = new DesktopControlEnvironment();
        var host = Host(new Files(), WithRecovery());
        var window = host.Workspaces.CreateInitial().Window;
        Assert.Equal(DesktopApplicationState.Starting, host.State);
        DesktopControlEnvironment.ShowInitialized(window);
        Assert.Empty(window.OwnedWindows);
        Assert.Equal(DesktopApplicationState.Ready, host.State);
        Assert.True(host.Recovery.HasUndecided);
        window.Close(); Dispatcher.UIThread.RunJobs();
        Assert.Equal(DesktopApplicationState.ShuttingDown, host.State);
    }

    [Fact]
    public void QuittingTheUnreadableRecoveryQuestionEndsInShuttingDownWithoutEverBeingReady()
    {
        using var environment = new DesktopControlEnvironment();
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new AvaloniaSynchronizationContext());
        try
        {
            var host = Host(new Files(), new TestRecoveryStorage { FailEnumerate = new IOException("Injected enumeration failure.") });
            var window = host.Workspaces.CreateInitial().Window;
            window.Show(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(DesktopApplicationState.Starting, host.State);
            Click(Assert.Single(window.OwnedWindows), Strings.Quit); Dispatcher.UIThread.RunJobs();
            Assert.False(window.IsVisible);
            Assert.Equal(DesktopApplicationState.ShuttingDown, host.State);
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    [Fact]
    public void QuitReturnsToReadyWhenCancelledAndShutsDownWhenCompleted()
    {
        using var environment = new DesktopControlEnvironment();
        // Leave dialogs resume on the UI thread in production through the dispatcher's synchronization context.
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new AvaloniaSynchronizationContext());
        try { QuitCore(); } finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    private static void QuitCore()
    {
        var host = Host(new Files(), WithRecovery());
        var window = host.Workspaces.CreateInitial().Window;
        DesktopControlEnvironment.ShowInitialized(window);
        Assert.Equal(DesktopApplicationState.Ready, host.State);
        // A restored, hence unsaved session makes the quit ask the leave question.
        var main = Assert.IsType<MainWindowViewModel>(window.DataContext);
        main.RestoreProject(new(State(), null, Now), RecoverySlot.Project(RecoveryId.New()), null);
        Dispatcher.UIThread.RunJobs();

        var quit = window.FindControl<MenuItem>("QuitMenuItem")!.Command!;
        quit.Execute(null); Dispatcher.UIThread.RunJobs();
        Assert.Equal(DesktopApplicationState.QuitInProgress, host.State);
        Click(Assert.Single(window.OwnedWindows), Strings.Cancel); Dispatcher.UIThread.RunJobs();
        Assert.Equal(DesktopApplicationState.Ready, host.State);
        Assert.True(window.IsVisible);

        quit.Execute(null); Dispatcher.UIThread.RunJobs();
        Click(Assert.Single(window.OwnedWindows), Strings.DontSave); Dispatcher.UIThread.RunJobs();
        Assert.False(window.IsVisible);
        Assert.Equal(DesktopApplicationState.ShuttingDown, host.State);
    }

    [Fact]
    public void BecomingReadyDoesNotReadTheSharedServicesAgain()
    {
        using var environment = new DesktopControlEnvironment();
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new AvaloniaSynchronizationContext());
        try
        {
            var files = new Files();
            var host = Host(files, WithRecovery());
            // Libraries and recents are shared services, loaded during bootstrap before any window exists.
            TestHosts.Initialized(host);
            var recents = RecentProjectsStore.Local(files).FilePath;
            int recentReads = 0;
            files.BeforeRead = p => { if (p == recents) recentReads++; return Task.CompletedTask; };
            var window = host.Workspaces.CreateInitial().Window;

            // A cancelled quit while the host is still starting returns to Starting; Ready follows with the window's startup.
            host.BeginQuit(); Assert.Equal(DesktopApplicationState.QuitInProgress, host.State);
            host.CancelQuit(); Assert.Equal(DesktopApplicationState.Starting, host.State);

            DesktopControlEnvironment.ShowInitialized(window);
            Assert.Equal(DesktopApplicationState.Ready, host.State);
            Assert.Equal(0, recentReads);
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    [Fact]
    public void WindowUsesTheStoresOfTheHostInsteadOfCreatingItsOwn()
    {
        using var environment = new DesktopControlEnvironment();
        var files = new Files();
        const string settingsPath = "custom-host-settings.json";
        var recoveryStorage = WithRecovery();
        bool recoveryRead = false;
        recoveryStorage.BeforeRead = _ => { recoveryRead = true; return Task.CompletedTask; };
        var host = new DesktopApplicationHost(new DesktopStartupContext(files, new LocalSettingsStore(files, settingsPath), UserSettings.Default)
        { StateStores = DesktopStateStores.Local(files) with { Recovery = recoveryStorage } });
        var reads = new List<string>();
        files.BeforeRead = p => { reads.Add(p); return Task.CompletedTask; };
        // The bootstrap (here the test) loads the shared services from the host's stores; the window only adapts them.
        TestHosts.Initialized(host);
        var window = host.Workspaces.CreateInitial().Window;
        DesktopControlEnvironment.ShowInitialized(window);
        Assert.Contains(MaterialLibraryStore.Local(files).FilePath, reads);
        Assert.Contains(RecentProjectsStore.Local(files).FilePath, reads);
        Assert.True(recoveryRead);

        var main = Assert.IsType<MainWindowViewModel>(window.DataContext);
        var apply = main.ApplySettingsAsync(SettingsPatch.From(UserSettings.Default with { SignificantDigits = 5 }));
        Dispatcher.UIThread.RunJobs();
        Assert.True(apply.IsCompletedSuccessfully);
        Assert.Contains(settingsPath, files.Writes);
        window.Close(); Dispatcher.UIThread.RunJobs();
    }
}
