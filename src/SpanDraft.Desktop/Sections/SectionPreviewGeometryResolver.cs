using System.Collections.ObjectModel;
using Avalonia;
using SpanDraft.Core.Sections.Geometry;
using SpanDraft.Core.Sections.Parametric;
using SpanDraft.Desktop.ViewModels;

namespace SpanDraft.Desktop.Sections;

// Drawing coordinates use y right, z up, in metres. These types carry no section properties.
public sealed record PreviewSegment(Point Start, Point End, Point? Center = null, double Sweep = 0);
public sealed record PreviewDimension(string Key, double Value, bool IsConfirmed, Point Anchor);
public sealed record SectionPreviewGeometry(SectionEditorType Type, double Width, double Height,
    IReadOnlyList<IReadOnlyList<PreviewSegment>> Contours, IReadOnlyDictionary<string, PreviewDimension> Dimensions,
    Point? Centroid = null, double? PrincipalAngle = null);

public sealed class PreviewGeometryException(string key) : Exception
{
    public string Key { get; } = key;
}

/// <summary>Neutral drawing proportions, not standard profiles. Never constructs a Core section or computes A/I/W.</summary>
public static class SectionPreviewGeometryResolver
{
    private const double NeutralHeight = .1, ThinRatio = .06, FlangeRatio = .10, RadiusRatio = .04;
    public static string[] ParameterKeys(SectionEditorType type) => type switch
    {
        SectionEditorType.Rectangle => ["b", "h"], SectionEditorType.RectangularHollow => ["b", "h", "t", "r"],
        SectionEditorType.Circle => ["d"], SectionEditorType.CircularHollow => ["D", "t"],
        SectionEditorType.ISection or SectionEditorType.USection or SectionEditorType.TSection => ["h", "b", "tw", "tf", "r"],
        SectionEditorType.Angle => ["b", "h", "t", "r"], _ => []
    };

