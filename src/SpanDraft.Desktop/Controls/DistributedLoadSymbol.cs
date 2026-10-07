using Avalonia;
using Avalonia.Media;
using SpanDraft.Desktop.Layout;
using SpanDraft.Desktop.State;

namespace SpanDraft.Desktop.Controls;

public sealed record DistributedLoadVisual(Guid? Id, DistributedLoadPreview Preview, string Name,
    double StartX, double EndX, double BeamY, bool IsPreview)
{
    public double TopY => BeamY - SchematicMetrics.DistributedTopOffset;
    public double BottomY => BeamY - SchematicMetrics.ForceBeamOffset;
    public double CenterX => StartX + (EndX - StartX) / 2;
    public Rect FillBounds => new(StartX, TopY, EndX - StartX, BeamY - SchematicMetrics.BeamStrokeWidth / 2 - TopY);
    public Rect Bounds => new Rect(StartX, TopY, EndX - StartX, BottomY - TopY).Inflate(DistributedLoadSymbol.HitPadding);
}

/// <summary>A shared interior shaft; entities remain separate for labels, selection and endpoint drags.</summary>
public sealed record DistributedLoadInnerGlyph(double X, IReadOnlyList<DistributedLoadVisual> Entities)
{
    public bool Positive => Entities.Any(e => e.Preview.Intensity > 0);
    public bool Negative => Entities.Any(e => e.Preview.Intensity < 0);
    public bool IsPreview => Entities.Any(e => e.IsPreview);
}

/// <summary>Each load has its own symbol geometry; arrow spacing is exclusively in screen space.</summary>
public static class DistributedLoadSymbol
{
    public const double HitPadding = PointLoadSymbol.HitPadding;

    public static IReadOnlyList<DistributedLoadVisual> Layout(IReadOnlyList<EditorUniformDistributedLoad> loads,
        BeamLayoutFrame frame, DistributedLoadPreview? preview = null, Guid? hiddenId = null, string? name = null)
    {
        var visuals = new List<DistributedLoadVisual>();
        foreach (var load in loads)
        {
            bool draft = load.Id == hiddenId && preview is not null;
            Add(load.Id, draft ? preview! : new(load.StartPosition, load.EndPosition, load.Intensity.NewtonsPerMeter),
                draft ? name ?? load.Name : load.Name, draft);
        }
        if (preview is not null && !loads.Any(l => l.Id == hiddenId)) Add(null, preview, name ?? "", true);
        return visuals.AsReadOnly();
        void Add(Guid? id, DistributedLoadPreview p, string label, bool draft) => visuals.Add(new(id, p, label.Trim(),
            frame.Layout.Transform.PhysicalToScreen(p.StartPosition.Meters),
            frame.Layout.Transform.PhysicalToScreen(p.EndPosition.Meters), frame.Viewport.BeamY, draft));
    }

