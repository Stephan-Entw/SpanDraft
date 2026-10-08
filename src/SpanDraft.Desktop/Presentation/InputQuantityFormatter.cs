using System.Globalization;
using SpanDraft.Core.Units;

namespace SpanDraft.Desktop.Presentation;

/// <summary>Editable model values, independent of result rounding.</summary>
public static class InputQuantityFormatter
{
    public static string Format(double siValue, UnitDefinition unit, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        try
        {
            string text = unit.FromSi(siValue).ToString("R", culture);
            if (TryParse(text, unit, out _, culture)) return text;
        }
        catch (OverflowException) { /* Format a finite SI value beyond double's display-unit range. */ }
        var value = PresentationNumber.FromDouble(siValue) / unit.Scale;
        int exponent = value.DecimalExponent - 19;
        return (value / PresentationNumber.PowerOfTen(exponent)).RoundToInteger().ToString(culture)
            + "E" + exponent.ToString(CultureInfo.InvariantCulture);
    }

    public static bool TryParse(string? text, UnitDefinition unit, out double siValue, CultureInfo? culture = null)
    {
        siValue = default;
        culture ??= CultureInfo.CurrentCulture;
        if (text is null || text.Length > 512 || !double.TryParse(text, NumberStyles.Float, culture, out _)) return false;
        string normalized = text.Trim().Replace(culture.NumberFormat.NumberDecimalSeparator, ".")
            .Replace(culture.NumberFormat.NegativeSign, "-").Replace(culture.NumberFormat.PositiveSign, "+");
        int separator = normalized.IndexOfAny(['E', 'e']);
        if (separator >= 0 && (!int.TryParse(normalized.AsSpan(separator + 1), CultureInfo.InvariantCulture, out int exponent)
            || exponent is < -400 or > 400)) return false;
        try
        {
            var value = PresentationNumber.Parse(normalized) * unit.Scale;
            siValue = value.ToDouble();
            return double.IsFinite(siValue) && (siValue != 0 || value.IsZero);
        }
        catch (Exception e) when (e is OverflowException or FormatException or ArgumentException) { return false; }
    }

    public static bool TryParseLength(string? text, UnitDefinition unit, out Length length)
    {
        length = default;
        if (!TryParse(text, unit, out double meters) || meters <= 0) return false;
        length = Length.FromMeters(meters);
        return true;
    }

    public static bool TryParsePosition(string? text, UnitDefinition unit, Length beamLength, out Length position)
    {
        position = default;
        // Inclusive endpoints retain the document's exact SI representation.
        if (text == Format(beamLength.Meters, unit)) { position = beamLength; return true; }
        if (!TryParse(text, unit, out double meters) || meters < 0 || meters > beamLength.Meters) return false;
        position = Length.FromMeters(meters);
        return true;
    }

    public static string Display(double siValue, UnitDefinition unit)
    {
        try { return SpanDraft.Desktop.State.UiNumbers.Compact(unit.FromSi(siValue), CultureInfo.CurrentCulture); }
        catch (OverflowException) { /* The read-only label can represent a wider exponent range. */ }
        var value = PresentationNumber.FromDouble(siValue) / unit.Scale;
        if (value.IsZero) return "0";
        int exponent = value.DecimalExponent - 2;
        var digits = (value / PresentationNumber.PowerOfTen(exponent)).RoundToInteger();
        return NumberFormatter.Format(new DecimalNumber(digits, exponent));
    }

    public static string WithUnit(double siValue, UnitDefinition unit) => Display(siValue, unit) + " " + unit.Symbol;
}
