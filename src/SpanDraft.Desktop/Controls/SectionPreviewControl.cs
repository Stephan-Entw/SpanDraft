using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SpanDraft.Core.Sections;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.Sections;
using SpanDraft.Reporting.Notation;

namespace SpanDraft.Desktop.Controls;

/// <summary>Cross-section drawing only. All geometry and authoritative axis data arrive as drawing data.</summary>
public sealed class SectionPreviewControl : Control
{
    public static readonly StyledProperty<bool> CompactProperty = AvaloniaProperty.Register<SectionPreviewControl, bool>(nameof(Compact));
    public bool Compact { get => GetValue(CompactProperty); set => SetValue(CompactProperty, value); }
    public const double AxisDashLength = 20, AxisDashGap = 4, AxisDotLength = 1.6;
    public const double AxisSolidEndLength = 26;
    public static readonly StyledProperty<bool> ShowCanvasBoundsProperty = AvaloniaProperty.Register<SectionPreviewControl, bool>(nameof(ShowCanvasBounds));
    public static readonly StyledProperty<SectionPreviewGeometry?> GeometryProperty = AvaloniaProperty.Register<SectionPreviewControl, SectionPreviewGeometry?>(nameof(Geometry));
    public static readonly StyledProperty<SectionAxisDesignation?> SelectedAxisProperty = AvaloniaProperty.Register<SectionPreviewControl, SectionAxisDesignation?>(nameof(SelectedAxis));
    public static readonly StyledProperty<string?> HighlightedParameterProperty = AvaloniaProperty.Register<SectionPreviewControl, string?>(nameof(HighlightedParameter));
    public static readonly StyledProperty<UnitProfile> UnitProfileProperty = AvaloniaProperty.Register<SectionPreviewControl, UnitProfile>(nameof(UnitProfile), UnitProfile.Default);
    static SectionPreviewControl() => AffectsRender<SectionPreviewControl>(GeometryProperty, SelectedAxisProperty, HighlightedParameterProperty, UnitProfileProperty, ShowCanvasBoundsProperty, CompactProperty);
    public bool ShowCanvasBounds { get => GetValue(ShowCanvasBoundsProperty); set => SetValue(ShowCanvasBoundsProperty, value); }
    public SectionPreviewGeometry? Geometry { get => GetValue(GeometryProperty); set => SetValue(GeometryProperty, value); }
    public SectionAxisDesignation? SelectedAxis { get => GetValue(SelectedAxisProperty); set => SetValue(SelectedAxisProperty, value); }
    public string? HighlightedParameter { get => GetValue(HighlightedParameterProperty); set => SetValue(HighlightedParameterProperty, value); }
    public UnitProfile UnitProfile { get => GetValue(UnitProfileProperty); set => SetValue(UnitProfileProperty, value); }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        // Temporary layout aid: draw inside the actual control bounds without affecting measurement.
        if (ShowCanvasBounds && Bounds.Width > 1 && Bounds.Height > 1)
            context.DrawRectangle(null, new Pen(Brush("TextSecondary", Brushes.Gray), 1),
                new Rect(.5, .5, Bounds.Width - 1, Bounds.Height - 1));
        if (Geometry is not { } geometry || Bounds.Width < 180 || Bounds.Height < 180) return;
        var foreground = Brush("TextPrimary", Brushes.DarkSlateGray);
        var secondary = Brush("TextSecondary", Brushes.Gray);
        var accent = Brush("Accent", Brushes.SteelBlue);
        var fill = Brush("SurfacePressed", Brushes.Transparent);
        var layout = SectionPreviewDrawingLayout.Create(geometry, Bounds.Size, UnitProfile[QuantityKind.SectionDimension], text =>
        {
            using var formatted = SchematicText.Format(text, Typeface.Default, 12, foreground);
            return new Size(formatted.Width, formatted.Height);
        }, Compact);
        double scale = layout.Scale;
        Point Project(Point p) => layout.Project(p);
        var path = new StreamGeometry();
        using (var drawing = path.Open())
        {
            drawing.SetFillRule(FillRule.EvenOdd);
            foreach (var contour in geometry.Contours)
            {
                if (contour.Count == 0) continue;
                drawing.BeginFigure(Project(contour[0].Start), true);
                foreach (var segment in contour)
                {
                    if (segment.Center is { } center)
                    {
                        double radius = ((Vector)(segment.Start-center)).Length*scale;
                        drawing.ArcTo(Project(segment.End), new Size(radius,radius), 0, Math.Abs(segment.Sweep)>Math.PI,
                            segment.Sweep>0 ? SweepDirection.CounterClockwise : SweepDirection.Clockwise);
                    }
                    else drawing.LineTo(Project(segment.End));
                }
                drawing.EndFigure(true);
            }
        }
        context.DrawGeometry(fill, new Pen(foreground, 1.2, lineJoin: PenLineJoin.Round), path);
        foreach (var dimension in layout.Dimensions)
        {
            bool highlighted = dimension.Key == HighlightedParameter;
            var brush = highlighted ? accent : secondary;
            var pen = new Pen(brush, highlighted ? 1.5 : .8);
            foreach (var line in dimension.Lines) context.DrawLine(pen, line.Start, line.End);
            foreach (var arrow in dimension.Arrows)
                FilledArrow(context, brush, arrow.Points);
            if (dimension.Rotated)
            {
                using var rotation = context.PushTransform(Matrix.CreateRotation(-Math.PI / 2)
                    * Matrix.CreateTranslation(dimension.TextBounds.Left, dimension.TextBounds.Bottom));
                Text(context, dimension.Content, new(0, 0), brush);
            }
            else Text(context, dimension.Content, dimension.TextBounds.TopLeft, brush);
        }
        foreach (var axis in layout.Axes)
        {
            bool selected = !axis.IsReference && axis.Axis == SelectedAxis;
            var brush = selected ? accent : secondary;
            double thickness = selected ? 1.6 : .9;
            // Dash lengths are stroke-width multiples. Compensate for round caps so
            // the visible 20-DIP dash, gaps and dots stay fixed when selecting an axis.
            var dashStyle = new DashStyle([(AxisDashLength - thickness) / thickness,
                (AxisDashGap + thickness) / thickness, (AxisDotLength - thickness) / thickness,
                (AxisDashGap + thickness) / thickness], 0);
            Vector direction = axis.End - axis.Start; direction /= direction.Length;
            Point solidStart = axis.End - direction * AxisSolidEndLength;
            context.DrawLine(new Pen(brush, thickness, dashStyle, lineCap: PenLineCap.Round), axis.Start, solidStart);
            context.DrawLine(new Pen(brush, thickness), solidStart, axis.End);
            Arrow(context, brush, axis.End, -direction);
            Text(context, TechnicalSymbols.Axis(axis.Axis.ToString().ToLowerInvariant()), axis.LabelPosition, brush);
        }
        if (geometry.Centroid is { } centroid)
            context.DrawEllipse(foreground, new Pen(Brush("AppBackground", Brushes.White), 1), Project(centroid), 3, 3);
        else Text(context, Strings.SectionDefinitionPreviewHint, new(8, Bounds.Height - 18), secondary, 11);
    }
    protected override Size MeasureOverride(Size availableSize) => new(Math.Min(520, availableSize.Width), Math.Min(440, availableSize.Height));
    private IBrush Brush(string name, IBrush fallback) => this.TryFindResource(name,out var resource) && resource is IBrush brush ? brush : fallback;
    private static void Text(DrawingContext context,TechnicalText text,Point point,IBrush brush,double size=12)
    {
        using var formatted = SchematicText.Format(text,Typeface.Default,size,brush);
        formatted.Draw(context,point);
    }
    private static void Arrow(DrawingContext context,IBrush brush,Point point,Vector direction)
    {
        var normal = new Vector(-direction.Y,direction.X);
        const double length = 12, halfWidth = 3;
        FilledArrow(context, brush, [point, point + direction * length + normal * halfWidth,
            point + direction * length - normal * halfWidth]);
    }
    private static void FilledArrow(DrawingContext context, IBrush brush, IReadOnlyList<Point> points)
    {
        var path = new StreamGeometry();
        using (var drawing = path.Open())
        {
            drawing.BeginFigure(points[0], true);
            drawing.LineTo(points[1]); drawing.LineTo(points[2]); drawing.EndFigure(true);
        }
        context.DrawGeometry(brush, null, path);
    }
}
