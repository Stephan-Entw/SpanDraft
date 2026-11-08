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
        using var scope = new ProcessCultureScope(culture, uiCulture);
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
