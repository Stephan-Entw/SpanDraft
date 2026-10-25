using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SpanDraft.Reporting.Notation;

namespace SpanDraft.Desktop.Controls;

/// <summary>One property, with a shared value column and a measured common text baseline.</summary>
public sealed class SectionPropertyRow : Grid
{
    public static readonly StyledProperty<TechnicalText> LabelProperty = AvaloniaProperty.Register<SectionPropertyRow, TechnicalText>(nameof(Label), (TechnicalText)"");
    public static readonly StyledProperty<string> ValueProperty = AvaloniaProperty.Register<SectionPropertyRow, string>(nameof(Value), "");
    private readonly TextBlock _label = new() { FontSize = 14, FontWeight = FontWeight.Normal, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _value = new() { FontSize = 14, FontWeight = FontWeight.SemiBold, TextAlignment = TextAlignment.Right };
    public TechnicalText Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public string Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    public SectionPropertyRow()
    {
        MinHeight = 28; ColumnSpacing = 12;
        ColumnDefinitions = new ColumnDefinitions("*,Auto");
        ColumnDefinitions[1].SharedSizeGroup = "SectionPropertyValues";
        _label.Classes.Add("definitionLabel"); _value.Classes.Add("definitionValue");
        Children.Add(_label); Children.Add(_value); SetColumn(_value, 1);
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == LabelProperty) TechnicalTextPresenter.SetContent(_label, Label);
        if (change.Property == ValueProperty) _value.Text = Value;
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        base.ArrangeOverride(finalSize);
        double labelBaseline = _label.TextLayout.TextLines[0].Baseline;
        double valueBaseline = _value.TextLayout.TextLines[0].Baseline;
        double above = Math.Max(labelBaseline, valueBaseline);
        double below = Math.Max(_label.TextLayout.Height - labelBaseline, _value.TextLayout.Height - valueBaseline);
        double baseline = Math.Max(above, (finalSize.Height - above - below) / 2 + above);
        Align(_label, labelBaseline); Align(_value, valueBaseline);
        return finalSize;

        void Align(TextBlock text, double ownBaseline) => text.Arrange(new Rect(text.Bounds.X,
            baseline - ownBaseline, text.Bounds.Width, text.TextLayout.Height));
    }
}
