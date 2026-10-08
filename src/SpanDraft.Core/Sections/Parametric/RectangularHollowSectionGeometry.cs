using SpanDraft.Core.Sections.Geometry;
using SpanDraft.Core.Units;
using static SpanDraft.Core.Sections.Parametric.ParametricGeometry;

namespace SpanDraft.Core.Sections.Parametric;

/// <summary>A rectangular tube with an outer corner radius; the inner radius is max(0, OuterRadius - WallThickness).</summary>
public sealed class RectangularHollowSectionGeometry
{
    public RectangularHollowSectionGeometry(Length width, Length height, Length wallThickness, Length outerRadius)
    {
        var b = Positive(width, nameof(width));
        var h = Positive(height, nameof(height));
        var t = Positive(wallThickness, nameof(wallThickness));
        var r = Radius(outerRadius, nameof(outerRadius));
        Require(t < Math.Min(b, h) / 2, nameof(wallThickness), "The inner width and height must be positive.");
        Require(r <= Math.Min(b, h) / 2, nameof(outerRadius), "The outer radius must fit within the outer dimensions.");
        var right = b - t;
        var top = h - t;
        Require(right < b && top < h && right > t && top > t, nameof(wallThickness),
            "Both walls and inner dimensions must remain representable.");
        var ri = Math.Max(0, r - t);
        Require(ri <= Math.Min(right - t, top - t) / 2, nameof(outerRadius),
            "The derived inner radius must fit within the inner dimensions.");
        Require(r == 0 || r - ri > 0, nameof(wallThickness), "The radial wall thickness must remain representable.");
        Width = width;
        Height = height;
        WallThickness = wallThickness;
        OuterRadius = outerRadius;
        InnerRadius = Length.FromMeters(ri);
        Geometry = Validated(() => new(Rectangle(0, 0, b, h, r), [Rectangle(t, t, right, top, ri)]));
    }

    public Length Width { get; }
    public Length Height { get; }
    public Length WallThickness { get; }
    public Length OuterRadius { get; }
    public Length InnerRadius { get; }
    public SectionGeometry Geometry { get; }
}
