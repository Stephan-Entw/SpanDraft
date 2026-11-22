using SpanDraft.Desktop.Presentation;

namespace SpanDraft.Desktop.Services;

/// <summary>
/// The preferences a user edits in the settings dialog. It never carries <c>LastAuthor</c>: a dialog that stayed open
/// while a project was created must not reset the newer author.
/// </summary>
public sealed record SettingsPatch(UnitProfile Profile, UnitProfile? CustomProfile, UnitProfileKind LastStandardProfile,
    int SignificantDigits, EngineeringNotation Notation, UiLanguagePreference UiLanguage, RegionalFormatPreference RegionalFormat)
{
    public static SettingsPatch From(UserSettings settings) => new(settings.Profile, settings.CustomProfile,
        settings.LastStandardProfile, settings.SignificantDigits, settings.Notation, settings.UiLanguage, settings.RegionalFormat);

    /// <summary>Merges the patch into the given current settings; every field outside the patch is taken from them.</summary>
    public UserSettings ApplyTo(UserSettings current) => current with
    {
        Profile = Profile, CustomProfile = CustomProfile, LastStandardProfile = LastStandardProfile,
        SignificantDigits = SignificantDigits, Notation = Notation, UiLanguage = UiLanguage, RegionalFormat = RegionalFormat
    };
}
