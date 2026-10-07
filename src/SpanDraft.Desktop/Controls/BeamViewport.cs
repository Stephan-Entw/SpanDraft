using SpanDraft.Desktop.Layout;

namespace SpanDraft.Desktop.Controls;

/// <summary>Pane geometry only. Horizontal physical mapping belongs to StationTransform.</summary>
public readonly record struct BeamViewport(double Width, double Height, double BeamY)
{
    public double BelowBeamSpace { get; init; } = SchematicMetrics.BelowBeamSpace;

    public static BeamViewport Fit(double width, double height, double belowBeamSpace = SchematicMetrics.BelowBeamSpace)
    {
        double reserve = Math.Max(SchematicMetrics.BelowBeamSpace, belowBeamSpace);
        return new(width, height, Math.Max(0, height - reserve)) { BelowBeamSpace = reserve };
    }
}
