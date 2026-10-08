using System.Globalization;
using System.Numerics;
using SpanDraft.Desktop.Presentation;
using Xunit;

namespace SpanDraft.Tests;

public sealed class DesktopNumericPresentationTests
{
    private static ModelReferenceValues References(double length = 1, double force = 1000,
        double moment = 1000, double yield = 235e6) => new(length, force, moment, yield);

    public static IEnumerable<object[]> StandardExamples()
    {
        (QuantityKind Kind, double Si, ModelReferenceValues References, string Expected)[] examples =
        [
            (QuantityKind.TransverseDisplacement, 0.00765 / 1000, References(length: 0.05), "0,0075 mm"),
            (QuantityKind.TransverseDisplacement, 0.008 / 1000, References(length: 0.07312), "0,008 mm"),
            (QuantityKind.TransverseDisplacement, 0.00765 / 1000, References(), "0,01 mm"),
            (QuantityKind.TransverseDisplacement, 0.05 / 1000, References(), "0,05 mm"),
            (QuantityKind.TransverseDisplacement, 10.01 / 1000, References(), "10 mm"),
            (QuantityKind.TransverseDisplacement, 0.05 / 1000, References(length: 10), "0,1 mm"),
            (QuantityKind.TransverseDisplacement, 0.04 / 1000, References(length: 10), "≈ 0 mm"),
            (QuantityKind.TransverseDisplacement, 3.07 / 1000, References(length: 10), "3,1 mm"),
            (QuantityKind.TransverseForce, 0.000084, References(), "≈ 0 N"),
            (QuantityKind.TransverseForce, 2.7, References(), "3 N"),
            (QuantityKind.TransverseForce, 0.0347, References(force: 0.1), "0,0347 N"),
            (QuantityKind.TransverseForce, 4516.02, References(force: 5000), "4520 N"),
            (QuantityKind.Moment, 0.034, References(), "≈ 0 N·m"),
            (QuantityKind.Moment, 1491.23, References(moment: 2000), "1490 N·m"),
            (QuantityKind.Stress, 26.01e6, References(), "26 MPa"),
            (QuantityKind.Stress, 234.9e6, References(), "235 MPa"),
            (QuantityKind.AxialForce, 0.000084, References(), "84·10⁻⁶ N"),
            (QuantityKind.AxialDisplacement, 0.00007654 / 1000, References(), "76,5·10⁻⁶ mm"),
            (QuantityKind.Rotation, 0.001234, References(), "1,23·10⁻³ rad"),
            (QuantityKind.Area, 1900.45 / 1e6, References(), "1900 mm²"),
            (QuantityKind.SecondMomentOfArea, 2874599 / 1e12, References(), "2,87·10⁶ mm⁴"),
            (QuantityKind.SectionModulus, 57316.7 / 1e9, References(), "57300 mm³"),
            (QuantityKind.SafetyFactor, 1.2134, References(), "1,21"),
            (QuantityKind.SafetyFactor, 2.996, References(), "3"),
            (QuantityKind.SafetyFactor, 3.456, References(), "3,5"),
            (QuantityKind.SafetyFactor, 9.96, References(), "10"),
            (QuantityKind.SafetyFactor, 10.376, References(), "10"),
            (QuantityKind.SafetyFactor, 25.673, References(), "26")
        ];
        foreach (string culture in new[] { "de-DE", "en-US" })
        foreach (var example in examples)
            yield return [culture, example.Kind, example.Si, example.References,
                culture == "de-DE" ? example.Expected : example.Expected.Replace(',', '.')];
    }

    [Theory]
    [MemberData(nameof(StandardExamples))]
    public void AllSpecificationExamples(string culture, QuantityKind kind, double si,
        ModelReferenceValues references, string expected) =>
        Assert.Equal(expected, QuantityFormatter.Format(si, kind, references: references,
            culture: CultureInfo.GetCultureInfo(culture)));

    [Theory]
    [InlineData(1, 1, 0)]
    [InlineData(1.0000000000000002, 2, 0)]
    [InlineData(1.9999999999999998, 2, 0)]
    [InlineData(2, 2, 0)]
    [InlineData(2.0000000000000004, 5, 0)]
    [InlineData(4.999999999999999, 5, 0)]
    [InlineData(5, 5, 0)]
    [InlineData(5.000000000000001, 1, 1)]
    [InlineData(9.999999999999998, 1, 1)]
    [InlineData(10, 1, 1)]
    [InlineData(10.000000000000002, 2, 1)]
    [InlineData(0.1, 1, -1)]
    [InlineData(0.2, 2, -1)]
    [InlineData(0.5, 5, -1)]
    [InlineData(0.0007312, 1, -3)]
    [InlineData(1e-300, 1, -300)]
    [InlineData(1e300, 1, 300)]
    public void NiceStepsCeilWithoutAdvancingExactBoundaries(double minimum, int mantissa, int exponent) =>
        Assert.Equal(new DecimalNumber(mantissa, exponent), NumericRounding.Ceiling125(minimum));

