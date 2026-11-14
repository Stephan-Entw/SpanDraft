using SpanDraft.Core.Units;
using SpanDraft.Desktop.Resources;

namespace SpanDraft.Desktop.State;

/// <summary>The same inclusive beam bounds apply to every desktop length edit.</summary>
public static class BeamLengthConstraints
{
    public static IReadOnlyList<Guid> BlockingEntities(EditorDocument document, Length length) =>
        document.Supports.Where(s => s.Position.Meters > length.Meters).Select(s => s.Id)
            .Concat(document.Loads.Where(l => l.Position.Meters > length.Meters).Select(l => l.Id))
            .Concat(document.DistributedLoads.Where(l => l.EndPosition.Meters > length.Meters).Select(l => l.Id))
            // A release must lie strictly inside the beam, so the new end may not coincide with one.
            .Concat(document.MomentReleases.Where(r => r.Position.Meters >= length.Meters).Select(r => r.Id)).ToArray();

    public static string? Error(EditorDocument document, Length length) => Error(document, length, document.SegmentBoundaries);

    /// <summary>The length is also bounded by the given, possibly edited, segment boundaries instead of the document's own.</summary>
    public static string? Error(EditorDocument document, Length length, IReadOnlyList<Length> segmentBoundaries)
    {
        if (!double.IsFinite(length.Meters) || length.Meters <= 0) return Strings.InvalidLength;
        if (segmentBoundaries.Count > 0 && !(length.Meters > segmentBoundaries[^1].Meters))
            return Strings.LengthExcludesSegments;
        if (document.Loads.Any(l => l.Position.Meters > length.Meters)
            || document.DistributedLoads.Any(l => l.EndPosition.Meters > length.Meters)
            || document.MomentReleases.Any(r => r.Position.Meters >= length.Meters)) return Strings.LengthExcludesEntities;
        return document.Supports.Any(s => s.Position.Meters > length.Meters) ? Strings.LengthExcludesSupports : null;
    }
}
