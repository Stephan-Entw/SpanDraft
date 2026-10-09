using SpanDraft.Core.Sections.Geometry;
using SpanDraft.Core.Units;
using static SpanDraft.Core.Sections.Parametric.ParametricGeometry;

namespace SpanDraft.Core.Sections.Parametric;

/// <summary>An equal- or unequal-leg angle with its vertical leg on the left and horizontal leg at the bottom.</summary>
public sealed class AngleSectionGeometry : IParametricSectionDefinition
{
    private readonly ParametricSectionData data;

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
        data = Validated(SectionShapeKind.Angle, () => new(Contour([
            P(0, 0), P(b, 0), P(b, t), P(t, t), P(t, h), P(0, h)], [0, 0, 0, r, 0, 0])));
    }

    public Length Width { get; }
    public Length Height { get; }
    public Length Thickness { get; }
    public Length InnerRadius { get; }
    public SectionShapeKind ShapeKind => SectionShapeKind.Angle;
    public SectionGeometry Geometry => data.Geometry;
    public SectionGeometryProperties GeometryProperties => data.GeometryProperties;
    public Area Area => GeometryProperties.Area;
    public IReadOnlyList<SectionAxisProperties> Axes => data.Axes;
    public SectionAxisProperties GetAxis(SectionAxisDesignation axisDesignation) => data.GetAxis(axisDesignation);
}
