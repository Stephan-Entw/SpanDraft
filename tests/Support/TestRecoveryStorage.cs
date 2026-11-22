using System.Collections.Concurrent;
using SpanDraft.Desktop.Persistence;

namespace SpanDraft.Tests;

/// <summary>An in-memory recovery storage with the failure and ordering hooks the recovery tests need.</summary>
internal sealed class TestRecoveryStorage : IRecoveryStorage
{
    public ConcurrentDictionary<RecoverySlot, byte[]> Data { get; } = new();
    public ConcurrentQueue<RecoverySlot> Writes { get; } = new();
    public ConcurrentQueue<string> Discarded { get; } = new();
    public Func<RecoverySlot, byte[], CancellationToken, Task>? BeforeWrite { get; set; }
    public Func<RecoverySlot, Task>? BeforeRead { get; set; }
    public Func<RecoverySlot, bool>? FailWrite { get; set; }
    public Func<RecoverySlot, bool>? FailDelete { get; set; }
    public Func<RecoverySlot, bool>? FailRead { get; set; }
    public Func<RecoverySlot, RecoverySlot?, bool>? FailMove { get; set; }
    public Func<RecoverySlot, bool>? FailDiscard { get; set; }
    public Exception? FailEnumerate { get; set; }
    /// <summary>The exception the injected failures throw; an <see cref="IOException"/> unless set.</summary>
    public Func<string, Exception>? FailureFactory { get; set; }
    private Exception Failure(string operation) => FailureFactory?.Invoke(operation) ?? new IOException($"Injected {operation} failure.");
    public bool HonorWriteCancellation { get; set; } = true;

    public IReadOnlyList<RecoverySlot> Slots(RecoverySlotKind? kind = null) =>
        Data.Keys.Where(s => kind is null || s.Kind == kind).ToArray();

    public RecoverySlot OnlySlot(RecoverySlotKind kind) => Slots(kind).Single();

    public Task<IReadOnlyList<RecoverySlot>> EnumerateAsync(CancellationToken cancellationToken = default)
    {
        if (FailEnumerate is { } error) throw error;
        return Task.FromResult((IReadOnlyList<RecoverySlot>)Data.Keys.ToArray());
    }

    public async Task<byte[]?> ReadAsync(RecoverySlot slot)
    {
        if (BeforeRead is not null) await BeforeRead(slot);
        if (FailRead?.Invoke(slot) == true) throw Failure("read");
        return Data.TryGetValue(slot, out var bytes) ? bytes.ToArray() : null;
    }

    public async Task WriteAtomicAsync(RecoverySlot slot, byte[] bytes, CancellationToken cancellationToken = default)
    {
        if (BeforeWrite is not null) await BeforeWrite(slot, bytes, cancellationToken);
        if (HonorWriteCancellation) cancellationToken.ThrowIfCancellationRequested();
        if (FailWrite?.Invoke(slot) == true) throw Failure("write");
        Data[slot] = bytes.ToArray();
        Writes.Enqueue(slot);
    }

    public Task DeleteAsync(RecoverySlot slot)
    {
        if (FailDelete?.Invoke(slot) == true) throw Failure("delete");
        Data.TryRemove(slot, out _);
        return Task.CompletedTask;
    }

    public Task MoveAsync(RecoverySlot source, RecoverySlot target)
    {
        if (FailMove?.Invoke(source, target) == true) throw Failure("move");
        if (!Data.TryGetValue(source, out var bytes)) throw new FileNotFoundException("Missing slot.", source.FileName);
        if (!Data.TryAdd(target, bytes)) throw new IOException("The target slot exists.");
        Data.TryRemove(source, out _);
        return Task.CompletedTask;
    }

    public Task DiscardAsync(RecoverySlot slot)
    {
        if (FailDiscard?.Invoke(slot) == true) throw Failure("discard");
        if (!Data.TryRemove(slot, out _)) throw new FileNotFoundException("Missing slot.", slot.FileName);
        Discarded.Enqueue(slot.FileName);
        return Task.CompletedTask;
    }

    public void Put(RecoverySlot slot, byte[] bytes) => Data[slot] = bytes.ToArray();
}
