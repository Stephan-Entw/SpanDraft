using SpanDraft.Core.Sections.Geometry;
using SpanDraft.Core.Units;
using static SpanDraft.Core.Sections.Parametric.ParametricGeometry;

namespace SpanDraft.Core.Sections.Parametric;

/// <summary>A doubly symmetric I/H profile with a vertical web and four internal fillets.</summary>
public sealed class ISectionGeometry
{
    public ISectionGeometry(Length height, Length width, Length webThickness, Length flangeThickness, Length radius)
    {
        var h = Positive(height, nameof(height));
        var b = Positive(width, nameof(width));
        var tw = Positive(webThickness, nameof(webThickness));
        var tf = Positive(flangeThickness, nameof(flangeThickness));
        var r = ParametricGeometry.Radius(radius, nameof(radius));
        Require(tw < b, nameof(webThickness), "The web must be thinner than the profile width.");
        Require(tf < h / 2, nameof(flangeThickness), "Two flanges must leave a positive clear web height.");
        var left = (b - tw) / 2;
        var right = b - left;
        var upper = h - tf;
        Require(left > 0 && right > left && right < b, nameof(webThickness), "The web and flange overhangs must remain representable.");
        Require(upper > tf && upper < h, nameof(flangeThickness), "The flanges and clear web height must remain representable.");
        Require(r <= Math.Min(left, (upper - tf) / 2), nameof(radius), "The radius must fit the flange overhang and half the clear web height.");
        Height = height;
        Width = width;
        WebThickness = webThickness;
        FlangeThickness = flangeThickness;
        Radius = radius;
        Geometry = Validated(() => new(Contour([
            P(0, 0), P(b, 0), P(b, tf), P(right, tf), P(right, upper), P(b, upper),
            P(b, h), P(0, h), P(0, upper), P(left, upper), P(left, tf), P(0, tf)],
            [0, 0, 0, r, r, 0, 0, 0, 0, r, r, 0])));
    }

    public Length Height { get; }
    public Length Width { get; }
    public Length WebThickness { get; }
    public Length FlangeThickness { get; }
    public Length Radius { get; }
    public SectionGeometry Geometry { get; }
}
