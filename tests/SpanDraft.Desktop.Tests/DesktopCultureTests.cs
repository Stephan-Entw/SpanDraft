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
using SpanDraft.Desktop.Services;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.ViewModels;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

[Collection("Avalonia")]
public sealed class DesktopCultureTests
{

    [Theory]
    [InlineData(UiLanguagePreference.English, RegionalFormatPreference.GermanGermany, "File")]
    [InlineData(UiLanguagePreference.German, RegionalFormatPreference.EnglishUnitedStates, "Datei")]
    public async Task StartupReadsSettingsOnceBeforeTheFirstMainWindowAndPassesTheSameStoreThroughComposition(
        UiLanguagePreference language, RegionalFormatPreference region, string expectedFile)
    {
        using var environment = new DesktopControlEnvironment();
        using var scope = new ProcessCultureScope("ja-JP", "fr-CA");
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
        // LoadAsync wires the local recovery folder; the test keeps recovery in memory instead of touching the user profile.
        var isolated = new DesktopStartupContext(startup.Files, startup.SettingsStore, startup.Settings)
            { SettingsLoad = startup.SettingsLoad, StateStores = DesktopStateStores.Isolated(files) };
        var app = new SpanDraft.Desktop.App(TestHosts.Initialized(new DesktopApplicationHost(isolated))) { ApplicationLifetime = lifetime };
        app.OnFrameworkInitializationCompleted();
        var window = Assert.IsType<MainWindow>(lifetime.MainWindow);
        Assert.Equal(expectedFile, window.FindControl<Menu>("MainMenu")!.Items.OfType<MenuItem>().First().Header);
        window.Show(); Dispatcher.UIThread.RunJobs();
        var main = Assert.IsType<MainWindowViewModel>(window.DataContext);
        Assert.Same(startup.Settings, main.Settings); Assert.Equal(1, reads);
        Assert.NotNull(main.BuiltInMaterials); Assert.Null(main.MaterialLibraryError); Assert.Null(main.SectionLibraryError);
        var next = main.Settings with { SignificantDigits = 5 };
        Assert.Null(await main.ApplySettingsAsync(SettingsPatch.From(next)));
        Assert.True(next.ContentEquals(SettingsCodec.Deserialize(files.Data["settings"])));
        window.Close(); Dispatcher.UIThread.RunJobs();
    }

    [Theory]
    [InlineData("en-US", "de-DE")]
    [InlineData("de-DE", "en-US")]
    public async Task RecoveryCenterUsesTheRegionalDateInsideTheSelectedUiLanguage(string uiCulture, string culture)
    {
        using var scope = new ProcessCultureScope(culture, uiCulture);
        var storage = new TestRecoveryStorage();
        var time = new DateTimeOffset(2026, 10, 16, 15, 42, 0, TimeSpan.Zero);
        storage.Put(RecoverySlot.Project(RecoveryId.New()), ProjectRecovery.Encode(new(State(), null, time)));
        var catalog = new RecoveryCatalog(storage, new Files(), Identities, new ProjectPathRegistry());
        Assert.True(await catalog.InitializeAsync(TestContext.Current.CancellationToken));
        using var center = new RecoveryCenterViewModel(catalog, _ => Task.FromResult(true), () => { }, _ => Task.CompletedTask);
        var item = Assert.Single(center.Items);
        Assert.Equal(string.Format(CultureInfo.CurrentUICulture, Strings.RecoverySavedAt,
            time.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)), item.SavedLabel);
        Assert.Contains(uiCulture == "de-DE" ? "Gesichert" : "Saved", item.SavedLabel);
    }

    [Theory]
    [InlineData("en-US", "de-DE")]
    [InlineData("de-DE", "en-US")]
    public async Task UnavailableRecoveryDialogFollowsTheSelectedUiLanguage(string uiCulture, string culture)
    {
        using var environment = new DesktopControlEnvironment();
        using var scope = new ProcessCultureScope(culture, uiCulture);
        var owner = new Window(); owner.Show();
        var type = typeof(MainWindow).Assembly.GetType("SpanDraft.Desktop.ProjectDialogs")!;
        var dialogs = (IProjectDialogs)Activator.CreateInstance(type, [owner])!;
        var pending = dialogs.ConfirmRecoveryRetryAsync("The folder cannot be read.");
        var dialog = Assert.Single(owner.OwnedWindows);
        Assert.Single(dialog.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == Strings.RecoveryUnavailable + "\n\n" + "The folder cannot be read.");
        dialog.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, Strings.Quit))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.False(await WaitFor(pending));
        owner.Close();
    }
}
