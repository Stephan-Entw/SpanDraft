using System.Globalization;
using System.Resources;

namespace SpanDraft.Desktop.Resources;

public static class Strings
{
    private static readonly ResourceManager ResourceManager =
        new("SpanDraft.Desktop.Resources.Strings", typeof(Strings).Assembly);

    public static string ApplicationTitle =>
        ResourceManager.GetString(nameof(ApplicationTitle), CultureInfo.CurrentUICulture)
        ?? throw new MissingManifestResourceException(nameof(ApplicationTitle));
}
