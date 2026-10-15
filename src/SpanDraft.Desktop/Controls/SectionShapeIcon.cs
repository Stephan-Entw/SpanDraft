using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SpanDraft.Desktop.Sections;
using SpanDraft.Desktop.ViewModels;

namespace SpanDraft.Desktop.Controls;

/// <summary>Flat section silhouettes drawn from the same neutral preview as the editor.</summary>
public sealed class SectionShapeIcon : Control
{
    public static readonly StyledProperty<SectionEditorType> TypeProperty = AvaloniaProperty.Register<SectionShapeIcon, SectionEditorType>(nameof(Type));
    static SectionShapeIcon() => AffectsRender<SectionShapeIcon>(TypeProperty);
    public SectionEditorType Type { get => GetValue(TypeProperty); set => SetValue(TypeProperty,value); }
    public override void Render(DrawingContext context)
    {
        if (Type == SectionEditorType.Manual)
        {
            var manualBrush = this.TryFindResource("TextPrimary", out var manualResource) && manualResource is IBrush mb ? mb : Brushes.DarkSlateGray;
            context.DrawRectangle(null, new Pen(manualBrush, 1.5), new Rect(4, 4, Math.Max(0, Bounds.Width - 8), Math.Max(0, Bounds.Height - 8)), 2, 2);
            context.DrawLine(new Pen(manualBrush, 1.5), new Point(9, Bounds.Height / 2), new Point(Bounds.Width - 9, Bounds.Height / 2));
            return;
        }
        var dimensions = new Dictionary<string, double>
        {
            ["h"] = .1, ["b"] = Type == SectionEditorType.Rectangle ? .06 : .1,
            ["d"] = .1, ["D"] = .1, ["t"] = .01, ["tw"] = .01, ["tf"] = .01, ["r"] = 0
        };
        var geometry = SectionPreviewGeometryResolver.Resolve(Type, dimensions);
        double scale = Math.Min((Bounds.Width-4)/geometry.Width,(Bounds.Height-4)/geometry.Height);
        Point Project(Point p) => new((Bounds.Width-geometry.Width*scale)/2+p.X*scale,(Bounds.Height+geometry.Height*scale)/2-p.Y*scale);
        var path = new StreamGeometry();
        using (var drawing = path.Open())
        {
            drawing.SetFillRule(FillRule.EvenOdd);
            foreach (var contour in geometry.Contours)
            {
                drawing.BeginFigure(Project(contour[0].Start),true);
                foreach (var segment in contour)
                {
                    if (segment.Center is { } c)
                    {
                        double r=((Vector)(segment.Start-c)).Length*scale;
                        drawing.ArcTo(Project(segment.End),new(r,r),0,Math.Abs(segment.Sweep)>Math.PI,
                            segment.Sweep>0 ? SweepDirection.CounterClockwise : SweepDirection.Clockwise);
                    }
                    else drawing.LineTo(Project(segment.End));
                }
                drawing.EndFigure(true);
            }
        }
        var brush = this.TryFindResource("TextPrimary",out var resource) && resource is IBrush b ? b : Brushes.DarkSlateGray;
        context.DrawGeometry(this.TryFindResource("SurfacePressed", out var fill) && fill is IBrush fillBrush ? fillBrush : Brushes.Transparent,new Pen(brush,1.5),path);
    }
}
