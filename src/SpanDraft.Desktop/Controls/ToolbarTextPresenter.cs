using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace SpanDraft.Desktop.Controls;

/// <summary>Aligns toolbar captions and values on a shared digit-height axis and baseline.</summary>
public sealed class ToolbarTextPresenter : Decorator
{
    private (Typeface Face, double Size)? digitFont;
    private double digitBaselineOffset;

    public ToolbarTextPresenter() => UseLayoutRounding = false;

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Child is not TextBlock text) return base.ArrangeOverride(finalSize);

        var size = text.DesiredSize;
        text.Arrange(new Rect(size));
        var layout = text.TextLayout;
        double left = double.PositiveInfinity, right = double.NegativeInfinity, advance = 0;
        foreach (var run in layout.TextLines.SelectMany(line => line.TextRuns).OfType<ShapedTextRun>())
        {
            var glyphs = run.GlyphRun;
            left = Math.Min(left, advance + glyphs.InkBounds.Left - glyphs.BaselineOrigin.X);
            right = Math.Max(right, advance + glyphs.InkBounds.Right - glyphs.BaselineOrigin.X);
            advance += run.Size.Width;
        }
        double x = double.IsFinite(left) ? (finalSize.Width - right - left) / 2 : finalSize.Width / 2;
        var face = new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch);
        if (!Equals(digitFont, (face, text.FontSize)))
        {
            // A stable digit reference keeps the baseline fixed when the value or suffix changes.
            using var digit = new TextLayout("0", face, text.FontSize, Brushes.Black);
            digitBaselineOffset = digit.Baseline - digit.Height - digit.OverhangAfter + digit.Extent / 2;
            digitFont = (face, text.FontSize);
        }
        double baseline = finalSize.Height / 2 + digitBaselineOffset;
        text.Arrange(new Rect(x, baseline - layout.Baseline, size.Width, size.Height));
        return finalSize;
    }
}
