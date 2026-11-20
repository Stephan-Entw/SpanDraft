using System.Globalization;
using System.Resources;

namespace SpanDraft.Desktop.Resources;

internal static class ApplicationDocuments
{
    public static string ReadPrivacy() => ReadMarkdown($"privacy.{Language}.md");
    public static string ReadLicense() => ReadMarkdown("license.gpl-3.0.md");
    public static string ReadReleaseNotes() => ReadMarkdown($"release-notes.{Language}.md");

    private static string Language => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de" ? "de" : "en";

    private static string ReadMarkdown(string resource)
    {
        var name = $"SpanDraft.Desktop.{resource}";
        using var stream = typeof(ApplicationDocuments).Assembly.GetManifestResourceStream(name)
            ?? throw new MissingManifestResourceException(name);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