    public static SectionPreviewGeometry Resolve(SectionEditorType type, IReadOnlyDictionary<string, double> confirmed)
    {
        if (type == SectionEditorType.Manual) throw new ArgumentException("Manual sections have no preview.", nameof(type));
        double ratio = type is SectionEditorType.RectangularHollow or SectionEditorType.Angle ? .8 : .6;
        double h = confirmed.GetValueOrDefault("h", confirmed.GetValueOrDefault("b", NeutralHeight * ratio) / ratio);
        double b = confirmed.GetValueOrDefault("b", h * ratio);
        var values = new Dictionary<string, double>();
        bool fixedB = confirmed.ContainsKey("b"), fixedH = confirmed.ContainsKey("h");
        // A confirmed secondary dimension can expand missing outer dimensions, never confirmed ones.
        double secondary = Math.Max(confirmed.GetValueOrDefault("t"),
            Math.Max(confirmed.GetValueOrDefault("tw"), Math.Max(confirmed.GetValueOrDefault("tf"), confirmed.GetValueOrDefault("r"))));
        if (!fixedB) b = Math.Max(b, secondary * 6);
        if (!fixedH) h = Math.Max(h, secondary * 12);
        if (type is SectionEditorType.Circle or SectionEditorType.CircularHollow)
        {
            var key = type == SectionEditorType.Circle ? "d" : "D";
            b = h = confirmed.GetValueOrDefault(key, Math.Max(NeutralHeight, secondary * 8));
            values[key] = b;
        }
        else { values["b"] = b; values["h"] = h; }
        double small = Math.Min(b, h);
        double t = confirmed.GetValueOrDefault("t", small * ThinRatio);
        double tw = confirmed.GetValueOrDefault("tw", small * ThinRatio);
        double tf = confirmed.GetValueOrDefault("tf", small * FlangeRatio);
        // Missing thicknesses leave room for a confirmed radius.
        double fixedR = confirmed.GetValueOrDefault("r");
        if (!confirmed.ContainsKey("tw") && fixedR > 0)
            tw = Math.Min(tw, (b - fixedR * (type == SectionEditorType.USection ? 1 : 2)) / 2);
        if (!confirmed.ContainsKey("tf") && fixedR > 0)
            tf = Math.Min(tf, (h - fixedR * (type == SectionEditorType.TSection ? 1 : 2)) / 4);
        if (!confirmed.ContainsKey("t") && type == SectionEditorType.Angle && fixedR > 0)
            t = Math.Min(t, (small - fixedR) / 2);
        double maxR = type switch
        {
            SectionEditorType.RectangularHollow => small / 2,
            SectionEditorType.ISection => Math.Min((b - tw) / 2, (h - 2 * tf) / 2),
            SectionEditorType.USection => Math.Min(b - tw, (h - 2 * tf) / 2),
            SectionEditorType.TSection => Math.Min((b - tw) / 2, h - tf),
            SectionEditorType.Angle => small - t, _ => 0
        };
        double r = confirmed.GetValueOrDefault("r", Math.Min(small * RadiusRatio, Math.Max(0, maxR) / 2));
        foreach (var key in ParameterKeys(type))
            if (!values.ContainsKey(key)) values[key] = key switch { "t" => t, "tw" => tw, "tf" => tf, "r" => r, _ => 0 };
        foreach (var (key, value) in values)
            if (!double.IsFinite(value) || (key == "r" ? value < 0 : value <= 0)) throw new PreviewGeometryException(key);
        Require(b > 0 && h > 0, "b");
        if (type is SectionEditorType.RectangularHollow or SectionEditorType.CircularHollow) Require(t < small / 2, "t");
        if (type is SectionEditorType.ISection or SectionEditorType.USection or SectionEditorType.TSection)
        { Require(tw < b, "tw"); Require(tf < h / (type == SectionEditorType.TSection ? 1 : 2), "tf"); }
        if (type == SectionEditorType.Angle) Require(t < small, "t");
        if (values.ContainsKey("r")) Require(r <= maxR, "r");
        var contours = new List<IReadOnlyList<PreviewSegment>>();
        Point P(double y, double z) => new(y, z);
        void Polygon(Point[] points, double[]? radii = null) => contours.Add(RoundPolygon(points, radii ?? new double[points.Length]));
        void Rectangle(double left, double bottom, double right, double top, double radius) =>
            Polygon([P(left, bottom), P(right, bottom), P(right, top), P(left, top)], [radius, radius, radius, radius]);
        var left = (b - tw) / 2; var right = b - left;
        switch (type)
        {
            case SectionEditorType.Rectangle: Rectangle(0, 0, b, h, 0); break;
            case SectionEditorType.RectangularHollow:
                Rectangle(0, 0, b, h, r); Rectangle(t, t, b - t, h - t, Math.Max(0, r - t)); break;
            case SectionEditorType.Circle: contours.Add(Circle(b / 2, h / 2, b / 2)); break;
            case SectionEditorType.CircularHollow:
                contours.Add(Circle(b / 2, h / 2, b / 2)); contours.Add(Circle(b / 2, h / 2, b / 2 - t)); break;
            case SectionEditorType.ISection:
                Polygon([P(0,0),P(b,0),P(b,tf),P(right,tf),P(right,h-tf),P(b,h-tf),P(b,h),P(0,h),P(0,h-tf),P(left,h-tf),P(left,tf),P(0,tf)],
                    [0,0,0,r,r,0,0,0,0,r,r,0]); break;
            case SectionEditorType.USection:
                Polygon([P(0,0),P(b,0),P(b,tf),P(tw,tf),P(tw,h-tf),P(b,h-tf),P(b,h),P(0,h)], [0,0,0,r,r,0,0,0]); break;
            case SectionEditorType.TSection:
                Polygon([P(left,0),P(right,0),P(right,h-tf),P(b,h-tf),P(b,h),P(0,h),P(0,h-tf),P(left,h-tf)], [0,0,r,0,0,0,0,r]); break;
            case SectionEditorType.Angle:
                Polygon([P(0,0),P(b,0),P(b,t),P(t,t),P(t,h),P(0,h)], [0,0,0,r,0,0]); break;
        }
        return DrawingData(type, b, h, values, confirmed, contours.AsReadOnly());
    }

