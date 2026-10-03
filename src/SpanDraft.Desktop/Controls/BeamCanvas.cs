using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace SpanDraft.Desktop.Controls;

/// <summary>Technical graphics only; editing is performed by controls on BeamEditorSurface.</summary>
public sealed class BeamCanvas : Control
{
    public static readonly StyledProperty<double> LengthMetersProperty =
        AvaloniaProperty.Register<BeamCanvas, double>(nameof(LengthMeters), 1);

    public static readonly StyledProperty<IBrush?> BeamBrushProperty =
        AvaloniaProperty.Register<BeamCanvas, IBrush?>(nameof(BeamBrush));
    public static readonly StyledProperty<IBrush?> DimensionBrushProperty =
        AvaloniaProperty.Register<BeamCanvas, IBrush?>(nameof(DimensionBrush));
    public static readonly StyledProperty<IBrush?> GuideBrushProperty =
        AvaloniaProperty.Register<BeamCanvas, IBrush?>(nameof(GuideBrush));

    static BeamCanvas() => AffectsRender<BeamCanvas>(LengthMetersProperty, BoundsProperty,
        BeamBrushProperty, DimensionBrushProperty, GuideBrushProperty);

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
        var beam = new Pen(BeamBrush, 5);
        var dimension = new Pen(DimensionBrush, 1);
        var guide = new Pen(GuideBrush, 1);
        context.DrawLine(beam, new(v.Left, v.BeamY), new(v.Right, v.BeamY));
        foreach (double x in new[] { v.Left, v.Right })
            context.DrawLine(guide, new(x, v.DimensionY - 8), new(x, v.BeamY + 12));
        context.DrawLine(dimension, new(v.Left, v.DimensionY), new(v.Right, v.DimensionY));
        context.DrawLine(dimension, new(v.Left, v.DimensionY), new(v.Left + 9, v.DimensionY - 4));
        context.DrawLine(dimension, new(v.Left, v.DimensionY), new(v.Left + 9, v.DimensionY + 4));
        context.DrawLine(dimension, new(v.Right, v.DimensionY), new(v.Right - 9, v.DimensionY - 4));
        context.DrawLine(dimension, new(v.Right, v.DimensionY), new(v.Right - 9, v.DimensionY + 4));
    }
}
