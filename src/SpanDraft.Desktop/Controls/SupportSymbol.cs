using SpanDraft.Core.Supports;
using SpanDraft.Desktop.State;

namespace SpanDraft.Desktop.Controls;

/// <summary>Shared fixed-DIP drawing extents and visual hit testing.</summary>
public static class SupportSymbol
{
    public const double HalfWidth = 18;
    public const double TriangleHeight = 20;
    public const double GroundY = 32;
    public const double WallHalfHeight = 24;
    public const double HitPadding = 5;

    public static Guid? HitTest(IReadOnlyList<EditorSupport> supports, BeamViewport viewport, double x, double y)
    {
        Guid? hit = null;
        double nearest = double.PositiveInfinity;
        foreach (var support in supports)
        {
            double sx = viewport.BeamToScreen(support.Position.Meters);
            double dx = x - sx;
            double dy = y - viewport.BeamY;
            bool inside = support.Type == SupportType.Fixed
                ? dx >= -HalfWidth - HitPadding && dx <= HitPadding && Math.Abs(dy) <= WallHalfHeight + HitPadding
                : Math.Abs(dx) <= HalfWidth + HitPadding && dy >= -HitPadding && dy <= GroundY + HitPadding;
            double distance = dx * dx + dy * dy;
            if (inside && distance < nearest) { hit = support.Id; nearest = distance; }
        }
        return hit;
    }
}
