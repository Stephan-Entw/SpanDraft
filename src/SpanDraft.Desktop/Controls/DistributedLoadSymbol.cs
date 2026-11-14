using Avalonia;
using Avalonia.Media;
using SpanDraft.Desktop.Layout;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.Presentation;

namespace SpanDraft.Desktop.Controls;

public sealed record DistributedLoadVisual(Guid? Id, DistributedLoadPreview Preview, string Name,
    double StartX, double EndX, double BeamY, bool IsPreview)
{
    public double SymbolScale { get; init; } = 1;
    public bool CanDragEndpoints { get; init; } = true;
    public bool IsSelfWeight { get; init; }
    public double TopY => BeamY - SchematicMetrics.DistributedTopOffset * SymbolScale;
    public double BottomY => BeamY - SchematicMetrics.ForceBeamOffset * SymbolScale;
    public double CenterX => StartX + (EndX - StartX) / 2;

    /// <summary>A load whose end intensities differ; it is drawn with an envelope and never merged with others.</summary>
    public bool IsLinear => !Preview.IsConstant;

    /// <summary>The envelope height at a fraction of the width, proportional to |q| over the larger end magnitude.</summary>
    public double EnvelopeHeightAt(double fraction)
    {
        if (!IsLinear) return BottomY - TopY;
        double a = Math.Abs(Preview.StartIntensity), b = Math.Abs(Preview.EndIntensity), peak = Math.Max(a, b);
        double q = Preview.StartIntensity + (Preview.EndIntensity - Preview.StartIntensity) * fraction;
        return (BottomY - TopY) * Math.Abs(q) / peak;
    }

    public double EnvelopeTopYAt(double fraction) => BottomY - EnvelopeHeightAt(fraction);
    public double StartTopY => EnvelopeTopYAt(0);
    public double EndTopY => EnvelopeTopYAt(1);

    /// <summary>The envelope corner points from start to end; a sign change adds the zero crossing on the arrow base line.</summary>
    public IReadOnlyList<Point> EnvelopePoints()
    {
        if (!IsLinear) return [new(StartX, TopY), new(EndX, TopY)];
        var points = new List<Point> { new(StartX, StartTopY) };
        if (Preview.StartIntensity * Preview.EndIntensity < 0)
        {
            double share = Math.Abs(Preview.StartIntensity) / (Math.Abs(Preview.StartIntensity) + Math.Abs(Preview.EndIntensity));
            points.Add(new(StartX + (EndX - StartX) * share, BottomY));
        }
        points.Add(new(EndX, EndTopY));
        return points;
    }

    public Rect FillBounds => new(StartX, TopY, EndX - StartX, BeamY - SchematicMetrics.BeamStrokeWidth / 2 - TopY);
    public double FillBottomY => BeamY - SchematicMetrics.BeamStrokeWidth / 2;
    public double HighestTopY => IsLinear ? Math.Min(StartTopY, EndTopY) : TopY;
    public Rect Bounds => new Rect(StartX, HighestTopY, EndX - StartX, BottomY - HighestTopY).Inflate(DistributedLoadSymbol.HitPadding);
}

/// <summary>A shared interior shaft; entities remain separate for labels, selection and endpoint drags.</summary>
public sealed record DistributedLoadInnerGlyph(double X, IReadOnlyList<DistributedLoadVisual> Entities)
{
    public bool Positive => Entities.Any(e => e.Preview.StartIntensity > 0);
    public bool Negative => Entities.Any(e => e.Preview.StartIntensity < 0);
    public bool IsPreview => Entities.Any(e => e.IsPreview);
}

/// <summary>Each load has its own symbol geometry; arrow spacing is exclusively in screen space.</summary>
public static class DistributedLoadSymbol
{
    public const double HitPadding = PointLoadSymbol.HitPadding;

