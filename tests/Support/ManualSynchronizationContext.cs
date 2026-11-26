namespace SpanDraft.Tests;

/// <summary>
/// A synchronization context that only queues: continuations captured under it run when the test calls
/// <see cref="RunUntil"/>, so a test decides exactly what happens before an awaiting operation continues.
/// </summary>
internal sealed class ManualSynchronizationContext : SynchronizationContext
{
    private readonly Queue<(SendOrPostCallback Callback, object? State)> _queue = new();

    public override void Post(SendOrPostCallback d, object? state)
    {
        lock (_queue) _queue.Enqueue((d, state));
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

    /// <summary>Runs queued continuations until <paramref name="task"/> completed or nothing is queued any more.</summary>
    public void RunUntil(Task task)
    {
        while (!task.IsCompleted)
        {
            (SendOrPostCallback Callback, object? State) next;
            lock (_queue)
            {
                if (_queue.Count == 0) return;
                next = _queue.Dequeue();
            }
            Start(() => { next.Callback(next.State); return 0; });
        }
    }
}
