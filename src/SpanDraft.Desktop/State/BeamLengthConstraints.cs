using System.Globalization;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Resources;

namespace SpanDraft.Desktop.State;

/// <summary>Checks a new total length by resizing the last segment and resolving the elements' references.</summary>
public static class BeamLengthConstraints
{
    public static IReadOnlyList<Guid> BlockingEntities(EditorDocument document, Length length) =>
        Evaluate(document, length).BlockingEntityIds;

    public static string? Error(EditorDocument document, Length length) => Evaluate(document, length).ErrorText;

    internal static (EditorDocument? Document, string? ErrorText, IReadOnlyList<Guid> BlockingEntityIds)
        Evaluate(EditorDocument document, Length length)
    {
        if (!double.IsFinite(length.Meters) || length.Meters <= 0) return (null, Strings.InvalidLength, []);
        double start = document.SegmentBoundaries.LastOrDefault().Meters;
        if (length.Meters <= start) return (null, Strings.LengthExcludesSegments, []);

        var change = document.ResizeSegment(document.Segments[^1].Id, Length.FromMeters(length.Meters - start));
        if (change.Document is { } resized) return (resized, null, []);

        var ids = change.Conflicts.Select(c => c.EntityId).Distinct().ToArray();
        // Keep the familiar outside-beam messages only when fixed positions really are the blockers.
        var fixedEntitiesOutside = document.Loads.Where(l => l.Reference is null && l.Position.Meters > length.Meters).Select(l => l.Id)
            .Concat(document.DistributedLoads.Where(l => l.EndReference is null && l.EndPosition.Meters > length.Meters).Select(l => l.Id))
            .Concat(document.MomentReleases.Where(r => r.Reference is null && r.Position.Meters >= length.Meters).Select(r => r.Id))
            .ToHashSet();
        var fixedOutside = document.Supports.Where(s => s.Reference is null && s.Position.Meters > length.Meters).Select(s => s.Id)
            .Concat(fixedEntitiesOutside).ToHashSet();
        string error = change.Conflicts.All(c => c.Kind == GeometryConflictKind.RuleViolation && fixedOutside.Contains(c.EntityId))
            ? ids.Any(fixedEntitiesOutside.Contains) ? Strings.LengthExcludesEntities : Strings.LengthExcludesSupports
            : string.Format(CultureInfo.CurrentCulture, Strings.SegmentChangeBlocked,
                string.Join(", ", change.Conflicts.Select(c => c.EntityName).Distinct()));
        return (null, error, ids);
    }

    /// <summary>Legacy outside-beam messages for fixed positions and possibly edited segment boundaries.</summary>
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
