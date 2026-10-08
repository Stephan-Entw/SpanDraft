using SpanDraft.Desktop.Controls;
using SpanDraft.Desktop.State;
using Xunit;

namespace SpanDraft.Tests;

public sealed class DesktopResultDiagramScaleTests
{
    [Theory]
    [InlineData(-.11, .02, 5, -2, -3, 1)]
    [InlineData(-7527, 7000, 5, 3, -2, 2)]
    [InlineData(-746, 706, 5, 2, -2, 2)]
    public void RequestedRangesProduceOutwardNiceLimits(double minimum, double maximum,
        double mantissa, int exponent, int lowerIndex, int upperIndex)
    {
        var scale = new ResultDiagramScale(minimum, maximum, 36, 132);
        Assert.Equal(minimum, scale.Minimum); Assert.Equal(maximum, scale.Maximum);
        Assert.Equal(mantissa, scale.StepMantissa); Assert.Equal(exponent, scale.StepExponent);
        Assert.Equal(lowerIndex, scale.LowerTickIndex); Assert.Equal(upperIndex, scale.UpperTickIndex);
        Assert.Equal(5, scale.Ticks().Count);
        AssertReadableAndContained(scale, 36, 132);
    }

    [Theory]
    [InlineData(0, 12, 0, 3)]
    [InlineData(2, 12, 0, 3)]
    [InlineData(12, 12, 0, 3)]
    [InlineData(-12, 0, -3, 0)]
    [InlineData(-12, -2, -3, 0)]
    [InlineData(-12, -12, -3, 0)]
    [InlineData(-2, 12, -1, 3)]
    public void SignedAndConstantRangesIncludeZeroWithoutEnforcedSymmetry(double minimum, double maximum,
        int lowerIndex, int upperIndex)
    {
        var scale = new ResultDiagramScale(minimum, maximum, 36, 132);
        Assert.Equal(5, scale.StepMantissa); Assert.Equal(0, scale.StepExponent);
        Assert.Equal(lowerIndex, scale.LowerTickIndex); Assert.Equal(upperIndex, scale.UpperTickIndex);
        AssertReadableAndContained(scale, 36, 132);
        if (minimum < 0) Assert.True(scale.ToScreen(minimum) > scale.ZeroY);
        if (maximum > 0) Assert.True(scale.ToScreen(maximum) < scale.ZeroY);
    }

    [Fact]
    public void ZeroRangeKeepsOnlyTheCenteredZeroAndItsTrueExtrema()
    {
        var scale = new ResultDiagramScale(-0d, 0, 36, 132);
        Assert.True(scale.IsZero);
        Assert.Equal(0, scale.Minimum); Assert.Equal(0, scale.Maximum);
        Assert.Equal(0, scale.StepMantissa);
        var tick = Assert.Single(scale.Ticks());
        Assert.Equal(0, tick.Index); Assert.Equal(102, tick.ScreenY);
        Assert.Equal(tick.ScreenY, scale.ToScreen(0));
        Assert.Equal("0", UiNumbers.AxisTick(tick.Index, scale.StepMantissa, scale.StepExponent));
    }

    [Theory]
    [InlineData(.095, 2.5, -2)]
    [InlineData(.95, 2.5, -1)]
    [InlineData(9.5, 2.5, 0)]
    [InlineData(95, 2.5, 1)]
    [InlineData(950, 2.5, 2)]
    public void TwoPointFiveStepsRemainPreferredAcrossOrdersOfMagnitude(double maximum, double mantissa, int exponent)
    {
        var scale = new ResultDiagramScale(0, maximum, 36, 132);
        Assert.Equal(mantissa, scale.StepMantissa); Assert.Equal(exponent, scale.StepExponent);
        Assert.Equal(0, scale.LowerTickIndex); Assert.Equal(4, scale.UpperTickIndex);
        AssertReadableAndContained(scale, 36, 132);
    }

