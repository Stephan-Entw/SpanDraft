using System.Text.Json;
using SpanDraft.Desktop.State;

namespace SpanDraft.Desktop.Persistence;

/// <summary>The result of a deliberate discard: the slot was moved to the Discarded area, or it did not exist.</summary>
internal enum SlotDiscardOutcome { Moved, Missing }

/// <summary>
/// A single private file slot written after a debounce. Generations invalidate stale work; the gate orders publication and
/// deletion, so a write that was superseded or deleted can never reappear.
/// </summary>
internal sealed class DebouncedSlot<TSnapshot>
{
    private readonly IRecoveryStorage _storage;
    private readonly Func<TSnapshot, byte[]> _encode;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Func<Task>? _beforeDelete;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _cancellation;
    private long _generation;
    private Task _pending = Task.CompletedTask;
    // Completes once every explicit flush requested so far has finished (successfully or not).
    private Task _flushes = Task.CompletedTask;

    public DebouncedSlot(IRecoveryStorage storage, RecoverySlot slot, Func<TSnapshot, byte[]> encode,
        Func<TimeSpan, CancellationToken, Task>? delay, Func<Task>? beforeDelete = null, bool published = false)
    {
        _storage = storage;
        Slot = slot;
        _encode = encode;
        _delay = delay ?? Task.Delay;
        _beforeDelete = beforeDelete;
        IsPublished = published;
    }

    public RecoverySlot Slot { get; }

    /// <summary>
    /// Whether this slot currently holds published content, as far as this writer knows: set by a successful write (and for
    /// a slot adopted from disk), cleared by a successful delete or discard. A failed operation never changes it, since
    /// every storage operation is atomic.
    /// </summary>
    public bool IsPublished { get; private set; }
    public event Action<Exception>? Failed;

    /// <summary>Writes the snapshot after the debounce unless a later call supersedes it.</summary>
    public void Schedule(TSnapshot snapshot)
    {
        var (generation, token) = Invalidate();
        _pending = ObserveAsync(DebounceAsync(snapshot, generation, token));
    }

    /// <summary>Deletes the slot as soon as the gate is free, unless a later call supersedes it.</summary>
    public void ScheduleDelete()
    {
        var (generation, _) = Invalidate();
        _pending = ObserveAsync(DeleteGenerationAsync(generation));
    }

    /// <summary>
    /// Publishes the snapshot now. A flush is an explicit request, so a later call neither cancels it nor turns it into
    /// a no-op; the writes of later generations follow it through the gate.
    /// </summary>
    public Task FlushAsync(TSnapshot snapshot, Func<Task>? afterPublish = null)
    {
        var (generation, _) = Invalidate();
        // Gate acquisition also drains any write which was already publishing.
        var flush = WriteGenerationAsync(snapshot, generation, CancellationToken.None, supersedable: false, afterPublish);
        _flushes = Task.WhenAll(_flushes, flush).ContinueWith(static _ => { }, CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return flush;
    }

    /// <summary>
    /// Deliberately discards the slot: moves it to the Discarded area instead of deleting it. Pending debounced work is
    /// invalidated (it can never publish afterwards); every explicit flush requested before this call is still completed in
    /// order and only then is the slot moved, so no older publication can recreate it. Flushes requested afterwards (a
    /// rollback that restores the protection) publish normally. A missing slot is <see cref="SlotDiscardOutcome.Missing"/>;
    /// every other failure is thrown and leaves the slot as it was.
    /// </summary>
    public async Task<SlotDiscardOutcome> DiscardAsync()
    {
        Invalidate();
        // Discard-only ordering rule: explicit flushes are never cancelled, so they are awaited, not overtaken.
        await _flushes;
        await _gate.WaitAsync();
        try
        {
            try { await _storage.DiscardAsync(Slot); }
            catch (Exception e) when (RecoveryErrors.IsMissing(e))
            {
                IsPublished = false;
                return SlotDiscardOutcome.Missing;
            }
            IsPublished = false;
            return SlotDiscardOutcome.Moved;
        }
        finally { _gate.Release(); }
    }

    public async Task DeleteAsync(Action? onDeleted = null)
    {
        var (generation, _) = Invalidate();
        await DeleteGenerationAsync(generation, onDeleted);
    }

    public Task DrainAsync() => _pending;

    private (long, CancellationToken) Invalidate()
    {
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = new();
        return (++_generation, _cancellation.Token);
    }

    private async Task ObserveAsync(Task task)
    {
        try { await task; }
        catch (OperationCanceledException) { }
        catch (Exception e) when (RecoveryErrors.IsStorage(e) || e is ProjectFormatException or JsonException)
        { Failed?.Invoke(e); }
    }

    private async Task DebounceAsync(TSnapshot snapshot, long generation, CancellationToken token)
    {
        await _delay(TimeSpan.FromSeconds(1), token);
        await WriteGenerationAsync(snapshot, generation, token, supersedable: true);
    }

    private async Task WriteGenerationAsync(TSnapshot snapshot, long generation, CancellationToken token, bool supersedable,
        Func<Task>? afterPublish = null)
    {
        await _gate.WaitAsync(token);
        try
        {
            if (supersedable && generation != _generation) return;
            token.ThrowIfCancellationRequested();
            await _storage.WriteAtomicAsync(Slot, _encode(snapshot), token);
            IsPublished = true;
            if (afterPublish is not null) await afterPublish();
        }
        finally { _gate.Release(); }
    }

    private async Task DeleteGenerationAsync(long generation, Action? onDeleted = null)
    {
        await _gate.WaitAsync();
        try
        {
            if (generation != _generation) return;
            if (_beforeDelete is not null) await _beforeDelete();
            // Companion cleanup may have yielded to an edit that still needs this slot.
            if (generation != _generation) return;
            await _storage.DeleteAsync(Slot);
            IsPublished = false;
            if (generation == _generation) onDeleted?.Invoke();
        }
        finally { _gate.Release(); }
    }
}