    public static IReadOnlyList<DistributedLoadVisual> Layout(IReadOnlyList<EditorDistributedLoad> loads,
        BeamLayoutFrame frame, DistributedLoadPreview? preview = null, Guid? hiddenId = null, string? name = null, double symbolScale = 1)
    {
        LayoutNumbers.Positive(symbolScale, nameof(symbolScale));
        var visuals = new List<DistributedLoadVisual>();
        foreach (var load in loads)
        {
            bool draft = load.Id == hiddenId && preview is not null;
            Add(load.Id, draft ? preview! : new(load.StartPosition, load.EndPosition, load.StartIntensity.NewtonsPerMeter,
                load.EndIntensity.NewtonsPerMeter),
                draft ? name ?? load.Name : load.Name, draft);
        }
        if (preview is not null && !loads.Any(l => l.Id == hiddenId)) Add(null, preview, name ?? "", true);
        return visuals.AsReadOnly();
        void Add(Guid? id, DistributedLoadPreview p, string label, bool draft) =>
            visuals.Add(CreateVisual(id, p, label, frame, draft, symbolScale));
    }

    public static DistributedLoadVisual CreateVisual(Guid? id, DistributedLoadPreview preview, string name,
        BeamLayoutFrame frame, bool isPreview = false, double symbolScale = 1) => new(id, preview, name.Trim(),
            frame.Layout.Transform.PhysicalToScreen(preview.StartPosition.Meters),
            frame.Layout.Transform.PhysicalToScreen(preview.EndPosition.Meters), frame.Viewport.BeamY, isPreview)
            { SymbolScale = symbolScale };

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

    public static IReadOnlyList<DistributedLoadInnerGlyph> InnerGlyphs(IReadOnlyList<DistributedLoadVisual> visuals,
        IEnumerable<double>? fixedPositions = null)
    {
        var glyphs = new List<DistributedLoadInnerGlyph>();
        foreach (var span in Spans(visuals, fixedPositions))
        {
            int intervals = IntervalCount(span.End - span.Start);
            for (int i = 1; i < intervals; i++)
                glyphs.Add(new(span.Start + (span.End - span.Start) * ((double)i / intervals), span.Entities));
        }
        return glyphs.AsReadOnly();
    }

    private static IEnumerable<(double Start, double End, IReadOnlyList<DistributedLoadVisual> Entities)>
        Spans(IReadOnlyList<DistributedLoadVisual> visuals, IEnumerable<double>? fixedPositions = null)
    {
        // Linear loads have their own arrows and fill and neither share shafts nor blend into spans.
        visuals = visuals.Where(v => !v.IsLinear).ToArray();
        var endpoints = visuals.SelectMany(v => new[] { v.StartX, v.EndX })
            .Concat(fixedPositions ?? []).Distinct().Order().ToArray();
        for (int i = 1; i < endpoints.Length; i++)
        {
            double start = endpoints[i - 1], end = endpoints[i];
            var entities = visuals.Where(v => v.StartX <= start && v.EndX >= end).ToArray();
            if (entities.Length > 0) yield return (start, end, Array.AsReadOnly(entities));
        }
    }

    public static DistributedLoadEndpoint? HitEndpoint(DistributedLoadVisual visual, double x, double y)
    {
        if (!visual.CanDragEndpoints) return null;
        // The handle covers the arrow at that end; an end without intensity keeps a handle on the envelope base.
        bool Contains(double anchor, double top)
        {
            double minimum = 2 * SchematicMetrics.DistributedArrowHeadHalfWidth * visual.SymbolScale;
            double height = Math.Max(visual.BottomY - top, minimum);
            return new Rect(anchor - SchematicMetrics.DistributedArrowHeadHalfWidth * visual.SymbolScale, visual.BottomY - height,
                2 * SchematicMetrics.DistributedArrowHeadHalfWidth * visual.SymbolScale, height).Inflate(HitPadding).Contains(new Point(x, y));
        }
        bool start = Contains(visual.StartX, visual.StartTopY), end = Contains(visual.EndX, visual.EndTopY);
        return start && (!end || Math.Abs(x - visual.StartX) <= Math.Abs(x - visual.EndX)) ? DistributedLoadEndpoint.Start
            : end ? DistributedLoadEndpoint.End : null;
    }

