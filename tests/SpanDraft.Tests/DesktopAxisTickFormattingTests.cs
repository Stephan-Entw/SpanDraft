using SpanDraft.Desktop.Presentation;
using Xunit;

namespace SpanDraft.Tests;

public sealed class DesktopAxisTickFormattingTests
{
    [Theory]
    [InlineData("de-DE", 1, 2.5, -2, "0,025")]
    [InlineData("en-US", 1, 2.5, -2, "0.025")]
    [InlineData("de-DE", -3, 5, -2, "-0,15")]
    [InlineData("en-US", -3, 5, -2, "-0.15")]
    [InlineData("de-DE", 5, 2.5, -3, "0,0125")]
    [InlineData("en-US", 5, 2.5, -3, "0.0125")]
    [InlineData("de-DE", 4, 2.5, -2, "0,1")]
    [InlineData("en-US", 4, 2.5, -2, "0.1")]
    [InlineData("de-DE", 1, 2.5, -3, "2,5·10⁻³")]
    [InlineData("en-US", 1, 2.5, -3, "2.5·10⁻³")]
    [InlineData("de-DE", 1, 2, -324, "2·10⁻³²⁴")]
    [InlineData("en-US", -1, 2, -324, "-2·10⁻³²⁴")]
    [InlineData("de-DE", 2, 1, 308, "200·10³⁰⁶")]
    [InlineData("en-US", 2, 1, 308, "200·10³⁰⁶")]
    [InlineData("de-DE", 1, 5, 3, "5000")]
    [InlineData("en-US", 2, 5, 3, "10·10³")]
    public void LabelsUseLocalizedExactDecimalsAndBoundedEngineeringNotation(string culture,
        int index, double mantissa, int exponent, string expected)
    {
        using var scope = new UiCultureScope(culture);
        Assert.Equal(expected, AxisTickFormatter.Format(index, mantissa, exponent));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("en-US")]
    public void ZeroHasNoSignOrRedundantDigitsAndNonzeroTicksRemainNonzero(string culture)
    {
        using var scope = new UiCultureScope(culture);
        Assert.Equal("0", AxisTickFormatter.Format(0, 0, 0));
        foreach (int exponent in new[] { -324, -300, -12, -3, -2, 0, 3, 4, 300, 309 })
            foreach (int index in new[] { -3, -1, 1, 3 })
            {
                string text = AxisTickFormatter.Format(index, 2.5, exponent);
                Assert.NotEqual("0", text); Assert.NotEqual("-0", text);
                Assert.DoesNotContain('E', text);
                Assert.True(text.Length <= 16, text);
            }
        // Existing read-only formatting stays unchanged; extra tick precision is scoped.
        Assert.Equal(culture == "de-DE" ? "0,025" : "0.025", QuantityFormatter.FormatNumber(.025, QuantityKind.TransverseForce, ResultPresentationOptions.Default));
    }
}
