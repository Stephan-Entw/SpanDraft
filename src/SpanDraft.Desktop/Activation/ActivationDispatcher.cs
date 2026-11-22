namespace SpanDraft.Desktop.Activation;

/// <summary>
/// The Activation Queue: all sources enqueue the same LaunchRequest; a consumer processes them in order.
/// A request the consumer cannot take yet stays queued and is retried on the next pump.
/// </summary>
public sealed class ActivationDispatcher
{
    private readonly object _gate = new();
    private readonly Queue<LaunchRequest> _queue = new();
    private Func<LaunchRequest, Task<bool>>? _consumer;
    private bool _pumping, _repump;

    /// <summary>Raised on the enqueuing thread; subscribers marshal to their own thread and pump.</summary>
    public event Action? Enqueued;

    public int Count { get { lock (_gate) return _queue.Count; } }

    public void Enqueue(LaunchRequest request)
    {
        if (!request.IsValid) throw new ArgumentException("Invalid launch request.", nameof(request));
        lock (_gate) _queue.Enqueue(request);
        Enqueued?.Invoke();
    }

    /// <summary>The consumer returns false when it cannot process the request right now.</summary>
    public void SetConsumer(Func<LaunchRequest, Task<bool>>? consumer)
    {
        lock (_gate) _consumer = consumer;
    }

    public async Task PumpAsync()
    {
        lock (_gate)
        {
            if (_pumping) { _repump = true; return; }
            _pumping = true;
        }
        while (true)
        {
            LaunchRequest? request;
            Func<LaunchRequest, Task<bool>>? consumer;
            lock (_gate)
            {
                consumer = _consumer;
                request = consumer is not null && _queue.TryPeek(out var head) ? head : null;
                if (request is null) { _pumping = _repump = false; return; }
                _repump = false;
            }
            bool handled;
            try { handled = await consumer!(request); }
            catch { lock (_gate) { _queue.TryDequeue(out _); _pumping = _repump = false; } throw; }
            lock (_gate)
            {
                if (handled) _queue.TryDequeue(out _);
                else if (!_repump) { _pumping = false; return; }
            }
        }
    }
}
