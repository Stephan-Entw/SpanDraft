using System.Resources;

namespace SpanDraft.Desktop.Resources;

/// <summary>Reads the repository LICENSE embedded directly in the desktop assembly.</summary>
public static class ApplicationLicense
{
    public static string ReadText()
    {
        using var stream = typeof(ApplicationLicense).Assembly.GetManifestResourceStream("SpanDraft.Desktop.LICENSE")
            ?? throw new MissingManifestResourceException("SpanDraft.Desktop.LICENSE");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
