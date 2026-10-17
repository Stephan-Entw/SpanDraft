using System.Globalization;
using SpanDraft.Desktop;
using SpanDraft.Desktop.Presentation;
using Xunit;

namespace SpanDraft.Tests;

public sealed class DesktopCultureResolutionTests
{
    [Theory]
    [InlineData(UiLanguagePreference.System, "fr-CA")]
    [InlineData(UiLanguagePreference.German, "de-DE")]
    [InlineData(UiLanguagePreference.English, "en-US")]
    public void LanguageResolutionUsesOnlyItsPreferenceAndOriginalSystemUiCulture(UiLanguagePreference language, string expected)
    {
        var original = CultureInfo.GetCultureInfo("fr-CA");
        foreach (var region in Enum.GetValues<RegionalFormatPreference>())
        {
            var settings = UserSettings.Default with { UiLanguage = language, RegionalFormat = region };
            var resolved = CultureConfigurator.Resolve(settings, CultureInfo.GetCultureInfo("ja-JP"), original);
            Assert.Equal(expected, resolved.UiCulture.Name);
            if (language == UiLanguagePreference.System) Assert.Same(original, resolved.UiCulture);
            Assert.Same(UnitProfile.MechanicalEngineering, settings.Profile);
        }
    }

    [Theory]
    [InlineData(RegionalFormatPreference.System, "ja-JP")]
    [InlineData(RegionalFormatPreference.GermanGermany, "de-DE")]
    [InlineData(RegionalFormatPreference.EnglishUnitedStates, "en-US")]
    public void RegionalResolutionUsesOnlyItsPreferenceAndOriginalSystemCulture(RegionalFormatPreference region, string expected)
    {
        var original = CultureInfo.GetCultureInfo("ja-JP");
        foreach (var language in Enum.GetValues<UiLanguagePreference>())
        {
            var settings = UserSettings.Default.Select(UnitProfileKind.UnitedStates) with { UiLanguage = language, RegionalFormat = region };
            var resolved = CultureConfigurator.Resolve(settings, original, CultureInfo.GetCultureInfo("fr-CA"));
            Assert.Equal(expected, resolved.Culture.Name);
            if (region == RegionalFormatPreference.System) Assert.Same(original, resolved.Culture);
            Assert.Same(UnitProfile.UnitedStates, settings.Profile);
        }
    }
}
