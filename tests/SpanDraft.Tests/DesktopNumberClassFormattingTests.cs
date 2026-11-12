using System.Globalization;
using SpanDraft.Desktop.Presentation;
using Xunit;

namespace SpanDraft.Tests;

/// <summary>
/// The two number classes share one rounding (R1–R5); given values are marked with "≈" when the display differs
/// from the exact value (R6, R7), results never are (R8).
/// </summary>
public sealed class DesktopNumberClassFormattingTests
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");
    private static readonly UnitProfile Kilonewtons = UnitProfile.Default.WithUnit(QuantityKind.TransverseForce, UnitCatalog.Kilonewton);

    [Theory]
    [InlineData(0.8, "800", false)]
    [InlineData(0.800000004, "800", true)]
    [InlineData(1.234, "1\u202f234", false)]
    [InlineData(-1.2345, "-1\u202f235", true)]
    [InlineData(0, "0", false)]
    [InlineData(12.5, "12\u202f500", false)]
    public void GivenValuesUseTheResultRoundingAndAreMarkedOnlyWhenRounded(double meters, string number, bool approximate)
    {
        string expected = approximate ? "≈ " + number : number;
        Assert.Equal(expected, QuantityFormatter.FormatInputNumber(meters, UnitCatalog.Millimeter, culture: German));
        Assert.Equal(expected + " mm", QuantityFormatter.FormatInput(meters, UnitCatalog.Millimeter, culture: German));
        Assert.Equal("a " + (approximate ? "≈" : "=") + " " + number + " mm",
            QuantityFormatter.FormatInputEquation("a", meters, UnitCatalog.Millimeter, culture: German));
        var text = QuantityFormatter.Input(meters, UnitCatalog.Millimeter, culture: German);
        Assert.Equal(approximate, text.IsApproximate);
        Assert.Equal(number, text.Number);
    }

    [Fact]
    public void NegativeZeroIsShownAsPlainZero()
    {
        Assert.Equal("0", QuantityFormatter.FormatInputNumber(-0.0, UnitCatalog.Millimeter, culture: German));
        Assert.Equal("0 N", QuantityFormatter.FormatResult(-0.0, QuantityKind.TransverseForce, culture: German));
    }

    [Theory]
    [InlineData(0.8, "800", "800")]
    [InlineData(0.800000004, "≈ 800", "800,000004")]
    [InlineData(1.234, "1\u202f234", "1234")]
    [InlineData(-1.2345, "≈ -1\u202f235", "-1234,5")]
    [InlineData(123.456789012, "≈ 123\u202f457", "123456,789012")]
    public void TheEditingBufferKeepsAllDigitsOfARoundedGivenValue(double meters, string display, string editing)
    {
        Assert.Equal(display, QuantityFormatter.FormatInputNumber(meters, UnitCatalog.Millimeter, culture: German));
        Assert.Equal(editing, InputQuantityFormatter.FormatExact(meters, UnitCatalog.Millimeter, German));
    }

    [Theory]
    [InlineData(1e-7, "100\u00a0·\u00a010⁻⁹", "0,0000001")]
    [InlineData(1e16, "10\u00a0·\u00a010¹⁵", "10\u202f000\u202f000\u202f000\u202f000\u202f000")]
    [InlineData(-1e16, "-10\u00a0·\u00a010¹⁵", "-10\u202f000\u202f000\u202f000\u202f000\u202f000")]
    [InlineData(123456789012, "≈ 123\u00a0·\u00a010⁹", "123\u202f456\u202f789\u202f012")]
    public void GivenValuesFollowTheNotationSetting(double newtons, string automatic, string off)
    {
        Assert.Equal(automatic, QuantityFormatter.FormatInputNumber(newtons, UnitCatalog.Newton, culture: German));
        Assert.Equal(off, QuantityFormatter.FormatInputNumber(newtons, UnitCatalog.Newton, notation: EngineeringNotation.Off, culture: German));
    }

    [Fact]
    public void GivenExtremesInTheDisplayUnitFollowTheNotationSetting()
    {
        // 1e-10 m is 1e-7 mm and 1e13 m is 1e16 mm.
        Assert.Equal("100\u00a0·\u00a010⁻⁹ mm", QuantityFormatter.FormatInput(1e-10, UnitCatalog.Millimeter, culture: German));
        Assert.Equal("10\u00a0·\u00a010¹⁵ mm", QuantityFormatter.FormatInput(1e13, UnitCatalog.Millimeter, culture: German));
        Assert.Equal("0,0000001 mm", QuantityFormatter.FormatInput(1e-10, UnitCatalog.Millimeter, notation: EngineeringNotation.Off, culture: German));
        Assert.Equal("10\u202f000\u202f000\u202f000\u202f000\u202f000 mm",
            QuantityFormatter.FormatInput(1e13, UnitCatalog.Millimeter, notation: EngineeringNotation.Off, culture: German));
    }

    [Fact]
    public void GivenValuesFollowThePrecisionAndNotationOptions()
    {
        using var scope = new ResultCultureScope("de-DE", "en-US");
        var automatic = new ResultPresentationOptions();
        var off = new ResultPresentationOptions(significantDigits: 5, notation: EngineeringNotation.Off);
        Assert.Equal("≈ 1,23\u00a0·\u00a010⁶ N", QuantityFormatter.FormatInput(1234567.125, UnitCatalog.Newton, automatic));
        Assert.Equal("≈ 1\u202f234\u202f567 N", QuantityFormatter.FormatInput(1234567.125, UnitCatalog.Newton, off));
        Assert.Equal("125\u00a0·\u00a010⁻⁶ N", QuantityFormatter.FormatInput(0.000125, UnitCatalog.Newton, automatic));
        Assert.Equal("0,000125 N", QuantityFormatter.FormatInput(0.000125, UnitCatalog.Newton, off));
        Assert.Equal("F ≈ 1\u202f234\u202f567 N", QuantityFormatter.FormatInputEquation("F", 1234567.125, UnitCatalog.Newton, off));
    }

    [Fact]
    public void RoundedGivenValuesAreMarkedInNumbersAndLabels()
    {
        // 0.1 m is 3.937007874015748 in.
        var text = QuantityFormatter.Input(0.1, UnitCatalog.Inch, culture: German);
        Assert.True(text.IsApproximate);
        Assert.Equal("3,94", text.Number);
        Assert.Equal("≈ 3,94", QuantityFormatter.FormatInputNumber(0.1, UnitCatalog.Inch, culture: German));
        Assert.Equal("≈ 3,94 in", QuantityFormatter.FormatInput(0.1, UnitCatalog.Inch, culture: German));
        Assert.Equal("F1 ≈ 3,94 in", QuantityFormatter.FormatInputEquation("F1", 0.1, UnitCatalog.Inch, culture: German));
        Assert.Equal("≈ -3,94 in", QuantityFormatter.FormatInput(-0.1, UnitCatalog.Inch, culture: German));
        // Every rounded value is marked, independent of how many digits the exact value has. No zeros are added.
        Assert.Equal("≈ 1,23 N", QuantityFormatter.FormatInput(1.23456789012, UnitCatalog.Newton, culture: German));
        Assert.Equal("≈ 1,23 N", QuantityFormatter.FormatInput(1.234567890123, UnitCatalog.Newton, culture: German));
        Assert.Equal("≈ 2 N", QuantityFormatter.FormatInput(2.000000000001, UnitCatalog.Newton, culture: German));
        Assert.Equal("≈ 1,2 N", QuantityFormatter.FormatInput(1.200000000001, UnitCatalog.Newton, culture: German));
        Assert.Equal("≈ 1\u202f234\u202f568 N", QuantityFormatter.FormatInput(1234567.890123456, UnitCatalog.Newton,
            notation: EngineeringNotation.Off, culture: German));
        // The stored value and the editing text are untouched.
        Assert.Equal("3,937007874015748", InputQuantityFormatter.FormatExact(0.1, UnitCatalog.Inch, German));
    }

    [Theory]
    [InlineData(0.1, "≈ 3,94")]                    // 3.937007874015748 in
    [InlineData(1, "≈ 39,4")]                      // 39.37007874015748 in
    [InlineData(10, "≈ 394")]                      // 393.7007874015748 in
    [InlineData(100, "≈ 3\u202f937")]                 // 3 937.007874015748 in
    [InlineData(-100, "≈ -3\u202f937")]
    [InlineData(0.0001, "≈ 0,00394")]              // 0.003937007874015748 in
    public void RoundedGivenValuesKeepWholeUnits(double meters, string expected) =>
        Assert.Equal(expected, QuantityFormatter.FormatInputNumber(meters, UnitCatalog.Inch, culture: German));

    [Theory]
    [InlineData(0.012356, QuantityKind.TransverseDisplacement, "12,4 mm")]
    [InlineData(0.012, QuantityKind.TransverseDisplacement, "12 mm")]
    [InlineData(0.000003456, QuantityKind.TransverseDisplacement, "0,00346 mm")]
    [InlineData(-0.012356, QuantityKind.TransverseDisplacement, "-12,4 mm")]
    [InlineData(1234.56, QuantityKind.TransverseForce, "1\u202f235 N")]
    [InlineData(12345.67, QuantityKind.TransverseForce, "12\u202f346 N")]
    [InlineData(-12345.67, QuantityKind.TransverseForce, "-12\u202f346 N")]
    [InlineData(1234567, QuantityKind.TransverseForce, "1,23\u00a0·\u00a010⁶ N")]
    [InlineData(999999.6, QuantityKind.TransverseForce, "1\u00a0·\u00a010⁶ N")]
    [InlineData(999499.4, QuantityKind.TransverseForce, "999\u202f499 N")]
    [InlineData(0.00012356, QuantityKind.TransverseForce, "124\u00a0·\u00a010⁻⁶ N")]
    [InlineData(0.0000012, QuantityKind.TransverseForce, "1,2\u00a0·\u00a010⁻⁶ N")]
    [InlineData(12000000, QuantityKind.TransverseForce, "12\u00a0·\u00a010⁶ N")]
    [InlineData(0, QuantityKind.TransverseForce, "0 N")]
    public void ResultsRoundToThePrecisionButNeverCoarserThanOneUnit(double si, QuantityKind kind, string expected) =>
        Assert.Equal(expected, QuantityFormatter.FormatResult(si, kind, culture: German));

    [Fact]
    public void ResultsRoundAfterConversionToTheDisplayUnit()
    {
        Assert.Equal("12,3 kN", QuantityFormatter.FormatResult(12345.67, QuantityKind.TransverseForce, Kilonewtons, culture: German));
        Assert.Equal("1\u202f235 kN", QuantityFormatter.FormatResult(1234560, QuantityKind.TransverseForce, Kilonewtons, culture: German));
    }

    [Theory]
    [InlineData(1234.56, 1, "1\u202f235")]
    [InlineData(1234.56, 4, "1\u202f235")]
    [InlineData(1234.56, 5, "1\u202f234,6")]
    [InlineData(1234.5, 5, "1\u202f234,5")]
    [InlineData(12, 1, "12")]
    [InlineData(12, 2, "12")]
    [InlineData(12, 5, "12")]
    [InlineData(12.5, 5, "12,5")]
    [InlineData(0.5, 1, "0,5")]
    [InlineData(0.25, 1, "0,3")]
    [InlineData(-0.25, 1, "-0,3")]
    [InlineData(1234.5, 2, "1\u202f235")]
    [InlineData(-1234.5, 2, "-1\u202f235")]
    public void ResultPrecisionShowsNoTrailingZeros(double value, int digits, string expected) =>
        Assert.Equal(expected, QuantityFormatter.FormatResultNumber(value, QuantityKind.TransverseForce,
            significantDigits: digits, culture: German));

    [Theory]
    [InlineData(1234567, "1\u202f234\u202f567")]
    [InlineData(999999.6, "1\u202f000\u202f000")]
    [InlineData(-1234567.5, "-1\u202f234\u202f568")]
    [InlineData(0.00012356, "0,000124")]
    [InlineData(0.0000012, "0,0000012")]
    public void ResultsWithNotationOffStayDecimalAndKeepWholeUnits(double value, string expected) =>
        Assert.Equal(expected, QuantityFormatter.FormatResultNumber(value, QuantityKind.TransverseForce,
            notation: EngineeringNotation.Off, culture: German));

    [Fact]
    public void EngineeringMantissaIsDeterminedByThePrecisionAlone()
    {
        string Number(double value, int digits) => QuantityFormatter.FormatResultNumber(value, QuantityKind.TransverseForce,
            significantDigits: digits, culture: German);
        Assert.Equal("1\u00a0·\u00a010⁶", Number(1234567, 1));
        Assert.Equal("1,2\u00a0·\u00a010⁶", Number(1234567, 2));
        Assert.Equal("1,2346\u00a0·\u00a010⁶", Number(1234567, 5));
        Assert.Equal("120\u00a0·\u00a010⁶", Number(123456789, 2));
        Assert.Equal("123,5\u00a0·\u00a010⁶", Number(123456789, 4));
        Assert.Equal("1\u00a0·\u00a010⁶", Number(999999.6, 5));
        Assert.Equal("999\u202f949", Number(999949, 5));
    }

    [Fact]
    public void InfiniteSafetyFactorAndExactZeroKeepTheirSymbols()
    {
        Assert.Equal("∞", QuantityFormatter.FormatResult(double.PositiveInfinity, QuantityKind.SafetyFactor, culture: German));
        Assert.Equal("2,5", QuantityFormatter.FormatResult(2.5, QuantityKind.SafetyFactor, culture: German));
        Assert.Equal("0", QuantityFormatter.FormatResultNumber(0, QuantityKind.Moment, significantDigits: 5, culture: German));
    }
}
