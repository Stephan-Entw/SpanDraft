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
