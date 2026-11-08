using System.Globalization;
using SpanDraft.Core.Materials;

namespace SpanDraft.Desktop.Presentation;

public static class MaterialNoteDisplay
{
    public static string? Resolve(MaterialNote? note, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        if (note is null) return null;
        for (var current = culture; current.Name.Length > 0; current = current.Parent)
            if (note.Translations.TryGetValue(current.Name.ToLowerInvariant(), out var text)) return text;
        return note.DefaultText;
    }
}
