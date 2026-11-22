using SpanDraft.Desktop.Activation;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Presentation;

namespace SpanDraft.Desktop;

public enum DesktopApplicationState { Starting, Ready, QuitInProgress, ShuttingDown }

/// <summary>Owns the application-global services and lifecycle state; windows receive it by constructor injection.</summary>
public sealed class DesktopApplicationHost
{
    private volatile DesktopApplicationState _state = DesktopApplicationState.Starting;
    private volatile DesktopApplicationState _stateBeforeQuit;

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
    public ActivationDispatcher Activation { get; } = new();

    /// <summary>Raised when an external request was answered with Retry, so a window can surface itself best-effort.</summary>
    public event Action? RetryServed;

    /// <summary>
    /// Broker decision: only a Ready host owns requests (Accepted); otherwise the Secondary keeps ownership.
    /// Legacy startup recovery keeps the host Starting, so no request is lost by cancelling it.
    /// </summary>
    public BrokerStatus HandleBrokerRequest(LaunchRequest request)
    {
        if (_state != DesktopApplicationState.Ready)
        {
            RetryServed?.Invoke();
            return BrokerStatus.Retry;
        }
        Activation.Enqueue(request);
        return BrokerStatus.Accepted;
    }

    public void AttachBroker(BrokerServer broker) => broker.Handler = HandleBrokerRequest;

    /// <summary>Global services are initialized and requests can be processed; only valid from Starting.</summary>
    public void MarkReady()
    {
        if (_state == DesktopApplicationState.Starting) _state = DesktopApplicationState.Ready;
        else if (_state == DesktopApplicationState.QuitInProgress) _stateBeforeQuit = DesktopApplicationState.Ready;
    }

    public void BeginQuit()
    {
        if (_state is DesktopApplicationState.Starting or DesktopApplicationState.Ready)
        {
            _stateBeforeQuit = _state;
            _state = DesktopApplicationState.QuitInProgress;
        }
    }

    public void CancelQuit()
    {
        if (_state == DesktopApplicationState.QuitInProgress) _state = _stateBeforeQuit;
    }

    public void BeginShutdown() => _state = DesktopApplicationState.ShuttingDown;
}
