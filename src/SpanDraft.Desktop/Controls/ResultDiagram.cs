using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System.Globalization;
using SpanDraft.Desktop.Layout;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.State;

namespace SpanDraft.Desktop.Controls;

public sealed record ResultDiagramMarkerLabel(ResultDiagramMarker Marker, string Text, Rect Bounds);

/// <summary>Read-only result drawing. Cached geometry is never a project/session state.</summary>
public sealed class ResultDiagram : Control
{
    public static readonly StyledProperty<AnalysisPresentationState?> PresentationProperty =
        AvaloniaProperty.Register<ResultDiagram, AnalysisPresentationState?>(nameof(Presentation));
    public static readonly StyledProperty<StationLayoutResult?> StationLayoutProperty =
        AvaloniaProperty.Register<ResultDiagram, StationLayoutResult?>(nameof(StationLayout));
    public static readonly StyledProperty<ResultDiagramKind> KindProperty =
        AvaloniaProperty.Register<ResultDiagram, ResultDiagramKind>(nameof(Kind));
    public AnalysisPresentationState? Presentation { get => GetValue(PresentationProperty); set => SetValue(PresentationProperty, value); }
    public StationLayoutResult? StationLayout { get => GetValue(StationLayoutProperty); set => SetValue(StationLayoutProperty, value); }
    public ResultDiagramKind Kind { get => GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public ResultDiagramProjection? Projection { get; private set; }
    public IReadOnlyList<ResultDiagramMarkerLabel> MarkerLabels { get; private set; } = [];
    public string Title => Kind switch
    {
        ResultDiagramKind.TransverseDisplacement => Strings.DeflectionDiagram + " w(x) [mm]",
        ResultDiagramKind.ShearForce => Strings.ShearDiagram + " V(x) [N]",
        _ => Strings.BendingDiagram + " M(x) [Nm]"
    };

    public ResultDiagram()
    {
        Height = 180;
        IsHitTestVisible = false;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PresentationProperty || change.Property == StationLayoutProperty
            || change.Property == KindProperty || change.Property == BoundsProperty)
        {
            Projection = Presentation?.Result is { } result && StationLayout is { } layout
                && Bounds.Width > 0 && Bounds.Height > 48
                ? ResultDiagramProjection.Create(result.Solution, Kind, layout, 36, Bounds.Height - 48) : null;
            MarkerLabels = Projection is { } projection ? PlaceMarkerLabels(projection) : [];
            InvalidateVisual();
        }
    }

    private IBrush Brush(string key) => this.TryFindResource(key, out var value) && value is IBrush brush ? brush : Brushes.Transparent;

    private string MarkerText(ResultDiagramMarker marker)
    {
        string format = marker.Kind switch
        {
            ResultDiagramExtremumKind.Minimum => Strings.DiagramMinimum,
            ResultDiagramExtremumKind.Maximum => Strings.DiagramMaximum,
            _ => Strings.DiagramMinimumAndMaximum
        };
        string unit = Kind switch
        {
            ResultDiagramKind.TransverseDisplacement => "mm",
            ResultDiagramKind.ShearForce => "N",
            _ => "Nm"
        };
        return string.Format(CultureInfo.CurrentUICulture, format, UiNumbers.Compact(marker.Value), unit);
    }

    private FormattedText TickText(ResultDiagramProjection projection, ResultDiagramTick tick, IBrush brush) =>
        SchematicText.Format(UiNumbers.AxisTick(tick.Index, projection.Scale.StepMantissa,
            projection.Scale.StepExponent), Typeface.Default, 12, brush);

    private Rect TickBounds(ResultDiagramProjection projection, ResultDiagramTick tick, FormattedText text)
    {
        double x = Math.Max(8, projection.StationLayout.Stations[0].ScreenX - text.WidthIncludingTrailingWhitespace - 8);
        text.MaxTextWidth = Math.Max(1, Bounds.Width - x - 8);
        return new(x, tick.ScreenY - text.Height / 2, text.WidthIncludingTrailingWhitespace, text.Height);
    }

