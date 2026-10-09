using SpanDraft.Core.Sections.Geometry;

namespace SpanDraft.Core.Sections.Parametric;

/// <summary>A parameter-preserving section definition with complete geometric properties.</summary>
public interface IParametricSectionDefinition : ISectionDefinition
{
    SectionShapeKind ShapeKind { get; }
    SectionGeometry Geometry { get; }
    SectionGeometryProperties GeometryProperties { get; }
}
