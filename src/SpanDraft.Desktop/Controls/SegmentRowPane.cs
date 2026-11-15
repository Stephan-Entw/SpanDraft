using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SpanDraft.Desktop.Layout;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.State;

namespace SpanDraft.Desktop.Controls;

/// <summary>
/// The row between the beam drawing and the x axis that assigns the beam's ranges to its segments: a thin line per
/// segment with short end strokes on the segment boundaries, interrupted around the most detailed label that fits. It carries no
/// coordinates; those belong to the axis. It has a fixed band of its own and exists only for a beam of several segments.
/// </summary>
public sealed class SegmentRowPane : Control
{
    public static readonly StyledProperty<IBrush?> StrokeBrushProperty = AvaloniaProperty.Register<SegmentRowPane, IBrush?>(nameof(StrokeBrush));
    public static readonly StyledProperty<double> LabelFontSizeProperty = AvaloniaProperty.Register<SegmentRowPane, double>(nameof(LabelFontSize), 13);

    private IReadOnlyList<SegmentRowSpan> _spans = [];
    private StationLayoutResult? _layout;
    private EditorDocument? _document;
    private ResultPresentationOptions? _options;

    static SegmentRowPane() => AffectsRender<SegmentRowPane>(StrokeBrushProperty, LabelFontSizeProperty);

    public SegmentRowPane() => Height = SchematicMetrics.SegmentRowHeight;

    public IBrush? StrokeBrush { get => GetValue(StrokeBrushProperty); set => SetValue(StrokeBrushProperty, value); }
    public double LabelFontSize { get => GetValue(LabelFontSizeProperty); set => SetValue(LabelFontSizeProperty, value); }

    /// <summary>The segments with their chosen labels, in order; empty while the row is not shown.</summary>
    public IReadOnlyList<SegmentRowSpan> Spans => _spans;

    /// <summary>Lays the row out on the station layout of the beam above it.</summary>
    public void Update(StationLayoutResult layout, EditorDocument document, ResultPresentationOptions options)
    {
        if (!document.HasSeveralSegments) { Hide(); return; }
        if (ReferenceEquals(_layout, layout) && ReferenceEquals(_document, document) && ReferenceEquals(_options, options)) return;
        _layout = layout; _document = document; _options = options;
        var inputs = new SegmentRowInput[document.Segments.Count];
        for (int i = 0; i < inputs.Length; i++)
        {
            var (start, end) = document.SegmentRange(i);
            inputs[i] = new(start.Meters, end.Meters, SegmentLabels.RowCandidates(document.Segments[i], i + 1, options));
        }
        _spans = SegmentRowLayout.Create(inputs, layout.Transform,
            text => SchematicText.Measure(text, AppTypography.Typeface, LabelFontSize).Width);
        IsVisible = true;
        InvalidateVisual();
    }

    public void Hide()
    {
        _layout = null; _document = null; _options = null;
        _spans = [];
        IsVisible = false;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        // Hit testing needs a painted background; the row is part of the axis area and takes no pointer input as a tool.
        context.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));
        if (_spans.Count == 0) return;
        var pen = new Pen(StrokeBrush, 1);
        double lineY = Math.Round(Bounds.Height / 2) + .5;
        const double stroke = 4;
        context.DrawLine(pen, new(_spans[0].StartX, lineY - stroke), new(_spans[0].StartX, lineY + stroke));
        foreach (var span in _spans)
        {
            context.DrawLine(pen, new(span.EndX, lineY - stroke), new(span.EndX, lineY + stroke));
            if (span.Text is not { } label)
            {
                context.DrawLine(pen, new(span.StartX, lineY), new(span.EndX, lineY));
                continue;
            }
            // The label sits in the line: the line stops short of it on both sides.
            var size = SchematicText.Measure(label, AppTypography.Typeface, LabelFontSize);
            double left = span.StartX + (span.Width - size.Width) / 2;
            double right = left + size.Width;
            context.DrawLine(pen, new(span.StartX, lineY), new(left - SegmentRowLayout.LabelGap, lineY));
            context.DrawLine(pen, new(right + SegmentRowLayout.LabelGap, lineY), new(span.EndX, lineY));
            using var text = SchematicText.Format(label, AppTypography.Typeface, LabelFontSize, StrokeBrush);
            text.Draw(context, new(left, Math.Max(0, lineY - size.Height / 2)));
        }
    }
}
