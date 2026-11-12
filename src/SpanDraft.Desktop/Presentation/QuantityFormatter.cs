using System.Globalization;
using System.Numerics;

namespace SpanDraft.Desktop.Presentation;

/// <summary>A given value's display digits; <see cref="IsApproximate"/> marks the rounded fallback.</summary>
public readonly record struct InputNumberText(string Number, bool IsApproximate)
{
    /// <summary>The relation for labels such as "F1 = 5 N": an approximation replaces the equals sign.</summary>
    public string Relation => IsApproximate ? QuantityFormatter.ApproximationSign : "=";
    /// <summary>The standalone number, marked when it is an approximation.</summary>
    public override string ToString() => IsApproximate ? QuantityFormatter.ApproximationSign + " " + Number : Number;
}

/// <summary>
/// Shared technical display boundary for UI and reports. Every displayed value is rounded by the same rule
/// (<see cref="NumericRounding"/>): n significant digits, never coarser than one whole display unit in decimal
/// notation, no trailing zeros. The two paths differ only in the marking:
/// <list type="bullet">
/// <item><b>Input</b> (<c>FormatInput…</c>): given or library values. A display that differs from the exact
/// value in the display unit is marked with "≈".</item>
/// <item><b>Result</b> (<c>FormatResult…</c>): calculated values. Never marked; safety factors round down.</item>
/// </list>
/// Editable text uses <see cref="InputQuantityFormatter.FormatExact"/>.
/// </summary>
public static class QuantityFormatter
{
    internal const string GroupSeparator = "\u202f";
    internal const string ApproximationSign = "≈";

    // ---- Input: given values -------------------------------------------------------------------------------

    /// <summary>A given value rounded like a result; <see cref="InputNumberText.IsApproximate"/> marks a change.</summary>
    public static InputNumberText Input(double siValue, UnitDefinition unit,
        int significantDigits = ResultPresentationOptions.DefaultSignificantDigits,
        EngineeringNotation notation = EngineeringNotation.Automatic, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(unit);
        if (!double.IsFinite(siValue)) throw new ArgumentOutOfRangeException(nameof(siValue));
        var exact = PresentationNumber.FromDouble(siValue) / unit.Scale;
        var rounded = NumericRounding.Round(exact, significantDigits, notation);
        return new(Format(rounded, culture), rounded.Value.Number.CompareTo(exact) != 0);
    }

    public static InputNumberText Input(double siValue, UnitDefinition unit, ResultPresentationOptions options,
        CultureInfo? culture = null) => Input(siValue, unit, options.SignificantDigits, options.Notation, culture);

    public static string FormatInputNumber(double siValue, UnitDefinition unit,
        int significantDigits = ResultPresentationOptions.DefaultSignificantDigits,
        EngineeringNotation notation = EngineeringNotation.Automatic, CultureInfo? culture = null) =>
        Input(siValue, unit, significantDigits, notation, culture).ToString();

    public static string FormatInputNumber(double siValue, UnitDefinition unit, ResultPresentationOptions options,
        CultureInfo? culture = null) => Input(siValue, unit, options, culture).ToString();

    public static string FormatInput(double siValue, UnitDefinition unit,
        int significantDigits = ResultPresentationOptions.DefaultSignificantDigits,
        EngineeringNotation notation = EngineeringNotation.Automatic, CultureInfo? culture = null) =>
        WithUnit(FormatInputNumber(siValue, unit, significantDigits, notation, culture), unit);

    public static string FormatInput(double siValue, UnitDefinition unit, ResultPresentationOptions options,
        CultureInfo? culture = null) => WithUnit(FormatInputNumber(siValue, unit, options, culture), unit);

    /// <summary>"F1 = 800 N", or "F1 ≈ 3.94 in" for a rounded given value.</summary>
    public static string FormatInputEquation(string label, double siValue, UnitDefinition unit,
        int significantDigits = ResultPresentationOptions.DefaultSignificantDigits,
        EngineeringNotation notation = EngineeringNotation.Automatic, CultureInfo? culture = null) =>
        Equation(label, Input(siValue, unit, significantDigits, notation, culture), unit);

