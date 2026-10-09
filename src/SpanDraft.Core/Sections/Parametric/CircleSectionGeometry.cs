using SpanDraft.Core.Sections.Geometry;
using SpanDraft.Core.Units;
using static SpanDraft.Core.Sections.Parametric.ParametricGeometry;

namespace SpanDraft.Core.Sections.Parametric;

/// <summary>An exact circle with bounding box [0, Diameter] x [0, Diameter].</summary>
public sealed class CircleSectionGeometry : IParametricSectionDefinition
{
    private readonly ParametricSectionData data;

    public CircleSectionGeometry(Length diameter)
    {
        var d = Positive(diameter, nameof(diameter));
        var radius = DomainGuard.Positive(d / 2, nameof(diameter));
        Diameter = diameter;
        data = Validated(SectionShapeKind.Circle, () => new(Circle(radius, radius, radius)));
    }

    public Length Diameter { get; }
    public SectionShapeKind ShapeKind => SectionShapeKind.Circle;
    public SectionGeometry Geometry => data.Geometry;
    public SectionGeometryProperties GeometryProperties => data.GeometryProperties;
    public Area Area => GeometryProperties.Area;
    public IReadOnlyList<SectionAxisProperties> Axes => data.Axes;
    public SectionAxisProperties GetAxis(SectionAxisDesignation axisDesignation) => data.GetAxis(axisDesignation);
}
