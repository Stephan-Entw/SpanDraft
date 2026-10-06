using System.Globalization;
using Avalonia;
using Avalonia.Media;
using SpanDraft.Desktop.State;

namespace SpanDraft.Desktop.Controls;

public sealed record PointLoadVisual(Guid? Id, PointLoadPreview Preview, double X, double Y, bool IsPreview);

/// <summary>Shared fixed-DIP layout, drawing and hit zones; never changes physical positions.</summary>
public static class PointLoadSymbol
{
    public const double HalfSize = 18;
    public const double LaneSpacing = 50;
    public const double HitPadding = 5;

    public static double MinimumHeight(IReadOnlyList<EditorPointLoad> loads,
        PointLoadPreview? preview = null, Guid? hiddenId = null, bool reserveAdditionalLane = false)
    {
        var positions = loads.Select(l => l.Id == hiddenId && preview is not null ? preview.Position : l.Position).ToList();
        if (preview is not null && !loads.Any(l => l.Id == hiddenId)) positions.Add(preview.Position);
        int lanes = positions.GroupBy(p => p).Select(g => g.Count()).DefaultIfEmpty(0).Max();
        if (reserveAdditionalLane) lanes++;
        // BeamViewport centers the beam and places the dimension 40 DIPs above it.
        // Add enough top space for every lane, including its symbol and hit padding.
        return lanes == 0 ? 0 : 2 * (40 + 46 + (lanes - 1) * LaneSpacing + HalfSize + HitPadding + 8);
    }

    public static IReadOnlyList<PointLoadVisual> Layout(IReadOnlyList<EditorPointLoad> loads, BeamViewport viewport,
        PointLoadPreview? preview = null, Guid? hiddenId = null)
    {
        var visuals = new List<PointLoadVisual>();
        // Reserve the original document slot while editing. Value text therefore
        // cannot change either the symbol lane or the flyout anchor.
        foreach (var load in loads)
        {
            var p = load.Id == hiddenId && preview is not null
                ? preview : new PointLoadPreview(load.Position, load.Kind, load.Value);
            Add(load.Id, p, load.Id == hiddenId);
        }
        if (preview is not null && !loads.Any(l => l.Id == hiddenId)) Add(null, preview, true);
        return visuals.AsReadOnly();

        void Add(Guid? id, PointLoadPreview p, bool draft)
        {
            int lane = visuals.Count(v => v.Preview.Position == p.Position);
            visuals.Add(new(id, p, viewport.BeamToScreen(p.Position.Meters),
                viewport.DimensionY - 46 - lane * LaneSpacing, draft));
        }
    }

    public static bool Contains(PointLoadVisual visual, double x, double y) =>
        Math.Abs(x - visual.X) <= HalfSize + HitPadding && Math.Abs(y - visual.Y) <= HalfSize + HitPadding;

    public static Guid? HitTest(IReadOnlyList<PointLoadVisual> visuals, double x, double y)
    {
        Guid? hit = null;
        double nearest = double.PositiveInfinity;
        foreach (var visual in visuals)
        {
            double dx = x - visual.X, dy = y - visual.Y;
            double distance = dx * dx + dy * dy;
            if (visual.Id is { } id && Contains(visual, x, y) && distance < nearest)
            {
                hit = id;
                nearest = distance;
            }
        }
        return hit;
    }

    public static string Label(PointLoadPreview preview) =>
        UiNumbers.Format(preview.Value) + (preview.Kind == PointLoadKind.Force ? " N" : " Nm");

    public static Point ForceTip(double value) => new(0, value > 0 ? -HalfSize : HalfSize);
    public static double MomentSweep(double value) => Math.Sign(value) * Math.PI * 1.5;

    public static void Draw(DrawingContext context, PointLoadVisual visual, BeamViewport viewport,
        IBrush? brush, IBrush? guideBrush, Typeface typeface, double fontSize)
    {
        var pen = new Pen(brush, 1.5, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        Point At(Point point) => new(visual.X + point.X, visual.Y + point.Y);
        void Line(Point start, Point end) => context.DrawLine(pen, At(start), At(end));
        void Arrow(Point tip, Vector direction)
        {
            var normal = new Vector(-direction.Y, direction.X);
            Line(tip - direction * 8 + normal * 4, tip);
            Line(tip - direction * 8 - normal * 4, tip);
        }

        using (context.PushOpacity(0.5))
            context.DrawLine(new Pen(guideBrush, 1, dashStyle: DashStyle.Dash),
                new(visual.X, visual.Y + HalfSize + 5), new(visual.X, viewport.BeamY));
        double value = visual.Preview.Value;
        if (visual.Preview.Kind == PointLoadKind.Force)
        {
            Line(new(0, -HalfSize), new(0, HalfSize));
            if (value != 0) Arrow(ForceTip(value), new(0, value > 0 ? -1 : 1));
        }
        else if (value == 0)
            context.DrawEllipse(null, pen, new(visual.X, visual.Y), 15, 15);
        else
        {
            double sweep = MomentSweep(value);
            Point Circle(double angle) => new(15 * Math.Cos(angle), -15 * Math.Sin(angle));
            double start = Math.PI / 4;
            var geometry = new StreamGeometry();
            using (var path = geometry.Open())
            {
                path.BeginFigure(At(Circle(start)), false);
                for (int i = 1; i <= 48; i++) path.LineTo(At(Circle(start + sweep * i / 48)));
                path.EndFigure(false);
            }
            context.DrawGeometry(null, pen, geometry);
            double end = start + sweep;
            Arrow(Circle(end), new(-Math.Sin(end) * Math.Sign(value), -Math.Cos(end) * Math.Sign(value)));
        }
        var text = new FormattedText(Label(visual.Preview), CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight, typeface, fontSize, brush);
        // Keep endpoint labels inside the canvas without changing the symbol or hit zone.
        double labelX = visual.X + 28;
        if (labelX + text.Width > viewport.Right + 60) labelX = visual.X - 28 - text.Width;
        context.DrawText(text, new(labelX, visual.Y - text.Height / 2));
    }
}