    public static bool Contains(DistributedLoadVisual visual, double x, double y)
    {
        if (HitEndpoint(visual, x, y) is not null) return true;
        if (!visual.Bounds.Contains(new Point(x, y))) return false;
        if (!visual.IsLinear) return true;
        // Below the envelope only; the empty corner above a triangle belongs to nothing.
        double fraction = visual.EndX > visual.StartX ? Math.Clamp((x - visual.StartX) / (visual.EndX - visual.StartX), 0, 1) : 0;
        return y >= visual.EnvelopeTopYAt(fraction) - HitPadding;
    }

    public static string Label(DistributedLoadPreview preview, string name, ResultPresentationOptions? options = null)
    {
        options ??= ResultPresentationOptions.Default;
        var unit = options.Profile[QuantityKind.DistributedLoad];
        return preview.IsConstant
            ? QuantityFormatter.FormatInputEquation(name.Trim(), preview.StartIntensity, unit, options)
            : QuantityFormatter.FormatInputRangeEquation(name.Trim(), preview.StartIntensity, preview.EndIntensity, unit, options);
    }

    /// <summary>A user load's intensity is given; the self-weight intensity is a calculated result.</summary>
    public static string Label(DistributedLoadVisual visual, ResultPresentationOptions options) => visual.IsSelfWeight
        ? SelfWeightLabel(visual.Name, visual.Preview.StartIntensity, options)
        : Label(visual.Preview, visual.Name, options);

    public static string SelfWeightLabel(string name, double newtonsPerMeter, ResultPresentationOptions options) =>
        name.Trim() + " = " + QuantityFormatter.FormatResult(newtonsPerMeter, QuantityKind.DistributedLoad, options);

    public static void DrawFill(DrawingContext context, DistributedLoadVisual visual, IBrush? neutralBrush)
    {
        if (visual.IsSelfWeight) return;
        using (context.PushOpacity(SchematicMetrics.DistributedFillOpacity))
            if (visual.IsLinear) context.DrawGeometry(neutralBrush, null, FillGeometry(visual));
            else context.DrawRectangle(neutralBrush, null, visual.FillBounds);
    }

    public static void DrawFills(DrawingContext context, IReadOnlyList<DistributedLoadVisual> visuals, IBrush? neutralBrush)
    {
        foreach (var span in Spans(visuals.Where(v => !v.IsSelfWeight).ToArray()))
        {
            var bounds = span.Entities[0].FillBounds;
            using (context.PushOpacity(SchematicMetrics.DistributedFillOpacityForCount(span.Entities.Count)))
                context.DrawRectangle(neutralBrush, null, new Rect(span.Start, bounds.Top, span.End - span.Start, bounds.Height));
        }
        foreach (var visual in visuals.Where(v => v.IsLinear && !v.IsSelfWeight))
            DrawFill(context, visual, neutralBrush);
    }

    /// <summary>The outline of the area between the beam and the envelope, which touches the base line at a sign change.</summary>
    public static IReadOnlyList<Point> FillPoints(DistributedLoadVisual visual) =>
        [new(visual.StartX, visual.FillBottomY), .. visual.EnvelopePoints(), new(visual.EndX, visual.FillBottomY)];

    private static Geometry FillGeometry(DistributedLoadVisual visual)
    {
        var geometry = new StreamGeometry();
        using var path = geometry.Open();
        var points = FillPoints(visual);
        path.BeginFigure(points[0], true);
        foreach (var point in points.Skip(1)) path.LineTo(point);
        path.EndFigure(true);
        return geometry;
    }

    public static void Draw(DrawingContext context, DistributedLoadVisual visual, IBrush? brush)
    {
        DrawEnvelope(context, visual, brush);
        foreach (double x in ArrowPositions(visual)) DrawLinearArrow(context, x, visual, brush);
    }

