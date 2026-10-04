using SpanDraft.Desktop.State;

namespace SpanDraft.Desktop.Controls;

/// <summary>Transient beam and dimension endpoints within the committed viewport.</summary>
public readonly record struct BeamLengthGeometry(double EndX, double DimensionMidpoint, bool HasGhost)
{
    public static BeamLengthGeometry Create(BeamViewport viewport, ConstraintConflictState? conflict)
    {
        double requested = conflict?.RequestedLength.Meters ?? viewport.LengthMeters;
        bool preview = conflict is { BlockingEntityIds.Count: > 0 }
            && double.IsFinite(requested) && requested > 0 && requested < viewport.LengthMeters;
        double length = preview ? requested : viewport.LengthMeters;
        return new(viewport.BeamToScreen(length), viewport.BeamToScreen(length / 2), preview);
    }
}
