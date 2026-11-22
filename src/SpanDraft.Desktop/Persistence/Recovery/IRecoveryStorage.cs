namespace SpanDraft.Desktop.Persistence;

/// <summary>
/// The one storage boundary of recovery: enumerate, read, publish atomically, delete and move slots inside the
/// recovery area of the local data directory. Production uses <see cref="FileRecoveryStorage"/>; tests and harnesses
/// inject <see cref="InMemoryRecoveryStorage"/> so nothing can reach the real user profile by accident.
/// </summary>
public interface IRecoveryStorage
{
    /// <summary>
    /// The active slots, legacy ones included. Discarded slots, temporary files and foreign names are not listed.
    /// A missing recovery area is empty; any other failure to list it is thrown: the recovery situation is then unknown.
    /// </summary>
    Task<IReadOnlyList<RecoverySlot>> EnumerateAsync(CancellationToken cancellationToken = default);

    Task<byte[]?> ReadAsync(RecoverySlot slot);

    /// <summary>Publishes the complete bytes atomically; a failure or cancellation preserves the previous content.</summary>
    Task WriteAtomicAsync(RecoverySlot slot, byte[] bytes, CancellationToken cancellationToken = default);

    Task DeleteAsync(RecoverySlot slot);

    /// <summary>Atomically renames a slot inside the local data area; fails without change if the target exists.</summary>
    Task MoveAsync(RecoverySlot source, RecoverySlot target);

    /// <summary>Atomically moves a slot to the Discarded area, so a deliberate discard never reappears as crash recovery.</summary>
    Task DiscardAsync(RecoverySlot slot);
}
