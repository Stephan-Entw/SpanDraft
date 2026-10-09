using SpanDraft.Core.Sections.Geometry;
using SpanDraft.Core.Units;
using static SpanDraft.Core.Sections.Parametric.ParametricGeometry;

namespace SpanDraft.Core.Sections.Parametric;

/// <summary>A horizontally symmetric channel with its web on the left and flanges pointing right.</summary>
public sealed class USectionGeometry : IParametricSectionDefinition
{
    private readonly ParametricSectionData data;

    public USectionGeometry(Length height, Length width, Length webThickness, Length flangeThickness, Length radius)
    {
        var h = Positive(height, nameof(height));
        var b = Positive(width, nameof(width));
        var tw = Positive(webThickness, nameof(webThickness));
        var tf = Positive(flangeThickness, nameof(flangeThickness));
        var r = ParametricGeometry.Radius(radius, nameof(radius));
        Require(tw < b, nameof(webThickness), "The web must be thinner than the profile width.");
        Require(tf < h / 2, nameof(flangeThickness), "Two flanges must leave a positive clear web height.");
        var upper = h - tf;
        Require(upper > tf && upper < h, nameof(flangeThickness), "The flanges and clear web height must remain representable.");
        Require(r <= Math.Min(b - tw, (upper - tf) / 2), nameof(radius), "The radius must fit the flange projection and half the clear web height.");
        Height = height;
        Width = width;
        WebThickness = webThickness;
        FlangeThickness = flangeThickness;
        Radius = radius;
        data = Validated(SectionShapeKind.USection, () => new(Contour([
            P(0, 0), P(b, 0), P(b, tf), P(tw, tf), P(tw, upper), P(b, upper), P(b, h), P(0, h)],
            [0, 0, 0, r, r, 0, 0, 0])));
    }

    public Length Height { get; }
    public Length Width { get; }
    public Length WebThickness { get; }
    public Length FlangeThickness { get; }
    public Length Radius { get; }
    public SectionShapeKind ShapeKind => SectionShapeKind.USection;
    public SectionGeometry Geometry => data.Geometry;
    public SectionGeometryProperties GeometryProperties => data.GeometryProperties;
    public Area Area => GeometryProperties.Area;
    public IReadOnlyList<SectionAxisProperties> Axes => data.Axes;
    public SectionAxisProperties GetAxis(SectionAxisDesignation axisDesignation) => data.GetAxis(axisDesignation);
}
