using System.Globalization;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.ViewModels;
using SpanDraft.Core.Units;
using Xunit;

namespace SpanDraft.Tests;

public sealed class DesktopNumericPresentationTests
{
    [Theory]
    [InlineData(12, 3, "12")]
    [InlineData(12.3, 3, "12.3")]
    [InlineData(12.356, 1, "10")]
    [InlineData(12.356, 2, "12")]
    [InlineData(12.356, 3, "12.4")]
    [InlineData(12.356, 4, "12.36")]
    [InlineData(12.356, 8, "12.356")]
    [InlineData(12.356, 15, "12.356")]
    [InlineData(-12.356, 3, "-12.4")]
    [InlineData(0.012356, 3, "0.0124")]
    [InlineData(0.0012356, 3, "0.00124")]
    [InlineData(0.00012356, 3, "124 · 10⁻⁶")]
    [InlineData(123456.55, 3, "123\u202f000")]
    [InlineData(999999, 3, "1 · 10⁶")]
    [InlineData(1234567, 3, "1.23 · 10⁶")]
    [InlineData(0.000000123, 3, "123 · 10⁻⁹")]
    [InlineData(1.25, 2, "1.3")]
    [InlineData(-1.25, 2, "-1.3")]
    [InlineData(0, 3, "0")]
    public void SignificantDigitsAreAMaximumAndNeverHideNonzeroValues(double value, int digits, string expected)
    {
        foreach (string culture in new[] { "en-US", "de-DE" })
            Assert.Equal(culture == "de-DE" ? expected.Replace('.', ',') : expected,
                QuantityFormatter.FormatNumber(value, QuantityKind.PoissonRatio, significantDigits: digits,
                    culture: CultureInfo.GetCultureInfo(culture)));
    }

    [Fact]
    public void ImmediateNeighborsOfHalfValuesRemainDistinctAndRoundAwayFromZero()
    {
        foreach (double sign in new[] { 1d, -1d })
        {
            Assert.Equal(sign * 1.2, NumericRounding.Round(sign * Math.BitDecrement(1.25), QuantityKind.Rotation, UnitCatalog.Radian, 2).Value.ToDouble());
            Assert.Equal(sign * 1.3, NumericRounding.Round(sign * 1.25, QuantityKind.Rotation, UnitCatalog.Radian, 2).Value.ToDouble());
            Assert.Equal(sign * 1.3, NumericRounding.Round(sign * Math.BitIncrement(1.25), QuantityKind.Rotation, UnitCatalog.Radian, 2).Value.ToDouble());
        }
    }

    [Theory]
    [InlineData(0.0009994, "999 · 10⁻⁶")]
    [InlineData(0.0009995, "0.001")]
    [InlineData(0.001, "0.001")]
    [InlineData(999499, "999\u202f000")]
    [InlineData(999500, "1 · 10⁶")]
    [InlineData(1000000, "1 · 10⁶")]
    public void NotationThresholdsUseTheRoundedDisplayValue(double value, string expected) =>
        Assert.Equal(expected, QuantityFormatter.FormatNumber(value, QuantityKind.PoissonRatio, culture: CultureInfo.InvariantCulture));

    [Fact]
    public void EngineeringExponentsAreMultiplesOfThreeAcrossTheEntireFiniteRange()
    {
        for (int exponent = -323; exponent <= 308; exponent++)
        {
            double value = Math.Pow(10, exponent) * 1.2345;
            string text = QuantityFormatter.FormatNumber(value, QuantityKind.PoissonRatio, culture: CultureInfo.InvariantCulture);
            if (exponent is >= -3 and < 6) { Assert.DoesNotContain('·', text); continue; }
            string superscript = text[(text.IndexOf("10", StringComparison.Ordinal) + 2)..];
            int engineeringExponent = int.Parse(string.Concat(superscript.Select(c => c == '⁻' ? '-' : (char)('0' + "⁰¹²³⁴⁵⁶⁷⁸⁹".IndexOf(c)))), CultureInfo.InvariantCulture);
            Assert.Equal(0, engineeringExponent % 3);
            Assert.NotEqual(0, engineeringExponent);
            double mantissa = double.Parse(text[..text.IndexOf(' ')], CultureInfo.InvariantCulture);
            Assert.InRange(mantissa, 1, 999.999);
        }
    }

    [Theory]
    [InlineData(1234567, "1\u202f230\u202f000")]
    [InlineData(0.00012356, "0.000124")]
    [InlineData(-1234567, "-1\u202f230\u202f000")]
    public void NotationOffAlwaysUsesGroupedDecimalNotation(double value, string expected) =>
        Assert.Equal(expected, QuantityFormatter.FormatNumber(value, QuantityKind.PoissonRatio,
            notation: EngineeringNotation.Off, culture: CultureInfo.InvariantCulture));

