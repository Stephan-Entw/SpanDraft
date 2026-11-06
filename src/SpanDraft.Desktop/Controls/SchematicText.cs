using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Utilities;
using SpanDraft.Reporting.Notation;

namespace SpanDraft.Desktop.Controls;

/// <summary>The single text measurement adapter used before pure packing.</summary>
public static class SchematicText
{
    public static TextLayout Format(TechnicalText text, Typeface typeface, double fontSize, IBrush? brush,
        double maxWidth = double.PositiveInfinity, TextAlignment alignment = TextAlignment.Left,
        TextWrapping wrapping = TextWrapping.Wrap, TextTrimming? trimming = null,
        double maxHeight = double.PositiveInfinity, int maxLines = 0)
    {
        text = text.ForDisplay();
        int offset = 0;
        var styles = text.Runs.Select(run =>
        {
            var span = new ValueSpan<TextRunProperties>(offset, run.Text.Length,
                TechnicalTextPresenter.Properties(run, typeface, fontSize, brush));
            offset += run.Text.Length;
            return span;
        }).ToArray();
        return new TextLayout(text.PlainText, typeface, fontSize, brush, textAlignment: alignment,
            textWrapping: wrapping, textTrimming: trimming, maxWidth: maxWidth, maxHeight: maxHeight, maxLines: maxLines,
            fontFeatures: AppTypography.TabularFigures, textStyleOverrides: styles);
    }
    public static Size Measure(TechnicalText text, Typeface typeface, double fontSize)
    {
        using var layout = Format(text, typeface, fontSize, Brushes.Black);
        return new(layout.WidthIncludingTrailingWhitespace, layout.Height);
    }
    public static TextLayout Format(string text, Typeface typeface, double fontSize, IBrush? brush,
        double maxWidth = double.PositiveInfinity, TextAlignment alignment = TextAlignment.Left,
        TextWrapping wrapping = TextWrapping.Wrap, TextTrimming? trimming = null,
        double maxHeight = double.PositiveInfinity, int maxLines = 0) =>
        Format((TechnicalText)text, typeface, fontSize, brush, maxWidth, alignment, wrapping, trimming, maxHeight, maxLines);
    public static Size Measure(string text, Typeface typeface, double fontSize)
    {
        using var formatted = Format(text, typeface, fontSize, Brushes.Black);
        return new(formatted.WidthIncludingTrailingWhitespace, formatted.Height);
    }
}
