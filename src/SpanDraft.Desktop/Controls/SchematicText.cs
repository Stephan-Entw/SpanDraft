using System.Globalization;
using Avalonia;
using Avalonia.Media;

namespace SpanDraft.Desktop.Controls;

/// <summary>The single text measurement adapter used before pure packing.</summary>
public static class SchematicText
{
    public static FormattedText Format(string text, Typeface typeface, double fontSize, IBrush? brush) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, fontSize, brush);
    public static Size Measure(string text, Typeface typeface, double fontSize)
    {
        var formatted = Format(text, typeface, fontSize, Brushes.Black);
        return new(formatted.WidthIncludingTrailingWhitespace, formatted.Height);
    }
}