    private static SectionPreviewGeometry DrawingData(SectionEditorType type, double b, double h,
        IReadOnlyDictionary<string, double> values, IReadOnlyDictionary<string, double> confirmed,
        IReadOnlyList<IReadOnlyList<PreviewSegment>> contours)
    {
        Point P(double y, double z) => new(y, z);
        double t=values.GetValueOrDefault("t"), tw=values.GetValueOrDefault("tw"), tf=values.GetValueOrDefault("tf"), r=values.GetValueOrDefault("r");
        double right=(b+tw)/2;
        Point Anchor(string key) => key switch
        {
            "tw" => P(type == SectionEditorType.USection ? tw / 2 : b / 2, h / 2),
            "tf" => P(b * .85, type == SectionEditorType.TSection ? h - tf / 2 : tf / 2),
            "t" => P(b - t / 2, type == SectionEditorType.Angle ? t / 2 : h / 2),
            "r" => type switch
            {
                SectionEditorType.RectangularHollow => P(b - r + r / Math.Sqrt(2), h - r + r / Math.Sqrt(2)),
                SectionEditorType.Angle => P(t + r - r / Math.Sqrt(2), t + r - r / Math.Sqrt(2)),
                SectionEditorType.TSection => P(right + r - r / Math.Sqrt(2), h - tf - r + r / Math.Sqrt(2)),
                _ => P((type == SectionEditorType.USection ? tw : right) + r - r / Math.Sqrt(2), tf + r - r / Math.Sqrt(2))
            }, _ => P(b / 2, h / 2)
        };
        var dimensions = ParameterKeys(type).ToDictionary(k => k, k => new PreviewDimension(k, values[k], confirmed.ContainsKey(k), Anchor(k)));
        return new(type, b, h, contours, new ReadOnlyDictionary<string, PreviewDimension>(dimensions));
    }

    /// <summary>Use the authoritative contours and orientation only after all real inputs pass Core validation.</summary>
    public static SectionPreviewGeometry FromSection(SectionEditorType type, IReadOnlyDictionary<string, double> values,
        IParametricSectionDefinition section)
    {
        double width=values.GetValueOrDefault("b", values.GetValueOrDefault("d", values.GetValueOrDefault("D")));
        double height=values.GetValueOrDefault("h", width);
        return FromSection(DrawingData(type, width, height, values, values, []), section);
    }
    private static SectionPreviewGeometry FromSection(SectionPreviewGeometry preview, IParametricSectionDefinition section)
    {
        IReadOnlyList<PreviewSegment> Convert(SectionContour contour) => contour.Segments.Select(s =>
            s is SectionArc arc ? new PreviewSegment(PointOf(s.Start), PointOf(s.End), PointOf(arc.Center), arc.SweepRadians)
                : new PreviewSegment(PointOf(s.Start), PointOf(s.End))).ToArray();
        return preview with
        {
            Contours = new[] { section.Geometry.OuterContour }.Concat(section.Geometry.Holes).Select(Convert).ToArray(),
            Centroid = PointOf(section.GeometryProperties.Centroid),
            PrincipalAngle = section.GeometryProperties.PrincipalAxisAngleRadians
        };
    }
    private static Point PointOf(SectionPoint p) => new(p.Y.Meters, p.Z.Meters);
    private static void Require(bool condition, string key) { if (!condition) throw new PreviewGeometryException(key); }
    private static IReadOnlyList<PreviewSegment> Circle(double y, double z, double r)
    {
        var points = new[] { new Point(y+r,z), new Point(y,z+r), new Point(y-r,z), new Point(y,z-r) };
        return points.Select((p,i) => new PreviewSegment(p, points[(i+1)%4], new(y,z), Math.PI/2)).ToArray();
    }
    // Graphic fillets only: no topology or mechanical integration. Completed sections use Core contours instead.
    private static IReadOnlyList<PreviewSegment> RoundPolygon(Point[] vertices, double[] radii)
    {
        int n = vertices.Length;
        var entries = new Point[n]; var exits = new Point[n]; var centers = new Point[n]; var sweeps = new double[n];
        for (int i = 0; i < n; i++)
        {
            Vector incoming = vertices[i] - vertices[(i+n-1)%n]; Vector outgoing = vertices[(i+1)%n] - vertices[i];
            incoming /= incoming.Length; outgoing /= outgoing.Length;
            entries[i] = vertices[i] - incoming * radii[i]; exits[i] = vertices[i] + outgoing * radii[i];
            centers[i] = vertices[i] + (outgoing-incoming)*radii[i];
            sweeps[i] = Math.Sign(incoming.X*outgoing.Y-incoming.Y*outgoing.X)*Math.PI/2;
        }
        var segments = new List<PreviewSegment>();
        for (int i = 0; i < n; i++)
        {
            if (radii[i] > 0) segments.Add(new(entries[i], exits[i], centers[i], sweeps[i]));
            var next = (i+1)%n;
            if (((Vector)(entries[next]-exits[i])).Length > 0) segments.Add(new(exits[i], entries[next]));
        }
        return segments.AsReadOnly();
    }
}
