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
/// Shared technical display boundary for UI and reports. Every displayed value belongs to exactly one class:
/// <list type="bullet">
/// <item><b>Input</b> (<c>FormatInput…</c>): given or library values. Exact shortest decimal digits in the display
/// unit, independent of the result precision and notation settings.</item>
/// <item><b>Result</b> (<c>FormatResult…</c>): calculated values. Rounded to the result precision, in decimal
/// notation never coarser than one whole display unit.</item>
/// </list>
/// Editable text uses <see cref="InputQuantityFormatter.FormatExact"/>.
/// </summary>
public static class QuantityFormatter
{
    internal const string GroupSeparator = " ";
    internal const string ApproximationSign = "≈";
    /// <summary>Given values with more significant digits are shown as marked approximations.</summary>
    public const int MaximumExactInputDigits = 12;
    /// <summary>Target digits of an approximation; it is never coarser than one whole display unit.</summary>
    public const int ApproximateInputDigits = 3;

    // ---- Input: given values -------------------------------------------------------------------------------

    /// <summary>The digits of <see cref="InputQuantityFormatter.FormatExact"/>, grouped and always decimal.</summary>
    public static InputNumberText Input(double siValue, UnitDefinition unit, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(unit);
        if (!double.IsFinite(siValue)) throw new ArgumentOutOfRangeException(nameof(siValue));
        culture ??= CultureInfo.CurrentCulture;
        var value = DecimalNumber.Parse(InputQuantityFormatter.FormatExact(siValue, unit, CultureInfo.InvariantCulture));
        bool approximate = value.SignificantDigits > MaximumExactInputDigits;
        if (approximate) value = NumericRounding.RoundWithinUnit(value.Number, ApproximateInputDigits, out _);
        return new(value.IsZero ? "0" : Decimal(value, 0, 0, culture), approximate);
    }

    public static string FormatInputNumber(double siValue, UnitDefinition unit, CultureInfo? culture = null) =>
        Input(siValue, unit, culture).ToString();

    public static string FormatInput(double siValue, UnitDefinition unit, CultureInfo? culture = null) =>
        WithUnit(FormatInputNumber(siValue, unit, culture), unit);

    /// <summary>"F1 = 800 N", or "F1 ≈ 3.94 in" for an approximation.</summary>
    public static string FormatInputEquation(string label, double siValue, UnitDefinition unit, CultureInfo? culture = null)
    {
        var text = Input(siValue, unit, culture);
        return label + " " + text.Relation + " " + WithUnit(text.Number, unit);
    }

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

    /// <summary>Formats a rounded result, keeping trailing zeros up to its rounding place.</summary>
    public static string Format(RoundedNumber rounded, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(rounded);
        if (rounded.IsPositiveInfinity) return "∞";
        culture ??= CultureInfo.CurrentCulture;
        var value = rounded.Value;
        if (value.IsZero) return "0";
        int shift = rounded.UsesEngineeringNotation ? (int)Math.Floor(value.MagnitudeExponent / 3d) * 3 : 0;
        string number = Decimal(value, shift, Math.Max(0, shift - rounded.Step.Exponent), culture);
        return rounded.UsesEngineeringNotation ? number + " · 10" + Superscript(shift) : number;
    }

    // ---- Shared digits -------------------------------------------------------------------------------------

    /// <summary>Formats an exact decimal without adding digits.</summary>
    public static string Format(DecimalNumber value, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.IsZero ? "0" : Decimal(value, 0, 0, culture ?? CultureInfo.CurrentCulture);
    }

    private static string Decimal(DecimalNumber value, int shift, int minimumFractionDigits, CultureInfo culture)
    {
        string digits = BigInteger.Abs(value.Mantissa).ToString(CultureInfo.InvariantCulture);
        int exponent = value.Exponent - shift;
        int point = digits.Length + exponent;
        string integer = point > 0 ? (exponent >= 0 ? digits + new string('0', exponent) : digits[..point]) : "0";
        for (int i = integer.Length - 3; i > 0; i -= 3) integer = integer.Insert(i, GroupSeparator);
        string fraction = exponent >= 0 ? "" : point > 0 ? digits[point..] : new string('0', -point) + digits;
        fraction = fraction.PadRight(minimumFractionDigits, '0');
        string result = integer + (fraction.Length == 0 ? "" : culture.NumberFormat.NumberDecimalSeparator + fraction);
        return value.Mantissa.Sign < 0 ? culture.NumberFormat.NegativeSign + result : result;
    }

    private static string Superscript(int exponent) => string.Concat(exponent.ToString(CultureInfo.InvariantCulture)
        .Select(c => c == '-' ? '⁻' : "⁰¹²³⁴⁵⁶⁷⁸⁹"[c - '0']));

    internal static string WithUnit(string number, UnitDefinition unit) => unit.Symbol.Length == 0 ? number : number + " " + unit.Symbol;
}
