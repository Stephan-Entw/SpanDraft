using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SpanDraft.Core.Supports;
using SpanDraft.Desktop.State;

namespace SpanDraft.Desktop.Controls;

/// <summary>Technical graphics only; editing is performed by controls on BeamEditorSurface.</summary>
public sealed class BeamCanvas : Control
{
    public static readonly StyledProperty<IReadOnlyList<EditorPointLoad>?> LoadsProperty =
        AvaloniaProperty.Register<BeamCanvas, IReadOnlyList<EditorPointLoad>?>(nameof(Loads));
    public static readonly StyledProperty<PointLoadPreview?> LoadPreviewProperty =
        AvaloniaProperty.Register<BeamCanvas, PointLoadPreview?>(nameof(LoadPreview));
    public static readonly StyledProperty<Guid?> HighlightedLoadIdProperty =
        AvaloniaProperty.Register<BeamCanvas, Guid?>(nameof(HighlightedLoadId));
    public static readonly StyledProperty<Guid?> HiddenLoadIdProperty =
        AvaloniaProperty.Register<BeamCanvas, Guid?>(nameof(HiddenLoadId));
    public static readonly StyledProperty<double> LabelFontSizeProperty =
        AvaloniaProperty.Register<BeamCanvas, double>(nameof(LabelFontSize), 13);
    public IReadOnlyList<EditorPointLoad>? Loads { get => GetValue(LoadsProperty); set => SetValue(LoadsProperty, value); }
    public PointLoadPreview? LoadPreview { get => GetValue(LoadPreviewProperty); set => SetValue(LoadPreviewProperty, value); }
    public Guid? HighlightedLoadId { get => GetValue(HighlightedLoadIdProperty); set => SetValue(HighlightedLoadIdProperty, value); }
    public Guid? HiddenLoadId { get => GetValue(HiddenLoadIdProperty); set => SetValue(HiddenLoadIdProperty, value); }
    public double LabelFontSize { get => GetValue(LabelFontSizeProperty); set => SetValue(LabelFontSizeProperty, value); }
    public static readonly StyledProperty<IReadOnlyList<EditorSupport>?> SupportsProperty =
        AvaloniaProperty.Register<BeamCanvas, IReadOnlyList<EditorSupport>?>(nameof(Supports));
    public static readonly StyledProperty<SupportPreview?> PreviewProperty =
        AvaloniaProperty.Register<BeamCanvas, SupportPreview?>(nameof(Preview));
    public static readonly StyledProperty<Guid?> HighlightedSupportIdProperty =
        AvaloniaProperty.Register<BeamCanvas, Guid?>(nameof(HighlightedSupportId));
    public static readonly StyledProperty<Guid?> HiddenSupportIdProperty =
        AvaloniaProperty.Register<BeamCanvas, Guid?>(nameof(HiddenSupportId));
    public static readonly StyledProperty<IReadOnlyList<Guid>?> ConflictEntityIdsProperty =
        AvaloniaProperty.Register<BeamCanvas, IReadOnlyList<Guid>?>(nameof(ConflictEntityIds));
    public static readonly StyledProperty<ConstraintConflictState?> ConstraintConflictProperty =
        AvaloniaProperty.Register<BeamCanvas, ConstraintConflictState?>(nameof(ConstraintConflict));
    public static readonly StyledProperty<IBrush?> AccentBrushProperty =
        AvaloniaProperty.Register<BeamCanvas, IBrush?>(nameof(AccentBrush));
    public static readonly StyledProperty<IBrush?> ErrorBrushProperty =
        AvaloniaProperty.Register<BeamCanvas, IBrush?>(nameof(ErrorBrush));
    public IReadOnlyList<EditorSupport>? Supports { get => GetValue(SupportsProperty); set => SetValue(SupportsProperty, value); }
    public SupportPreview? Preview { get => GetValue(PreviewProperty); set => SetValue(PreviewProperty, value); }
    public Guid? HighlightedSupportId { get => GetValue(HighlightedSupportIdProperty); set => SetValue(HighlightedSupportIdProperty, value); }
    public Guid? HiddenSupportId { get => GetValue(HiddenSupportIdProperty); set => SetValue(HiddenSupportIdProperty, value); }
    public IReadOnlyList<Guid>? ConflictEntityIds { get => GetValue(ConflictEntityIdsProperty); set => SetValue(ConflictEntityIdsProperty, value); }
    public ConstraintConflictState? ConstraintConflict { get => GetValue(ConstraintConflictProperty); set => SetValue(ConstraintConflictProperty, value); }
    public IBrush? AccentBrush { get => GetValue(AccentBrushProperty); set => SetValue(AccentBrushProperty, value); }
    public IBrush? ErrorBrush { get => GetValue(ErrorBrushProperty); set => SetValue(ErrorBrushProperty, value); }
    public static readonly StyledProperty<double> LengthMetersProperty =
        AvaloniaProperty.Register<BeamCanvas, double>(nameof(LengthMeters), 1);

    public static readonly StyledProperty<IBrush?> BeamBrushProperty =
        AvaloniaProperty.Register<BeamCanvas, IBrush?>(nameof(BeamBrush));
    public static readonly StyledProperty<IBrush?> DimensionBrushProperty =
        AvaloniaProperty.Register<BeamCanvas, IBrush?>(nameof(DimensionBrush));
    public static readonly StyledProperty<IBrush?> GuideBrushProperty =
        AvaloniaProperty.Register<BeamCanvas, IBrush?>(nameof(GuideBrush));

