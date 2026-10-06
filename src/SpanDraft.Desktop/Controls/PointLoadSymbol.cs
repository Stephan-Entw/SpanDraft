using Avalonia;
using Avalonia.Media;
using SpanDraft.Desktop.State;
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
            Y - SchematicMetrics.ForceHeight - PointLoadSymbol.HitPadding,
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

    public static string Label(PointLoadPreview preview, string name) => name.Trim() + " = " +
        UiNumbers.Format(preview.Value) + (preview.Kind == PointLoadKind.Force ? " N" : " Nm");

    public static Point ForceTip(double value) => new(0, value > 0 ? -SchematicMetrics.ForceHeight : 0);
    public static double MomentSweep(double value) => Math.Sign(value) * Math.PI * 1.5;
    public static Point MomentTip(bool positive) => positive ? new(0, SchematicMetrics.MomentRadius) : new(SchematicMetrics.MomentRadius, 0);

    public static void Draw(DrawingContext context, PointLoadGlyph glyph, IBrush? brush)
    {
        var pen = new Pen(brush, SchematicMetrics.SymbolStrokeWidth, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        Point At(Point point) => new(glyph.X + point.X, glyph.Y + point.Y);
        void Line(Point start, Point end) => context.DrawLine(pen, At(start), At(end));
        void Arrow(Point tip, Vector direction, double halfWidth)
        {
            var normal = new Vector(-direction.Y, direction.X);
            Line(tip - direction * SchematicMetrics.ArrowHeadLength + normal * halfWidth, tip);
            Line(tip - direction * SchematicMetrics.ArrowHeadLength - normal * halfWidth, tip);
        }
        if (glyph.Kind == PointLoadKind.Force)
        {
            Line(new(0, -SchematicMetrics.ForceHeight), new(0, 0));
            if (glyph.Positive) Arrow(ForceTip(1), new(0, -1), SchematicMetrics.ForceArrowHalfWidth);
            if (glyph.Negative) Arrow(ForceTip(-1), new(0, 1), SchematicMetrics.ForceArrowHalfWidth);
        }
        else
        {
            // One upper/left 3/4-circle from 3 o'clock to 6 o'clock. Both signs
            // use this same arc, with opposite terminal tangents.
            var geometry = new StreamGeometry();
            using (var path = geometry.Open())
            {
                path.BeginFigure(At(new(SchematicMetrics.MomentRadius, 0)), false);
                for (int i = 1; i <= 48; i++)
                {
                    double angle = 1.5 * Math.PI * i / 48;
                    path.LineTo(At(new(SchematicMetrics.MomentRadius * Math.Cos(angle), -SchematicMetrics.MomentRadius * Math.Sin(angle))));
                }
                path.EndFigure(false);
            }
            context.DrawGeometry(null, pen, geometry);
            if (glyph.Positive) Arrow(MomentTip(true), new(1, 0), SchematicMetrics.MomentArrowHalfWidth);
            if (glyph.Negative) Arrow(MomentTip(false), new(0, 1), SchematicMetrics.MomentArrowHalfWidth);
        }
    }
}
