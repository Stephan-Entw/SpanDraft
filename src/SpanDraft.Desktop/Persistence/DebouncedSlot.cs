using System.Text.Json;
using SpanDraft.Desktop.State;

namespace SpanDraft.Desktop.Persistence;

/// <summary>
/// A single private file slot written after a debounce. Generations invalidate stale work; the gate orders publication and
/// deletion, so a write that was superseded or deleted can never reappear.
/// </summary>
internal sealed class DebouncedSlot<TSnapshot>
{
    private readonly IRecoveryStorage _storage;
    private readonly Func<TSnapshot, byte[]> _encode;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _cancellation;
    private long _generation;
    private Task _pending = Task.CompletedTask;

    public DebouncedSlot(IRecoveryStorage storage, RecoverySlot slot, Func<TSnapshot, byte[]> encode,
        Func<TimeSpan, CancellationToken, Task>? delay)
    {
        _storage = storage;
        Slot = slot;
        _encode = encode;
        _delay = delay ?? Task.Delay;
    }

    public RecoverySlot Slot { get; }
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
    public async Task FlushAsync(TSnapshot snapshot)
    {
        var (generation, _) = Invalidate();
        // Gate acquisition also drains any write which was already publishing.
        await WriteGenerationAsync(snapshot, generation, CancellationToken.None, supersedable: false);
    }

    public async Task DeleteAsync()
    {
        var (generation, _) = Invalidate();
        await DeleteGenerationAsync(generation);
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
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ProjectFormatException or JsonException)
        { Failed?.Invoke(e); }
    }

    private async Task DebounceAsync(TSnapshot snapshot, long generation, CancellationToken token)
    {
        await _delay(TimeSpan.FromSeconds(1), token);
        await WriteGenerationAsync(snapshot, generation, token, supersedable: true);
    }

    private async Task WriteGenerationAsync(TSnapshot snapshot, long generation, CancellationToken token, bool supersedable)
    {
        await _gate.WaitAsync(token);
        try
        {
            if (supersedable && generation != _generation) return;
            token.ThrowIfCancellationRequested();
            await _storage.WriteAtomicAsync(Slot, _encode(snapshot), token);
        }
        finally { _gate.Release(); }
    }

    private async Task DeleteGenerationAsync(long generation)
    {
        await _gate.WaitAsync();
        try { if (generation == _generation) await _storage.DeleteAsync(Slot); }
        finally { _gate.Release(); }
    }
}
