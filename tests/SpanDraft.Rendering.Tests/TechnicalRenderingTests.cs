using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using SkiaSharp;
using SpanDraft.Desktop.Controls;
using SpanDraft.Reporting;
using SpanDraft.Reporting.Notation;
using Xunit;

namespace SpanDraft.Tests;

[Collection("Avalonia")]
public sealed class TechnicalRenderingTests
{

    [Fact]
    public void NativeInlinesAndCanvasUseIdenticalSizesBaselinesAndAccessibleText()
    {
        foreach (var symbol in new[] { TechnicalSymbols.Inertia("y"), TechnicalSymbols.Modulus("y", 1),
            TechnicalSymbols.Modulus("y", -1), TechnicalSymbols.ProofStrength, TechnicalSymbols.MaximumStress,
            TechnicalSymbols.Function(TechnicalSymbols.Displacement), TechnicalSymbols.Function(TechnicalSymbols.Shear),
            TechnicalSymbols.Function(TechnicalSymbols.Moment) })
        {
            var block = new TextBlock { FontSize = 14 };
            TechnicalTextPresenter.SetContent(block, symbol);
            Assert.Equal(symbol.PlainText, new TextBlockAutomationPeer(block).GetName());
            Check(14); block.FontSize = 20; Check(20);

            void Check(double size)
            {
                block.Measure(Size.Infinity);
                var runs = block.Inlines!.Cast<Run>().ToArray();
                Assert.Equal(symbol.Runs.Count, runs.Length);
                foreach (var (run, definition) in runs.Zip(symbol.Runs))
                {
                    Assert.Equal(definition.Text, run.Text);
                    Assert.Equal(size * TechnicalTypography.FontScale(definition), run.FontSize);
                    Assert.Equal(TechnicalTextPresenter.BaselineOf(definition), run.BaselineAlignment);
                    Assert.Equal(TechnicalTextPresenter.FontStyleOf(definition), run.FontStyle);
                }
                var face = new Typeface(block.FontFamily, block.FontStyle, block.FontWeight, block.FontStretch);
                using var layout = SchematicText.Format(symbol, face, size, Brushes.Black);
                Assert.Equal(block.TextLayout.WidthIncludingTrailingWhitespace, layout.WidthIncludingTrailingWhitespace, 6);
                Assert.Equal(block.TextLayout.Height, layout.Height, 6);
                Assert.Equal(block.TextLayout.Baseline, layout.Baseline, 6);
                Assert.Equal(new Size(layout.WidthIncludingTrailingWhitespace, layout.Height), SchematicText.Measure(symbol, face, size));
                Assert.All(layout.TextLines.SelectMany(l => l.TextRuns).OfType<ShapedTextRun>(), run =>
                    Assert.All(run.GlyphRun.GlyphInfos, glyph => Assert.NotEqual(0, glyph.GlyphIndex)));
            }
        }
    }

    [Fact]
    public void SkiaFunctionParenthesesUseTheSameItalicTypefaceAsTheVariables()
    {
        foreach (var quantity in new[] { TechnicalSymbols.Displacement, TechnicalSymbols.Shear, TechnicalSymbols.Moment })
        {
            using var layout = new ReportTextLayout(TechnicalSymbols.Function(quantity) + " [mm]", 14);
            Assert.All(layout.Runs.SkipLast(1), run => Assert.True(run.Font.Typeface.IsItalic));
            Assert.False(layout.Runs[^1].Font.Typeface.IsItalic);
        }
    }

    [Fact]
    public void SkiaRunsAreVectorTextWithSmallerShiftedScriptsInsideMeasuredBounds()
    {
        foreach (var symbol in new[] { TechnicalSymbols.Modulus("y", 1), TechnicalSymbols.Modulus("y", -1),
            TechnicalSymbols.ProofStrength, TechnicalSymbols.MaximumStress })
        {
            using var layout = new ReportTextLayout(symbol, 14);
            Assert.Equal(symbol.PlainText, string.Concat(layout.Runs.Select(r => r.Text)));
            var main = layout.Runs[0]; var sub = layout.Runs[1];
            Assert.Equal(14, main.Font.Size); Assert.Equal(9.8f, sub.Font.Size, .001f);
            Assert.True(sub.Baseline > main.Baseline);
            if (layout.Runs.Count > 2) Assert.True(layout.Runs[2].Baseline < main.Baseline);
            Assert.True(main.Font.Typeface.IsItalic); Assert.Equal(symbol.Runs[1].Role == TechnicalRole.Variable, sub.Font.Typeface.IsItalic);
            Assert.All(layout.Runs, run =>
            {
                Assert.True(run.Ink.Left >= 0 && run.Ink.Top >= 0);
                Assert.True(run.Ink.Right <= layout.Width + .001 && run.Ink.Bottom <= layout.Height + .001);
            });
            using var pdf = new MemoryStream();
            using (var document = SKDocument.CreatePdf(pdf))
            {
                using var paint = new SKPaint { Color = SKColors.Black };
                layout.Draw(document.BeginPage(200, 80), 10, 10, paint);
                document.EndPage(); document.Close();
            }
            string content = System.Text.Encoding.Latin1.GetString(pdf.ToArray());
            Assert.DoesNotMatch(@"/Subtype\s*/Image\b", content);
            Assert.Contains("/Font", content);
        }
    }
}
