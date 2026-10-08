using SpanDraft.Core.Sections.Geometry;
using SpanDraft.Core.Units;
using static SpanDraft.Core.Sections.Parametric.ParametricGeometry;

namespace SpanDraft.Core.Sections.Parametric;

/// <summary>An exact circle with bounding box [0, Diameter] x [0, Diameter].</summary>
public sealed class CircleSectionGeometry
{
    public CircleSectionGeometry(Length diameter)
    {
        var d = Positive(diameter, nameof(diameter));
        var radius = DomainGuard.Positive(d / 2, nameof(diameter));
        Diameter = diameter;
        Geometry = Validated(() => new(Circle(radius, radius, radius)));
    }

    public Length Diameter { get; }
    public SectionGeometry Geometry { get; }
}
