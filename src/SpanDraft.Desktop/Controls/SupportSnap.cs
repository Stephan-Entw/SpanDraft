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

    /// <summary>A moment release lies strictly inside the beam, so the ends do not attract it.</summary>
    public static Length? ReleasePlacement(StationTransform transform, double beamY, double screenX, double screenY) =>
        double.IsFinite(screenY) && Math.Abs(screenY - beamY) <= BeamHitRadius
            ? AtX(transform, screenX, endpointRadius: 0) : null;

    /// <summary>
    /// The position under a screen x. The beam ends and the stations in between (segment boundaries and the positions
    /// of the other elements) attract within their radius, as they do for the result probe; elsewhere the position is
    /// rounded to a whole millimetre.
    /// </summary>
    public static Length? AtX(StationTransform transform, double screenX, double endpointRadius = EndpointRadius,
        double stationRadius = EndpointRadius)
    {
        double left = transform.Stations[0].ScreenX, right = transform.Stations[^1].ScreenX;
        double length = transform.Stations[^1].PhysicalX;
        if (!double.IsFinite(screenX) || !double.IsFinite(endpointRadius) || endpointRadius < 0) return null;
        double leftDistance = Math.Abs(screenX - left);
        double rightDistance = Math.Abs(screenX - right);
        if (Math.Min(leftDistance, rightDistance) <= endpointRadius)
            return Length.FromMeters(leftDistance <= rightDistance ? 0 : length);
        if (screenX < left || screenX > right) return null;
        LayoutStation? nearest = null;
        double nearestDistance = stationRadius;
        for (int i = 1; i < transform.Stations.Count - 1; i++)
        {
            double distance = Math.Abs(screenX - transform.Stations[i].ScreenX);
            if (distance <= nearestDistance && (nearest is null || distance < nearestDistance))
            { nearest = transform.Stations[i]; nearestDistance = distance; }
        }
        if (nearest is { } station) return Length.FromMeters(station.PhysicalX);
        double mm = Math.Round(transform.ScreenToPhysical(screenX) * 1000, MidpointRounding.AwayFromZero);
        if (!double.IsFinite(mm)) return null;
        var position = Length.FromMillimeters(mm);
        return position.Meters <= length ? position : null;
    }
}
