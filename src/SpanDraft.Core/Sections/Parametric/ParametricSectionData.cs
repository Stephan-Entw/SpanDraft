using SpanDraft.Core.Sections.Geometry;

namespace SpanDraft.Core.Sections.Parametric;

internal sealed class ParametricSectionData
{
    private readonly SectionAxes axes;

    internal ParametricSectionData(SectionGeometry geometry, SectionShapeKind shapeKind)
    {
        Geometry = geometry;
        if (shapeKind == SectionShapeKind.Angle)
        {
            GeometryProperties = geometry.CalculateProperties();
            axes = new(
                new(SectionAxisDesignation.U, GeometryProperties.I1, GeometryProperties.W1Positive, GeometryProperties.W1Negative),
                new(SectionAxisDesignation.V, GeometryProperties.I2, GeometryProperties.W2Positive, GeometryProperties.W2Negative));
        }
        else
        {
            GeometryProperties = GeometryIntegration.Calculate(geometry, out var moduli);
            axes = new(
                new(SectionAxisDesignation.Y, GeometryProperties.Iy, moduli.WyPositive, moduli.WyNegative),
                new(SectionAxisDesignation.Z, GeometryProperties.Iz, moduli.WzPositive, moduli.WzNegative));
        }
    }

    internal SectionGeometry Geometry { get; }
    internal SectionGeometryProperties GeometryProperties { get; }
    internal IReadOnlyList<SectionAxisProperties> Axes => axes.Values;
    internal SectionAxisProperties GetAxis(SectionAxisDesignation axisDesignation) => axes.GetAxis(axisDesignation);
}
