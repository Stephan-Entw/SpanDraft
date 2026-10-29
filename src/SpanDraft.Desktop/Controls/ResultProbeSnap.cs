using SpanDraft.Core.Units;
using SpanDraft.Desktop.Layout;
using SpanDraft.Desktop.Presentation;

namespace SpanDraft.Desktop.Controls;

/// <summary>Exact model stations take priority over a unit-aware technical grid.</summary>
public static class ResultProbeSnap
{
    public static Length? Placement(StationLayoutResult layout, double beamY, double screenX, double screenY,
        UnitDefinition lengthUnit)
    {
        if (!double.IsFinite(screenX) || !double.IsFinite(screenY) || !double.IsFinite(beamY)
            || Math.Abs(screenY - beamY) > SupportSnap.BeamHitRadius
            || screenX < layout.Stations[0].ScreenX || screenX > layout.Stations[^1].ScreenX) return null;

        LayoutStation? nearest = null;
        double distance = SupportSnap.EndpointRadius;
        foreach (var station in layout.Stations)
        {
            double candidate = Math.Abs(screenX - station.ScreenX);
            if (candidate <= distance && (nearest is null || candidate < distance))
            {
                nearest = station;
                distance = candidate;
            }
        }
        if (nearest is { } snap) return Length.FromMeters(snap.PhysicalX);

        double length = layout.Stations[^1].PhysicalX;
        var target = PresentationNumber.FromDouble(length) / lengthUnit.Scale / new PresentationNumber(1000, 1);
        var power = PresentationNumber.PowerOfTen(target.DecimalExponent);
        var step = new[] { 1, 2, 5, 10 }.Select(n => new PresentationNumber(n, 1) * power)
            .First(value => value.CompareTo(target) >= 0);
        var raw = PresentationNumber.FromDouble(layout.Transform.ScreenToPhysical(screenX)) / lengthUnit.Scale;
        var position = new PresentationNumber((raw / step).RoundToInteger(), 1) * step * lengthUnit.Scale;
        return Length.FromMeters(Math.Clamp(position.ToDouble(), 0, length));
    }
}