    [Fact]
    public void NiceStepCanExceedDoubleRange()
    {
        Assert.Equal(new DecimalNumber(2, 308), NumericRounding.Ceiling125(double.MaxValue));
        Assert.Equal(new DecimalNumber(5, -324), NumericRounding.Ceiling125(double.Epsilon));
    }

    [Theory]
    [InlineData(0.05, "0.1")]
    [InlineData(-0.05, "-0.1")]
    [InlineData(0.15, "0.2")]
    [InlineData(-0.15, "-0.2")]
    [InlineData(1.25, "1.3")]
    [InlineData(-1.25, "-1.3")]
    [InlineData(0.04, "≈ 0")]
    [InlineData(-0.04, "≈ 0")]
    [InlineData(0, "0")]
    public void HalfValuesRoundAwayAndSmallValuesAreMarked(double value, string expected) =>
        Assert.Equal(expected, NumberFormatter.Format(NumericRounding.RoundToStep(value, new(1, -1)), CultureInfo.InvariantCulture));

    [Fact]
    public void SignedZeroIsExactInAllQuantitiesAndModes()
    {
        double negativeZero = BitConverter.Int64BitsToDouble(long.MinValue);
        foreach (var mode in Enum.GetValues<PresentationMode>())
        foreach (var kind in Enum.GetValues<QuantityKind>())
        foreach (double zero in new[] { 0d, negativeZero })
        {
            var rounded = NumericRounding.Round(zero, kind, UnitProfile.Default[kind], mode, References());
            Assert.False(rounded.IsApproximateZero);
            Assert.Equal("0", NumberFormatter.Format(rounded));
        }
    }

    [Theory]
    [InlineData(0.05)]
    [InlineData(0.15)]
    [InlineData(1.25)]
    public void ImmediateNeighborsOfHalfValuesRemainDistinct(double half)
    {
        var step = new DecimalNumber(1, -1);
        var below = NumericRounding.RoundToStep(Math.BitDecrement(half), step);
        var equal = NumericRounding.RoundToStep(half, step);
        var above = NumericRounding.RoundToStep(Math.BitIncrement(half), step);
        Assert.NotEqual(below.Value, equal.Value);
        Assert.Equal(equal.Value, above.Value);
        Assert.Equal(equal.Value.Mantissa * -1,
            NumericRounding.RoundToStep(-half, step).Value.Mantissa);
        var negativeBelow = NumericRounding.RoundToStep(-Math.BitDecrement(half), step).Value;
        Assert.Equal(-below.Value.Mantissa, negativeBelow.Mantissa);
        Assert.Equal(below.Value.Exponent, negativeBelow.Exponent);
    }

    [Theory]
    [InlineData(0.05, 5, -4)]
    [InlineData(0.07312, 1, -3)]
    [InlineData(1, 1, -2)]
    [InlineData(10, 1, -1)]
    [InlineData(1e7, 1, 5)]
    public void DeflectionResolutionGrowsWithoutLengthCap(double length, int mantissa, int exponent)
    {
        var rounded = NumericRounding.Round(1e-10, QuantityKind.TransverseDisplacement,
            UnitCatalog.Millimeter, references: References(length: length));
        Assert.Equal(new DecimalNumber(mantissa, exponent), rounded.Step);
        Assert.True(rounded.IsApproximateZero);
    }

    [Theory]
    [InlineData(QuantityKind.TransverseForce, 0.1, 1, -4)]
    [InlineData(QuantityKind.TransverseForce, 1000, 1, 0)]
    [InlineData(QuantityKind.TransverseForce, 1e9, 1, 0)]
    [InlineData(QuantityKind.Moment, 0.1, 1, -4)]
    [InlineData(QuantityKind.Moment, 100, 1, -1)]
    [InlineData(QuantityKind.Moment, 1e9, 1, -1)]
    [InlineData(QuantityKind.Stress, 1e4, 1, 1)]
    [InlineData(QuantityKind.Stress, 235e6, 1, 5)]
    public void ReferenceContributionsAndCaps(QuantityKind kind, double reference, int mantissa, int exponent)
    {
        var references = References(force: reference, moment: reference, yield: reference);
        var unit = kind == QuantityKind.TransverseForce ? UnitCatalog.Newton
            : kind == QuantityKind.Moment ? UnitCatalog.NewtonMeter : UnitCatalog.Pascal;
        Assert.Equal(new DecimalNumber(mantissa, exponent),
            NumericRounding.Round(1e-8, kind, unit, references: references).Step);
    }

