using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SpanDraft.Desktop;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.ViewModels;
using SpanDraft.Desktop.Views;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;
using Path = System.IO.Path;

namespace SpanDraft.Tests;

[Collection("Schematic text")]
public sealed class DesktopReportToolbarTests
{
    [Theory]
    [InlineData("de-DE", false, 1100, 650)] [InlineData("en-US", true, 1100, 650)]
    [InlineData("de-DE", true, 1250, 800)] [InlineData("en-US", false, 1250, 800)]
    public void ReportFollowsSupportsAndSharesMenuCommandWithoutOverflow(string language, bool dark, int width, int height)
    {
        using var environment = new DesktopControlEnvironment(); using var culture = new UiCultureScope(language);
        Application.Current!.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        var (window, main) = OpenWindow();
        window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        window.Width = width; window.Height = height;
        Layout(window, width, height);
        var view = Assert.Single(window.GetVisualDescendants().OfType<EditorView>());
        var bar = view.FindControl<Border>("EditorToolbar")!;
        var button = view.FindControl<Button>("ReportToolbarButton")!;
        var menu = window.FindControl<MenuItem>("ReportMenuItem")!;
        Assert.Same(main.ReportCommand, button.Command); Assert.Same(menu.Command, button.Command);
        Assert.True(main.CanReport, main.Editor!.Presentation.StatusText);
        Assert.True(button.IsEffectivelyEnabled);
        Assert.Equal(language == "de-DE" ? "Bericht" : "Report", Assert.Single(button.GetVisualDescendants().OfType<TextBlock>()).Text);
        var group = Assert.IsType<StackPanel>(bar.Child);
        var separator = view.FindControl<Rectangle>("ReportToolbarSeparator")!;
        var roller = Assert.IsType<ToggleButton>(group.Children[^3]);
        Assert.Same(main.Editor!.RollerToolCommand, roller.Command);
        Assert.Same(separator, group.Children[^2]); Assert.Same(button, group.Children[^1]);
        Assert.Equal(roller.Bounds.Height, button.Bounds.Height);
        Assert.Equal(roller.Bounds.Top, button.Bounds.Top);
        Assert.True(roller.Bounds.Right <= separator.Bounds.Left && separator.Bounds.Right <= button.Bounds.Left);
        var bounds = new Rect(button.TranslatePoint(default, bar)!.Value, button.Bounds.Size);
        Assert.True(new Rect(bar.Bounds.Size).Contains(bounds));
        var icon = Assert.Single(button.GetVisualDescendants().OfType<PathIcon>());
        Assert.Equal(new Size(20, 20), icon.Bounds.Size);
        Assert.Same(Application.Current.FindResource("FluentIcon.Document"), icon.Data);
        Assert.Same(button.Foreground, icon.Foreground);
        var slot = Assert.IsType<Grid>(icon.Parent);
        var existingIcon = bar.GetVisualDescendants().OfType<Image>().First(i => i.Classes.Contains("toolbarIcon"));
        Assert.Equal(existingIcon.Bounds.Size, slot.Bounds.Size);
        Assert.Equal(slot.Bounds.Width / 2, icon.Bounds.Center.X);
        Assert.Equal(slot.Bounds.Height / 2, icon.Bounds.Center.Y);
        var reportText = Assert.Single(button.GetVisualDescendants().OfType<TextBlock>());
        var rollerText = Assert.Single(roller.GetVisualDescendants().OfType<TextBlock>());
        var rollerIcon = Assert.Single(roller.GetVisualDescendants().OfType<Image>());
        Assert.Equal(rollerText.TranslatePoint(default, bar)!.Value.X - rollerIcon.TranslatePoint(new(rollerIcon.Bounds.Width, 0), bar)!.Value.X,
            reportText.TranslatePoint(default, bar)!.Value.X - icon.TranslatePoint(new(icon.Bounds.Width, 0), bar)!.Value.X);
        using var image = new RenderTargetBitmap(new((int)bar.Bounds.Width, (int)bar.Bounds.Height), new(96, 96));
        image.Render(bar);
        string directory = Path.Combine(RepositoryRoot(), "output", "pdf", "toolbar"); Directory.CreateDirectory(directory);
        image.Save(Path.Combine(directory, $"report-{language}-{(dark ? "dark" : "light")}-{width}.png"), PngBitmapEncoderOptions.Default);
        var state = main.Session!.CurrentRevision.State;
        Assert.True(main.Session.Commit(state with { Document = state.Document.WithSupports([]) }));
        Dispatcher.UIThread.RunJobs(); Assert.False(button.IsEffectivelyEnabled); Assert.False(main.ReportCommand.CanExecute(null));
        Assert.True(main.Undo()); Dispatcher.UIThread.RunJobs(); Assert.True(button.IsEffectivelyEnabled);
        window.Close();
    }

