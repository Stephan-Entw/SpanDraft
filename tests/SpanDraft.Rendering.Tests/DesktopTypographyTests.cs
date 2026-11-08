using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using SkiaSharp;
using SpanDraft.Desktop.Controls;
using SpanDraft.Reporting;
using SpanDraft.Reporting.Notation;
using Xunit;

namespace SpanDraft.Tests;

[Collection("Avalonia")]
public sealed class DesktopTypographyTests
{
    private const string Symbols = "−·≈±²³ΣσεØäöüÄÖÜßρν∞⁰¹⁴⁵⁶⁷⁸⁹₀₁₂₃₄₅₆₇₈₉\u202f\u00a0×≤≥—…";

    private static void AssertPlexGlyphs(TextLayout layout)
    {
        var runs = layout.TextLines.SelectMany(line => line.TextRuns).OfType<ShapedTextRun>().ToArray();
        Assert.NotEmpty(runs);
        Assert.All(runs, run =>
        {
            Assert.Contains(run.GlyphRun.GlyphTypeface.FamilyName, new[] { "IBM Plex Sans", "IBM Plex Sans SmBld" });
            Assert.All(run.GlyphRun.GlyphInfos, glyph => Assert.NotEqual(0, glyph.GlyphIndex));
        });
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public void EveryBundledFaceCoversTechnicalSymbolsAndHasTabularFigures(bool strong, bool italic)
    {
        using var environment = new DesktopControlEnvironment();
        var face = ReportFonts.Face(strong, italic);
        Assert.Equal("IBM Plex Sans", face.FamilyName);
        using var font = new SKFont(face, 13);
        Assert.True(font.ContainsGlyphs(Symbols));
        Assert.False(font.ContainsGlyphs("⁻"));
        Assert.All("0123456789", digit => Assert.Equal(font.MeasureText("0"), font.MeasureText(digit.ToString()), 5));
        var typeface = new Typeface(AppTypography.Family, italic ? FontStyle.Italic : FontStyle.Normal,
            strong ? FontWeight.SemiBold : FontWeight.Normal);
        using var layout = SchematicText.Format(Symbols + " 1\u202f234·10⁻³²⁴", typeface, 13, Brushes.Black);
        AssertPlexGlyphs(layout);
    }

    [Fact]
    public void ScientificDisplayRunsPreserveSourceAndBoundTextAcrossUpdates()
    {
        using var environment = new DesktopControlEnvironment();
        TechnicalText source = "1\u202f234 · 10⁻⁶ mm³";
        var displayed = source.ForDisplay();
        Assert.Equal("1\u202f234 · 10⁻⁶ mm³", source.PlainText);
        var exponent = Assert.Single(displayed.Runs, run => run.Position == TechnicalPosition.Superscript);
        Assert.Equal("−6", exponent.Text);
        var text = new TechnicalTextBlock { Text = source.PlainText };
        var window = new Window { Content = text };
        window.Measure(new Size(500, 100)); window.Arrange(new Rect(0, 0, 500, 100));
        Assert.Equal(source.PlainText, text.Text); AssertPlexGlyphs(text.TextLayout);
        text.Text = "2·10⁻³ mm";
        window.Measure(new Size(500, 100)); window.Arrange(new Rect(0, 0, 500, 100));
        Assert.Equal("2·10⁻³ mm", text.Text); AssertPlexGlyphs(text.TextLayout);
        using var report = new ReportTextLayout(source, 8.5f);
        Assert.Contains(report.Runs, run => run.Text == "−6" && run.Font.Size < 8.5f);
        Assert.All(report.Runs, run => Assert.Equal("IBM Plex Sans", run.Font.Typeface.FamilyName));
    }
}
