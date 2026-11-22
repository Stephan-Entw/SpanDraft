using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Presentation;

namespace SpanDraft.Desktop;

public enum DesktopApplicationState { Starting, Ready, QuitInProgress, ShuttingDown }

/// <summary>Owns the application-global services and lifecycle state; windows receive it by constructor injection.</summary>
public sealed class DesktopApplicationHost
{
    private DesktopApplicationState _state = DesktopApplicationState.Starting;

    public DesktopApplicationHost(DesktopStartupContext? startup = null)
    {
        Files = startup?.Files ?? new ProjectFileStore();
        SettingsStore = startup?.SettingsStore ?? LocalSettingsStore.Local(Files);
        Settings = startup?.Settings ?? UserSettings.Default;
        StateStores = startup?.StateStores ?? DesktopStateStores.Local(Files);
        UserNameProvider = startup?.UserNameProvider ?? (static () => OperatingSystem.IsWindows() ? Environment.UserName : null);
    }

    public IProjectFileStore Files { get; }
    public LocalSettingsStore SettingsStore { get; }
    public UserSettings Settings { get; }
    public DesktopStateStores StateStores { get; }
    public Func<string?> UserNameProvider { get; }
    public DesktopApplicationState State => _state;

    /// <summary>Global services are initialized and requests can be processed; only valid from Starting.</summary>
    public void MarkReady()
    {
        if (_state == DesktopApplicationState.Starting) _state = DesktopApplicationState.Ready;
    }

    public void BeginQuit()
    {
        if (_state is DesktopApplicationState.Starting or DesktopApplicationState.Ready)
            _state = DesktopApplicationState.QuitInProgress;
    }

    public void CancelQuit()
    {
        if (_state == DesktopApplicationState.QuitInProgress) _state = DesktopApplicationState.Ready;
    }

    public void BeginShutdown() => _state = DesktopApplicationState.ShuttingDown;
}
