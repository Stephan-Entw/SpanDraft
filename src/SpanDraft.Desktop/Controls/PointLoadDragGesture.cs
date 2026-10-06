using SpanDraft.Core.Units;

namespace SpanDraft.Desktop.Controls;

/// <summary>Uses the proven support gesture's threshold, grab offset and clamp/snap rules.</summary>
public sealed class PointLoadDragGesture(Guid loadId, Length originalPosition, double pressX)
{
    private readonly SupportDragGesture _gesture = new(loadId, originalPosition, pressX);
    public Guid LoadId => loadId;
    public bool IsDragging => _gesture.IsDragging;
    public Length? Update(BeamViewport viewport, double x, double y, double width, double height) =>
        _gesture.Update(viewport, x, y, width, height);
}
