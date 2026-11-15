using SpanDraft.Core.Units;

namespace SpanDraft.Desktop.State;

/// <summary>
/// What a position on the beam is measured from. The reference is part of the meaning of the input: it is never
/// changed by a change of the geometry, and a position that merely happens to lie on a segment boundary keeps the
/// reference the user gave it. The global x of a position is derived from it by the document.
/// Each form stores exactly one truth: <see cref="FromBeamStart"/> and <see cref="FromSegmentStart"/> a distance,
/// <see cref="AtSegmentEnd"/> none, because it follows the length of its segment.
/// </summary>
public abstract record PositionRef
{
    private PositionRef() { }

    /// <summary>A fixed distance from the beam start.</summary>
    public sealed record FromBeamStart(Length Distance) : PositionRef;

    /// <summary>
    /// A fixed distance from the start of a segment, at most as long as the segment. A distance of zero stays at
    /// the segment start.
    /// </summary>
    public sealed record FromSegmentStart(Guid Segment, Length Offset) : PositionRef;

    /// <summary>The end of this segment, not the end of the beam: it follows the segment's length.</summary>
    public sealed record AtSegmentEnd(Guid Segment) : PositionRef;

    /// <summary>The segment this reference points at, or null for <see cref="FromBeamStart"/>.</summary>
    public Guid? SegmentId => this switch
    {
        FromSegmentStart start => start.Segment,
        AtSegmentEnd end => end.Segment,
        _ => null
    };
}

/// <summary>Why a position cannot be resolved against a chain of segments.</summary>
public enum GeometryConflictKind
{
    /// <summary>The position refers to a segment that is not there.</summary>
    SegmentMissing,
    /// <summary>A distance from a segment start exceeds the segment's length.</summary>
    BeyondSegment,
    /// <summary>The position lies outside the beam or breaks a position rule once resolved.</summary>
    RuleViolation
}

/// <summary>One element whose position would become invalid; <see cref="Role"/> tells the end of a line load.</summary>
public sealed record GeometryConflict(Guid EntityId, string EntityName, GeometryConflictKind Kind,
    DistributedLoadEndpoint? Role = null);

public enum GeometryRejection { None, LastSegment, UnknownSegment, Conflicts }

/// <summary>
/// The outcome of a change of the segment chain. It is either the changed document or a rejection; a rejection
/// leaves the original untouched and never corrects, limits, re-attaches or deletes any position to make the
/// change valid.
/// </summary>
public sealed record GeometryChange(EditorDocument? Document, GeometryRejection Rejection,
    IReadOnlyList<GeometryConflict> Conflicts)
{
    public bool IsAccepted => Document is not null;
    public static GeometryChange Accepted(EditorDocument document) => new(document, GeometryRejection.None, []);
    public static GeometryChange Rejected(GeometryRejection rejection) => new(null, rejection, []);
    public static GeometryChange Blocked(IReadOnlyList<GeometryConflict> conflicts) =>
        new(null, GeometryRejection.Conflicts, conflicts);
}
