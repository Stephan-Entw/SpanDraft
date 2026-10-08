using SpanDraft.Core.Sections.Geometry;
using SpanDraft.Core.Units;
using static SpanDraft.Core.Sections.Parametric.ParametricGeometry;

namespace SpanDraft.Core.Sections.Parametric;

/// <summary>An equal- or unequal-leg angle with its vertical leg on the left and horizontal leg at the bottom.</summary>
public sealed class AngleSectionGeometry
{
    public AngleSectionGeometry(Length width, Length height, Length thickness, Length innerRadius)
    {
        var b = Positive(width, nameof(width));
        var h = Positive(height, nameof(height));
        var t = Positive(thickness, nameof(thickness));
        var r = ParametricGeometry.Radius(innerRadius, nameof(innerRadius));
        Require(t < Math.Min(b, h), nameof(thickness), "Both legs must extend beyond their common thickness.");
        Require(r <= Math.Min(b - t, h - t), nameof(innerRadius), "The inner radius must fit within both leg projections.");
        Width = width;
        Height = height;
        Thickness = thickness;
        InnerRadius = innerRadius;
        Geometry = Validated(() => new(Contour([
            P(0, 0), P(b, 0), P(b, t), P(t, t), P(t, h), P(0, h)], [0, 0, 0, r, 0, 0])));
    }

    public Length Width { get; }
    public Length Height { get; }
    public Length Thickness { get; }
    public Length InnerRadius { get; }
    public SectionGeometry Geometry { get; }
}
