using SpanDraft.Core.Units;
using SpanDraft.Desktop.Layout;

namespace SpanDraft.Desktop.Controls;

/// <summary>Uses the proven support gesture's threshold, grab offset and clamp/snap rules.</summary>
public sealed class PointLoadDragGesture(Guid loadId, Length originalPosition, double pressX, StationTransform snapshot)
{
    private readonly SupportDragGesture _gesture = new(loadId, originalPosition, pressX, snapshot);
    public Guid LoadId => loadId;
    public bool IsDragging => _gesture.IsDragging;
    public StationTransform Snapshot => _gesture.Snapshot;
    public Length? Update(double x, double y, double height) => _gesture.Update(x, y, height);
}
