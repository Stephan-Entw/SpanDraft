using System.Text;
using Avalonia.Data.Converters;

namespace SpanDraft.Desktop.Presentation;

/// <summary>
/// Line breaks for technical values in narrow cells: a number never separates from its unit or its power of ten, and a
/// unit never splits at "/" or "·". The only break opportunities left are after "=" ("kθ = …") and after the ellipsis
/// of a range ("q₁ … q₂").
/// </summary>
public static class TechnicalLineBreaks
{
    public const char NoBreakSpace = '\u00A0';
    public const char WordJoiner = '\u2060';

    public static FuncValueConverter<string?, string?> Converter { get; } = new(text => text is null ? null : Protect(text));

    public static string Protect(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var result = new StringBuilder(text.Length + 8);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            bool breakable = c == ' ' && i > 0 && text[i - 1] is '=' or '…';
            if (c == ' ' && !breakable) result.Append(NoBreakSpace);
            else if (c == '/') result.Append('/').Append(WordJoiner);
            else if (c == '·') result.Append(WordJoiner).Append('·').Append(WordJoiner);
            else result.Append(c);
        }
        return result.ToString();
    }
}
