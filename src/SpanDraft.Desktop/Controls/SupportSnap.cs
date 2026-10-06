using SpanDraft.Core.Units;
using SpanDraft.Desktop.Layout;

namespace SpanDraft.Desktop.Controls;

/// <summary>Screen-based snapping, independent of pointer events and UI initialization.</summary>
public static class SupportSnap
{
    public const double BeamHitRadius = 18;
    public const double EndpointRadius = 10;

    public static Length? Placement(StationTransform transform, double beamY, double screenX, double screenY) =>
        double.IsFinite(screenY) && Math.Abs(screenY - beamY) <= BeamHitRadius
            ? AtX(transform, screenX) : null;

    public static Length? AtX(StationTransform transform, double screenX, double endpointRadius = EndpointRadius)
    {
        double left = transform.Stations[0].ScreenX, right = transform.Stations[^1].ScreenX;
        double length = transform.Stations[^1].PhysicalX;
        if (!double.IsFinite(screenX) || !double.IsFinite(endpointRadius) || endpointRadius < 0) return null;
        double leftDistance = Math.Abs(screenX - left);
        double rightDistance = Math.Abs(screenX - right);
        if (Math.Min(leftDistance, rightDistance) <= endpointRadius)
            return Length.FromMeters(leftDistance <= rightDistance ? 0 : length);
        if (screenX < left || screenX > right) return null;
        double mm = Math.Round(transform.ScreenToPhysical(screenX) * 1000, MidpointRounding.AwayFromZero);
        if (!double.IsFinite(mm)) return null;
        var position = Length.FromMillimeters(mm);
        return position.Meters <= length ? position : null;
    }
}
