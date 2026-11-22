namespace SpanDraft.Desktop.Activation;

/// <summary>
/// The Activation Queue: all sources enqueue the same LaunchRequest; a consumer processes them in order.
/// A request the consumer cannot take yet stays queued and is retried on the next pump.
/// </summary>
public sealed class ActivationDispatcher
{
    private readonly object _gate = new();
    private readonly LinkedList<LaunchRequest> _queue = new();
    private Func<LaunchRequest, Task<bool>>? _consumer;
    private bool _pumping, _repump;

    /// <summary>Raised on the enqueuing thread; subscribers marshal to their own thread and pump.</summary>
    public event Action? Enqueued;

    public int Count { get { lock (_gate) return _queue.Count; } }

    /// <summary>Confirmed requests that still wait and would open something (Activate-only requests are not listed).</summary>
    public IReadOnlyList<LaunchRequest> PendingOpenRequests()
    {
        lock (_gate) return _queue.Where(static r => r.Type != LaunchRequestType.Activate).ToList();
    }

    public void Enqueue(LaunchRequest request)
    {
        EnqueueSilently(request);
        NotifyEnqueued();
    }

    /// <summary>Queues without raising <see cref="Enqueued"/>, so callers holding their own lock can notify after releasing it.</summary>
    internal void EnqueueSilently(LaunchRequest request)
    {
        Validate(request);
        lock (_gate) _queue.AddLast(request);
    }

    /// <summary>Puts a request ahead of everything already waiting, without notification.</summary>
    internal void EnqueueFirstSilently(LaunchRequest request)
    {
        Validate(request);
        lock (_gate) _queue.AddFirst(request);
    }

    internal void NotifyEnqueued() => Enqueued?.Invoke();

    /// <summary>Drops every waiting request and the consumer; used once a shutdown is committed.</summary>
    internal void Close()
    {
        lock (_gate)
        {
            _queue.Clear();
            _consumer = null;
        }
    }

    private static void Validate(LaunchRequest request)
    {
        if (!request.IsValid) throw new ArgumentException("Invalid launch request.", nameof(request));
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
                request = consumer is not null ? _queue.First?.Value : null;
                if (request is null) { _pumping = _repump = false; return; }
                _repump = false;
            }
            bool handled;
            try { handled = await consumer!(request); }
            catch { lock (_gate) { Remove(request); _pumping = _repump = false; } throw; }
            lock (_gate)
            {
                if (handled) Remove(request);
                else if (!_repump) { _pumping = false; return; }
            }
        }
    }

    // The head may have changed while the consumer ran (EnqueueFirst or Close), so remove this exact instance.
    private void Remove(LaunchRequest request)
    {
        for (var node = _queue.First; node is not null; node = node.Next)
            if (ReferenceEquals(node.Value, request)) { _queue.Remove(node); return; }
    }
}
