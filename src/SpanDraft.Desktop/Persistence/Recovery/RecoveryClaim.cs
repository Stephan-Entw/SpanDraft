namespace SpanDraft.Desktop.Persistence;

/// <summary>
/// The exclusive right to restore one candidate. It ends with <see cref="CommitAsync"/> once the workspace owns the
/// restored recovery, or with <see cref="AbortAsync"/> when the restore failed and the candidate must stay offered.
/// </summary>
public sealed class RecoveryClaim
{
    private readonly RecoveryCatalog _catalog;
    private int _finished;

    internal RecoveryClaim(RecoveryCatalog catalog, RecoveryCandidate candidate, bool holdsPath)
    {
        _catalog = catalog;
        Candidate = candidate;
        HoldsPath = holdsPath;
    }

    /// <summary>
    /// True while the candidate holds its original path (RecoveryPending): only then may the restore take that path over
    /// (TryCommitRecovery). Otherwise it is restored detached from the original, which stays a recovery origin only.
    /// </summary>
    public bool HoldsPath { get; }

    /// <summary>The candidate as claimed; for a legacy slot <see cref="RecoveryCandidate.Slot"/> is the new identified slot.</summary>
    public RecoveryCandidate Candidate { get; }
    public RecoverySlot Slot => Candidate.Slot;

    public Task CommitAsync() => Interlocked.Exchange(ref _finished, 1) == 0 ? _catalog.FinishClaimAsync(Candidate.CandidateId, committed: true) : Task.CompletedTask;

    public Task AbortAsync() => Interlocked.Exchange(ref _finished, 1) == 0 ? _catalog.FinishClaimAsync(Candidate.CandidateId, committed: false) : Task.CompletedTask;
}
