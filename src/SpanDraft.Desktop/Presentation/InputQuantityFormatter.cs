using System.Globalization;
using SpanDraft.Core.Units;

namespace SpanDraft.Desktop.Presentation;

/// <summary>Editable model values, independent of result rounding.</summary>
public static class InputQuantityFormatter
{
    /// <summary>
    /// The full value in the display unit as editable text: always decimal, never with an exponent or grouping.
    /// <see cref="TryParse"/> returns exactly <paramref name="siValue"/> for it.
    /// </summary>
    public static string FormatExact(double siValue, UnitDefinition unit, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(unit);
        culture ??= CultureInfo.CurrentCulture;
        try
        {
            // The shortest round-trip digits of the display value, when they also lead back to the SI value.
            string text = QuantityFormatter.Plain(DecimalNumber.Parse(
                unit.FromSi(siValue).ToString("R", CultureInfo.InvariantCulture)), culture);
            if (RoundTrips(text)) return text;
        }
        catch (OverflowException) { /* Format a finite SI value beyond double's display-unit range. */ }
        // Otherwise the fewest exact significant digits that lead back to the SI value.
        var value = PresentationNumber.FromDouble(siValue) / unit.Scale;
        if (value.IsZero) return "0";
        string candidate = "";
        for (int digits = 1; digits <= MaximumExactDigits; digits++)
        {
            int exponent = value.DecimalExponent - digits + 1;
            candidate = QuantityFormatter.Plain(new((value / PresentationNumber.PowerOfTen(exponent)).RoundToInteger(), exponent), culture);
            if (RoundTrips(candidate)) break;
        }
        return candidate;

        bool RoundTrips(string text) => TryParse(text, unit, out double parsed, culture) && parsed == siValue;
    }

    // Enough digits to identify any double after an exact unit conversion; text stays below the parse limit.
    private const int MaximumExactDigits = 40;

    public static bool TryParse(string? text, UnitDefinition unit, out double siValue, CultureInfo? culture = null)
    {
        siValue = default;
        culture ??= CultureInfo.CurrentCulture;
        if (text is null || text.Length > 512) return false;
        string normalized = text.Trim(' ', '\t', '\r', '\n', '\v', '\f').Replace(culture.NumberFormat.NumberDecimalSeparator, ".")
            .Replace(culture.NumberFormat.NegativeSign, "-").Replace(culture.NumberFormat.PositiveSign, "+");
        if (normalized.Contains('\u202f'))
        {
            // Accept only SpanDraft's integer triplets, never separators in fractions or exponents.
            int integerEnd = normalized.IndexOfAny(['.', 'E', 'e']);
            string integer = integerEnd < 0 ? normalized : normalized[..integerEnd];
            if (integer.StartsWith('-') || integer.StartsWith('+')) integer = integer[1..];
            string[] groups = integer.Split('\u202f');
            if (groups.Length < 2 || groups[0].Length is < 1 or > 3
                || groups.Skip(1).Any(group => group.Length != 3)
                || groups.Any(group => group.Any(c => !char.IsAsciiDigit(c)))
                || integerEnd >= 0 && normalized.AsSpan(integerEnd).Contains('\u202f')) return false;
            text = text.Replace(QuantityFormatter.GroupSeparator, "");
            normalized = normalized.Replace(QuantityFormatter.GroupSeparator, "");
        }
        if (!double.TryParse(text, NumberStyles.Float, culture, out _)) return false;
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
        if (text == FormatExact(beamLength.Meters, unit)) { position = beamLength; return true; }
        if (!TryParse(text, unit, out double meters) || meters < 0 || meters > beamLength.Meters) return false;
        position = Length.FromMeters(meters);
        return true;
    }
}