    [Theory]
    [InlineData(QuantityKind.TransverseForce)]
    [InlineData(QuantityKind.Moment)]
    [InlineData(QuantityKind.Stress)]
    [InlineData(QuantityKind.TransverseDisplacement)]
    public void ZeroReferenceOmitsModelContribution(QuantityKind kind)
    {
        var unit = kind == QuantityKind.TransverseForce ? UnitCatalog.Newton
            : kind == QuantityKind.Moment ? UnitCatalog.NewtonMeter
            : kind == QuantityKind.Stress ? UnitCatalog.Pascal : UnitCatalog.Meter;
        Assert.False(NumericRounding.Round(8.4e-5, kind, unit, references: new(0, 0, 0, 0)).IsApproximateZero);
        Assert.Throws<ArgumentNullException>(() => NumericRounding.Round(1, kind, unit));
    }

    [Theory]
    [InlineData(QuantityKind.AxialForce, "84·10⁻⁶ N")]
    [InlineData(QuantityKind.AxialDisplacement, "0.084 mm")]
    [InlineData(QuantityKind.Rotation, "84·10⁻⁶ rad")]
    [InlineData(QuantityKind.Area, "84 mm²")]
    [InlineData(QuantityKind.SecondMomentOfArea, "84·10⁶ mm⁴")]
    [InlineData(QuantityKind.SectionModulus, "84000 mm³")]
    public void FallbackQuantitiesIgnoreModelSize(QuantityKind kind, string expected) =>
        Assert.Equal(expected, QuantityFormatter.Format(8.4e-5, kind,
            references: References(1e300, 1e300, 1e300, 1e300), culture: CultureInfo.InvariantCulture));