    public static string FormatInputEquation(string label, double siValue, UnitDefinition unit,
        ResultPresentationOptions options, CultureInfo? culture = null) =>
        Equation(label, Input(siValue, unit, options, culture), unit);

    private static string Equation(string label, InputNumberText text, UnitDefinition unit) =>
        label + " " + text.Relation + " " + WithUnit(text.Number, unit);

    // ---- Result: calculated values -------------------------------------------------------------------------

    public static string FormatResult(double siValue, QuantityKind kind, ResultPresentationOptions options,
        CultureInfo? culture = null) => FormatResult(siValue, kind, options.Profile, options.SignificantDigits, options.Notation, culture);

    public static string FormatResultNumber(double siValue, QuantityKind kind, ResultPresentationOptions options,
        CultureInfo? culture = null) => FormatResultNumber(siValue, kind, options.Profile, options.SignificantDigits, options.Notation, culture);

    public static string FormatResult(double siValue, QuantityKind kind, UnitProfile? profile = null,
        int significantDigits = ResultPresentationOptions.DefaultSignificantDigits,
        EngineeringNotation notation = EngineeringNotation.Automatic, CultureInfo? culture = null) =>
        WithUnit(FormatResultNumber(siValue, kind, profile, significantDigits, notation, culture), (profile ?? UnitProfile.Default)[kind]);

    public static string FormatResultNumber(double siValue, QuantityKind kind, UnitProfile? profile = null,
        int significantDigits = ResultPresentationOptions.DefaultSignificantDigits,
        EngineeringNotation notation = EngineeringNotation.Automatic, CultureInfo? culture = null) =>
        Format(NumericRounding.Round(siValue, kind, (profile ?? UnitProfile.Default)[kind], significantDigits, notation), culture);

    /// <summary>Formats a rounded number without trailing zeros, in decimal or engineering notation.</summary>
    public static string Format(RoundedNumber rounded, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(rounded);
        if (rounded.IsPositiveInfinity) return "∞";
        culture ??= CultureInfo.CurrentCulture;
        var value = rounded.Value;
        if (value.IsZero) return "0";
        int shift = rounded.UsesEngineeringNotation ? (int)Math.Floor(value.MagnitudeExponent / 3d) * 3 : 0;
        string number = Decimal(value, shift, culture);
        return rounded.UsesEngineeringNotation ? number + " · 10" + Superscript(shift) : number;
    }

    // ---- Shared digits -------------------------------------------------------------------------------------

    /// <summary>Formats an exact decimal without adding digits.</summary>
    public static string Format(DecimalNumber value, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.IsZero ? "0" : Decimal(value, 0, culture ?? CultureInfo.CurrentCulture);
    }

    /// <summary>Decimal digits of an exact value without grouping or exponent, as typed into an input field.</summary>
    internal static string Plain(DecimalNumber value, CultureInfo culture) =>
        value.IsZero ? "0" : Decimal(value, 0, culture, grouped: false);

    private static string Decimal(DecimalNumber value, int shift, CultureInfo culture, bool grouped = true)
    {
        string digits = BigInteger.Abs(value.Mantissa).ToString(CultureInfo.InvariantCulture);
        int exponent = value.Exponent - shift;
        int point = digits.Length + exponent;
        string integer = point > 0 ? (exponent >= 0 ? digits + new string('0', exponent) : digits[..point]) : "0";
        if (grouped) for (int i = integer.Length - 3; i > 0; i -= 3) integer = integer.Insert(i, GroupSeparator);
        string fraction = exponent >= 0 ? "" : point > 0 ? digits[point..] : new string('0', -point) + digits;
        string result = integer + (fraction.Length == 0 ? "" : culture.NumberFormat.NumberDecimalSeparator + fraction);
        return value.Mantissa.Sign < 0 ? culture.NumberFormat.NegativeSign + result : result;
    }

    private static string Superscript(int exponent) => string.Concat(exponent.ToString(CultureInfo.InvariantCulture)
        .Select(c => c == '-' ? '⁻' : "⁰¹²³⁴⁵⁶⁷⁸⁹"[c - '0']));

    internal static string WithUnit(string number, UnitDefinition unit) => unit.Symbol.Length == 0 ? number : number + " " + unit.Symbol;
}
