using SpanDraft.Core.Sections.Geometry;
using SpanDraft.Core.Units;
using static SpanDraft.Core.Sections.Parametric.ParametricGeometry;

namespace SpanDraft.Core.Sections.Parametric;

/// <summary>An exact annulus centred at (OuterDiameter/2, OuterDiameter/2).</summary>
public sealed class CircularHollowSectionGeometry
{
    public CircularHollowSectionGeometry(Length outerDiameter, Length wallThickness)
    {
        var d = Positive(outerDiameter, nameof(outerDiameter));
        var t = Positive(wallThickness, nameof(wallThickness));
        var ro = DomainGuard.Positive(d / 2, nameof(outerDiameter));
        Require(t < ro, nameof(wallThickness), "The inner diameter must be positive.");
        var ri = ro - t;
        Require(ri > 0 && ri < ro && ro + ri < d && ro - ri > 0, nameof(wallThickness),
            "The inner contour and both sides of the wall must remain representable.");
        OuterDiameter = outerDiameter;
        WallThickness = wallThickness;
        Geometry = Validated(() => new(Circle(ro, ro, ro), [Circle(ro, ro, ri)]));
    }

    public Length OuterDiameter { get; }
    public Length WallThickness { get; }
    public SectionGeometry Geometry { get; }
}
