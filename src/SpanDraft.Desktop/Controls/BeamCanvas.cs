using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SpanDraft.Core.Supports;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.Layout;

namespace SpanDraft.Desktop.Controls;

/// <summary>Technical beam scene only. Axis and edit controls belong to their own pane.</summary>
public sealed class BeamCanvas : Control
{
    public static readonly StyledProperty<BeamRenderState?> SceneProperty = AvaloniaProperty.Register<BeamCanvas, BeamRenderState?>(nameof(Scene));
    public static readonly StyledProperty<Guid?> HighlightedLoadIdProperty = AvaloniaProperty.Register<BeamCanvas, Guid?>(nameof(HighlightedLoadId));
    public static readonly StyledProperty<Guid?> HighlightedSupportIdProperty = AvaloniaProperty.Register<BeamCanvas, Guid?>(nameof(HighlightedSupportId));
    public static readonly StyledProperty<IReadOnlyList<Guid>?> ConflictEntityIdsProperty = AvaloniaProperty.Register<BeamCanvas, IReadOnlyList<Guid>?>(nameof(ConflictEntityIds));
    public static readonly StyledProperty<ConstraintConflictState?> ConstraintConflictProperty = AvaloniaProperty.Register<BeamCanvas, ConstraintConflictState?>(nameof(ConstraintConflict));
    public static readonly StyledProperty<double> LabelFontSizeProperty = AvaloniaProperty.Register<BeamCanvas, double>(nameof(LabelFontSize), 13);
    public static readonly StyledProperty<IBrush?> BeamBrushProperty = AvaloniaProperty.Register<BeamCanvas, IBrush?>(nameof(BeamBrush));
    public static readonly StyledProperty<IBrush?> GhostBrushProperty = AvaloniaProperty.Register<BeamCanvas, IBrush?>(nameof(GhostBrush));
    public static readonly StyledProperty<IBrush?> AccentBrushProperty = AvaloniaProperty.Register<BeamCanvas, IBrush?>(nameof(AccentBrush));
    public static readonly StyledProperty<IBrush?> ErrorBrushProperty = AvaloniaProperty.Register<BeamCanvas, IBrush?>(nameof(ErrorBrush));
    public BeamRenderState? Scene { get => GetValue(SceneProperty); set => SetValue(SceneProperty, value); }
    public Guid? HighlightedLoadId { get => GetValue(HighlightedLoadIdProperty); set => SetValue(HighlightedLoadIdProperty, value); }
    public Guid? HighlightedSupportId { get => GetValue(HighlightedSupportIdProperty); set => SetValue(HighlightedSupportIdProperty, value); }
    public IReadOnlyList<Guid>? ConflictEntityIds { get => GetValue(ConflictEntityIdsProperty); set => SetValue(ConflictEntityIdsProperty, value); }
    public ConstraintConflictState? ConstraintConflict { get => GetValue(ConstraintConflictProperty); set => SetValue(ConstraintConflictProperty, value); }
    public double LabelFontSize { get => GetValue(LabelFontSizeProperty); set => SetValue(LabelFontSizeProperty, value); }
    public IBrush? BeamBrush { get => GetValue(BeamBrushProperty); set => SetValue(BeamBrushProperty, value); }
    public IBrush? GhostBrush { get => GetValue(GhostBrushProperty); set => SetValue(GhostBrushProperty, value); }
    public IBrush? AccentBrush { get => GetValue(AccentBrushProperty); set => SetValue(AccentBrushProperty, value); }
    public IBrush? ErrorBrush { get => GetValue(ErrorBrushProperty); set => SetValue(ErrorBrushProperty, value); }
    static BeamCanvas() => AffectsRender<BeamCanvas>(SceneProperty, BeamBrushProperty, GhostBrushProperty,
        AccentBrushProperty, ErrorBrushProperty, HighlightedLoadIdProperty, HighlightedSupportIdProperty,
        ConflictEntityIdsProperty, ConstraintConflictProperty, LabelFontSizeProperty);

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Scene is not { } scene) return;
        var frame = scene.Frame;
        double y = frame.Viewport.BeamY;
        var length = BeamConflictGeometry.Create(frame.Layout.Transform, ConstraintConflict);
        context.DrawLine(new Pen(BeamBrush, SchematicMetrics.BeamStrokeWidth), new(frame.Layout.Stations[0].ScreenX, y), new(length.EndX, y));
        if (length.HasGhost)
        {
            using (context.PushOpacity(0.5))
                context.DrawLine(new Pen(GhostBrush, 1, dashStyle: DashStyle.Dash), new(length.EndX, y), new(frame.Layout.Stations[^1].ScreenX, y));
            context.DrawLine(new Pen(ErrorBrush, 1.5), new(length.EndX, y - 8), new(length.EndX, y + 8));
        }
        foreach (var support in scene.Supports)
            using (context.PushOpacity(support.IsPreview ? 0.75 : 1))
                DrawSupport(context, support.X, y, support.Preview.Type, support.IsMirrored,
                    Brush(support.Id, true, support.IsPreview, support.Preview.IsInvalid));
        foreach (var glyph in scene.Glyphs)
        {
            bool error = glyph.Entities.Any(e => e.Preview.IsInvalid || Conflict(e.Id));
            bool accent = glyph.IsPreview || glyph.Entities.Any(e => e.Id == HighlightedLoadId);
            using (context.PushOpacity(glyph.IsPreview ? 0.75 : 1))
                PointLoadSymbol.Draw(context, glyph, error ? ErrorBrush : accent ? AccentBrush : BeamBrush);
        }
        foreach (var label in scene.Annotations)
            context.DrawText(SchematicText.Format(label.Text, Typeface.Default, LabelFontSize,
                Brush(label.Id, label.IsSupport, label.IsPreview, label.IsInvalid)), label.Bounds.Position);
    }

    private bool Conflict(Guid? id) => id is { } entity && ConflictEntityIds?.Contains(entity) == true;
    private IBrush? Brush(Guid? id, bool support, bool preview, bool invalid) => invalid || Conflict(id) ? ErrorBrush
        : preview || id is not null && id == (support ? HighlightedSupportId : HighlightedLoadId) ? AccentBrush : BeamBrush;

    private static void DrawSupport(DrawingContext context, double x, double y, SupportType type, bool isMirrored, IBrush? brush)
    {
        var pen = new Pen(brush, SchematicMetrics.SymbolStrokeWidth, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        void Line(double x1, double y1, double x2, double y2) =>
            context.DrawLine(pen, new(x + x1, y + y1), new(x + x2, y + y2));
        if (type == SupportType.Fixed)
        {
            var geometry = new FixedSupportGeometry(isMirrored);
            var wallPen = new Pen(brush, SchematicMetrics.FixedWallStrokeWidth, lineCap: PenLineCap.Round);
            var wall = geometry.Wall;
            context.DrawLine(wallPen, new(x + wall.StartX, y + wall.StartY), new(x + wall.EndX, y + wall.EndY));
            foreach (var hatch in geometry.Hatches)
                Line(hatch.StartX, hatch.StartY, hatch.EndX, hatch.EndY);
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
