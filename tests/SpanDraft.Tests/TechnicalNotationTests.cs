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
