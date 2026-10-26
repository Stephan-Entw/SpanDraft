using System.Globalization;
using System.Numerics;

namespace SpanDraft.Desktop.Presentation;

/// <summary>Shared technical display boundary for UI and reports; editable text uses InputQuantityFormatter.FormatExact.</summary>
public static class QuantityFormatter
{
    internal const string GroupSeparator = "\u202f";

    public static string Format(RoundedNumber rounded, CultureInfo? culture = null,
        EngineeringNotation notation = EngineeringNotation.Automatic)
    {
        ArgumentNullException.ThrowIfNull(rounded);
        if (!Enum.IsDefined(notation)) throw new ArgumentOutOfRangeException(nameof(notation));
        return rounded.IsPositiveInfinity ? "∞" : Format(rounded.Value, culture, notation);
    }

    public static string Format(DecimalNumber value, CultureInfo? culture = null,
        EngineeringNotation notation = EngineeringNotation.Automatic)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!Enum.IsDefined(notation)) throw new ArgumentOutOfRangeException(nameof(notation));
        culture ??= CultureInfo.CurrentCulture;
        if (value.IsZero) return "0";
        bool engineering = notation == EngineeringNotation.Automatic && value.MagnitudeExponent is < -3 or >= 6;
        int shift = engineering ? (int)Math.Floor(value.MagnitudeExponent / 3d) * 3 : 0;
        string number = Decimal(value, shift, culture);
        return engineering ? number + "\u00a0·\u00a010" + Superscript(shift) : number;
    }

    private static string Decimal(DecimalNumber value, int shift, CultureInfo culture)
    {
        string digits = BigInteger.Abs(value.Mantissa).ToString(CultureInfo.InvariantCulture);
        int exponent = value.Exponent - shift;
        int point = digits.Length + exponent;
        string integer = point > 0 ? (exponent >= 0 ? digits + new string('0', exponent) : digits[..point]) : "0";
        for (int i = integer.Length - 3; i > 0; i -= 3) integer = integer.Insert(i, GroupSeparator);
        string fraction = exponent >= 0 ? "" : point > 0 ? digits[point..] : new string('0', -point) + digits;
        string result = integer + (fraction.Length == 0 ? "" : culture.NumberFormat.NumberDecimalSeparator + fraction);
        return value.Mantissa.Sign < 0 ? culture.NumberFormat.NegativeSign + result : result;
    }

    private static string Superscript(int exponent) => string.Concat(exponent.ToString(CultureInfo.InvariantCulture)
        .Select(c => c == '-' ? '⁻' : "⁰¹²³⁴⁵⁶⁷⁸⁹"[c - '0']));

    public static string Format(double siValue, QuantityKind kind, ResultPresentationOptions options,
        CultureInfo? culture = null) => Format(siValue, kind, options.Profile, options.SignificantDigits, options.Notation, culture);

    public static string FormatNumber(double siValue, QuantityKind kind, ResultPresentationOptions options,
        CultureInfo? culture = null) => FormatNumber(siValue, kind, options.Profile, options.SignificantDigits, options.Notation, culture);

    public static string Format(double siValue, QuantityKind kind, UnitProfile? profile = null,
        int significantDigits = ResultPresentationOptions.DefaultSignificantDigits,
        EngineeringNotation notation = EngineeringNotation.Automatic, CultureInfo? culture = null)
    {
        var unit = (profile ?? UnitProfile.Default)[kind];
        string number = FormatNumber(siValue, kind, profile, significantDigits, notation, culture);
        return WithUnit(number, unit);
    }

    public static string FormatNumber(double siValue, QuantityKind kind, UnitProfile? profile = null,
        int significantDigits = ResultPresentationOptions.DefaultSignificantDigits,
        EngineeringNotation notation = EngineeringNotation.Automatic, CultureInfo? culture = null) =>
        Format(NumericRounding.Round(siValue, kind, (profile ?? UnitProfile.Default)[kind], significantDigits), culture, notation);

    internal static string Display(double siValue, UnitDefinition unit, ResultPresentationOptions? options = null,
        CultureInfo? culture = null)
    {
        options ??= ResultPresentationOptions.Default;
        return Format(NumericRounding.Round(siValue, unit, options.SignificantDigits), culture, options.Notation);
    }

    internal static string WithUnit(double siValue, UnitDefinition unit, ResultPresentationOptions? options = null) =>
        WithUnit(Display(siValue, unit, options), unit);

    internal static string WithUnit(string number, UnitDefinition unit) => unit.Symbol.Length == 0 ? number : number + " " + unit.Symbol;
}
