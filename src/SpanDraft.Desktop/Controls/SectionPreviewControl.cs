using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SpanDraft.Core.Sections;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.Sections;

namespace SpanDraft.Desktop.Controls;

/// <summary>Cross-section drawing only. All geometry and authoritative axis data arrive as drawing data.</summary>
public sealed class SectionPreviewControl : Control
{
    public static readonly StyledProperty<SectionPreviewGeometry?> GeometryProperty = AvaloniaProperty.Register<SectionPreviewControl, SectionPreviewGeometry?>(nameof(Geometry));
    public static readonly StyledProperty<SectionAxisDesignation> SelectedAxisProperty = AvaloniaProperty.Register<SectionPreviewControl, SectionAxisDesignation>(nameof(SelectedAxis));
    public static readonly StyledProperty<string?> HighlightedParameterProperty = AvaloniaProperty.Register<SectionPreviewControl, string?>(nameof(HighlightedParameter));
    public static readonly StyledProperty<UnitProfile> UnitProfileProperty = AvaloniaProperty.Register<SectionPreviewControl, UnitProfile>(nameof(UnitProfile), UnitProfile.Default);
    static SectionPreviewControl() => AffectsRender<SectionPreviewControl>(GeometryProperty, SelectedAxisProperty, HighlightedParameterProperty, UnitProfileProperty);
    public SectionPreviewGeometry? Geometry { get => GetValue(GeometryProperty); set => SetValue(GeometryProperty, value); }
    public SectionAxisDesignation SelectedAxis { get => GetValue(SelectedAxisProperty); set => SetValue(SelectedAxisProperty, value); }
    public string? HighlightedParameter { get => GetValue(HighlightedParameterProperty); set => SetValue(HighlightedParameterProperty, value); }
    public UnitProfile UnitProfile { get => GetValue(UnitProfileProperty); set => SetValue(UnitProfileProperty, value); }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Geometry is not { } geometry || Bounds.Width < 180 || Bounds.Height < 180) return;
        var foreground = Brush("TextPrimary", Brushes.DarkSlateGray);
        var secondary = Brush("TextSecondary", Brushes.Gray);
        var accent = Brush("Accent", Brushes.SteelBlue);
        var fill = new SolidColorBrush(Color.FromArgb(25, 80, 110, 130));
        const double left = 62, right = 114, top = 48, bottom = 90;
        double width = Math.Max(1, Bounds.Width-left-right), height = Math.Max(1, Bounds.Height-top-bottom);
        double scale = Math.Min(width/geometry.Width, height/geometry.Height);
        double x = left+(width-geometry.Width*scale)/2, y = top+(height-geometry.Height*scale)/2;
        Point Project(Point p) => new(x+p.X*scale, y+(geometry.Height-p.Y)*scale);
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
        var unit = UnitProfile[QuantityKind.SectionDimension];
        string Label(PreviewDimension dimension) => dimension.Key + (dimension.IsConfirmed
            ? " = " + InputQuantityFormatter.WithUnit(dimension.Value, unit) : "");
        IBrush DimensionBrush(string key, bool primary = false) => key == HighlightedParameter ? accent : primary ? foreground : secondary;
        var bounds = new Rect(x,y,geometry.Width*scale,geometry.Height*scale);
        var horizontalKey = geometry.Dimensions.ContainsKey("b") ? "b" : geometry.Dimensions.ContainsKey("d") ? "d" : "D";
        var horizontal = geometry.Dimensions[horizontalKey];
        double measureY = bounds.Bottom+25;
        var hp = new Pen(DimensionBrush(horizontalKey,true), 1);
        context.DrawLine(hp, new(bounds.Left,bounds.Bottom+5),new(bounds.Left,measureY+5));
        context.DrawLine(hp, new(bounds.Right,bounds.Bottom+5),new(bounds.Right,measureY+5));
        DimensionLine(context,hp,new(bounds.Left,measureY),new(bounds.Right,measureY));
        string horizontalLabel = Label(horizontal);
        if (horizontalKey is "d" or "D") horizontalLabel = horizontalLabel.Replace(horizontalKey, "Ø", StringComparison.Ordinal);
        Text(context,horizontalLabel,new(bounds.Center.X,measureY+8),hp.Brush!,center:true);
        if (geometry.Dimensions.TryGetValue("h",out var vertical))
        {
            double measureX = bounds.Left-22;
            var vp = new Pen(DimensionBrush("h",true),1);
            context.DrawLine(vp,new(measureX-5,bounds.Top),new(bounds.Left-5,bounds.Top));
            context.DrawLine(vp,new(measureX-5,bounds.Bottom),new(bounds.Left-5,bounds.Bottom));
            DimensionLine(context,vp,new(measureX,bounds.Top),new(measureX,bounds.Bottom));
            // A horizontal caption at the top keeps digits readable even on very tall profiles.
            Text(context,Label(vertical),new(Math.Max(4,bounds.Left-22),bounds.Top-30),vp.Brush!);
        }
        var secondaryDimensions = geometry.Dimensions.Values.Where(d => d.Key is "t" or "tw" or "tf" or "r")
            .OrderBy(d => Project(d.Anchor).Y).ToArray();
        var labelPositions = new double[secondaryDimensions.Length];
        for (int i = 0; i < labelPositions.Length; i++)
            labelPositions[i] = Math.Max(Project(secondaryDimensions[i].Anchor).Y - 8, i == 0 ? top : labelPositions[i-1] + 44);
        double overflow = labelPositions.Length == 0 ? 0 : Math.Max(0, labelPositions[^1] - (Bounds.Height-bottom+12));
        for (int i = 0; i < secondaryDimensions.Length; i++)
        {
            var dimension = secondaryDimensions[i];
            var brush = DimensionBrush(dimension.Key);
            Point anchor = Project(dimension.Anchor);
            double labelX = Bounds.Width-right+12, labelY = labelPositions[i] - overflow;
            Point elbow = new(labelX-8,labelY+8);
            var pen = new Pen(brush,dimension.Key == HighlightedParameter ? 1.5 : .8);
            context.DrawEllipse(brush,null,anchor,2,2);
            context.DrawLine(pen,anchor,elbow);
            context.DrawLine(pen,elbow,new(labelX-2,labelY+8));
            // Two lines reserve predictable space for long values and imperial units.
            Text(context,dimension.Key,new(labelX,labelY),brush);
            if (dimension.IsConfirmed) Text(context,InputQuantityFormatter.WithUnit(dimension.Value,unit),new(labelX,labelY+16),brush,11);
        }
        if (geometry.Centroid is { } centroid)
        {
            Point c = Project(centroid);
            bool principal = geometry.Type == ViewModels.SectionEditorType.Angle;
            double angle = principal ? geometry.PrincipalAngle!.Value : 0;
            var first = principal ? SectionAxisDesignation.U : SectionAxisDesignation.Y;
            var second = principal ? SectionAxisDesignation.V : SectionAxisDesignation.Z;
            DrawAxis(first,new Vector(Math.Cos(angle),-Math.Sin(angle)));
            DrawAxis(second,new Vector(-Math.Sin(angle),-Math.Cos(angle)));
            context.DrawEllipse(foreground, new Pen(Brush("AppBackground", Brushes.White),1),c,3,3);
            void DrawAxis(SectionAxisDesignation designation, Vector direction)
            {
                var brush = designation == SelectedAxis ? accent : secondary;
                double extent = Math.Min(100, Math.Max(35, Math.Min(bounds.Width,bounds.Height)*.55));
                Point a = c-direction*extent, end = c+direction*extent;
                var pen = new Pen(brush,designation == SelectedAxis ? 1.6 : .9,DashStyle.Dash);
                context.DrawLine(pen,a,end); Arrow(context,brush,end,-direction,5);
                Text(context,designation.ToString().ToLowerInvariant(),end+direction*8,brush);
            }
        }
        else Text(context,Strings.SectionDefinitionPreviewHint,new(8,Bounds.Height-24),secondary,11);
    }
    private IBrush Brush(string name, IBrush fallback) => this.TryFindResource(name,out var resource) && resource is IBrush brush ? brush : fallback;
    private static void Text(DrawingContext context,string text,Point point,IBrush brush,double size=12,bool center=false)
    {
        var formatted = SchematicText.Format(text,Typeface.Default,size,brush);
        context.DrawText(formatted,center ? new Point(point.X-formatted.Width/2,point.Y) : point);
    }
    private static void DimensionLine(DrawingContext context,Pen pen,Point a,Point b)
    {
        context.DrawLine(pen,a,b); Vector direction = b-a; if (direction.Length <= 0) return; direction /= direction.Length;
        Arrow(context,pen.Brush!,a,direction,4); Arrow(context,pen.Brush!,b,-direction,4);
    }
    private static void Arrow(DrawingContext context,IBrush brush,Point point,Vector direction,double size)
    {
        var normal = new Vector(-direction.Y,direction.X); var pen = new Pen(brush,1);
        context.DrawLine(pen,point,point+direction*size+normal*size*.4);
        context.DrawLine(pen,point,point+direction*size-normal*size*.4);
    }
}
