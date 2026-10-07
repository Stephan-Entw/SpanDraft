using SpanDraft.Desktop.Resources;
using Xunit;

namespace SpanDraft.Tests;

public sealed class DesktopUnicodeTests
{
    [Theory]
    [InlineData("de-DE")]
    [InlineData("en-US")]
    [InlineData("fr-FR")]
    public void CompiledResourcesRetainGermanUnicodeAndEnglishFallback(string culture)
    {
        using var scope = new UiCultureScope(culture);
        string[] expected = culture == "de-DE"
            ? ["Ändern", "Übernehmen", "Öffnen…", "Rückgängig", "Löschen", "Balkenlänge", "Maßgebendes Moment", "Balkenmodell", "Länge {0} mm"]
            : ["Change", "Apply", "Open…", "Undo", "Delete", "Beam Length", "Governing moment", "Beam model", "Length {0} mm"];
        Assert.Equal(expected, new[] { Strings.Change, Strings.Apply, Strings.Open, Strings.Undo, Strings.Delete,
            Strings.BeamLength, Strings.GoverningMoment, Strings.BeamModel, Strings.OverviewLength });
    }
}