    [Theory]
    [InlineData(2.9999999999999996, 1, -2, "3")]
    [InlineData(3, 1, -1, "3")]
    [InlineData(3.0000000000000004, 1, -1, "3")]
    [InlineData(9.999999999999998, 1, -1, "10")]
    [InlineData(10, 1, 0, "10")]
    [InlineData(10.000000000000002, 1, 0, "10")]
    [InlineData(99.99999999999999, 1, 0, "100")]
    [InlineData(100, 1, 1, "100")]
    [InlineData(100.00000000000001, 1, 1, "100")]
    [InlineData(105, 1, 1, "110")]
    public void SafetyBandUsesUnroundedValue(double value, int mantissa, int exponent, string expected)
    {
        var rounded = NumericRounding.Round(value, QuantityKind.SafetyFactor, UnitCatalog.Dimensionless);
        Assert.Equal(new DecimalNumber(mantissa, exponent), rounded.Step);
        Assert.Equal(expected, NumberFormatter.Format(rounded, CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData(114.99, 1, 0, "110 MPa")]
    [InlineData(115, 1, 0, "115 MPa")]
    [InlineData(117.49, 1, 0, "117 MPa")]
    [InlineData(117.5, 1, 0, "118 MPa")]
    [InlineData(117.51, 1, 0, "118 MPa")]
    [InlineData(-115, 1, 0, "-115 MPa")]
    public void StressPromotesPrecisionBeforeYieldHalfThreshold(double megapascals, int mantissa, int exponent, string expected)
    {
        var rounded = NumericRounding.Round(megapascals * 1e6, QuantityKind.Stress, UnitCatalog.Megapascal,
            references: References());
        // Before promotion the step is 10 MPa, afterwards 1 MPa.
        Assert.Equal(new DecimalNumber(mantissa, exponent + (Math.Abs(megapascals) < 115 ? 1 : 0)), rounded.Step);
        Assert.Equal(expected, QuantityFormatter.Format(megapascals * 1e6, QuantityKind.Stress,
            references: References(), culture: CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData(QuantityKind.TransverseDisplacement, 0.00765 / 1000, "7.65·10⁻³ mm")]
    [InlineData(QuantityKind.TransverseForce, 0.000084, "84·10⁻⁶ N")]
    [InlineData(QuantityKind.Moment, 0.034, "0.034 N·m")]
    [InlineData(QuantityKind.Stress, 26.01e6, "26 MPa")]
    [InlineData(QuantityKind.Stress, 114.99e6, "115 MPa")]
    [InlineData(QuantityKind.SafetyFactor, 3.456, "3.46")]
    [InlineData(QuantityKind.SafetyFactor, 25.673, "25.7")]
    [InlineData(QuantityKind.SafetyFactor, 100.5, "101")]
    public void DetailedUsesThreeDigitsWithoutReferences(QuantityKind kind, double value, string expected) =>
        Assert.Equal(expected, QuantityFormatter.Format(value, kind, mode: PresentationMode.Detailed,
            culture: CultureInfo.InvariantCulture));

    [Theory]
    [InlineData(PresentationMode.Standard)]
    [InlineData(PresentationMode.Detailed)]
    public void VerySmallAndLargeFiniteValuesRemainFiniteInText(PresentationMode mode)
    {
        Assert.Equal("4.94·10⁻³²⁴ rad", QuantityFormatter.Format(double.Epsilon, QuantityKind.Rotation,
            mode: mode, culture: CultureInfo.InvariantCulture));
        Assert.Equal("-4.94·10⁻³²⁴ rad", QuantityFormatter.Format(-double.Epsilon, QuantityKind.Rotation,
            mode: mode, culture: CultureInfo.InvariantCulture));
        Assert.Equal("180·10³⁰⁶ rad", QuantityFormatter.Format(double.MaxValue, QuantityKind.Rotation,
            mode: mode, culture: CultureInfo.InvariantCulture));
        Assert.Equal("180·10³¹⁸ mm⁴", QuantityFormatter.Format(double.MaxValue, QuantityKind.SecondMomentOfArea,
            mode: mode, culture: CultureInfo.InvariantCulture));
        Assert.Equal("1·10⁻³⁰⁰ rad", QuantityFormatter.Format(1e-300, QuantityKind.Rotation,
            mode: mode, culture: CultureInfo.InvariantCulture));
        Assert.Equal("∞", QuantityFormatter.Format(double.PositiveInfinity, QuantityKind.SafetyFactor, mode: mode));
    }

    [Theory]
    [InlineData(123, -5, "1.23·10⁻³")]
    [InlineData(123, -4, "0.0123")]
    [InlineData(75, -4, "0.0075")]
    [InlineData(1, -4, "0.0001")]
    [InlineData(1, -5, "10·10⁻⁶")]
    [InlineData(999999, 0, "999999")]
    [InlineData(1, 6, "1·10⁶")]
    [InlineData(123456, -5, "1.23456")]
    [InlineData(-123, -5, "-1.23·10⁻³")]
    public void NotationOnlyRearrangesRoundedDigits(int mantissa, int exponent, string expected) =>
        Assert.Equal(expected, NumberFormatter.Format(new DecimalNumber(mantissa, exponent), CultureInfo.InvariantCulture));

    [Fact]
    public void NormalizedEndZerosDoNotCountAsFractionalDigits()
    {
        Assert.Equal(new DecimalNumber(75, -4), new DecimalNumber(75000, -7));
        Assert.Equal("0.0075", NumberFormatter.Format(new DecimalNumber(75000, -7), CultureInfo.InvariantCulture));
        Assert.Equal("12.3456789", NumberFormatter.Format(new DecimalNumber(123456789, -7), CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("de-DE", "en-US", "1,23 rad", "1.23 rad")]
    [InlineData("en-US", "de-DE", "1.23 rad", "1,23 rad")]
    public void NumericCultureAndExplicitOverrideAreIndependentOfUi(string current, string ui, string expected, string overridden)
    {
        var oldCulture = CultureInfo.CurrentCulture;
        var oldUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(current);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(ui);
            Assert.Equal(expected, QuantityFormatter.Format(1.234, QuantityKind.Rotation));
            Assert.Equal(overridden, QuantityFormatter.Format(1.234, QuantityKind.Rotation, culture: CultureInfo.CurrentUICulture));
            Assert.Same(UnitCatalog.Millimeter, UnitProfile.Default[QuantityKind.BeamLength]);
        }
        finally
        {
            CultureInfo.CurrentCulture = oldCulture;
            CultureInfo.CurrentUICulture = oldUi;
        }
    }

    [Fact]
    public void InvalidValuesAndIncompatibleUnitsAreRejected()
    {
        foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            Assert.Throws<ArgumentOutOfRangeException>(() => NumericRounding.Round(invalid, QuantityKind.AxialForce, UnitCatalog.Newton));
        Assert.Throws<ArgumentOutOfRangeException>(() => QuantityFormatter.Format(-1, QuantityKind.SafetyFactor));
        Assert.Throws<ArgumentOutOfRangeException>(() => QuantityFormatter.Format(double.NegativeInfinity, QuantityKind.SafetyFactor));
        Assert.Throws<ArgumentException>(() => NumericRounding.Round(1, QuantityKind.AxialForce, UnitCatalog.Meter));
        Assert.Throws<ArgumentOutOfRangeException>(() => NumericRounding.Round(1, QuantityKind.Rotation, UnitCatalog.Radian, (PresentationMode)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => NumericRounding.RoundToStep(1, new(0, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => NumericRounding.Ceiling125(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => NumericRounding.Ceiling125(double.NaN));
    }
}
