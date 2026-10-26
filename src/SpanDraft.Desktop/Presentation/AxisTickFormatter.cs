using System.Globalization;

namespace SpanDraft.Desktop.Presentation;

public static class AxisTickFormatter
{
    /// <summary>Exact nice-tick labels, including decimal steps outside double's range.</summary>
    public static string Format(int index, double stepMantissa, int stepExponent, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        if (index == 0) return "0";
        if (stepMantissa is not (1 or 2 or 2.5 or 5 or 10))
            throw new ArgumentOutOfRangeException(nameof(stepMantissa));
        if (stepExponent is < -324 or > 309) throw new ArgumentOutOfRangeException(nameof(stepExponent));
        double coefficient = index * stepMantissa;
        int exponent = stepExponent + (int)Math.Floor(Math.Log10(Math.Abs(coefficient)));
        int decimals = Math.Max(0, -stepExponent + (stepMantissa == 2.5 ? 1 : 0));
        if (exponent is >= -2 and < 4 && decimals <= 4)
        {
            string pattern = decimals == 0 ? "0" : "0." + new string('#', decimals);
            return (coefficient * Math.Pow(10, stepExponent)).ToString(pattern, culture);
        }
        int engineeringExponent = (int)Math.Floor(exponent / 3d) * 3;
        double mantissa = coefficient * Math.Pow(10, stepExponent - engineeringExponent);
        return mantissa.ToString("0.##", culture) + "·10" + Superscript(engineeringExponent);
    }

    private static string Superscript(int exponent) => string.Concat(exponent.ToString(CultureInfo.InvariantCulture)
        .Select(c => c == '-' ? '⁻' : "⁰¹²³⁴⁵⁶⁷⁸⁹"[c - '0']));
}
