using System.Globalization;
using SpanDraft.Core.Units;

namespace SpanDraft.Desktop.State;

public static class UiNumbers
{
    public static bool TryParseSignedValue(string? text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
        && double.IsFinite(value);

    public static string Format(double value) => double.IsPositiveInfinity(value)
        ? "∞" : value.ToString("G", CultureInfo.CurrentCulture);

    public static string Indicator(double value) => Compact(value);

    /// <summary>Exact nice-tick labels, including decimal steps outside double's range.</summary>
    public static string AxisTick(int index, double stepMantissa, int stepExponent, CultureInfo? culture = null)
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

    /// <summary>Technical display; precision and notation live in the central formatter.</summary>
    public static string Compact(double value, CultureInfo? culture = null) => double.IsPositiveInfinity(value) ? "∞" :
        SpanDraft.Desktop.Presentation.QuantityFormatter.FormatNumber(value,
            SpanDraft.Desktop.Presentation.QuantityKind.PoissonRatio, culture: culture);

    private static string Superscript(int exponent) => string.Concat(exponent.ToString(CultureInfo.InvariantCulture)
        .Select(c => c == '-' ? '⁻' : "⁰¹²³⁴⁵⁶⁷⁸⁹"[c - '0']));

    public static bool TryParseLength(string? text, out Length length)
    {
        length = default;
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out double mm)
            || !double.IsFinite(mm) || mm <= 0 || mm / 1000 <= 0) return false;
        length = Length.FromMillimeters(mm);
        return true;
    }

    public static bool TryParsePosition(string? text, Length beamLength, out Length position)
    {
        position = default;
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out double mm)
            || !double.IsFinite(mm) || mm < 0 || mm > beamLength.Millimeters) return false;
        position = Length.FromMillimeters(mm);
        // Inclusive endpoints retain the document's exact SI representation.
        if (mm == beamLength.Millimeters) position = beamLength;
        return position.Meters <= beamLength.Meters;
    }
}
