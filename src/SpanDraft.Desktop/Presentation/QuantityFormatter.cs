using System.Globalization;
using System.Numerics;

namespace SpanDraft.Desktop.Presentation;

/// <summary>Writes already-rounded values. Notation and culture never perform numerical rounding.</summary>
public static class NumberFormatter
{
    public static string Format(RoundedNumber rounded, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(rounded);
        culture ??= CultureInfo.CurrentCulture;
        if (rounded.IsPositiveInfinity) return "∞";
        if (rounded.IsApproximateZero) return "≈ 0";
        return Format(rounded.Value, culture);
    }

    public static string Format(DecimalNumber value, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(value);
        culture ??= CultureInfo.CurrentCulture;
        if (value.IsZero) return "0";
        bool ordinary = value.MagnitudeExponent < 6 && value.Exponent >= -4;
        int engineeringExponent = (int)Math.Floor(value.MagnitudeExponent / 3d) * 3;
        if (ordinary || engineeringExponent == 0) return Decimal(value, 0, culture);
        return Decimal(value, engineeringExponent, culture) + "·10" + Superscript(engineeringExponent);
    }

    private static string Decimal(DecimalNumber value, int shift, CultureInfo culture)
    {
        string digits = BigInteger.Abs(value.Mantissa).ToString(CultureInfo.InvariantCulture);
        int exponent = value.Exponent - shift;
        string result;
        if (exponent >= 0) result = digits + new string('0', exponent);
        else
        {
            int point = digits.Length + exponent;
            string separator = culture.NumberFormat.NumberDecimalSeparator;
            result = point > 0 ? digits.Insert(point, separator)
                : "0" + separator + new string('0', -point) + digits;
        }
        return value.Mantissa.Sign < 0 ? culture.NumberFormat.NegativeSign + result : result;
    }

    private static string Superscript(int exponent) => string.Concat(exponent.ToString(CultureInfo.InvariantCulture)
        .Select(c => c == '-' ? '⁻' : "⁰¹²³⁴⁵⁶⁷⁸⁹"[c - '0']));
}

/// <summary>The integration boundary for read-only results; inputs and axis ticks retain their own formatting.</summary>
public static class QuantityFormatter
{
    public static string Format(double siValue, QuantityKind kind, UnitProfile? profile = null,
        PresentationMode mode = PresentationMode.Standard, ModelReferenceValues? references = null,
        CultureInfo? culture = null)
    {
        profile ??= UnitProfile.Default;
        var unit = profile[kind];
        string number = FormatNumber(siValue, kind, profile, mode, references, culture);
        return unit.Symbol.Length == 0 ? number : number + " " + unit.Symbol;
    }

    /// <summary>Same result precision as Format, without a unit suffix for cells with unit headers.</summary>
    public static string FormatNumber(double siValue, QuantityKind kind, UnitProfile? profile = null,
        PresentationMode mode = PresentationMode.Standard, ModelReferenceValues? references = null,
        CultureInfo? culture = null)
    {
        profile ??= UnitProfile.Default;
        var rounded = NumericRounding.Round(siValue, kind, profile[kind], mode, references);
        return NumberFormatter.Format(rounded, culture);
    }
}