    [Theory]
    [InlineData(40, 2, -1, -1, 1, 3)]
    [InlineData(80, 2, -1, -1, 1, 3)]
    [InlineData(96, 1, -1, -2, 1, 4)]
    [InlineData(132, 5, -2, -3, 1, 5)]
    [InlineData(200, 5, -2, -3, 1, 5)]
    [InlineData(300, 5, -2, -3, 1, 5)]
    public void AvailableHeightControlsTickDensity(double height, double mantissa, int exponent,
        int lowerIndex, int upperIndex, int count)
    {
        var scale = new ResultDiagramScale(-.11, .02, 36, height);
        Assert.Equal(mantissa, scale.StepMantissa); Assert.Equal(exponent, scale.StepExponent);
        Assert.Equal(lowerIndex, scale.LowerTickIndex); Assert.Equal(upperIndex, scale.UpperTickIndex);
        Assert.Equal(count, scale.Ticks().Count);
        AssertReadableAndContained(scale, 36, height);
    }

    [Fact]
    public void SmallHeightUsesTwoTicksForAnEntirelyPositiveRange()
    {
        var scale = new ResultDiagramScale(0, 12, 0, 10);
        Assert.Equal(2, scale.Ticks().Count);
        AssertReadableAndContained(scale, 0, 10);
    }

    [Fact]
    public void OutwardRoundingNeverClipsAdjacentFloatingPointValues()
    {
        foreach (double boundary in new[] { .01, .025, .05, .1, 1, 2.5, 5, 10, 1000, 10000 })
            foreach (double value in new[] { Math.BitDecrement(boundary), boundary, Math.BitIncrement(boundary) })
            {
                AssertReadableAndContained(new(0, value, 36, 132), 36, 132);
                AssertReadableAndContained(new(-value, 0, 36, 132), 36, 132);
                AssertReadableAndContained(new(-value, boundary, 36, 132), 36, 132);
            }
        var exact = new ResultDiagramScale(0, 10, 36, 132);
        Assert.Equal(2.5, exact.StepMantissa); Assert.Equal(0, exact.StepExponent);
        Assert.Equal(4, exact.UpperTickIndex);
    }

    [Theory]
    [InlineData(1e-300)]
    [InlineData(1e-100)]
    [InlineData(1e-12)]
    [InlineData(1e-3)]
    [InlineData(1)]
    [InlineData(1e6)]
    [InlineData(1e100)]
    [InlineData(1e300)]
    public void ChangingMagnitudePreservesTheNicePatternAndDoesNotTurnSmallValuesIntoZero(double factor)
    {
        var scale = new ResultDiagramScale(-7.527 * factor, 7 * factor, 36, 132);
        Assert.Equal(5, scale.StepMantissa);
        Assert.Equal(-2, scale.LowerTickIndex); Assert.Equal(2, scale.UpperTickIndex);
        Assert.False(scale.IsZero);
        AssertReadableAndContained(scale, 36, 132);
        Assert.All(scale.Ticks().Where(t => t.Index != 0), t =>
            Assert.NotEqual("0", UiNumbers.AxisTick(t.Index, scale.StepMantissa, scale.StepExponent)));
    }

    [Fact]
    public void SubnormalAndLargestMixedSignValuesHaveFiniteDistinctTicks()
    {
        foreach (var (minimum, maximum) in new[]
        {
            (0d, double.Epsilon), (-double.Epsilon, 0d), (-double.Epsilon, double.Epsilon),
            (-double.MaxValue, double.MaxValue), (0d, double.MaxValue),
            (-double.Epsilon, double.MaxValue), (-double.MaxValue, double.Epsilon)
        })
        {
            var scale = new ResultDiagramScale(minimum, maximum, 36, 132);
            AssertReadableAndContained(scale, 36, 132);
            var labels = scale.Ticks().Select(t => UiNumbers.AxisTick(t.Index, scale.StepMantissa, scale.StepExponent)).ToArray();
            Assert.Equal(labels.Length, labels.Distinct().Count());
            Assert.DoesNotContain(labels, label => label.Contains('∞') || label.Contains('E') || label.Contains("NaN"));
            if (minimum < 0) Assert.True(scale.LowerTickIndex < 0);
            if (maximum > 0) Assert.True(scale.UpperTickIndex > 0);
        }
    }

