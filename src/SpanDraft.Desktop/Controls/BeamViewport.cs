namespace SpanDraft.Desktop.Controls;

/// <summary>Pane geometry only. Horizontal physical mapping belongs to StationTransform.</summary>
public readonly record struct BeamViewport(double Width, double Height, double BeamY)
{
    public static BeamViewport Fit(double width, double height) => new(width, height, height / 2);
}
