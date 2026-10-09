using SpanDraft.Core.Sections.Geometry;
using SpanDraft.Core.Units;
using static SpanDraft.Core.Sections.Parametric.ParametricGeometry;

namespace SpanDraft.Core.Sections.Parametric;

/// <summary>A rectangle occupying [0, Width] x [0, Height] in the y/z plane.</summary>
public sealed class RectangleSectionGeometry : IParametricSectionDefinition
{
    private readonly ParametricSectionData data;

    public RectangleSectionGeometry(Length width, Length height)
    {
        var b = Positive(width, nameof(width));
        var h = Positive(height, nameof(height));
        Width = width;
        Height = height;
        data = Validated(SectionShapeKind.Rectangle, () => new(Rectangle(0, 0, b, h)));
    }

    public Length Width { get; }
    public Length Height { get; }
    public SectionShapeKind ShapeKind => SectionShapeKind.Rectangle;
    public SectionGeometry Geometry => data.Geometry;
    public SectionGeometryProperties GeometryProperties => data.GeometryProperties;
    public Area Area => GeometryProperties.Area;
    public IReadOnlyList<SectionAxisProperties> Axes => data.Axes;
    public SectionAxisProperties GetAxis(SectionAxisDesignation axisDesignation) => data.GetAxis(axisDesignation);
}
