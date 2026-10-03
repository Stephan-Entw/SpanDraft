using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace SpanDraft.Desktop.Controls;

/// <summary>A foreground-inheriting vector presenter with a shared 24 DIP artboard.</summary>
public sealed class EngineeringIcon : TemplatedControl
{
    public static readonly StyledProperty<Geometry?> DataProperty =
        AvaloniaProperty.Register<EngineeringIcon, Geometry?>(nameof(Data));
    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<EngineeringIcon, double>(nameof(StrokeThickness), 1.5);

    static EngineeringIcon() => AffectsRender<EngineeringIcon>(DataProperty, ForegroundProperty, StrokeThicknessProperty);

    public Geometry? Data { get => GetValue(DataProperty); set => SetValue(DataProperty, value); }
    public double StrokeThickness { get => GetValue(StrokeThicknessProperty); set => SetValue(StrokeThicknessProperty, value); }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Data is null) return;
        double scale = Math.Min(Bounds.Width, Bounds.Height) / 24;
        var transform = Matrix.CreateScale(scale, scale) *
            Matrix.CreateTranslation((Bounds.Width - 24 * scale) / 2, (Bounds.Height - 24 * scale) / 2);
        using (context.PushTransform(transform))
            context.DrawGeometry(null, new Pen(Foreground, StrokeThickness, lineCap: PenLineCap.Round,
                lineJoin: PenLineJoin.Round), Data);
    }
}
