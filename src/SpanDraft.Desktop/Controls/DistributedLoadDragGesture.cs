using SpanDraft.Core.Units;
using SpanDraft.Desktop.Layout;
using SpanDraft.Desktop.State;

namespace SpanDraft.Desktop.Controls;

/// <summary>An endpoint gesture with the existing horizontal threshold, grab offset and beam snap.</summary>
public sealed class DistributedLoadDragGesture(Guid? loadId, DistributedLoadEndpoint endpoint,
    Length position, double pressX, StationTransform snapshot)
{
    private readonly SupportDragGesture _gesture = new(loadId ?? Guid.Empty, position, pressX, snapshot);
    public Guid? LoadId => loadId;
    public DistributedLoadEndpoint Endpoint => endpoint;
    public bool IsDragging => _gesture.IsDragging;
    public StationTransform Snapshot => _gesture.Snapshot;
    public Length? Update(double x, double y, double height) => _gesture.Update(x, y, height);
}
