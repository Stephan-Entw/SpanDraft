namespace SpanDraft.Core.Sections.Geometry;

/// <summary>One simple outer boundary with strictly interior, disjoint, non-nested holes.</summary>
public sealed class SectionGeometry
{
    public SectionGeometry(SectionContour outerContour, IEnumerable<SectionContour>? holes = null)
    {
        ArgumentNullException.ThrowIfNull(outerContour);
        OuterContour = outerContour;
        var copy = holes?.ToArray() ?? [];
        if (copy.Any(hole => hole is null))
            throw new ArgumentException("Holes cannot contain null contours.", nameof(holes));
        Holes = Array.AsReadOnly(copy);
        GeometryTopology.ValidateHoles(this);
    }

    public SectionContour OuterContour { get; }
    public IReadOnlyList<SectionContour> Holes { get; }

    public SectionGeometryProperties CalculateProperties() => GeometryIntegration.Calculate(this);
}