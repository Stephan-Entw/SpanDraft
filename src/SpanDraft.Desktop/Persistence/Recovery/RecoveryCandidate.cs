namespace SpanDraft.Desktop.Persistence;

/// <summary>Where a recovery candidate stands in the catalog.</summary>
public enum RecoveryCandidateState
{
    /// <summary>Undecided: it is offered for restore or discard.</summary>
    Orphaned,
    /// <summary>A restore is running; the candidate can neither be claimed nor discarded again.</summary>
    Claiming,
    /// <summary>Restored into a workspace, which owns its slot now.</summary>
    Claimed,
    /// <summary>The leftover Create draft of a project that has been created from it; it never appears on its own.</summary>
    Superseded,
    /// <summary>Identical to the project file it came from; it is removed and not offered.</summary>
    Redundant
}

/// <summary>
/// One recovery slot as the catalog knows it. A candidate is immutable: state changes replace it, so readers on other
/// threads never see a half-updated candidate. <see cref="CandidateId"/> is the registry's candidate and is not a
/// <see cref="RecoveryId"/> (a legacy slot has no id and gets one only when it is claimed).
/// </summary>
public sealed record RecoveryCandidate
{
    public required Guid CandidateId { get; init; }
    public required RecoverySlot Slot { get; init; }
    public RecoveryCandidateState State { get; init; } = RecoveryCandidateState.Orphaned;
    /// <summary>Why the slot cannot be restored; null for a valid candidate.</summary>
    public string? ValidationError { get; init; }
    public RecoverySnapshot? Project { get; init; }
    public SetupRecoverySnapshot? Setup { get; init; }
    /// <summary>The identity of the original project file; null without one or if it cannot be resolved.</summary>
    public ProjectIdentity? Identity { get; init; }
    /// <summary>The newest undecided variant of its original: the only one that holds the path (RecoveryPending).</summary>
    public bool IsPreferred { get; init; }

    public RecoverySlotKind Kind => Slot.Kind;
    public bool IsLegacy => Slot.IsLegacy;
    public bool IsDamaged => ValidationError is not null;
    public DateTimeOffset? WrittenAtUtc => Project?.WrittenAtUtc ?? Setup?.WrittenAtUtc;
    public string? OriginalFilePath => Project?.OriginalFilePath;
}
