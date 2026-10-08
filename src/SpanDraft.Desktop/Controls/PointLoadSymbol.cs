using Avalonia;
using Avalonia.Media;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Layout;

namespace SpanDraft.Desktop.Controls;

public sealed record PointLoadVisual(Guid? Id, PointLoadPreview Preview, string Name, double X, double Y, bool IsPreview);
public sealed record PointLoadGlyph(PointLoadKind Kind, double X, double Y, IReadOnlyList<PointLoadVisual> Entities)
{
    public bool Positive => Entities.Any(e => e.Preview.Value > 0);
    public bool Negative => Entities.Any(e => e.Preview.Value < 0);
    public bool IsPreview => Entities.Any(e => e.IsPreview);
    public Rect Bounds => Kind == PointLoadKind.Force
        ? new(X - PointLoadSymbol.HitPadding - SchematicMetrics.ForceArrowHalfWidth,
            Y - SchematicMetrics.ForceTopOffset - PointLoadSymbol.HitPadding,
            2 * (PointLoadSymbol.HitPadding + SchematicMetrics.ForceArrowHalfWidth),
            SchematicMetrics.ForceHeight + 2 * PointLoadSymbol.HitPadding)
        : new(X - PointLoadSymbol.HalfSize - PointLoadSymbol.HitPadding,
            Y - PointLoadSymbol.HalfSize - PointLoadSymbol.HitPadding,
            2 * (PointLoadSymbol.HalfSize + PointLoadSymbol.HitPadding),
            2 * (PointLoadSymbol.HalfSize + PointLoadSymbol.HitPadding));
}

/// <summary>Station-based technical glyph geometry; labels and physical values stay per entity.</summary>
public static class PointLoadSymbol
{
    public const double HalfSize = SchematicMetrics.PointLoadHalfSize;
    public const double HitPadding = 5;
    // Clock angles in degrees: 12 o'clock = 0, increasing clockwise.
    private const double MomentStartDegrees = 110;
    private const double MomentEndDegrees = 190;
    private const double MomentArrowTiltDegrees = 16;
    private const double MomentArcSweepDegrees = 360 - (MomentEndDegrees - MomentStartDegrees);
    private const int MomentArcSegments = 48;
    private const int CombinedMomentArcSegments = 59;

    public static IReadOnlyList<PointLoadVisual> Layout(IReadOnlyList<EditorPointLoad> loads, BeamLayoutFrame frame,
        PointLoadPreview? preview = null, Guid? hiddenId = null, string? previewName = null)
    {
        var visuals = new List<PointLoadVisual>();
        foreach (var load in loads)
        {
            bool draft = load.Id == hiddenId && preview is not null;
            Add(load.Id, draft ? preview! : new(load.Position, load.Kind, load.Value),
                draft ? previewName ?? load.Name : load.Name, draft);
        }
        if (preview is not null && !loads.Any(l => l.Id == hiddenId)) Add(null, preview, previewName ?? "", true);
        return visuals.AsReadOnly();
        void Add(Guid? id, PointLoadPreview p, string name, bool draft) =>
            visuals.Add(new(id, p, name, frame.Layout.Transform.PhysicalToScreen(p.Position.Meters), frame.Viewport.BeamY, draft));
    }

    public static IReadOnlyList<PointLoadGlyph> Group(IReadOnlyList<PointLoadVisual> visuals) =>
        Array.AsReadOnly(visuals.GroupBy(v => (v.Preview.Position, v.Preview.Kind))
            .OrderBy(g => g.Key.Position.Meters).ThenByDescending(g => g.Key.Kind)
            .Select(g => new PointLoadGlyph(g.Key.Kind, g.First().X, g.First().Y, Array.AsReadOnly(g.ToArray()))).ToArray());

    public static bool Contains(PointLoadVisual visual, double x, double y) =>
        new PointLoadGlyph(visual.Preview.Kind, visual.X, visual.Y, [visual]).Bounds.Contains(new Point(x, y));

    public static PointLoadGlyph? HitTestGlyph(IReadOnlyList<PointLoadGlyph> glyphs, double x, double y) =>
        glyphs.Where(g => g.Bounds.Contains(new Point(x, y)))
            .OrderBy(g => (g.X - x) * (g.X - x) + (g.Y - y) * (g.Y - y)).ThenBy(g => g.Kind).FirstOrDefault();

    public static Guid? ResolveEntity(PointLoadGlyph glyph, Guid? editedId = null) =>
        editedId is { } id && glyph.Entities.Any(e => e.Id == id) ? id
            : glyph.Entities.Count == 1 ? glyph.Entities[0].Id : null;

