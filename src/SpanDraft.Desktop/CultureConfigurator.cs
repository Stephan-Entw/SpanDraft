using System.Globalization;
using SpanDraft.Desktop.Presentation;

namespace SpanDraft.Desktop;

public static class CultureConfigurator
{
    public static (CultureInfo Culture, CultureInfo UiCulture) Resolve(UserSettings settings,
        CultureInfo systemCulture, CultureInfo systemUiCulture) =>
        (settings.RegionalFormat switch
        {
            RegionalFormatPreference.System => systemCulture,
            RegionalFormatPreference.GermanGermany => CultureInfo.GetCultureInfo("de-DE"),
            RegionalFormatPreference.EnglishUnitedStates => CultureInfo.GetCultureInfo("en-US"),
            _ => throw new ArgumentOutOfRangeException(nameof(settings))
        }, settings.UiLanguage switch
        {
            UiLanguagePreference.System => systemUiCulture,
            UiLanguagePreference.German => CultureInfo.GetCultureInfo("de-DE"),
            UiLanguagePreference.English => CultureInfo.GetCultureInfo("en-US"),
            _ => throw new ArgumentOutOfRangeException(nameof(settings))
        });

    // Startup only: changing cultures after XAML creation would leave a partially localized UI.
    public static void Apply((CultureInfo Culture, CultureInfo UiCulture) cultures)
    {
        CultureInfo.DefaultThreadCurrentCulture = cultures.Culture;
        CultureInfo.DefaultThreadCurrentUICulture = cultures.UiCulture;
        CultureInfo.CurrentCulture = cultures.Culture;
        CultureInfo.CurrentUICulture = cultures.UiCulture;
    }
}
