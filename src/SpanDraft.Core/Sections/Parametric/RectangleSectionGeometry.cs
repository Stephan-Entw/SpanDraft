using SpanDraft.Core.Sections.Geometry;
using SpanDraft.Core.Units;
using static SpanDraft.Core.Sections.Parametric.ParametricGeometry;

namespace SpanDraft.Core.Sections.Parametric;

/// <summary>A rectangle occupying [0, Width] x [0, Height] in the y/z plane.</summary>
public sealed class RectangleSectionGeometry
{
    public RectangleSectionGeometry(Length width, Length height)
    {
        var b = Positive(width, nameof(width));
        var h = Positive(height, nameof(height));
        Width = width;
        Height = height;
        Geometry = Validated(() => new(Rectangle(0, 0, b, h)));
    }

    public Length Width { get; }
    public Length Height { get; }
    public SectionGeometry Geometry { get; }
}