    public static void DrawEndpoints(DrawingContext context, DistributedLoadVisual visual, IBrush? brush)
    {
        DrawEnvelope(context, visual, brush);
        if (visual.IsLinear)
        {
            // Interior arrows have the height and direction of the intensity there.
            foreach (double x in ArrowPositions(visual)) DrawLinearArrow(context, x, visual, brush);
            return;
        }
        DrawArrow(context, visual.StartX, visual, visual.Preview.StartIntensity > 0, visual.Preview.StartIntensity < 0, brush);
        DrawArrow(context, visual.EndX, visual, visual.Preview.StartIntensity > 0, visual.Preview.StartIntensity < 0, brush);
    }

    private static void DrawEnvelope(DrawingContext context, DistributedLoadVisual visual, IBrush? brush)
    {
        var points = visual.EnvelopePoints();
        var pen = SymbolPen(brush, visual.SymbolScale);
        for (int i = 1; i < points.Count; i++) context.DrawLine(pen, points[i - 1], points[i]);
    }

    /// <summary>The arrow height at x, or 0 when it would be shorter than its head and is therefore omitted.</summary>
    public static double LinearArrowHeight(DistributedLoadVisual visual, double x)
    {
        double fraction = visual.EndX > visual.StartX ? Math.Clamp((x - visual.StartX) / (visual.EndX - visual.StartX), 0, 1) : 0;
        double height = visual.EnvelopeHeightAt(fraction);
        return height >= SchematicMetrics.DistributedArrowHeadLength * visual.SymbolScale ? height : 0;
    }

    public static int LinearArrowSign(DistributedLoadVisual visual, double x)
    {
        double fraction = visual.EndX > visual.StartX ? Math.Clamp((x - visual.StartX) / (visual.EndX - visual.StartX), 0, 1) : 0;
        return Math.Sign(visual.Preview.StartIntensity + (visual.Preview.EndIntensity - visual.Preview.StartIntensity) * fraction);
    }

    private static void DrawLinearArrow(DrawingContext context, double x, DistributedLoadVisual visual, IBrush? brush)
    {
        double height = LinearArrowHeight(visual, x);
        if (height == 0) return;
        int sign = LinearArrowSign(visual, x);
        DrawArrow(context, x, visual, sign > 0, sign < 0, brush, visual.BottomY - height);
    }

    public static void DrawInner(DrawingContext context, DistributedLoadInnerGlyph glyph, IBrush? brush) =>
        DrawArrow(context, glyph.X, glyph.Entities[0], glyph.Positive, glyph.Negative, brush);

    private static Pen SymbolPen(IBrush? brush, double scale) => new(brush, SchematicMetrics.SymbolStrokeWidth * scale,
        lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);

    private static void DrawArrow(DrawingContext context, double x, DistributedLoadVisual visual,
        bool positive, bool negative, IBrush? brush, double? topY = null)
    {
        var pen = SymbolPen(brush, visual.SymbolScale);
        double top = topY ?? visual.TopY;
        context.DrawLine(pen, new(x, top), new(x, visual.BottomY));
        if (positive) Head(-1);
        if (negative) Head(1);
        void Head(double direction)
        {
            double tip = direction > 0 ? visual.BottomY : top;
            var geometry = new StreamGeometry();
            using (var path = geometry.Open())
            {
                path.BeginFigure(new(x, tip), true);
                path.LineTo(new(x - SchematicMetrics.DistributedArrowHeadHalfWidth * visual.SymbolScale,
                    tip - direction * SchematicMetrics.DistributedArrowHeadLength * visual.SymbolScale));
                path.LineTo(new(x + SchematicMetrics.DistributedArrowHeadHalfWidth * visual.SymbolScale,
                    tip - direction * SchematicMetrics.DistributedArrowHeadLength * visual.SymbolScale));
                path.EndFigure(true);
            }
            context.DrawGeometry(brush, pen, geometry);
        }
    }
}