    [Theory]
    [InlineData("click")] [InlineData("releaseOutside")] [InlineData("captureLost")]
    public void ReportPointerPressProtectsDraftsAndAlwaysReleasesTemporaryProtection(string gesture)
    {
        using var environment = new DesktopControlEnvironment(); using var culture = new UiCultureScope("de-DE");
        var pending = new TaskCompletionSource(); int calls = 0;
        var (window, main) = OpenWindow(editor =>
        {
            calls++; Assert.True(editor.IsBusy); Assert.True(editor.PreserveDrafts);
            Assert.True(editor.DimensionLength.IsEditing); Assert.Equal("9000", editor.DimensionLength.Text);
            return pending.Task;
        });
        Layout(window, 1100, 650);
        var button = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ReportToolbarButton");
        var source = Assert.Single(button.GetVisualDescendants().OfType<TextBlock>());
        var editor = main.Editor!; var revision = main.Session!.CurrentRevision;
        editor.DimensionLength.Begin(); editor.DimensionLength.Text = "9000";
        var pointer = new Avalonia.Input.Pointer(1, PointerType.Mouse, true);
        var point = button.TranslatePoint(new(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        source.RaiseEvent(new PointerPressedEventArgs(source, pointer, window, point, 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None, 1));
        Assert.True(editor.IsReportOpen);
        editor.DimensionLength.LoseFocus();
        Assert.Same(revision, main.Session.CurrentRevision); Assert.True(editor.DimensionLength.IsEditing);
        if (gesture == "captureLost")
        {
            pointer.Capture(button);
            pointer.Capture(null);
        }
        else if (gesture == "releaseOutside")
        {
            window.RaiseEvent(new PointerReleasedEventArgs(window, pointer, window, new Point(20, 200), 1,
                new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left));
        }
        else
        {
            source.RaiseEvent(new PointerReleasedEventArgs(source, pointer, window, point, 1,
                new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left));
            pointer.Capture(null);
            // The inert window does not hit-test the native pointer; invoke the button's normal click path.
            typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, null);
            Assert.Equal(1, calls); Assert.True(main.IsBusy);
        }
        Dispatcher.UIThread.RunJobs(); Assert.False(editor.IsReportOpen);
        if (gesture == "click")
        {
            Assert.True(editor.PreserveDrafts);
            pending.SetResult(); Dispatcher.UIThread.RunJobs(); Assert.False(main.IsBusy);
        }
        else Assert.Equal(0, calls);
        Assert.False(editor.PreserveDrafts);
        Assert.Same(revision, main.Session.CurrentRevision); Assert.False(main.Session.IsDirty);
        Assert.True(editor.DimensionLength.IsEditing); Assert.Equal("9000", editor.DimensionLength.Text);
        window.Close();
    }

    private static (MainWindow Window, MainWindowViewModel Main) OpenWindow(Func<EditorViewModel, Task>? showReport = null)
    {
        var files = new Files(); string path = TestPath("toolbar.spandraft");
        var state = State();
        files.Data[path] = ProjectFileCodec.Serialize(state with { Document = state.Document with { Length = M(1) } });
        var window = new MainWindow(new DesktopStartupContext(files, new LocalSettingsStore(files, "settings"), UserSettings.Default));
        window.Show(); Dispatcher.UIThread.RunJobs();
        var main = new MainWindowViewModel(files: files, showReport: showReport);
        Assert.True(main.OpenAsync(path).GetAwaiter().GetResult());
        typeof(MainWindow).GetField("_model", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, main);
        window.DataContext = main;
        return (window, main);
    }
    private static void Layout(Window window, int width, int height)
    {
        window.ApplyTemplate();
        window.Measure(new(width, height)); window.Arrange(new Rect(0, 0, width, height));
        var content = (Control)window.Content!;
        foreach (var child in content.GetVisualDescendants().OfType<Control>())
        { child.InvalidateMeasure(); child.InvalidateArrange(); }
        content.InvalidateMeasure(); content.Measure(new(width, height));
        content.InvalidateArrange(); content.Arrange(new Rect(0, 0, width, height));
        Dispatcher.UIThread.RunJobs();
    }
    private static string RepositoryRoot([System.Runtime.CompilerServices.CallerFilePath] string source = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "../.."));
}
