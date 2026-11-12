using System.Globalization;
using SpanDraft.Desktop;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.ViewModels;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

[Collection("Process culture")]
public sealed class ProcessCultureTests
{
    [Theory]
    [InlineData(UiLanguagePreference.English, RegionalFormatPreference.GermanGermany, "File", "Settings…", "12,5", "12.5", "de-DE", "en-US")]
    [InlineData(UiLanguagePreference.German, RegionalFormatPreference.EnglishUnitedStates, "Datei", "Einstellungen…", "12.5", "12,5", "en-US", "de-DE")]
    public void MixedCulturesSeparateResourcesInputsResultsAndDates(UiLanguagePreference language, RegionalFormatPreference region,
        string file, string settingsLabel, string number, string wrongSeparator, string cultureName, string uiCultureName)
    {
        using var scope = new ProcessCultureScope("fr-CA", "ja-JP");
        var settings = UserSettings.Default with { UiLanguage = language, RegionalFormat = region };
        var resolved = CultureConfigurator.Resolve(settings, CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        CultureConfigurator.Apply(resolved);
        Assert.Equal(cultureName, CultureInfo.CurrentCulture.Name);
        Assert.Equal(uiCultureName, CultureInfo.CurrentUICulture.Name);
        Assert.Same(resolved.Culture, CultureInfo.DefaultThreadCurrentCulture);
        Assert.Same(resolved.UiCulture, CultureInfo.DefaultThreadCurrentUICulture);
        Assert.Equal(file, Strings.File); Assert.Equal(settingsLabel, Strings.Settings);
        Assert.True(InputQuantityFormatter.TryParse(number, UnitCatalog.Newton, out double parsed)); Assert.Equal(12.5, parsed);
        Assert.False(InputQuantityFormatter.TryParse(wrongSeparator, UnitCatalog.Newton, out _));
        Assert.True(InputQuantityFormatter.TryParseLength(number, UnitCatalog.Millimeter, out var length)); Assert.Equal(.0125, length.Meters);
        Assert.True(InputQuantityFormatter.TryParsePosition(number, UnitCatalog.Millimeter, M(1), out var position)); Assert.Equal(length, position);
        Assert.Equal(number, InputQuantityFormatter.FormatExact(12.5, UnitCatalog.Newton)); Assert.Equal(number, QuantityFormatter.FormatResultNumber(12.5, QuantityKind.TransverseForce, ResultPresentationOptions.Default));
        Assert.Equal(number, AxisTickFormatter.Format(5, 2.5, 0));
        Assert.True(InputQuantityFormatter.TryParse(number, UnitCatalog.Millimeter, out double si)); Assert.Equal(.0125, si);
        Assert.False(InputQuantityFormatter.TryParse(wrongSeparator, UnitCatalog.Millimeter, out _));
        Assert.Equal(number, InputQuantityFormatter.FormatExact(si, UnitCatalog.Millimeter));
        Assert.Equal(number, QuantityFormatter.Format(new DecimalNumber(125, -1)));
        Assert.Equal(number + " MPa", QuantityFormatter.FormatResult(12.5e6, QuantityKind.Stress, settings.Profile,
            5));
        Assert.Equal("mm", settings.Profile[QuantityKind.BeamLength].Symbol);
        Assert.Equal("N", settings.Profile[QuantityKind.TransverseForce].Symbol);
        Assert.Equal("MPa", settings.Profile[QuantityKind.Stress].Symbol);
        var expected = CultureInfo.GetCultureInfo(cultureName);
        var local = new DateTimeOffset(new DateTime(2026, 10, 16, 15, 42, 0, DateTimeKind.Local));
        Assert.Equal(Strings.Today + ", " + local.ToString("t", expected),
            RecentProjectItemViewModel.FormatLastUsed(local, local));
        Assert.Equal(local.AddDays(-2).ToString("d", expected),
            RecentProjectItemViewModel.FormatLastUsed(local.AddDays(-2), local));
    }

    [Fact]
    public void SystemUsesTheOriginalCulturesAfterAnExplicitConfigurationAndResourcesFallBackToEnglish()
    {
        using var scope = new ProcessCultureScope("ja-JP", "fr-CA");
        var systemCulture = CultureInfo.CurrentCulture; var systemUiCulture = CultureInfo.CurrentUICulture;
        CultureConfigurator.Apply(CultureConfigurator.Resolve(UserSettings.Default with
        { UiLanguage = UiLanguagePreference.German, RegionalFormat = RegionalFormatPreference.GermanGermany }, systemCulture, systemUiCulture));
        CultureConfigurator.Apply(CultureConfigurator.Resolve(UserSettings.Default, systemCulture, systemUiCulture));
        Assert.Same(systemCulture, CultureInfo.CurrentCulture); Assert.Same(systemUiCulture, CultureInfo.CurrentUICulture);
        Assert.Equal("File", Strings.File); Assert.Equal("Settings…", Strings.Settings);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SavingChangesLivePreferencesButKeepsAllActiveCulturesUntilTheNextStartup(bool failSave)
    {
        using var scope = new ProcessCultureScope("de-DE", "de-DE");
        CultureConfigurator.Apply(CultureConfigurator.Resolve(UserSettings.Default, CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture));
        var culture = CultureInfo.CurrentCulture; var uiCulture = CultureInfo.CurrentUICulture;
        var files = new Files(); var store = new LocalSettingsStore(files, "settings");
        await store.SaveAsync(UserSettings.Default);
        var main = new MainWindowViewModel(files: files, settingsStore: store);
        var dialog = new SettingsViewModel(main.Settings, main.ApplySettingsAsync);
        dialog.SelectedUiLanguage = UiLanguagePreference.English;
        dialog.SelectedRegionalFormat = RegionalFormatPreference.EnglishUnitedStates;
        dialog.Select(UnitProfileKind.UnitedStates); dialog.SignificantDigits = 6;
        var pending = dialog.Pending;
        if (failSave) files.FailWritePath = "settings";
        Assert.Equal(!failSave, await dialog.ApplyAsync());
        Assert.Same(culture, CultureInfo.CurrentCulture); Assert.Same(uiCulture, CultureInfo.CurrentUICulture);
        Assert.Same(culture, CultureInfo.DefaultThreadCurrentCulture); Assert.Same(uiCulture, CultureInfo.DefaultThreadCurrentUICulture);
        Assert.Equal("Datei", Strings.File);
        if (failSave)
        {
            Assert.Same(UserSettings.Default, main.Settings); Assert.Same(pending, dialog.Pending);
            Assert.True(dialog.HasChanges); Assert.True(dialog.HasError);
            Assert.True(UserSettings.Default.ContentEquals(await store.LoadAsync()));
            return;
        }
        Assert.Same(pending, main.Settings); Assert.False(dialog.HasChanges);
        Assert.Same(UnitProfile.UnitedStates, main.ResultPresentation.Profile);
        Assert.Equal(6, main.ResultPresentation.SignificantDigits);
        var reopened = new SettingsViewModel(main.Settings, main.ApplySettingsAsync);
        Assert.Equal(UiLanguagePreference.English, reopened.SelectedUiLanguage);
        Assert.Equal(RegionalFormatPreference.EnglishUnitedStates, reopened.SelectedRegionalFormat);
        Assert.Equal("Systemsprache", reopened.UiLanguages[0].Label);
        reopened.SelectedUiLanguage = UiLanguagePreference.System;
        reopened.SelectedRegionalFormat = RegionalFormatPreference.System;
        reopened.DiscardPending(); Assert.True(pending.ContentEquals(reopened.Pending));
        var restarted = await DesktopStartupContext.LoadAsync(files, store);
        CultureConfigurator.Apply(CultureConfigurator.Resolve(restarted.Settings, culture, uiCulture));
        Assert.Equal("en-US", CultureInfo.CurrentCulture.Name); Assert.Equal("en-US", CultureInfo.CurrentUICulture.Name);
        Assert.Equal("File", Strings.File);
    }
}
