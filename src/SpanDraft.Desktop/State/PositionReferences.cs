using SpanDraft.Core.Units;

namespace SpanDraft.Desktop.State;

/// <summary>
/// How a position gets its reference when the user places or moves it directly. Changing the geometry of the beam never
/// changes a reference; only a direct action on the position does, and a position that merely lies on a
/// segment boundary is never turned into an end binding by any of these rules.
/// </summary>
public static class PositionReferences
{
    /// <summary>
    /// The reference of a position that is set freshly: a fixed distance from the beam start while the beam has one
    /// segment, the segment under the position otherwise. On an inner boundary that is the segment that starts there,
    /// at the beam end the last segment.
    /// </summary>
    public static PositionRef ForNewPosition(EditorDocument document, Length position) =>
        document.HasSeveralSegments
            ? InSegment(document, document.SegmentIndexAt(position), position)
            : new PositionRef.FromBeamStart(position);

    /// <summary>
    /// The reference after the user moved a position, by dragging or by editing it, from <paramref name="before"/> to
    /// <paramref name="after"/>. A position that did not move keeps its reference and its end binding. A fixed distance
    /// from the beam start stays one. A segment reference stays while the position lies in its own segment, on its start
    /// or end included; it follows the position only when it lies inside another segment, and on a boundary that does
    /// not touch its own segment it stays on the side the position came from. A moved end binding becomes a fixed distance.
    /// </summary>
    public static PositionRef AfterMove(EditorDocument document, PositionRef current, Length before, Length after)
    {
        if (after == before) return current;
        if (current is PositionRef.FromBeamStart) return new PositionRef.FromBeamStart(after);
        int own = document.IndexOfSegment(current.SegmentId!.Value);
        if (own < 0) return ForNewPosition(document, after);
        var (start, end) = document.SegmentRange(own);
        if (after.Meters >= start.Meters && after.Meters <= end.Meters) return InSegment(document, own, after);
        int at = document.SegmentIndexAt(after);
        // Arriving from the left on a boundary, the position belongs to the segment that ends there.
        if (at > 0 && document.SegmentRange(at).Start == after && before.Meters < after.Meters) at--;
        return InSegment(document, at, after);
    }

    /// <summary>A distance from the start of the segment that reaches exactly the given position.</summary>
    public static PositionRef.FromSegmentStart InSegment(EditorDocument document, int index, Length position)
    {
        var (start, end) = document.SegmentRange(index);
        var segment = document.Segments[index];
        if (position == end) return new(segment.Id, segment.Length);
        // The offset is chosen so that the start plus the offset is exactly the position, not an ulp beside it.
        double offset = position.Meters - start.Meters;
        for (int i = 0; i < 4 && start.Meters + offset != position.Meters; i++) offset += position.Meters - (start.Meters + offset);
        return new(segment.Id, Length.FromMeters(Math.Clamp(offset, 0, segment.Length.Meters)));
    }

    /// <summary>The binding to the end of a segment.</summary>
    public static PositionRef.AtSegmentEnd AtEndOf(EditorDocument document, int index) => new(document.Segments[index].Id);

    /// <summary>The segment whose end is exactly this position, or null; only there an end binding is possible.</summary>
    public static int? EndingSegmentIndex(EditorDocument document, Length position)
    {
        for (int i = 0; i < document.Segments.Count; i++)
            if (document.SegmentRange(i).End == position) return i;
        return null;
    }

    /// <summary>The segments that contain the position, their start and end included.</summary>
    public static IReadOnlyList<int> SegmentsContaining(EditorDocument document, Length position) =>
        Enumerable.Range(0, document.Segments.Count)
            .Where(i => document.SegmentRange(i) is var (start, end) && position.Meters >= start.Meters && position.Meters <= end.Meters)
            .ToArray();
}