    public static Guid? HitTest(IReadOnlyList<PointLoadVisual> visuals, double x, double y, Guid? editedId = null) =>
        HitTestGlyph(Group(visuals), x, y) is { } glyph ? ResolveEntity(glyph, editedId) : null;

    public static string Label(PointLoadPreview preview, string name, UnitProfile? profile = null) =>
        name.Trim() + " = " + InputQuantityFormatter.WithUnit(preview.Value,
            (profile ?? UnitProfile.Default)[preview.Kind == PointLoadKind.Force
                ? QuantityKind.TransverseForce : QuantityKind.Moment]);

    public static Point ForceTip(double value) => new(0,
        value > 0 ? -SchematicMetrics.ForceTopOffset : -SchematicMetrics.ForceBeamOffset);
    public static double MomentSweep(double value) => Math.Sign(value) * Radians(MomentArcSweepDegrees);
    public static Point MomentTip(bool positive) => MirrorMomentPoint(MomentPoint(MomentEndDegrees), !positive);

    private static double Radians(double degrees) => degrees * Math.PI / 180;
    private static Point MirrorMomentPoint(Point point, bool mirror) => mirror ? new(-point.X, point.Y) : point;
    private static Point MomentPoint(double degrees)
    {
        double angle = Radians(degrees);
        return new(SchematicMetrics.MomentRadius * Math.Sin(angle), -SchematicMetrics.MomentRadius * Math.Cos(angle));
    }

    private static Vector MomentArrowDirection(bool positive)
    {
        double angle = Radians(MomentEndDegrees + MomentArrowTiltDegrees);
        var direction = MirrorMomentPoint(new(-Math.Cos(angle), -Math.Sin(angle)), !positive);
        return new(direction.X, direction.Y);
    }

    private static StreamGeometry CreateMomentArc(Point origin, bool positive, bool negative)
    {
        bool combined = positive && negative;
        bool mirror = negative && !positive;
        // The union runs from the mirrored arrow tip to the positive arrow tip,
        // counterclockwise over 340 degrees, without drawing overlaps twice.
        double start = combined ? 360 - MomentEndDegrees : MomentStartDegrees;
        double sweep = combined ? 360 - (MomentEndDegrees - start) : MomentArcSweepDegrees;
        int segments = combined ? CombinedMomentArcSegments : MomentArcSegments;
        Point At(double degrees)
        {
            var point = MirrorMomentPoint(MomentPoint(degrees), mirror);
            return new(origin.X + point.X, origin.Y + point.Y);
        }
        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            path.BeginFigure(At(start), false);
            for (int i = 1; i <= segments; i++)
                path.LineTo(At(start - sweep * i / segments));
            path.EndFigure(false);
        }
        return geometry;
    }

    public static void Draw(DrawingContext context, PointLoadGlyph glyph, IBrush? brush)
    {
        var pen = new Pen(brush, SchematicMetrics.SymbolStrokeWidth, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        Point At(Point point) => new(glyph.X + point.X, glyph.Y + point.Y);
        void Line(Point start, Point end) => context.DrawLine(pen, At(start), At(end));
        void Arrow(Point tip, Vector direction, double halfWidth)
        {
            var normal = new Vector(-direction.Y, direction.X);
            var geometry = new StreamGeometry();
            using (var path = geometry.Open())
            {
                path.BeginFigure(At(tip), true);
                path.LineTo(At(tip - direction * SchematicMetrics.ArrowHeadLength + normal * halfWidth));
                path.LineTo(At(tip - direction * SchematicMetrics.ArrowHeadLength - normal * halfWidth));
                path.EndFigure(true);
            }
            context.DrawGeometry(brush, pen, geometry);
        }
        if (glyph.Kind == PointLoadKind.Force)
        {
            Line(ForceTip(1), ForceTip(-1));
            if (glyph.Positive) Arrow(ForceTip(1), new(0, -1), SchematicMetrics.ForceArrowHalfWidth);
            if (glyph.Negative) Arrow(ForceTip(-1), new(0, 1), SchematicMetrics.ForceArrowHalfWidth);
        }
        else
        {
            context.DrawGeometry(null, pen, CreateMomentArc(new(glyph.X, glyph.Y), glyph.Positive, glyph.Negative));
            if (glyph.Positive) Arrow(MomentTip(true), MomentArrowDirection(true), SchematicMetrics.MomentArrowHalfWidth);
            if (glyph.Negative) Arrow(MomentTip(false), MomentArrowDirection(false), SchematicMetrics.MomentArrowHalfWidth);
        }
    }
}
