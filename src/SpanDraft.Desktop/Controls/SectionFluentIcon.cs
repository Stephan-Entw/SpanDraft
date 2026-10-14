using Avalonia;
using Avalonia.Media;

namespace SpanDraft.Desktop.Controls;

/// <summary>Local Fluent SVG artwork tinted with the section view's semantic brushes.</summary>
public sealed class SectionFluentIcon : Avalonia.Svg.Skia.Svg
{
    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<SectionFluentIcon, IBrush?>(nameof(Foreground));
    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    public SectionFluentIcon() : base(new Uri("avares://SpanDraft.Desktop/"))
    {
        Width = Height = 16;
        Stretch = Stretch.Uniform;
        Focusable = IsHitTestVisible = false;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ForegroundProperty && Foreground is ISolidColorBrush brush)
        {
            var color = brush.Color;
            SetCss(this, $"path {{ fill: #{color.R:X2}{color.G:X2}{color.B:X2}; }}");
        }
    }
}
