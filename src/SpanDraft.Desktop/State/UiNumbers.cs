using System.Globalization;
using SpanDraft.Core.Units;

namespace SpanDraft.Desktop.State;

public static class UiNumbers
{
    public static bool TryParseSignedValue(string? text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentUICulture, out value)
        && double.IsFinite(value);

    public static string Format(double value) => double.IsPositiveInfinity(value)
        ? "∞" : value.ToString("G", CultureInfo.CurrentUICulture);

    public static string Indicator(double value) => Compact(value);

    /// <summary>Compact read-only values; retain small nonzero values and bound very large numbers.</summary>
    public static string Compact(double value)
    {
        if (!double.IsFinite(value)) return Format(value);
        if (value == 0) return "0";
        double magnitude = Math.Abs(value);
        if (magnitude is >= 0.01 and < 10000)
            return value.ToString("0.##", CultureInfo.CurrentUICulture);

        // Round before regrouping so a carry cannot be lost to binary scaling.
        // The normalized representation also avoids underflow for subnormal
        // doubles and overflow for very large values.
        string scientific = magnitude.ToString("E2", CultureInfo.InvariantCulture);
        int separator = scientific.IndexOf('E');
        double mantissa = double.Parse(scientific.AsSpan(0, separator), CultureInfo.InvariantCulture);
        int exponent = int.Parse(scientific.AsSpan(separator + 1), CultureInfo.InvariantCulture);
        int engineeringExponent = (int)Math.Floor(exponent / 3d) * 3;
        mantissa *= Math.Pow(10, exponent - engineeringExponent);
        if (value < 0) mantissa = -mantissa;
        return mantissa.ToString("0.##", CultureInfo.CurrentUICulture) + "·10" + Superscript(engineeringExponent);
    }

    private static string Superscript(int exponent) => string.Concat(exponent.ToString(CultureInfo.InvariantCulture)
        .Select(c => c == '-' ? '⁻' : "⁰¹²³⁴⁵⁶⁷⁸⁹"[c - '0']));

    public static bool TryParseLength(string? text, out Length length)
    {
        length = default;
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentUICulture, out double mm)
            || !double.IsFinite(mm) || mm <= 0 || mm / 1000 <= 0) return false;
        length = Length.FromMillimeters(mm);
        return true;
    }

    public static bool TryParsePosition(string? text, Length beamLength, out Length position)
    {
        position = default;
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentUICulture, out double mm)
            || !double.IsFinite(mm) || mm < 0 || mm > beamLength.Millimeters) return false;
        position = Length.FromMillimeters(mm);
        // Inclusive endpoints retain the document's exact SI representation.
        if (mm == beamLength.Millimeters) position = beamLength;
        return position.Meters <= beamLength.Meters;
    }
}
