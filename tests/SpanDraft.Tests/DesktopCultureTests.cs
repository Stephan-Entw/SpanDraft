using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SpanDraft.Desktop;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

[Collection("Schematic text")]
public sealed class DesktopCultureTests
{
    [Theory]
    [InlineData(UiLanguagePreference.English, RegionalFormatPreference.GermanGermany, "File", "Settings…", "12,5", "12.5", "de-DE", "en-US")]
    [InlineData(UiLanguagePreference.German, RegionalFormatPreference.EnglishUnitedStates, "Datei", "Einstellungen…", "12.5", "12,5", "en-US", "de-DE")]
    public void MixedCulturesSeparateResourcesInputsResultsAndDates(UiLanguagePreference language, RegionalFormatPreference region,
        string file, string settingsLabel, string number, string wrongSeparator, string cultureName, string uiCultureName)
    {
        using var scope = new ResultCultureScope("fr-CA", "ja-JP");
        var settings = UserSettings.Default with { UiLanguage = language, RegionalFormat = region };
        var resolved = CultureConfigurator.Resolve(settings, CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        CultureConfigurator.Apply(resolved);
        Assert.Equal(cultureName, CultureInfo.CurrentCulture.Name);
        Assert.Equal(uiCultureName, CultureInfo.CurrentUICulture.Name);
        Assert.Same(resolved.Culture, CultureInfo.DefaultThreadCurrentCulture);
        Assert.Same(resolved.UiCulture, CultureInfo.DefaultThreadCurrentUICulture);
        Assert.Equal(file, Strings.File); Assert.Equal(settingsLabel, Strings.Settings);
        Assert.True(UiNumbers.TryParseSignedValue(number, out double parsed)); Assert.Equal(12.5, parsed);
        Assert.False(UiNumbers.TryParseSignedValue(wrongSeparator, out _));
        Assert.True(UiNumbers.TryParseLength(number, out var length)); Assert.Equal(.0125, length.Meters);
        Assert.True(UiNumbers.TryParsePosition(number, M(1), out var position)); Assert.Equal(length, position);
        Assert.Equal(number, UiNumbers.Format(12.5)); Assert.Equal(number, UiNumbers.Compact(12.5));
        Assert.Equal(number, UiNumbers.AxisTick(5, 2.5, 0));
        Assert.True(InputQuantityFormatter.TryParse(number, UnitCatalog.Millimeter, out double si)); Assert.Equal(.0125, si);
        Assert.False(InputQuantityFormatter.TryParse(wrongSeparator, UnitCatalog.Millimeter, out _));
        Assert.Equal(number, InputQuantityFormatter.Format(si, UnitCatalog.Millimeter));
        Assert.Equal(number, NumberFormatter.Format(new DecimalNumber(125, -1)));
        Assert.Equal(number + " MPa", QuantityFormatter.Format(12.5e6, QuantityKind.Stress, settings.Profile,
            6));
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
        using var scope = new ResultCultureScope("ja-JP", "fr-CA");
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
        using var scope = new ResultCultureScope("de-DE", "de-DE");
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

    [Theory]
    [InlineData(UiLanguagePreference.English, RegionalFormatPreference.GermanGermany, "File")]
    [InlineData(UiLanguagePreference.German, RegionalFormatPreference.EnglishUnitedStates, "Datei")]
    public async Task StartupReadsSettingsOnceBeforeTheFirstMainWindowAndPassesTheSameStoreThroughComposition(
        UiLanguagePreference language, RegionalFormatPreference region, string expectedFile)
    {
        using var environment = new DesktopControlEnvironment();
        using var scope = new ResultCultureScope("ja-JP", "fr-CA");
        var files = new Files(); var store = new LocalSettingsStore(files, "settings");
        var settings = UserSettings.Default with { UiLanguage = language, RegionalFormat = region };
        await store.SaveAsync(settings);
        int reads = 0;
        files.BeforeRead = path => { if (path == store.FilePath) reads++; return Task.CompletedTask; };
        var startup = await DesktopStartupContext.LoadAsync(files, store);
        Assert.Equal(1, reads); Assert.Same(files, startup.Files); Assert.Same(store, startup.SettingsStore);
        CultureConfigurator.Apply(CultureConfigurator.Resolve(startup.Settings, CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture));
        // Changing the file now must not affect the already loaded startup preferences.
        files.Data["settings"] = SettingsCodec.Serialize(UserSettings.Default);
        var lifetime = new ClassicDesktopStyleApplicationLifetime();
        var app = new SpanDraft.Desktop.App(startup) { ApplicationLifetime = lifetime };
        app.OnFrameworkInitializationCompleted();
        var window = Assert.IsType<MainWindow>(lifetime.MainWindow);
        Assert.Equal(expectedFile, window.FindControl<Menu>("MainMenu")!.Items.OfType<MenuItem>().First().Header);
        window.Show(); Dispatcher.UIThread.RunJobs();
        var main = Assert.IsType<MainWindowViewModel>(window.DataContext);
        Assert.Same(startup.Settings, main.Settings); Assert.Equal(1, reads);
        Assert.NotNull(main.BuiltInMaterials); Assert.Null(main.MaterialLibraryError); Assert.Null(main.SectionLibraryError);
        var next = main.Settings with { SignificantDigits = 6 };
        Assert.Null(await main.ApplySettingsAsync(next));
        Assert.True(next.ContentEquals(SettingsCodec.Deserialize(files.Data["settings"])));
        window.Close(); Dispatcher.UIThread.RunJobs();
    }

    [Theory]
    [InlineData("en-US", "de-DE", false)]
    [InlineData("de-DE", "en-US", false)]
    [InlineData("en-US", "de-DE", true)]
    [InlineData("de-DE", "en-US", true)]
    public async Task RecoveryPromptUsesTheRegionalDateInsideTheSelectedUiLanguage(string uiCulture, string culture, bool setup)
    {
        using var environment = new DesktopControlEnvironment();
        using var scope = new ResultCultureScope(culture, uiCulture);
        var owner = new Window(); owner.Show();
        var type = typeof(MainWindow).Assembly.GetType("SpanDraft.Desktop.ProjectDialogs")!;
        var dialogs = (IProjectDialogs)Activator.CreateInstance(type, [owner])!;
        var time = new DateTimeOffset(2026, 10, 16, 15, 42, 0, TimeSpan.Zero);
        var pending = setup ? dialogs.ConfirmSetupRecoveryAsync(false, time) : dialogs.ConfirmRecoveryAsync(false, time);
        var dialog = Assert.Single(owner.OwnedWindows);
        var text = Assert.Single(dialog.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains("2026") == true);
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, setup ? Strings.SetupRecoveryQuestion : Strings.RecoveryQuestion,
            time.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)), text.Text);
        dialog.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, Strings.Discard))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(RecoveryDecision.Discard, await WaitFor(pending));
        owner.Close();
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)]
    [InlineData(true, false)] [InlineData(true, true)]
    public async Task SetupRecoveryEscapeAbortsWindowStartupWithoutDeletingEitherSlot(bool restoreProject, bool damaged)
    {
        using var environment = new DesktopControlEnvironment();
        var files = new Files(); var setupSlot = ProjectSetupRecovery.Local(files).SlotPath;
        var setupBytes = damaged ? System.Text.Encoding.UTF8.GetBytes("{")
            : ProjectSetupRecovery.Encode(new(new(Length: M(1.2345678901234567)), Now));
        files.Data[setupSlot] = setupBytes;
        var projectSlot = ProjectRecovery.Local(files).SlotPath;
        if (restoreProject) files.Data[projectSlot] = ProjectRecovery.Encode(new(State(), null, Now));
        var window = new MainWindow(new(files, new(files, "settings"), UserSettings.Default));
        window.Show(); Dispatcher.UIThread.RunJobs();
        if (restoreProject)
        {
            Click(Strings.Restore); Dispatcher.UIThread.RunJobs();
            Assert.NotNull(Assert.IsType<MainWindowViewModel>(window.DataContext).Session);
        }
        if (damaged) { Click(Strings.OK); Dispatcher.UIThread.RunJobs(); }
        var dialog = Assert.Single(window.OwnedWindows);
        dialog.RaiseEvent(new KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Key.Escape });
        Dispatcher.UIThread.RunJobs();
        Assert.False(window.IsVisible); Assert.Equal(setupBytes, files.Data[setupSlot]);
        if (restoreProject) Assert.True(files.Data.ContainsKey(projectSlot));

        void Click(string label) => Assert.Single(window.OwnedWindows).GetVisualDescendants().OfType<Button>()
            .Single(b => Equals(b.Content, label)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
}
