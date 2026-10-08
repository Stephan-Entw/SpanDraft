using SpanDraft.Core.Sections.Geometry;
using SpanDraft.Core.Units;
using static SpanDraft.Core.Sections.Parametric.ParametricGeometry;

namespace SpanDraft.Core.Sections.Parametric;

/// <summary>A vertically symmetric T profile with its flange at the top.</summary>
public sealed class TSectionGeometry
{
    public TSectionGeometry(Length height, Length width, Length webThickness, Length flangeThickness, Length radius)
    {
        var h = Positive(height, nameof(height));
        var b = Positive(width, nameof(width));
        var tw = Positive(webThickness, nameof(webThickness));
        var tf = Positive(flangeThickness, nameof(flangeThickness));
        var r = ParametricGeometry.Radius(radius, nameof(radius));
        Require(tw < b, nameof(webThickness), "The web must be thinner than the profile width.");
        Require(tf < h, nameof(flangeThickness), "The flange must leave a positive web height.");
        var left = (b - tw) / 2;
        var right = b - left;
        var underside = h - tf;
        Require(left > 0 && right > left && right < b, nameof(webThickness), "The web and flange overhangs must remain representable.");
        Require(underside > 0 && underside < h, nameof(flangeThickness), "The flange and web height must remain representable.");
        Require(r <= Math.Min(left, underside), nameof(radius), "The radius must fit the flange overhang and web height.");
        Height = height;
        Width = width;
        WebThickness = webThickness;
        FlangeThickness = flangeThickness;
        Radius = radius;
        Geometry = Validated(() => new(Contour([
            P(left, 0), P(right, 0), P(right, underside), P(b, underside), P(b, h), P(0, h),
            P(0, underside), P(left, underside)], [0, 0, r, 0, 0, 0, 0, r])));
    }

    public Length Height { get; }
    public Length Width { get; }
    public Length WebThickness { get; }
    public Length FlangeThickness { get; }
    public Length Radius { get; }
    public SectionGeometry Geometry { get; }
}
