using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SpanDraft.Desktop;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.ViewModels;
using SpanDraft.Desktop.Views;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

[Collection("Schematic text")]
public sealed class StartPageControlTests
{
    [Theory]
    [InlineData("de-DE", 1250, 800)] [InlineData("en-US", 1250, 800)]
    [InlineData("de-DE", 1100, 650)] [InlineData("en-US", 1100, 650)]
    [InlineData("de-DE", 1800, 900)] [InlineData("en-US", 1800, 900)]
    public async Task RealShellShowsCompactStartAndRecentRowsAtAllSupportedSizes(string culture, int width, int height)
    {
        using var environment = new DesktopControlEnvironment(); using var scope = new UiCultureScope(culture);
        var files = new Files(); var store = new RecentProjectsStore(files, "recents", () => Now);
        var main = new MainWindowViewModel(files: files, recentStore: store);
        var shell = new MainWindow(); var content = shell.Content; shell.Content = null;
        var window = new Window { Content = content, DataContext = main, Width = width, Height = height };
        foreach (var template in shell.DataTemplates) window.DataTemplates.Add(template);
        window.Show(); Arrange(window, width, height);
        var view = Assert.Single(window.GetVisualDescendants().OfType<StartPageView>());
        Assert.Same(main.StartPage, view.DataContext);
        Assert.Single(view.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == Strings.Start);
        var header = Assert.Single(view.GetVisualDescendants().OfType<Border>(), b => b.Classes.Contains("workspaceHeader"));
        Assert.Equal(70, header.Bounds.Height);
        var create = view.FindControl<Button>("NewProjectButton")!;
        var open = view.FindControl<Button>("OpenProjectButton")!;
        var manual = view.FindControl<Button>("UserManualButton")!;
        Assert.Equal(280, create.Bounds.Width); Assert.Equal(38, create.Bounds.Height);
        Assert.Equal(create.Bounds.Size, open.Bounds.Size); Assert.Equal(30, manual.Bounds.Height);
        Assert.Same(main.NewCommand, create.Command); Assert.Same(main.OpenCommand, open.Command);
        Assert.Same(main.UserManualCommand, manual.Command);
        Assert.Same(main.UserManualCommand, shell.FindControl<MenuItem>("UserManualMenuItem")!.Command);
        Assert.True(view.FindControl<TextBlock>("EmptyRecentProjects")!.IsEffectivelyVisible);
        Assert.Single(view.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == Strings.NoRecentProjects && t.IsEffectivelyVisible);
        Capture(window, culture, width, height, "empty");

        string longName = new('W', 180);
        for (int i = 0; i < 10; i++) await store.RecordUseAsync(TestPath(longName + i + ".spandraft"));
        await main.InitializeRecentProjectsAsync(); Arrange(window, width, height);
        Assert.False(view.FindControl<TextBlock>("EmptyRecentProjects")!.IsEffectivelyVisible);
        var rows = view.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("recentProject")).ToArray();
        Assert.Equal(10, rows.Length);
        var list = view.FindControl<ItemsControl>("RecentProjectsList")!;
        Assert.InRange(list.Bounds.Width, 680, 760);
        var scroll = Assert.Single(view.GetVisualDescendants().OfType<ScrollViewer>());
        Assert.Equal(ScrollBarVisibility.Disabled, scroll.HorizontalScrollBarVisibility);
        Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 1);
        foreach (var row in rows)
        {
            Assert.Equal(56, row.Bounds.Height); Assert.True(row.Bounds.Width > 0);
            var item = Assert.IsType<RecentProjectItemViewModel>(row.DataContext);
            Assert.Same(item.OpenCommand, row.Command); Assert.Equal(item.AutomationName, AutomationProperties.GetName(row));
            var texts = row.GetVisualDescendants().OfType<TextBlock>().ToArray();
            foreach (var text in texts.Where(t => t.Text == item.Name || t.Text == item.FilePath))
            {
                Assert.Equal(TextTrimming.CharacterEllipsis, text.TextTrimming);
                Assert.True(Assert.Single(text.TextLayout.TextLines).HasCollapsed);
            }
            Assert.Equal(item.FilePath, ToolTip.GetTip(Assert.Single(texts, t => t.Text == item.FilePath)));
        }
        Assert.True(create.Focus(NavigationMethod.Tab)); Assert.True(create.IsFocused);
        Assert.True(DesktopControlEnvironment.MoveFocus(window, create, NavigationDirection.Next)); Assert.True(open.IsFocused);
        Assert.True(DesktopControlEnvironment.MoveFocus(window, open, NavigationDirection.Next)); Assert.True(manual.IsFocused);
        Assert.True(DesktopControlEnvironment.MoveFocus(window, manual, NavigationDirection.Next)); Assert.True(rows[0].IsFocused);
        Assert.True(Assert.Single(rows[0].GetVisualDescendants().OfType<Border>(), b => b.Name == "FocusRing").IsVisible);
        Capture(window, culture, width, height, "recents");
        window.Close(); shell.Close();
    }

    [Theory]
    [InlineData(Key.Enter)] [InlineData(Key.Space)]
    public async Task KeyboardActivatesTheSharedCommandsAndRecentRows(Key key)
    {
        using var environment = new DesktopControlEnvironment();
        var files = new Files(); var dialogs = new Dialogs(); int calls = 0;
        string path = TestPath("keyboard.spandraft"); files.Data[path] = ProjectFileCodec.Serialize(State());
        var store = new RecentProjectsStore(files, "recents", () => Now); await store.RecordUseAsync(path);
        var main = new MainWindowViewModel(files: files, dialogs: dialogs, recentStore: store,
            openUserManual: () => { calls++; return Task.CompletedTask; });
        await main.InitializeRecentProjectsAsync();
        var view = new StartPageView { DataContext = main.StartPage };
        var window = new Window { Content = view, Width = 1250, Height = 800 }; window.Show(); Arrange(window, 1250, 800);
        Activate(view.FindControl<Button>("UserManualButton")!, key); Assert.Equal(1, calls);
        Activate(view.FindControl<Button>("OpenProjectButton")!, key); Assert.Equal(MainViewMode.Start, main.Mode);
        Activate(view.FindControl<Button>("NewProjectButton")!, key); Assert.Equal(MainViewMode.ProjectSetup, main.Mode);
        main.Setup.Escape(); Assert.Equal(MainViewMode.Start, main.Mode);
        var row = Assert.Single(view.GetVisualDescendants().OfType<Button>(), b => b.Classes.Contains("recentProject"));
        Activate(row, key); Assert.Equal(MainViewMode.Editor, main.Mode); Assert.Equal(path, main.Session!.FilePath);
        window.Close();
    }

    [Theory]
    [InlineData("de-DE")] [InlineData("en-US")]
    public async Task MissingRecentDialogUsesLocalizedChoicesAndResults(string culture)
    {
        using var environment = new DesktopControlEnvironment(); using var scope = new UiCultureScope(culture);
        string path = TestPath(new string('W', 90) + ".spandraft");
        var owner = new Window { Width = 1100, Height = 650 };
        var dialogs = new ProjectDialogs(owner);
        owner.Show();
        try
        {
            string remove = culture == "de-DE" ? "Aus Liste entfernen" : "Remove from list";
            string cancel = culture == "de-DE" ? "Abbrechen" : "Cancel";
            string message = culture == "de-DE"
                ? "Projektdatei nicht gefunden.\n\nDie Datei ist am gespeicherten Speicherort nicht mehr vorhanden. Sie wurde möglicherweise gelöscht, verschoben oder umbenannt.\n\n" + path + "\n\nSoll der Eintrag aus „Zuletzt verwendet“ entfernt werden?"
                : "Project file not found.\n\nThe file is no longer available at the saved location. It may have been deleted, moved or renamed.\n\n" + path + "\n\nRemove the entry from Recent projects?";
            foreach (string action in new[] { "cancel", "escape", "close", "remove" })
            {
                var pending = dialogs.ConfirmRemoveMissingRecentAsync(path); Dispatcher.UIThread.RunJobs();
                var dialog = Assert.Single(owner.OwnedWindows); Arrange(dialog, 520, 500);
                var text = Assert.Single(dialog.GetVisualDescendants().OfType<TextBlock>(),
                    t => t.Text?.ReplaceLineEndings("\n") == message);
                Assert.Equal(TextWrapping.Wrap, text.TextWrapping);
                Assert.True(text.TextLayout.Height <= text.Bounds.Height + 1);
                var buttons = dialog.GetVisualDescendants().OfType<Button>().ToArray();
                Assert.Equal(new[] { remove, cancel }, buttons.Select(b => b.Content));
                Assert.True(buttons[0].IsDefault); Assert.False(buttons[1].IsDefault);
                var panel = (Control)dialog.Content!;
                foreach (var button in buttons)
                {
                    var bounds = button.Bounds;
                    Assert.True(bounds.Width >= button.DesiredSize.Width);
                    var position = button.TranslatePoint(default, panel)!.Value;
                    Assert.True(position.X >= 0 && position.X + bounds.Width <= panel.Bounds.Width + 1);
                }
                if (action == "escape") dialog.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
                else if (action == "close") dialog.Close();
                else buttons[action == "remove" ? 0 : 1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(action == "remove", await WaitFor(pending));
            }
        }
        finally
        {
            foreach (var dialog in owner.OwnedWindows.ToArray()) dialog.Close();
            owner.Close();
        }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task MissingRecentCommandUpdatesVisibleRowsOnlyWhenRemovalIsConfirmed(bool remove)
    {
        using var environment = new DesktopControlEnvironment();
        var files = new Files(); var store = new RecentProjectsStore(files, "recents", () => Now);
        string path = TestPath("missing.spandraft"); await store.RecordUseAsync(path);
        var dialogs = new Dialogs { RemoveMissingRecent = remove };
        var main = new MainWindowViewModel(files: files, dialogs: dialogs, recentStore: store);
        await main.InitializeRecentProjectsAsync();
        var view = new StartPageView { DataContext = main.StartPage };
        var window = new Window { Content = view, Width = 1100, Height = 650 }; window.Show();
        try
        {
            Arrange(window, 1100, 650);
            var row = Assert.Single(view.GetVisualDescendants().OfType<Button>(), b => b.Classes.Contains("recentProject"));
            Activate(row, Key.Enter); Arrange(window, 1100, 650);
            Assert.Equal(new[] { path }, dialogs.MissingRecentPaths);
            Assert.Equal(remove ? 0 : 1, view.GetVisualDescendants().OfType<Button>().Count(b => b.Classes.Contains("recentProject")));
            Assert.Equal(remove, view.FindControl<TextBlock>("EmptyRecentProjects")!.IsEffectivelyVisible);
        }
        finally { window.Close(); }
    }

    private static void Activate(Button button, Key key)
    {
        Assert.True(button.Focus(NavigationMethod.Tab));
        button.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });
        button.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = key });
        Dispatcher.UIThread.RunJobs();
    }

    private static void Arrange(Window window, int width, int height)
    {
        window.ApplyTemplate();
        for (int i = 0; i < 3; i++)
        {
            window.Measure(new Size(width, height)); window.Arrange(new Rect(0, 0, width, height));
            var content = (Control)window.Content!;
            content.InvalidateMeasure(); content.Measure(new Size(width, height));
            content.InvalidateArrange(); content.Arrange(new Rect(0, 0, width, height));
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static void Capture(Window window, string culture, int width, int height, string state)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "TestResults", "start"); Directory.CreateDirectory(directory);
        using var bitmap = new RenderTargetBitmap(new PixelSize(width, height)); bitmap.Render((Control)window.Content!);
        bitmap.Save(Path.Combine(directory, $"start-{culture}-{width}x{height}-{state}.png"), PngBitmapEncoderOptions.Default);
    }
}
