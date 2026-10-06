using SpanDraft.Core.Supports;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.Layout;

namespace SpanDraft.Desktop.Controls;

/// <summary>Shared fixed-DIP drawing extents and visual hit testing.</summary>
public static class SupportSymbol
{
    public const double HitPadding = 5;

    public static bool Contains(SupportPreview preview, StationTransform transform, double beamY, double x, double y) =>
        Contains(preview.Type, preview.Position.Meters, transform, x - transform.PhysicalToScreen(preview.Position.Meters), y - beamY);

    private static bool Contains(SupportType type, double positionMeters, StationTransform transform, double dx, double dy) => type switch
    {
        SupportType.Fixed => FixedSupportGeometry.AtPosition(positionMeters, transform.Stations[^1].PhysicalX).Contains(dx, dy, HitPadding),
        SupportType.Pinned or SupportType.Roller => new HingedSupportGeometry(type == SupportType.Roller).Contains(dx, dy, HitPadding),
        _ => false
    };

    public static Guid? HitTest(IReadOnlyList<EditorSupport> supports, StationTransform transform, double beamY, double x, double y)
    {
        Guid? hit = null;
        double nearest = double.PositiveInfinity;
        foreach (var support in supports)
        {
            double sx = transform.PhysicalToScreen(support.Position.Meters);
            double dx = x - sx;
            double dy = y - beamY;
            bool inside = Contains(support.Type, support.Position.Meters, transform, dx, dy);
            double distance = dx * dx + dy * dy;
            if (inside && distance < nearest) { hit = support.Id; nearest = distance; }
        }
        return hit;
    }
}