    [Fact]
    public void TickGenerationStaysBoundedAcrossTheDoubleExponentRange()
    {
        // Deterministic coverage of magnitudes and sign balances, including rounding
        // transitions. ScaleB generates only input data; it is not the tick algorithm.
        for (int exponent = -1074; exponent <= 1023; exponent += 13)
        {
            double magnitude = Math.ScaleB(1, exponent);
            foreach (double ratio in new[] { 0d, .001, .05, .25, .9, 1 })
                foreach (double height in new[] { 12d, 64, 96, 132, 240 })
                {
                    AssertReadableAndContained(new(-magnitude * ratio, magnitude, 36, height), 36, height);
                    AssertReadableAndContained(new(-magnitude, magnitude * ratio, 36, height), 36, height);
                }
        }
    }

    [Theory]
    [InlineData(double.NaN, 1, 0, 132)]
    [InlineData(0, double.PositiveInfinity, 0, 132)]
    [InlineData(1, 0, 0, 132)]
    [InlineData(0, 1, double.NaN, 132)]
    [InlineData(0, 1, 0, 0)]
    [InlineData(0, 1, 0, -1)]
    [InlineData(0, 1, 0, double.PositiveInfinity)]
    public void InvalidScaleInputsAreRejected(double minimum, double maximum, double top, double height) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new ResultDiagramScale(minimum, maximum, top, height));

    private static void AssertReadableAndContained(ResultDiagramScale scale, double top, double height)
    {
        var ticks = scale.Ticks();
        Assert.InRange(ticks.Count, 2, 6);
        Assert.Contains(ticks, t => t.Index == 0 && t.ScreenY == scale.ZeroY);
        Assert.Equal(scale.UpperTickIndex, ticks[0].Index);
        Assert.Equal(scale.LowerTickIndex, ticks[^1].Index);
        Assert.Equal(top, ticks[0].ScreenY); Assert.Equal(top + height, ticks[^1].ScreenY);
        Assert.All(ticks, t => Assert.True(double.IsFinite(t.ScreenY)));
        Assert.InRange(scale.ToScreen(scale.Minimum), top, top + height);
        Assert.InRange(scale.ToScreen(scale.Maximum), top, top + height);
        for (int i = 1; i < ticks.Count; i++)
        {
            Assert.Equal(ticks[i - 1].Index - 1, ticks[i].Index);
            Assert.True(ticks[i].ScreenY > ticks[i - 1].ScreenY);
            int minimumTicks = scale.Minimum < 0 && scale.Maximum > 0 ? 3 : 2;
            if (ticks.Count > minimumTicks) Assert.True(ticks[i].ScreenY - ticks[i - 1].ScreenY >= 28 - 1e-10);
        }
    }
}

[Collection("Schematic text")]
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
        Assert.Equal(expected, UiNumbers.AxisTick(index, mantissa, exponent));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("en-US")]
    public void ZeroHasNoSignOrRedundantDigitsAndNonzeroTicksRemainNonzero(string culture)
    {
        using var scope = new UiCultureScope(culture);
        Assert.Equal("0", UiNumbers.AxisTick(0, 0, 0));
        foreach (int exponent in new[] { -324, -300, -12, -3, -2, 0, 3, 4, 300, 309 })
            foreach (int index in new[] { -3, -1, 1, 3 })
            {
                string text = UiNumbers.AxisTick(index, 2.5, exponent);
                Assert.NotEqual("0", text); Assert.NotEqual("-0", text);
                Assert.DoesNotContain('E', text);
                Assert.True(text.Length <= 16, text);
            }
        // Existing read-only formatting stays unchanged; extra tick precision is scoped.
        Assert.Equal(culture == "de-DE" ? "0,03" : "0.03", UiNumbers.Compact(.025));
    }
}
