using System.Collections.Concurrent;

namespace SpanDraft.Desktop.Persistence;

/// <summary>A storage without any file system access, for tests, harnesses and isolated environments.</summary>
public sealed class InMemoryRecoveryStorage : IRecoveryStorage
{
    private readonly ConcurrentDictionary<RecoverySlot, byte[]> _slots = new();
    private readonly ConcurrentQueue<(string Name, byte[] Bytes)> _discarded = new();
    private readonly object _gate = new();

    public IReadOnlyCollection<RecoverySlot> Slots => _slots.Keys.ToArray();
    public IReadOnlyList<string> DiscardedNames => _discarded.Select(d => d.Name).ToArray();
    public byte[]? Peek(RecoverySlot slot) => _slots.TryGetValue(slot, out var bytes) ? bytes.ToArray() : null;
    public void Put(RecoverySlot slot, byte[] bytes) => _slots[slot] = bytes.ToArray();

    public Task<IReadOnlyList<RecoverySlot>> EnumerateAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult((IReadOnlyList<RecoverySlot>)_slots.Keys.ToArray());

    public Task<byte[]?> ReadAsync(RecoverySlot slot) => Task.FromResult(Peek(slot));

    public Task WriteAtomicAsync(RecoverySlot slot, byte[] bytes, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Put(slot, bytes);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(RecoverySlot slot)
    {
        _slots.TryRemove(slot, out _);
        return Task.CompletedTask;
    }

    public Task MoveAsync(RecoverySlot source, RecoverySlot target)
    {
        lock (_gate)
        {
            if (!_slots.TryGetValue(source, out var bytes)) throw new FileNotFoundException("The recovery slot does not exist.", source.FileName);
            if (_slots.ContainsKey(target)) throw new IOException("The target recovery slot already exists.");
            _slots[target] = bytes;
            _slots.TryRemove(source, out _);
        }
        return Task.CompletedTask;
    }

    public Task DiscardAsync(RecoverySlot slot)
    {
        lock (_gate)
        {
            if (!_slots.TryRemove(slot, out var bytes)) throw new RecoverySlotMissingException(slot.FileName);
            string name = slot.IsLegacy ? "legacy-" + Path.GetFileNameWithoutExtension(slot.FileName) + "-" + Guid.NewGuid().ToString("N") + ".json" : slot.FileName;
            _discarded.Enqueue((name, bytes));
        }
        return Task.CompletedTask;
    }
}
