using System.Globalization;
using SpanDraft.Core.Units;

namespace SpanDraft.Desktop.State;

public static class UiNumbers
{
    public static string Format(double value) => double.IsPositiveInfinity(value)
        ? "∞" : value.ToString("G", CultureInfo.CurrentUICulture);

    public static string Indicator(double value) => double.IsPositiveInfinity(value)
        ? "∞" : value.ToString("0.##", CultureInfo.CurrentUICulture);

    public static bool TryParseLength(string? text, out Length length)
    {
        length = default;
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentUICulture, out double mm)
            || !double.IsFinite(mm) || mm <= 0 || mm / 1000 <= 0) return false;
        length = Length.FromMillimeters(mm);
        return true;
    }
}
