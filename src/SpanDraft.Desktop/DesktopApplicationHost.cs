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
    // State checks and enqueue of external requests are atomic against every lifecycle transition.
    private readonly object _gate = new();
    private LaunchRequest? _initialRequest;

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
        bool accepted;
        lock (_gate)
        {
            // Accepted means the request is fully queued before any later BeginQuit/BeginShutdown can run.
            accepted = _state == DesktopApplicationState.Ready;
            if (accepted) Activation.EnqueueSilently(request);
        }
        // Subscribers are notified outside the lifecycle gate.
        if (accepted) { Activation.NotifyEnqueued(); return BrokerStatus.Accepted; }
        RetryServed?.Invoke();
        return BrokerStatus.Retry;
    }

    public void AttachBroker(BrokerServer broker) => broker.Handler = HandleBrokerRequest;

    /// <summary>
    /// Entry for platform activations (macOS file/URI/reopen). They have no external owner that could redeliver
    /// them, so they are queued in every state before a committed shutdown, including legacy startup recovery.
    /// Returns false once the shutdown is committed; nothing is accepted then.
    /// </summary>
    public bool SubmitPlatformActivation(LaunchRequest request)
    {
        lock (_gate)
        {
            if (_state == DesktopApplicationState.ShuttingDown) return false;
            Activation.EnqueueSilently(request);
        }
        Activation.NotifyEnqueued();
        return true;
    }

    /// <summary>
    /// The startup's own request (command line or a Secondary that became Primary). It is held apart from the queue
    /// and joins it, ahead of everything else, only when the host actually becomes Ready; a cancelled startup drops it.
    /// </summary>
    public void SetInitialRequest(LaunchRequest request)
    {
        if (!request.IsValid) throw new ArgumentException("Invalid launch request.", nameof(request));
        if (request.Type == LaunchRequestType.Activate) return;
        lock (_gate)
        {
            if (_state != DesktopApplicationState.Starting) throw new InvalidOperationException("The initial request belongs to the startup.");
            _initialRequest = request;
        }
    }

    /// <summary>Global services are initialized and requests can be processed; only valid from Starting.</summary>
    public void MarkReady()
    {
        bool queued;
        lock (_gate)
        {
            queued = false;
            if (_state == DesktopApplicationState.Starting) queued = EnterReadyLocked();
            else if (_state == DesktopApplicationState.QuitInProgress) _stateBeforeQuit = DesktopApplicationState.Ready;
        }
        if (queued) Activation.NotifyEnqueued();
    }

    public void BeginQuit()
    {
        lock (_gate)
        {
            if (_state is DesktopApplicationState.Starting or DesktopApplicationState.Ready)
            {
                _stateBeforeQuit = _state;
                _state = DesktopApplicationState.QuitInProgress;
            }
        }
    }

    public void CancelQuit()
    {
        bool queued = false;
        lock (_gate)
        {
            if (_state != DesktopApplicationState.QuitInProgress) return;
            if (_stateBeforeQuit == DesktopApplicationState.Ready) queued = EnterReadyLocked();
            else _state = _stateBeforeQuit;
        }
        if (queued) Activation.NotifyEnqueued();
    }

    /// <summary>
    /// Commits the shutdown atomically against new activations: succeeds only if every waiting open request was
    /// explicitly confirmed for discarding. Then the queue and the initial request are dropped, no consumer remains
    /// and nothing is accepted afterwards. Fails without any change if an unconfirmed request is waiting.
    /// </summary>
    public bool TryCommitShutdown(IReadOnlySet<LaunchRequest> discardConfirmed)
    {
        lock (_gate)
        {
            if (Activation.PendingOpenRequests().Any(r => !discardConfirmed.Contains(r))) return false;
            Activation.Close();
            _initialRequest = null;
            _state = DesktopApplicationState.ShuttingDown;
            return true;
        }
    }

    public void BeginShutdown()
    {
        lock (_gate)
        {
            Activation.Close();
            _initialRequest = null;
            _state = DesktopApplicationState.ShuttingDown;
        }
    }

    // Queue insertion happens before the state change so no Accepted broker request can overtake the initial one.
    private bool EnterReadyLocked()
    {
        bool queued = false;
        if (_initialRequest is { } initial)
        {
            Activation.EnqueueFirstSilently(initial);
            _initialRequest = null;
            queued = true;
        }
        _state = DesktopApplicationState.Ready;
        return queued;
    }
}
