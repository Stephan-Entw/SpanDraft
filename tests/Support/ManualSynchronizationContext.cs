namespace SpanDraft.Tests;

/// <summary>
/// A synchronization context that only queues: continuations captured under it run when the test calls
/// <see cref="RunUntil"/>, so a test decides exactly what happens before an awaiting operation continues.
/// </summary>
internal sealed class ManualSynchronizationContext : SynchronizationContext
{
    // Bounds a test that would otherwise hang; a signal (a post or the completion) ends every wait at once.
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(30);
    private readonly Queue<(SendOrPostCallback Callback, object? State)> _queue = new();

    public override void Post(SendOrPostCallback d, object? state)
    {
        lock (_queue)
        {
            _queue.Enqueue((d, state));
            Monitor.PulseAll(_queue);
        }
    }

    public override SynchronizationContext CreateCopy() => this;

    /// <summary>Starts <paramref name="operation"/> under this context; it runs synchronously up to its first queued continuation.</summary>
    public T Start<T>(Func<T> operation)
    {
        var previous = Current;
        SetSynchronizationContext(this);
        try { return operation(); }
        finally { SetSynchronizationContext(previous); }
    }

    /// <summary>
    /// Runs queued continuations, one complete callback at a time, until <paramref name="signal"/> completed. An empty queue
    /// does not end it: a continuation may still be posted from another thread, so it waits for the next post or for the
    /// completion of <paramref name="signal"/>. To stop where an operation is parked, pass a checkpoint it reaches there.
    /// </summary>
    public void RunUntil(Task signal)
    {
        signal.ContinueWith(_ => { lock (_queue) Monitor.PulseAll(_queue); }, CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        while (true)
        {
            (SendOrPostCallback Callback, object? State) next;
            lock (_queue)
            {
                while (!signal.IsCompleted && _queue.Count == 0)
                    if (!Monitor.Wait(_queue, Guard)) throw new TimeoutException("Nothing was posted and the signal did not complete.");
                if (signal.IsCompleted) return;
                next = _queue.Dequeue();
            }
            Start(() => { next.Callback(next.State); return 0; });
        }
    }
}
