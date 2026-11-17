using SpanDraft.Core.Supports;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.Layout;

namespace SpanDraft.Desktop.Controls;

/// <summary>Shared fixed-DIP drawing extents and visual hit testing.</summary>
public static class SupportSymbol
{
    public const double HitPadding = 5;

    public static bool Contains(SupportPreview preview, StationTransform transform, double beamY, double x, double y) =>
        Contains(preview.Type, preview.Spring, preview.Position.Meters, transform, x - transform.PhysicalToScreen(preview.Position.Meters), y - beamY);

    private static bool Contains(SupportType type, SpringData? spring, double positionMeters, StationTransform transform,
        double dx, double dy) => type switch
    {
        SupportType.Spring when spring is not null => SpringGeometry(spring).Contains(dx, dy, HitPadding),
        SupportType.Fixed => FixedSupportGeometry.AtPosition(positionMeters, transform.Stations[^1].PhysicalX).Contains(dx, dy, HitPadding),
        SupportType.Pinned or SupportType.Roller => new HingedSupportGeometry(type == SupportType.Roller).Contains(dx, dy, HitPadding),
        _ => false
    };

    public static SpringSupportGeometry SpringGeometry(SpringData spring) =>
        new(spring.Translational is not null, spring.Rotational is not null, spring.RigidTranslation);

    /// <summary>
    /// The support under the point. Where a translational and a rotational spring share a station, the point belongs to the
    /// spring whose own strokes it is closest to; the ring, the ground line and the hatches belong to both, so a point that
    /// is not clearly nearer to one of them hits neither.
    /// </summary>
    public static Guid? HitTest(IReadOnlyList<EditorSupport> supports, StationTransform transform, double beamY, double x, double y)
    {
        EditorSupport? hit = null;
        double nearest = double.PositiveInfinity;
        var inside = new List<(EditorSupport Support, double Dx, double Dy)>();
        foreach (var support in supports)
        {
            double dx = x - transform.PhysicalToScreen(support.Position.Meters);
            double dy = y - beamY;
            if (!Contains(support.Type, support.Spring, support.Position.Meters, transform, dx, dy)) continue;
            inside.Add((support, dx, dy));
            double distance = dx * dx + dy * dy;
            if (distance < nearest) { hit = support; nearest = distance; }
        }
        if (hit is null) return null;
        var sharing = inside.Where(c => c.Support.Position.Meters == hit.Position.Meters && c.Support.Type == SupportType.Spring).ToList();
        if (hit.Type != SupportType.Spring || sharing.Count < 2) return hit.Id;
        var ranked = sharing.Select(c => (c.Support, Distance: SpringGeometry(c.Support.Spring!).DistanceToSpring(c.Dx, c.Dy)))
            .OrderBy(c => c.Distance).ToList();
        return ranked[0].Distance <= HitPadding && ranked[1].Distance - ranked[0].Distance > 0.5 ? ranked[0].Support.Id : null;
    }
}