    private IReadOnlyList<ResultDiagramMarkerLabel> PlaceMarkerLabels(ResultDiagramProjection projection)
    {
        var area = new Rect(8, 30, Math.Max(1, Bounds.Width - 16), Bounds.Height - 34);
        var tickBounds = projection.Ticks.Select(t => TickBounds(projection, t, TickText(projection, t, Brushes.Black))).ToArray();
        var segments = projection.Sections.SelectMany(s => s.Zip(s.Skip(1), (a, b) => (a.Screen, b.Screen)))
            .Concat(projection.Jumps.Select(j => (j.Left.Screen, j.Right.Screen))).ToArray();
        var choices = projection.Markers.Select(marker =>
        {
            string text = MarkerText(marker);
            var formatted = SchematicText.Format(text, Typeface.Default, 11, Brushes.Black);
            formatted.MaxTextWidth = area.Width;
            var size = new Size(formatted.WidthIncludingTrailingWhitespace, formatted.Height);
            double above = marker.Screen.Y - size.Height - 7, below = marker.Screen.Y + 7;
            double[] nearY = marker.Kind == ResultDiagramExtremumKind.Minimum ? [below, above] : [above, below];
            // Nearby positions first; bounded fallback rows handle adjacent extrema
            // and exact scale boundaries without changing the plot or its X mapping.
            var ys = nearY.Append(marker.Screen.Y - size.Height / 2)
                .Concat(Enumerable.Range(0, Math.Min(32, (int)(area.Height / (size.Height + 4)) + 1))
                    .Select(row => area.Top + row * (size.Height + 4))).Append(area.Bottom - size.Height);
            double[] xs = [marker.Screen.X - size.Width / 2, marker.Screen.X + 7, marker.Screen.X - size.Width - 7];
            return ys.SelectMany(y => xs.Select(x => new Rect(
                    Math.Clamp(x, area.Left, Math.Max(area.Left, area.Right - size.Width)),
                    Math.Clamp(y, area.Top, Math.Max(area.Top, area.Bottom - size.Height)), size.Width, size.Height)))
                .Distinct().Where(r => !projection.Markers.Any(m => r.Inflate(4).Contains(m.Screen)))
                .Select(r =>
                {
                    var painted = r.Inflate(2);
                    var distance = r.Center - marker.Screen;
                    double score = Math.Sqrt(distance.X * distance.X + distance.Y * distance.Y)
                        + (tickBounds.Any(t => painted.Intersects(t.Inflate(2))) ? 100000 : 0)
                        + (segments.Any(s => IntersectsSegment(painted, s.Item1, s.Item2)) ? 10000 : 0);
                    return (Label: new ResultDiagramMarkerLabel(marker, text, r), Score: score);
                }).OrderBy(c => c.Score).ToArray();
        }).ToArray();
        if (choices.Length == 1) return choices[0].Take(1).Select(c => c.Label).ToArray();
        // Select the pair together so one label cannot occupy the other's only
        // readable position. There are always just two global extrema.
        var pair = (from first in choices[0]
                    from second in choices[1]
                    where !first.Label.Bounds.Inflate(2).Intersects(second.Label.Bounds.Inflate(2))
                    orderby first.Score + second.Score
                    select new[] { first.Label, second.Label }).FirstOrDefault();
        return pair ?? choices.SelectMany(c => c.Take(1).Select(candidate => candidate.Label)).ToArray();
    }

    private static bool IntersectsSegment(Rect rect, Point start, Point end)
    {
        double enter = 0, exit = 1;
        return Clip(start.X, end.X - start.X, rect.Left, rect.Right)
            && Clip(start.Y, end.Y - start.Y, rect.Top, rect.Bottom);

        bool Clip(double origin, double delta, double minimum, double maximum)
        {
            if (delta == 0) return origin >= minimum && origin <= maximum;
            double a = (minimum - origin) / delta, b = (maximum - origin) / delta;
            enter = Math.Max(enter, Math.Min(a, b));
            exit = Math.Min(exit, Math.Max(a, b));
            return enter <= exit;
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var background = Brush("CanvasBackground");
        var text = Brush("TextSecondary");
        context.FillRectangle(background, new Rect(Bounds.Size));
        var title = SchematicText.Format(Title, Typeface.Default, 13, Brush("TextPrimary"));
        title.MaxTextWidth = Math.Max(1, Bounds.Width - 16);
        title.TextAlignment = TextAlignment.Center;
        context.DrawText(title, new(8, 8));
        if (Projection is not { } projection)
        {
            var status = SchematicText.Format(Presentation?.StatusText ?? Strings.CalculationUnavailable, Typeface.Default, 12, text);
            status.MaxTextWidth = Math.Max(1, Bounds.Width - 16);
            context.DrawText(status, new(8, 48));
            return;
        }
        double left = projection.StationLayout.Stations[0].ScreenX;
        double right = projection.StationLayout.Stations[^1].ScreenX;
        var gridPen = new Pen(Brush("Border"), 1);
        foreach (var tick in projection.Ticks.Where(t => t.Index != 0))
            context.DrawLine(gridPen, new(left, tick.ScreenY), new(right, tick.ScreenY));
        context.DrawLine(new Pen(Brush("AxisStroke"), 1.25), new(left, projection.Scale.ZeroY), new(right, projection.Scale.ZeroY));
        var curvePen = new Pen(Brush("Accent"), 1.5);
        foreach (var section in projection.Sections)
            for (int i = 1; i < section.Count; i++) context.DrawLine(curvePen, section[i - 1].Screen, section[i].Screen);
        foreach (var jump in projection.Jumps) context.DrawLine(curvePen, jump.Left.Screen, jump.Right.Screen);
        foreach (var tick in projection.Ticks)
        {
            var label = TickText(projection, tick, text);
            // The scale uses the existing left gutter; long values can extend inward
            // without introducing a different horizontal physical mapping.
            var bounds = TickBounds(projection, tick, label);
            context.FillRectangle(background, bounds);
            context.DrawText(label, bounds.Position);
        }
        foreach (var label in MarkerLabels)
        {
            var formatted = SchematicText.Format(label.Text, Typeface.Default, 11, text);
            formatted.MaxTextWidth = label.Bounds.Width;
            context.FillRectangle(background, label.Bounds.Inflate(2));
            context.DrawText(formatted, label.Bounds.Position);
        }
        foreach (var marker in projection.Markers)
            context.DrawEllipse(Brush("Accent"), new Pen(background, 1), marker.Screen, 3, 3);
    }
}