    static BeamCanvas() => AffectsRender<BeamCanvas>(LengthMetersProperty, BoundsProperty,
        BeamBrushProperty, DimensionBrushProperty, GuideBrushProperty, SupportsProperty, PreviewProperty,
        HighlightedSupportIdProperty, HiddenSupportIdProperty, ConflictEntityIdsProperty, ConstraintConflictProperty,
        AccentBrushProperty, ErrorBrushProperty, LoadsProperty, LoadPreviewProperty,
        HighlightedLoadIdProperty, HiddenLoadIdProperty, LabelFontSizeProperty);

    public IBrush? BeamBrush { get => GetValue(BeamBrushProperty); set => SetValue(BeamBrushProperty, value); }
    public IBrush? DimensionBrush { get => GetValue(DimensionBrushProperty); set => SetValue(DimensionBrushProperty, value); }
    public IBrush? GuideBrush { get => GetValue(GuideBrushProperty); set => SetValue(GuideBrushProperty, value); }

    public double LengthMeters
    {
        get => GetValue(LengthMetersProperty);
        set => SetValue(LengthMetersProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Bounds.Width <= 0 || Bounds.Height <= 0 || LengthMeters <= 0) return;
        var v = BeamViewport.Fit(Bounds.Width, Bounds.Height, LengthMeters);
        var length = BeamLengthGeometry.Create(v, ConstraintConflict);
        var beam = new Pen(BeamBrush, 5);
        var dimension = new Pen(DimensionBrush, 1);
        var guide = new Pen(GuideBrush, 1);
        context.DrawLine(beam, new(v.Left, v.BeamY), new(length.EndX, v.BeamY));
        if (length.HasGhost)
            using (context.PushOpacity(0.5))
                context.DrawLine(new Pen(DimensionBrush, 1, dashStyle: DashStyle.Dash),
                    new(length.EndX, v.BeamY), new(v.Right, v.BeamY));
        foreach (double x in new[] { v.Left, length.EndX })
            context.DrawLine(guide, new(x, v.DimensionY - 8), new(x, v.BeamY + 12));
        context.DrawLine(dimension, new(v.Left, v.DimensionY), new(length.EndX, v.DimensionY));
        context.DrawLine(dimension, new(v.Left, v.DimensionY), new(v.Left + 9, v.DimensionY - 4));
        context.DrawLine(dimension, new(v.Left, v.DimensionY), new(v.Left + 9, v.DimensionY + 4));
        context.DrawLine(dimension, new(length.EndX, v.DimensionY), new(length.EndX - 9, v.DimensionY - 4));
        context.DrawLine(dimension, new(length.EndX, v.DimensionY), new(length.EndX - 9, v.DimensionY + 4));
        foreach (var support in Supports ?? [])
            if (support.Id != HiddenSupportId)
                DrawSupport(context, v, support.Position.Meters, support.Type,
                    ConflictEntityIds?.Contains(support.Id) == true ? ErrorBrush
                        : support.Id == HighlightedSupportId ? AccentBrush : BeamBrush);
        if (Preview is { } preview)
            using (context.PushOpacity(0.75))
                DrawSupport(context, v, preview.Position.Meters, preview.Type,
                    preview.IsInvalid || HiddenSupportId is { } id && ConflictEntityIds?.Contains(id) == true
                        ? ErrorBrush : AccentBrush);
        foreach (var visual in PointLoadSymbol.Layout(Loads ?? [], v, LoadPreview, HiddenLoadId))
        {
            bool conflict = visual.Id is { } loadId && ConflictEntityIds?.Contains(loadId) == true;
            var brush = conflict || visual.Preview.IsInvalid ? ErrorBrush
                : visual.IsPreview || visual.Id == HighlightedLoadId ? AccentBrush : BeamBrush;
            using (context.PushOpacity(visual.IsPreview ? 0.75 : 1))
                PointLoadSymbol.Draw(context, visual, v, brush, conflict ? ErrorBrush : GuideBrush,
                    Typeface.Default, LabelFontSize);
        }
    }

    private static void DrawSupport(DrawingContext context, BeamViewport viewport, double position, SupportType type, IBrush? brush)
    {
        var pen = new Pen(brush, 1.5, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        double x = viewport.BeamToScreen(position), y = viewport.BeamY;
        void Line(double x1, double y1, double x2, double y2) =>
            context.DrawLine(pen, new(x + x1, y + y1), new(x + x2, y + y2));
        if (type == SupportType.Fixed)
        {
            Line(0, -SupportSymbol.WallHalfHeight, 0, SupportSymbol.WallHalfHeight);
            for (int offset = -20; offset <= 16; offset += 9) Line(0, offset, -12, offset + 8);
            return;
        }
        Line(0, 0, -14, SupportSymbol.TriangleHeight);
        Line(-14, SupportSymbol.TriangleHeight, 14, SupportSymbol.TriangleHeight);
        Line(14, SupportSymbol.TriangleHeight, 0, 0);
        Line(-SupportSymbol.HalfWidth, SupportSymbol.GroundY, SupportSymbol.HalfWidth, SupportSymbol.GroundY);
        if (type == SupportType.Roller)
        {
            context.DrawEllipse(null, pen, new Point(x - 8, y + 26), 3, 3);
            context.DrawEllipse(null, pen, new Point(x + 8, y + 26), 3, 3);
        }
        else
            for (int offset = -14; offset <= 14; offset += 7) Line(offset, 32, offset + 6, 24);
    }
}
