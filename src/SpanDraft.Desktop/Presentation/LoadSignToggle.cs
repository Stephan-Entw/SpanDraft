using System.Globalization;

namespace SpanDraft.Desktop.Presentation;

internal static class LoadSignToggle
{
    public static string Apply(string text, bool valid, double value)
    {
        var number = CultureInfo.CurrentCulture.NumberFormat;
        if (!valid || value == 0 || text.TrimEnd(' ', '\t', '\r', '\n', '\v', '\f')
            .EndsWith(number.NumberDecimalSeparator, StringComparison.Ordinal)) return text;

        int start = 0;
        while (start < text.Length && text[start] is ' ' or '\t' or '\r' or '\n' or '\v' or '\f') start++;
        var remaining = text.AsSpan(start);
        if (remaining.StartsWith(number.NegativeSign, StringComparison.Ordinal))
            return text.Remove(start, number.NegativeSign.Length);
        if (remaining.Length > 0 && remaining[0] is '-' or QuantityFormatter.MinusSign)
            return text.Remove(start, 1);
        if (remaining.StartsWith(number.PositiveSign, StringComparison.Ordinal))
            text = text.Remove(start, number.PositiveSign.Length);
        return text.Length < 512 ? text.Insert(start, QuantityFormatter.MinusSign.ToString()) : text;
    }
}
