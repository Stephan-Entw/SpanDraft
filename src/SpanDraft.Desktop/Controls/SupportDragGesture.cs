using SpanDraft.Core.Units;
using SpanDraft.Desktop.Layout;

namespace SpanDraft.Desktop.Controls;

/// <summary>One horizontal pointer gesture. Screen positions are transient only.</summary>
public sealed class SupportDragGesture(Guid supportId, Length originalPosition, double pressX, StationTransform snapshot)
{
    public const double Threshold = 4;
    public Guid SupportId { get; } = supportId;
    public bool IsDragging { get; private set; }

    public StationTransform Snapshot { get; } = snapshot;
    public Length? Update(double x, double y, double height)
    {
        if (Math.Abs(x - pressX) >= Threshold) IsDragging = true;
        if (!IsDragging || !double.IsFinite(x) || !double.IsFinite(y) || y < 0 || y > height) return null;
        // Keep the original grab offset; never jump the symbol to the pointer on press.
        double effectiveX = Snapshot.PhysicalToScreen(originalPosition.Meters) + x - pressX;
        return SupportSnap.AtX(Snapshot, Math.Clamp(effectiveX, Snapshot.Stations[0].ScreenX, Snapshot.Stations[^1].ScreenX));
    }
}