    [Theory]
    [InlineData("de-DE", "12\u202f200,05", "123\u202f456,7")]
    [InlineData("en-US", "12\u202f200.05", "123\u202f456.7")]
    public void GroupingAlwaysUsesNarrowNonbreakingSpaces(string culture, string first, string second)
    {
        using var scope = new ResultCultureScope(culture, culture == "de-DE" ? "en-US" : "de-DE");
        Assert.Equal(first, QuantityFormatter.FormatNumber(12200.05, QuantityKind.PoissonRatio, significantDigits: 7));
        Assert.Equal(second, QuantityFormatter.FormatNumber(123456.7, QuantityKind.PoissonRatio, significantDigits: 7));
        Assert.Equal("\u202f", NumberFormatter.GroupSeparator);
    }

    [Fact]
    public void ExtremesSignedZeroAndSemanticInfinityRemainDistinct()
    {
        Assert.Equal("4.94 · 10⁻³²⁴", QuantityFormatter.FormatNumber(double.Epsilon, QuantityKind.PoissonRatio, culture: CultureInfo.InvariantCulture));
        Assert.Equal("-4.94 · 10⁻³²⁴", QuantityFormatter.FormatNumber(-double.Epsilon, QuantityKind.PoissonRatio, culture: CultureInfo.InvariantCulture));
        Assert.Equal("180 · 10³¹⁸ mm⁴", QuantityFormatter.Format(double.MaxValue, QuantityKind.SecondMomentOfArea, culture: CultureInfo.InvariantCulture));
        Assert.Equal("∞", QuantityFormatter.Format(double.PositiveInfinity, QuantityKind.SafetyFactor));
        foreach (var kind in Enum.GetValues<QuantityKind>())
            Assert.Equal("0", QuantityFormatter.FormatNumber(BitConverter.Int64BitsToDouble(long.MinValue), kind));
        foreach (double value in new[] { double.Epsilon, double.MaxValue })
        {
            string text = QuantityFormatter.FormatNumber(value, QuantityKind.PoissonRatio, notation: EngineeringNotation.Off);
            Assert.DoesNotContain('·', text); Assert.DoesNotContain('∞', text); Assert.NotEqual("0", text);
        }
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("en-US")]
    public void RoundedDisplayNeverBecomesTheEditingBufferOnFocusRefreshOrCommit(string culture)
    {
        using var scope = new ResultCultureScope(culture, culture);
        var original = Length.FromMillimeters(12.356);
        var stored = original;
        var options = new ResultPresentationOptions();
        var input = new LengthInputViewModel(() => stored, value => { stored = value; return LengthCommitResult.Success; },
            readPresentation: () => options);
        Assert.Equal(culture == "de-DE" ? "12,4 mm" : "12.4 mm", input.DisplayText);
        for (int pass = 0; pass < 3; pass++)
        {
            input.Begin();
            Assert.Equal(culture == "de-DE" ? "12,356" : "12.356", input.Text);
            options = new(significantDigits: pass + 1, notation: EngineeringNotation.Off);
            input.Refresh();
            Assert.True(input.TryGetLength(out var pending)); Assert.Equal(original, pending);
            input.LoseFocus(); Assert.Equal(original, stored);
        }
        var section = new SectionDefinitionInput("b", UnitCatalog.Millimeter, (_, _) => { });
        section.Load(original.Meters);
        for (int pass = 0; pass < 3; pass++) { Assert.True(section.Commit()); Assert.Equal(original.Meters, section.ConfirmedValue); }
        var setup = new SetupNumberInput("b", "b", UnitCatalog.Inch, () => { }, original.Meters);
        Assert.True(setup.TryRead(out var value)); Assert.Equal(original.Meters, value);
    }

    [Theory]
    [InlineData(0)] [InlineData(16)] [InlineData(-1)]
    public void SignificantDigitRangeIsOneThroughFifteen(int invalid)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ResultPresentationOptions(significantDigits: invalid));
        Assert.Throws<ArgumentOutOfRangeException>(() => UserSettings.Default with { SignificantDigits = invalid });
        Assert.Throws<ArgumentOutOfRangeException>(() => QuantityFormatter.Format(1, QuantityKind.BeamLength, significantDigits: invalid));
    }

    [Fact]
    public void InvalidNumbersUnitsAndNotationAreRejected()
    {
        foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            Assert.Throws<ArgumentOutOfRangeException>(() => QuantityFormatter.Format(invalid, QuantityKind.TransverseForce));
        Assert.Throws<ArgumentOutOfRangeException>(() => QuantityFormatter.Format(-1, QuantityKind.SafetyFactor));
        Assert.Throws<ArgumentException>(() => NumericRounding.Round(1, QuantityKind.TransverseForce, UnitCatalog.Meter));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ResultPresentationOptions(notation: (EngineeringNotation)99));
    }
}
