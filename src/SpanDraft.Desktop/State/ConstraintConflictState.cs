using SpanDraft.Core.Units;

namespace SpanDraft.Desktop.State;

/// <summary>Transient objects blocking a requested length; never part of the document.</summary>
public sealed class ConstraintConflictState(Length requestedLength, IEnumerable<Guid> blockingEntityIds)
{
    public Length RequestedLength { get; } = requestedLength;
    public IReadOnlyList<Guid> BlockingEntityIds { get; } = Array.AsReadOnly(blockingEntityIds.ToArray());
}
