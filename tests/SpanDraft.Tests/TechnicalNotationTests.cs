using Avalonia;
using Avalonia.Automation;
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

public sealed class TechnicalNotationTests
{
    [Fact]
    public void CatalogDefinesMeaningPositionAndStyleWithoutUnicodeScripts()
    {
        Assert.Equal(new[] { new TechnicalRun("I", Role: TechnicalRole.Variable),
            new TechnicalRun("y", TechnicalPosition.Subscript, TechnicalRole.Variable) }, TechnicalSymbols.Inertia("y").Runs);
        Assert.Equal(new[] { new TechnicalRun("W", Role: TechnicalRole.Variable),
            new TechnicalRun("y", TechnicalPosition.Subscript, TechnicalRole.Variable),
            new TechnicalRun("+", TechnicalPosition.Superscript) }, TechnicalSymbols.Modulus("y", 1).Runs);
        Assert.Equal(new[] { new TechnicalRun("R", Role: TechnicalRole.Variable),
            new TechnicalRun("p0,2", TechnicalPosition.Subscript) }, TechnicalSymbols.ProofStrength.Runs);
        Assert.Equal(new[] { new TechnicalRun("σ", Role: TechnicalRole.Variable),
            new TechnicalRun("b,max", TechnicalPosition.Subscript) }, TechnicalSymbols.MaximumStress.Runs);
        Assert.Equal("Iy", TechnicalSymbols.Inertia("y").PlainText);
        Assert.Equal("Wy+", TechnicalSymbols.Modulus("y", 1).PlainText);
        Assert.Equal("Wy−", TechnicalSymbols.Modulus("y", -1).PlainText);
        Assert.Equal("Rp0,2", TechnicalSymbols.ProofStrength.PlainText);
        Assert.Equal("σb,max", TechnicalSymbols.MaximumStress.PlainText);
        Assert.Equal("|M|max", TechnicalSymbols.MaximumMoment.PlainText);
        Assert.Equal("|w|max", TechnicalSymbols.MaximumDisplacement.PlainText);
        foreach (var axis in new[] { "y", "z", "u", "v" })
        {
            Assert.Equal("I" + axis, TechnicalSymbols.Inertia(axis).PlainText);
            Assert.Equal("W" + axis, TechnicalSymbols.Modulus(axis).PlainText);
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => TechnicalSymbols.Inertia("q"));
        Assert.Throws<ArgumentOutOfRangeException>(() => TechnicalSymbols.Modulus("y", 2));
    }

    [Fact]
    public void FunctionParenthesesFollowTheVariableStyleWhileUnitBracketsStayUpright()
    {
        foreach (var quantity in new[] { TechnicalSymbols.Displacement, TechnicalSymbols.Shear, TechnicalSymbols.Moment })
        {
            var function = TechnicalSymbols.Function(quantity);
            Assert.Equal(quantity.PlainText + "(x)", function.PlainText);
            Assert.All(function.Runs, run => Assert.True(TechnicalTypography.IsItalic(run)));
            Assert.All(function.Runs, run => Assert.False(TechnicalTypography.IsItalic(run, false)));
            TechnicalText label = function + " [mm]";
            Assert.False(TechnicalTypography.IsItalic(label.Runs[^1]));
        }
    }

    [Fact]
    public void UprightPolicyPreservesSymbolsAndScriptPositions()
    {
        foreach (var symbol in new[] { TechnicalSymbols.Inertia("y"), TechnicalSymbols.Modulus("y", 1),
            TechnicalSymbols.ProofStrength, TechnicalSymbols.MaximumStress, TechnicalSymbols.SectionInput("tw") })
        {
            Assert.All(symbol.Runs, run => Assert.False(TechnicalTypography.IsItalic(run, false)));
            Assert.All(symbol.Runs, run => Assert.Equal(run.Role == TechnicalRole.Variable, TechnicalTypography.IsItalic(run)));
            Assert.All(symbol.Runs, run => Assert.Equal(run.Position == TechnicalPosition.Normal ? 1 : .7,
                TechnicalTypography.FontScale(run)));
        }
    }

    [Fact]
    public void TextCopiesInputAndSlicesRetainRunSemantics()
    {
        TechnicalRun[] source = [new("I", Role: TechnicalRole.Variable), new("y", TechnicalPosition.Subscript, TechnicalRole.Variable)];
        var text = new TechnicalText(source); source[0] = new("changed");
        Assert.Equal("Iy", text.PlainText);
        var composite = "Area moment " + text + " = 12";
        Assert.Equal(text.Runs, composite.Slice(12, 2).Runs);
        Assert.Equal("Iy / Rp0,2", TechnicalText.Join(" / ", [text, TechnicalSymbols.ProofStrength]).PlainText);
        Assert.Throws<ArgumentOutOfRangeException>(() => text.Slice(1, 2));
    }
}

[Collection("Schematic text")]
public sealed class TechnicalRenderingTests
{
    [Fact]
    public void RebindingAndRemovingNotationKeepNativeAccessibilityText()
    {
        var block = new TextBlock();
        var peer = new TextBlockAutomationPeer(block);
        TechnicalTextPresenter.SetContent(block, TechnicalSymbols.YieldStrength);
        Assert.Equal("Re", peer.GetName());
        TechnicalTextPresenter.SetContent(block, TechnicalSymbols.ProofStrength);
        Assert.Equal("Rp0,2", peer.GetName());
        AutomationProperties.SetName(block, "Material strength");
        TechnicalTextPresenter.SetContent(block, TechnicalSymbols.YieldStrength);
        Assert.Equal("Material strength", AutomationProperties.GetName(block));
        Assert.Equal("Re", peer.GetName());
        TechnicalTextPresenter.SetContent(block, null);
        Assert.Empty(block.Inlines!);
        Assert.Equal("Material strength", AutomationProperties.GetName(block));
        block.ClearValue(AutomationProperties.NameProperty);
        block.Text = "Plain text";
        Assert.Equal("Plain text", peer.GetName());
    }

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
