using System.Globalization;
using System.Resources;

namespace SpanDraft.Desktop.Resources;

public static class ApplicationPrivacy
{
    public static string ReadText()
    {
        var language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de" ? "de" : "en";
        var name = $"SpanDraft.Desktop.privacy.{language}.md";
        using var stream = typeof(ApplicationPrivacy).Assembly.GetManifestResourceStream(name)
            ?? throw new MissingManifestResourceException(name);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
