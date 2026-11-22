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

    private static DesktopApplicationHost Host(Files files, Delay? delay = null, bool withRecovery = false)
    {
        var recovery = new ProjectRecovery(files, ProjectRecovery.Local(files).SlotPath, (delay ?? new Delay()).Wait, () => Now);
        if (withRecovery) files.Data[recovery.SlotPath] = ProjectRecovery.Encode(new(State(), null, Now));
        return new(new DesktopStartupContext(files, new LocalSettingsStore(files, "settings"), UserSettings.Default)
        { StateStores = DesktopStateStores.Local(files) with { Recovery = recovery } });
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
    public void HostStaysStartingWhileRecoveryIsOpenAndBecomesReadyAfterRestore()
    {
        using var environment = new DesktopControlEnvironment();
        var delay = new Delay();
        var host = Host(new Files(), delay, withRecovery: true);
        var window = host.Workspaces.CreateInitial().Window;
        window.Show(); Dispatcher.UIThread.RunJobs();
        var dialog = Assert.Single(window.OwnedWindows);
        Assert.Equal(DesktopApplicationState.Starting, host.State);
        Click(dialog, Strings.Restore); Dispatcher.UIThread.RunJobs();
        Assert.Equal(DesktopApplicationState.Ready, host.State);
        typeof(MainWindow).GetField("_closeApproved", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(window, true);
        window.Close(); Dispatcher.UIThread.RunJobs();
        Assert.Equal(DesktopApplicationState.ShuttingDown, host.State);
    }

    [Fact]
    public void CancelledRecoveryEndsInShuttingDownWithoutEverBeingReady()
    {
        using var environment = new DesktopControlEnvironment();
        var host = Host(new Files(), withRecovery: true);
        var window = host.Workspaces.CreateInitial().Window;
        window.Show(); Dispatcher.UIThread.RunJobs();
        Click(Assert.Single(window.OwnedWindows), Strings.Cancel); Dispatcher.UIThread.RunJobs();
        Assert.False(window.IsVisible);
        Assert.Equal(DesktopApplicationState.ShuttingDown, host.State);
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
        var host = Host(new Files(), withRecovery: true);
        var window = host.Workspaces.CreateInitial().Window;
        window.Show(); Dispatcher.UIThread.RunJobs();
        Click(Assert.Single(window.OwnedWindows), Strings.Restore); Dispatcher.UIThread.RunJobs();
        Assert.Equal(DesktopApplicationState.Ready, host.State);

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
    public void RestoringTheRecoveryMakesTheHostReadyWithoutReadingTheSharedServicesAgain()
    {
        using var environment = new DesktopControlEnvironment();
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new AvaloniaSynchronizationContext());
        try
        {
            var files = new Files();
            var host = Host(files, withRecovery: true);
            // Libraries and recents are shared services, loaded during bootstrap before any window exists.
            TestHosts.Initialized(host);
            var recents = RecentProjectsStore.Local(files).FilePath;
            int recentReads = 0;
            files.BeforeRead = p => { if (p == recents) recentReads++; return Task.CompletedTask; };
            var window = host.Workspaces.CreateInitial().Window;
            window.Show(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(DesktopApplicationState.Starting, host.State);

            // The cancelled quit during the open recovery restores Starting; Ready follows only with the restore.
            host.BeginQuit(); Assert.Equal(DesktopApplicationState.QuitInProgress, host.State);
            host.CancelQuit(); Assert.Equal(DesktopApplicationState.Starting, host.State);

            Click(Assert.Single(window.OwnedWindows), Strings.Restore); Dispatcher.UIThread.RunJobs();
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
        var host = new DesktopApplicationHost(new DesktopStartupContext(files, new LocalSettingsStore(files, settingsPath), UserSettings.Default)
        { StateStores = DesktopStateStores.Local(files) });
        var reads = new List<string>();
        files.BeforeRead = p => { reads.Add(p); return Task.CompletedTask; };
        // The bootstrap (here the test) loads the shared services from the host's stores; the window only adapts them.
        TestHosts.Initialized(host);
        var window = host.Workspaces.CreateInitial().Window;
        DesktopControlEnvironment.ShowInitialized(window);
        Assert.Contains(MaterialLibraryStore.Local(files).FilePath, reads);
        Assert.Contains(RecentProjectsStore.Local(files).FilePath, reads);
        Assert.Contains(ProjectRecovery.Local(files).SlotPath, reads);

        var main = Assert.IsType<MainWindowViewModel>(window.DataContext);
        var apply = main.ApplySettingsAsync(SettingsPatch.From(UserSettings.Default with { SignificantDigits = 5 }));
        Dispatcher.UIThread.RunJobs();
        Assert.True(apply.IsCompletedSuccessfully);
        Assert.Contains(settingsPath, files.Writes);
        window.Close(); Dispatcher.UIThread.RunJobs();
    }
}
