using SpanDraft.Core.Units;

namespace SpanDraft.Desktop.Controls;

/// <summary>Screen-based snapping, independent of pointer events and UI initialization.</summary>
public static class SupportSnap
{
    public const double BeamHitRadius = 18;
    public const double EndpointRadius = 10;

    public static Length? Placement(BeamViewport viewport, double screenX, double screenY) =>
        double.IsFinite(screenY) && Math.Abs(screenY - viewport.BeamY) <= BeamHitRadius
            ? AtX(viewport, screenX) : null;

    public static Length? AtX(BeamViewport viewport, double screenX, double endpointRadius = EndpointRadius)
    {
        if (!double.IsFinite(screenX) || !double.IsFinite(viewport.LengthMeters)
            || viewport.LengthMeters <= 0 || !double.IsFinite(viewport.Left)
            || !double.IsFinite(viewport.Right) || viewport.Right <= viewport.Left
            || !double.IsFinite(endpointRadius) || endpointRadius < 0) return null;
        double leftDistance = Math.Abs(screenX - viewport.Left);
        double rightDistance = Math.Abs(screenX - viewport.Right);
        if (Math.Min(leftDistance, rightDistance) <= endpointRadius)
            return Length.FromMeters(leftDistance <= rightDistance ? 0 : viewport.LengthMeters);
        if (screenX < viewport.Left || screenX > viewport.Right) return null;
        double mm = Math.Round(viewport.ScreenToBeam(screenX) * 1000, MidpointRounding.AwayFromZero);
        if (!double.IsFinite(mm)) return null;
        var position = Length.FromMillimeters(mm);
        return position.Meters <= viewport.LengthMeters ? position : null;
    }
}
