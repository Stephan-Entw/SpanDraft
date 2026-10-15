using SpanDraft.Core.Units;
using SpanDraft.Desktop.Resources;

namespace SpanDraft.Desktop.State;

/// <summary>The same inclusive beam bounds apply to every desktop length edit.</summary>
public static class BeamLengthConstraints
{
    public static IReadOnlyList<Guid> BlockingEntities(EditorDocument document, Length length) =>
        document.Supports.Where(s => s.Position.Meters > length.Meters).Select(s => s.Id)
            .Concat(document.Loads.Where(l => l.Position.Meters > length.Meters).Select(l => l.Id))
            .Concat(document.DistributedLoads.Where(l => l.EndPosition.Meters > length.Meters).Select(l => l.Id)).ToArray();

    public static string? Error(EditorDocument document, Length length)
    {
        if (!double.IsFinite(length.Meters) || length.Meters <= 0) return Strings.InvalidLength;
        if (document.Loads.Any(l => l.Position.Meters > length.Meters)
            || document.DistributedLoads.Any(l => l.EndPosition.Meters > length.Meters)) return Strings.LengthExcludesEntities;
        return document.Supports.Any(s => s.Position.Meters > length.Meters) ? Strings.LengthExcludesSupports : null;
    }
}
