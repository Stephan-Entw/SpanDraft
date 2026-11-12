using System.Globalization;
using System.Text.RegularExpressions;
using SpanDraft.Desktop.Presentation;
using Xunit;

namespace SpanDraft.Tests;

/// <summary>
/// Acceptance tables of the unified number presentation (R1–R10): every displayed value is rounded to n
/// significant digits, never coarser than one whole unit in decimal notation, without trailing zeros; rounded
/// given values carry "≈". The expectations use plain spaces for readability; <see cref="App"/> maps them to the
/// app's group separator (U+202F) and the protected engineering separators.
/// </summary>
public sealed class DesktopNumberPresentationAcceptanceTests
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>Digit groups use U+202F, "a · 10ⁿ" uses protected spaces and minus is the culture's sign.</summary>
    internal static string App(string readable) => Regex.Replace(
        readable.Replace(" · ", " · ").Replace('−', '-'), "(?<=[0-9]) (?=[0-9]{3}(?![0-9]))", " ");

    private static UnitDefinition Unit(string id) => UnitCatalog.All.Single(u => u.Id == id);

    /// <summary>The SI value of a value typed in <paramref name="inputUnit"/>, through the app's parser.</summary>
    private static double Entered(string text, string inputUnit)
    {
        Assert.True(InputQuantityFormatter.TryParse(text, Unit(inputUnit), out double si, German), text);
        return si;
    }

    private static string Given(string entered, string inputUnit, string displayUnit, int digits = 3,
        EngineeringNotation notation = EngineeringNotation.Automatic) =>
        QuantityFormatter.FormatInput(Entered(entered, inputUnit), Unit(displayUnit), digits, notation, German);

    [Theory]
    [InlineData("V1", "800", "mm", "mm", "800 mm")]
    [InlineData("V2", "800,000004", "mm", "mm", "≈ 800 mm")]
    [InlineData("V3", "800,1", "mm", "mm", "≈ 800 mm")]
    [InlineData("V4", "1234,5", "mm", "mm", "≈ 1 235 mm")]
    [InlineData("V5", "1250,5", "mm", "mm", "≈ 1 251 mm")]
    [InlineData("V6", "12345,567", "mm", "mm", "≈ 12 346 mm")]
    [InlineData("V7", "999,95", "mm", "mm", "≈ 1 000 mm")]
    [InlineData("V8", "12,375", "mm", "mm", "≈ 12,4 mm")]
    [InlineData("V9", "0", "mm", "mm", "0 mm")]
    [InlineData("V10", "6000", "mm", "mm", "6 000 mm")]
    [InlineData("V11", "12334567,89", "mm", "mm", "≈ 12,3 · 10⁶ mm")]
    [InlineData("V12", "0,8", "mm", "mm", "0,8 mm")]
    [InlineData("V13", "6,35", "mm", "mm", "6,35 mm")]
    [InlineData("V14", "0,00042", "mm", "mm", "420 · 10⁻⁶ mm")]
    [InlineData("V15", "-1000", "N", "N", "−1 000 N")]
    [InlineData("V16", "-1234,5", "N", "N", "≈ −1 235 N")]
    [InlineData("V17", "-1,25", "kN/m", "kN/m", "−1,25 kN/m")]
    [InlineData("V18", "210", "GPa", "GPa", "210 GPa")]
    [InlineData("V19", "235", "MPa", "MPa", "235 MPa")]
    [InlineData("V20", "7850", "kg/m3", "kg/m3", "7 850 kg/m³")]
    [InlineData("V21", "0,3", "1", "1", "0,3")]
    [InlineData("V22", "800", "mm", "m", "0,8 m")]
    [InlineData("V23", "1234,5", "mm", "m", "≈ 1,23 m")]
    [InlineData("V24", "12,5", "mm", "m", "0,0125 m")]
    [InlineData("V25", "800", "mm", "in", "≈ 31,5 in")]
    [InlineData("V26", "100", "mm", "in", "≈ 3,94 in")]
    [InlineData("V27", "50", "mm", "in", "≈ 1,97 in")]
    [InlineData("V28", "25,4", "mm", "in", "1 in")]
    [InlineData("V29", "6,35", "mm", "in", "0,25 in")]
    [InlineData("V30", "1234,5", "mm", "in", "≈ 48,6 in")]
    [InlineData("V31", "12345,567", "mm", "in", "≈ 486 in")]
    [InlineData("V32", "800,000004", "mm", "in", "≈ 31,5 in")]
    [InlineData("V33", "6000", "mm", "ft", "≈ 19,7 ft")]
    [InlineData("V34", "-1000", "N", "kip", "≈ −0,225 kip")]
    [InlineData("V35", "-1234,5", "N", "kip", "≈ −0,278 kip")]
    [InlineData("V36", "-1,25", "kN/m", "kip/ft", "≈ −0,0857 kip/ft")]
    [InlineData("V37", "235", "MPa", "ksi", "≈ 34,1 ksi")]
    [InlineData("V38", "210", "GPa", "ksi", "≈ 30 458 ksi")]
    [InlineData("V39", "31,5", "in", "mm", "≈ 800 mm")]
    [InlineData("V40", "1", "in", "mm", "25,4 mm")]
    [InlineData("V41", "0,25", "in", "mm", "6,35 mm")]
    [InlineData("V42", "20", "ft", "mm", "6 096 mm")]
    [InlineData("V43", "19,685", "ft", "mm", "≈ 6 000 mm")]
    [InlineData("V44", "-1", "kip", "N", "≈ −4 448 N")]
    [InlineData("V45", "-1", "kip", "kN", "≈ −4,45 kN")]
    [InlineData("V46", "-2,5", "kip", "kN", "≈ −11,1 kN")]
    [InlineData("V47", "36", "ksi", "MPa", "≈ 248 MPa")]
    [InlineData("V48", "29000", "ksi", "GPa", "≈ 200 GPa")]
    [InlineData("V49", "-0,5", "kip/ft", "kN/m", "≈ −7,3 kN/m")]
    [InlineData("V50", "12,375", "in", "mm", "≈ 314 mm")]
    [InlineData("V51", "114,3", "mm", "mm", "≈ 114 mm")]
    [InlineData("V52", "101,6", "mm", "mm", "≈ 102 mm")]
    [InlineData("V53", "304,8", "mm", "mm", "≈ 305 mm")]
    [InlineData("V54", "3,175", "mm", "mm", "≈ 3,18 mm")]
    [InlineData("V55", "1,125", "mm", "mm", "≈ 1,13 mm")]
    [InlineData("V56", "0,045", "in", "mm", "≈ 1,14 mm")]
    [InlineData("V57", "3,14", "in2", "mm2", "≈ 2 026 mm²")]
    [InlineData("V58", "15,3", "in4", "mm4", "≈ 6,37 · 10⁶ mm⁴")]
    [InlineData("V59", "19430000", "mm4", "mm4", "≈ 19,4 · 10⁶ mm⁴")]
    [InlineData("V60", "0,004", "mm", "mm", "0,004 mm")]
    [InlineData("V61", "12,375", "in", "in", "≈ 12,4 in")]
    [InlineData("V62", "-1", "kip", "kip", "−1 kip")]
    [InlineData("V63", "19,685", "ft", "ft", "≈ 19,7 ft")]
    [InlineData("V64", "1234,5", "N/m", "kN/m", "≈ 1,23 kN/m")]
    [InlineData("V65", "-0,333", "kN/m", "N/m", "−333 N/m")]
    [InlineData("V66", "12345,5", "mm", "mm", "≈ 12 346 mm")]
    [InlineData("V67", "123456,78", "mm", "mm", "≈ 123 457 mm")]
    public void GivenValues(string row, string entered, string inputUnit, string displayUnit, string expected)
    {
        Assert.Equal(App(expected), Given(entered, inputUnit, displayUnit));
        // R6: the same decision marks labels, where "≈" replaces the equals sign.
        var options = new ResultPresentationOptions();
        double si = Entered(entered, inputUnit);
        var text = QuantityFormatter.Input(si, Unit(displayUnit), options, German);
        Assert.Equal(expected.StartsWith('≈'), text.IsApproximate);
        string symbol = Unit(displayUnit).Symbol;
        string number = App(expected.TrimStart('≈', ' '));
        if (symbol.Length > 0) number = number[..^(symbol.Length + 1)];
        Assert.Equal(number, text.Number);
        Assert.Equal(row + " " + (text.IsApproximate ? "≈" : "=") + " " + App(expected.TrimStart('≈', ' ')),
            QuantityFormatter.FormatInputEquation(row, si, Unit(displayUnit), options, German));
    }

    [Theory]
    [InlineData("E1", 1487.5, "N", QuantityKind.TransverseForce, "1 488 N")]
    [InlineData("E2", 12.5, "N", QuantityKind.TransverseForce, "12,5 N")]
    [InlineData("E3", -1487.5, "N", QuantityKind.TransverseForce, "−1 488 N")]
    [InlineData("E4", 999.6, "N", QuantityKind.TransverseForce, "1 000 N")]
    [InlineData("E5", 25012.4, "N", QuantityKind.TransverseForce, "25 012 N")]
    [InlineData("E6", 999999.6, "N", QuantityKind.TransverseForce, "1 · 10⁶ N")]
    [InlineData("E7", 3.55e-15, "N", QuantityKind.TransverseForce, "3,55 · 10⁻¹⁵ N")]
    [InlineData("E8", 0d, "N", QuantityKind.TransverseForce, "0 N")]
    [InlineData("E9", -743.75, "N", QuantityKind.TransverseForce, "−744 N")]
    [InlineData("E10", 0.4999, "N", QuantityKind.TransverseForce, "0,5 N")]
    [InlineData("E11", 743.75, "Nm", QuantityKind.Moment, "744 N·m")]
    [InlineData("E12", 0.0625, "Nm", QuantityKind.Moment, "0,0625 N·m")]
    [InlineData("E13", -1234.567, "Nm", QuantityKind.Moment, "−1 235 N·m")]
    [InlineData("E14", 9.996, "Nm", QuantityKind.Moment, "10 N·m")]
    [InlineData("E15", 15625d, "Nm", QuantityKind.Moment, "15 625 N·m")]
    [InlineData("E16", 1234567d, "Nm", QuantityKind.Moment, "1,23 · 10⁶ N·m")]
    [InlineData("E17", 0.00049, "Nm", QuantityKind.Moment, "490 · 10⁻⁶ N·m")]
    [InlineData("E18", 87.3416, "MPa", QuantityKind.Stress, "87,3 MPa")]
    [InlineData("E19", 0.4127, "MPa", QuantityKind.Stress, "0,413 MPa")]
    [InlineData("E20", 235.04, "MPa", QuantityKind.Stress, "235 MPa")]
    [InlineData("E21", -12.06, "MPa", QuantityKind.Stress, "−12,1 MPa")]
    [InlineData("E22", 1.005, "MPa", QuantityKind.Stress, "1,01 MPa")]
    [InlineData("E23", 0.000871, "MPa", QuantityKind.Stress, "871 · 10⁻⁶ MPa")]
    [InlineData("E24", 99.96, "MPa", QuantityKind.Stress, "100 MPa")]
    [InlineData("E25", 2.4583, "mm", QuantityKind.TransverseDisplacement, "2,46 mm")]
    [InlineData("E26", 0.98, "mm", QuantityKind.TransverseDisplacement, "0,98 mm")]
    [InlineData("E27", 0.104, "mm", QuantityKind.TransverseDisplacement, "0,104 mm")]
    [InlineData("E28", 0.021, "mm", QuantityKind.TransverseDisplacement, "0,021 mm")]
    [InlineData("E29", 0.0042, "mm", QuantityKind.TransverseDisplacement, "0,0042 mm")]
    [InlineData("E30", 0.000213, "mm", QuantityKind.TransverseDisplacement, "213 · 10⁻⁶ mm")]
    [InlineData("E31", -12.0, "mm", QuantityKind.TransverseDisplacement, "−12 mm")]
    [InlineData("E32", 125.67, "mm", QuantityKind.TransverseDisplacement, "126 mm")]
    [InlineData("E33", 0.0009996, "mm", QuantityKind.TransverseDisplacement, "0,001 mm")]
    [InlineData("E34", 0.0012345, "rad", QuantityKind.Rotation, "0,00123 rad")]
    [InlineData("E35", 2848.5, "mm2", QuantityKind.Area, "2 849 mm²")]
    [InlineData("E36", 19430000d, "mm4", QuantityKind.SecondMomentOfArea, "19,4 · 10⁶ mm⁴")]
    [InlineData("E37", 842.67, "mm4", QuantityKind.SecondMomentOfArea, "843 mm⁴")]
    [InlineData("E38", 194300d, "mm3", QuantityKind.SectionModulus, "194 300 mm³")]
    [InlineData("E39", 1234.56, "mm3", QuantityKind.SectionModulus, "1 235 mm³")]
    [InlineData("E40", -224.0, "N/m", QuantityKind.DistributedLoad, "−224 N/m")]
    [InlineData("E41", -0.2237, "kN/m", QuantityKind.DistributedLoad, "−0,224 kN/m")]
    [InlineData("E42", 1234.5678, "mm", QuantityKind.BeamLength, "1 235 mm")]
    [InlineData("E43", 12.3456, "mm", QuantityKind.BeamLength, "12,3 mm")]
    [InlineData("E44", 2.6837, "1", QuantityKind.SafetyFactor, "2,68")]
    [InlineData("E45", 13.4, "1", QuantityKind.SafetyFactor, "13,4")]
    [InlineData("E46", 1.0049, "1", QuantityKind.SafetyFactor, "1")]
    [InlineData("E47", 0.9995, "1", QuantityKind.SafetyFactor, "0,999")]
    [InlineData("E48", 0.99999, "1", QuantityKind.SafetyFactor, "0,999")]
    [InlineData("E49", 1.9999, "1", QuantityKind.SafetyFactor, "1,99")]
    [InlineData("E50", double.PositiveInfinity, "1", QuantityKind.SafetyFactor, "∞")]
    public void CalculatedValues(string row, double displayValue, string unit, QuantityKind kind, string expected)
    {
        string text = Calculated(displayValue, unit, kind);
        Assert.Equal(App(expected), text);
        Assert.DoesNotContain("≈", text); // R8
        Assert.NotNull(row);
    }

    private static string Calculated(double displayValue, string unit, QuantityKind kind, int digits = 3,
        EngineeringNotation notation = EngineeringNotation.Automatic)
    {
        double si = double.IsPositiveInfinity(displayValue) ? displayValue : Unit(unit).ToSi(displayValue);
        var options = new ResultPresentationOptions(UnitProfile.Default.WithUnit(kind, Unit(unit)), digits, notation);
        return QuantityFormatter.FormatResult(si, kind, options, German);
    }

    // ---- Settings variants ---------------------------------------------------------------------------------

    [Theory]
    [InlineData("V4", "1234,5", "mm", "mm", 1, "≈ 1 235 mm")]
    [InlineData("V4", "1234,5", "mm", "mm", 5, "1 234,5 mm")]
    [InlineData("V8", "12,375", "mm", "mm", 1, "≈ 12 mm")]
    [InlineData("V8", "12,375", "mm", "mm", 5, "12,375 mm")]
    [InlineData("V11", "12334567,89", "mm", "mm", 1, "≈ 10 · 10⁶ mm")]
    [InlineData("V11", "12334567,89", "mm", "mm", 5, "≈ 12,335 · 10⁶ mm")]
    [InlineData("V12", "0,8", "mm", "mm", 1, "0,8 mm")]
    [InlineData("V13", "6,35", "mm", "mm", 1, "≈ 6 mm")]
    [InlineData("V14", "0,00042", "mm", "mm", 1, "≈ 400 · 10⁻⁶ mm")]
    [InlineData("V14", "0,00042", "mm", "mm", 5, "420 · 10⁻⁶ mm")]
    [InlineData("V24", "12,5", "mm", "m", 1, "≈ 0,01 m")]
    [InlineData("V26", "100", "mm", "in", 1, "≈ 4 in")]
    [InlineData("V26", "100", "mm", "in", 5, "≈ 3,937 in")]
    [InlineData("V38", "210", "GPa", "ksi", 5, "≈ 30 458 ksi")]
    [InlineData("V59", "19430000", "mm4", "mm4", 5, "19,43 · 10⁶ mm⁴")]
    public void GivenValuesFollowTheDigitSetting(string row, string entered, string inputUnit, string displayUnit,
        int digits, string expected) =>
        Assert.True(App(expected) == Given(entered, inputUnit, displayUnit, digits),
            $"{row}, n = {digits}: expected {App(expected)}, got {Given(entered, inputUnit, displayUnit, digits)}");

    [Theory]
    [InlineData("E1", 1487.5, "N", QuantityKind.TransverseForce, 1, "1 488 N")]
    [InlineData("E1", 1487.5, "N", QuantityKind.TransverseForce, 5, "1 487,5 N")]
    [InlineData("E6", 999999.6, "N", QuantityKind.TransverseForce, 1, "1 · 10⁶ N")]
    [InlineData("E6", 999999.6, "N", QuantityKind.TransverseForce, 5, "1 · 10⁶ N")]
    [InlineData("E16", 1234567d, "Nm", QuantityKind.Moment, 1, "1 · 10⁶ N·m")]
    [InlineData("E16", 1234567d, "Nm", QuantityKind.Moment, 5, "1,2346 · 10⁶ N·m")]
    [InlineData("E18", 87.3416, "MPa", QuantityKind.Stress, 1, "87 MPa")]
    [InlineData("E18", 87.3416, "MPa", QuantityKind.Stress, 5, "87,342 MPa")]
    [InlineData("E25", 2.4583, "mm", QuantityKind.TransverseDisplacement, 1, "2 mm")]
    [InlineData("E25", 2.4583, "mm", QuantityKind.TransverseDisplacement, 5, "2,4583 mm")]
    [InlineData("E31", -12.0, "mm", QuantityKind.TransverseDisplacement, 5, "−12 mm")]
    [InlineData("E44", 2.6837, "1", QuantityKind.SafetyFactor, 1, "2")]
    [InlineData("E44", 2.6837, "1", QuantityKind.SafetyFactor, 5, "2,6837")]
    [InlineData("E47", 0.9995, "1", QuantityKind.SafetyFactor, 1, "0,9")]
    [InlineData("E47", 0.9995, "1", QuantityKind.SafetyFactor, 5, "0,9995")]
    [InlineData("E48", 0.99999, "1", QuantityKind.SafetyFactor, 5, "0,99999")]
    [InlineData("E49", 1.9999, "1", QuantityKind.SafetyFactor, 1, "1")]
    public void CalculatedValuesFollowTheDigitSetting(string row, double displayValue, string unit, QuantityKind kind,
        int digits, string expected) =>
        Assert.True(App(expected) == Calculated(displayValue, unit, kind, digits),
            $"{row}, n = {digits}: expected {App(expected)}, got {Calculated(displayValue, unit, kind, digits)}");

    [Fact]
    public void NotationOffAlwaysUsesDecimalNotationWithinWholeUnits()
    {
        Assert.Equal(App("≈ 12 334 568 mm"), Given("12334567,89", "mm", "mm", notation: EngineeringNotation.Off));
        Assert.Equal(App("0,00042 mm"), Given("0,00042", "mm", "mm", notation: EngineeringNotation.Off));
        Assert.Equal(App("1 000 000 N"), Calculated(999999.6, "N", QuantityKind.TransverseForce, notation: EngineeringNotation.Off));
        Assert.Equal(App("0,000213 mm"), Calculated(0.000213, "mm", QuantityKind.TransverseDisplacement, notation: EngineeringNotation.Off));
    }

    // ---- Editing text --------------------------------------------------------------------------------------

    [Theory]
    [InlineData("0,00042", "0,00042")]
    [InlineData("0,0000042", "0,0000042")]
    [InlineData("12334567,89", "12334567,89")]
    [InlineData("-1234,5", "-1234,5")]
    [InlineData("1e16", "10000000000000000")]
    public void EditingTextIsTheFullDecimalValueWithoutExponentOrGrouping(string entered, string expected)
    {
        double si = Entered(entered, "mm");
        string text = InputQuantityFormatter.FormatExact(si, Unit("mm"), German);
        Assert.Equal(expected, text);
        Assert.True(InputQuantityFormatter.TryParse(text, Unit("mm"), out double parsed, German));
        Assert.Equal(si, parsed);
    }

    public static TheoryData<double> DoubleExtremes => new()
    {
        double.Epsilon, -double.Epsilon, 2 * double.Epsilon, 2.2250738585072014e-308, 2.2250738585072009e-308,
        1e-300, 1e-200, 1e-20, 4.2e-9, 0.1, 1 / 3d, Math.PI, 1e20, 1e200, 1e300, double.MaxValue, -double.MaxValue,
        BitConverter.Int64BitsToDouble(0x7FEFFFFFFFFFFFFE)
    };

    [Theory]
    [MemberData(nameof(DoubleExtremes))]
    public void EditingTextRoundTripsExactlyInEveryUnit(double si)
    {
        foreach (var unit in UnitCatalog.All)
        foreach (var culture in new[] { German, CultureInfo.GetCultureInfo("en-US") })
        {
            string text = InputQuantityFormatter.FormatExact(si, unit, culture);
            Assert.DoesNotContain("E", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(" ", text);
            Assert.True(text.Length <= 512, $"{unit.Id}: {text.Length} characters");
            Assert.True(InputQuantityFormatter.TryParse(text, unit, out double parsed, culture), $"{unit.Id}: {text}");
            Assert.True(si.Equals(parsed), $"{unit.Id}: {si:R} → {text} → {parsed:R}");
        }
    }
}
