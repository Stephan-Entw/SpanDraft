using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Presentation;
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
}
