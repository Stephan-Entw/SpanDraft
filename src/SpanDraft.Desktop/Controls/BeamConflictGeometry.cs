using SpanDraft.Desktop.Layout;
using SpanDraft.Desktop.State;

namespace SpanDraft.Desktop.Controls;

public readonly record struct BeamConflictGeometry(double EndX, bool HasGhost)
{
    public static BeamConflictGeometry Create(StationTransform transform, ConstraintConflictState? conflict)
    {
        double length = transform.Stations[^1].PhysicalX;
        double requested = conflict?.RequestedLength.Meters ?? length;
        bool preview = conflict is { BlockingEntityIds.Count: > 0 }
            && double.IsFinite(requested) && requested > 0 && requested < length;
        return new(transform.PhysicalToScreen(preview ? requested : length), preview);
    }
}