    public static int IntervalCount(double width)
    {
        if (!double.IsFinite(width) || width < 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (width < SchematicMetrics.DistributedMinimumWidth) return 1;
        int maximum = (int)Math.Min(int.MaxValue, Math.Floor(width / SchematicMetrics.DistributedMinimumWidth));
        int lower = (int)Math.Clamp(Math.Floor(width / SchematicMetrics.DistributedTargetSpacing), 1, maximum);
        int upper = Math.Min(maximum, lower == int.MaxValue ? lower : lower + 1);
        return Math.Abs(width / lower - SchematicMetrics.DistributedTargetSpacing)
            <= Math.Abs(width / upper - SchematicMetrics.DistributedTargetSpacing) ? lower : upper;
    }

    public static IReadOnlyList<double> ArrowPositions(DistributedLoadVisual visual)
    {
        int intervals = IntervalCount(visual.EndX - visual.StartX);
        return Array.AsReadOnly(Enumerable.Range(0, intervals + 1).Select(i => i == intervals ? visual.EndX
            : visual.StartX + (visual.EndX - visual.StartX) * ((double)i / intervals)).ToArray());
    }

    public static IReadOnlyList<DistributedLoadInnerGlyph> InnerGlyphs(IReadOnlyList<DistributedLoadVisual> visuals)
    {
        var glyphs = new List<DistributedLoadInnerGlyph>();
        foreach (var span in Spans(visuals))
        {
            int intervals = IntervalCount(span.End - span.Start);
            for (int i = 1; i < intervals; i++)
                glyphs.Add(new(span.Start + (span.End - span.Start) * ((double)i / intervals), span.Entities));
        }
        return glyphs.AsReadOnly();
    }

    private static IEnumerable<(double Start, double End, IReadOnlyList<DistributedLoadVisual> Entities)>
        Spans(IReadOnlyList<DistributedLoadVisual> visuals)
    {
        var endpoints = visuals.SelectMany(v => new[] { v.StartX, v.EndX }).Distinct().Order().ToArray();
        for (int i = 1; i < endpoints.Length; i++)
        {
            double start = endpoints[i - 1], end = endpoints[i];
            var entities = visuals.Where(v => v.StartX <= start && v.EndX >= end).ToArray();
            if (entities.Length > 0) yield return (start, end, Array.AsReadOnly(entities));
        }
    }

    public static DistributedLoadEndpoint? HitEndpoint(DistributedLoadVisual visual, double x, double y)
    {
        bool Contains(double anchor) => new Rect(anchor - SchematicMetrics.DistributedArrowHeadHalfWidth,
            visual.TopY, 2 * SchematicMetrics.DistributedArrowHeadHalfWidth, SchematicMetrics.DistributedArrowHeight)
            .Inflate(HitPadding).Contains(new Point(x, y));
        bool start = Contains(visual.StartX), end = Contains(visual.EndX);
        return start && (!end || Math.Abs(x - visual.StartX) <= Math.Abs(x - visual.EndX)) ? DistributedLoadEndpoint.Start
            : end ? DistributedLoadEndpoint.End : null;
    }

    public static bool Contains(DistributedLoadVisual visual, double x, double y) =>
        HitEndpoint(visual, x, y) is not null || visual.Bounds.Contains(new Point(x, y));

    public static string Label(DistributedLoadPreview preview, string name) =>
        name.Trim() + " = " + UiNumbers.Format(preview.Intensity) + " N/m";

    public static void DrawFill(DrawingContext context, DistributedLoadVisual visual, IBrush? neutralBrush)
    {
        using (context.PushOpacity(SchematicMetrics.DistributedFillOpacity))
            context.DrawRectangle(neutralBrush, null, visual.FillBounds);
    }

    public static void DrawFills(DrawingContext context, IReadOnlyList<DistributedLoadVisual> visuals, IBrush? neutralBrush)
    {
        foreach (var span in Spans(visuals))
        {
            var bounds = span.Entities[0].FillBounds;
            using (context.PushOpacity(SchematicMetrics.DistributedFillOpacityForCount(span.Entities.Count)))
                context.DrawRectangle(neutralBrush, null, new Rect(span.Start, bounds.Top, span.End - span.Start, bounds.Height));
        }
    }

    public static void Draw(DrawingContext context, DistributedLoadVisual visual, IBrush? brush)
    {
        var pen = SymbolPen(brush);
        context.DrawLine(pen, new(visual.StartX, visual.TopY), new(visual.EndX, visual.TopY));
        foreach (double x in ArrowPositions(visual))
            DrawArrow(context, x, visual, visual.Preview.Intensity > 0, visual.Preview.Intensity < 0, brush);
    }

    public static void DrawEndpoints(DrawingContext context, DistributedLoadVisual visual, IBrush? brush)
    {
        context.DrawLine(SymbolPen(brush), new(visual.StartX, visual.TopY), new(visual.EndX, visual.TopY));
        DrawArrow(context, visual.StartX, visual, visual.Preview.Intensity > 0, visual.Preview.Intensity < 0, brush);
        DrawArrow(context, visual.EndX, visual, visual.Preview.Intensity > 0, visual.Preview.Intensity < 0, brush);
    }

    public static void DrawInner(DrawingContext context, DistributedLoadInnerGlyph glyph, IBrush? brush) =>
        DrawArrow(context, glyph.X, glyph.Entities[0], glyph.Positive, glyph.Negative, brush);

    private static Pen SymbolPen(IBrush? brush) => new(brush, SchematicMetrics.SymbolStrokeWidth,
        lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);

    private static void DrawArrow(DrawingContext context, double x, DistributedLoadVisual visual,
        bool positive, bool negative, IBrush? brush)
    {
        var pen = SymbolPen(brush);
        context.DrawLine(pen, new(x, visual.TopY), new(x, visual.BottomY));
        if (positive) Head(-1);
        if (negative) Head(1);
        void Head(double direction)
        {
            double tip = direction > 0 ? visual.BottomY : visual.TopY;
            var geometry = new StreamGeometry();
            using (var path = geometry.Open())
            {
                path.BeginFigure(new(x, tip), true);
                path.LineTo(new(x - SchematicMetrics.DistributedArrowHeadHalfWidth,
                    tip - direction * SchematicMetrics.DistributedArrowHeadLength));
                path.LineTo(new(x + SchematicMetrics.DistributedArrowHeadHalfWidth,
                    tip - direction * SchematicMetrics.DistributedArrowHeadLength));
                path.EndFigure(true);
            }
            context.DrawGeometry(brush, pen, geometry);
        }
    }
}
