using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using SpanDraft.Reporting.Notation;

namespace SpanDraft.Desktop.Controls;

/// <summary>Preserves the bound string while shaping scientific exponents with the bundled font.</summary>
public sealed class TechnicalTextBlock : TextBlock
{
    protected override TextLayout CreateTextLayout(string? text)
    {
        text ??= Inlines?.Text;
        if (text is null || !TechnicalText.HasExponents(text)) return base.CreateTextLayout(text);
        var size = GetMaxSizeFromConstraint();
        return SchematicText.Format((TechnicalText)text, new Typeface(FontFamily, FontStyle, FontWeight, FontStretch), FontSize,
            Foreground, size.Width, IsMeasureValid ? TextAlignment : TextAlignment.Left,
            TextWrapping, TextTrimming, size.Height, MaxLines);
    }
}
