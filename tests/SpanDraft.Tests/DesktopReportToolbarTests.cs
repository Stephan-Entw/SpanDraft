using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
    public void ReportPrecedesProjectPropertiesAndSharesMenuCommandWithoutOverflow(string language, bool dark, int width, int height)
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
        var layout = Assert.IsType<Grid>(bar.Child);
        var tools = Assert.IsType<StackPanel>(layout.Children[0]);
        var group = Assert.IsType<StackPanel>(layout.Children[1]);
        var properties = view.FindControl<Button>("ProjectPropertiesToolbarButton")!;
        var roller = Assert.IsType<ToggleButton>(tools.Children[^1]);
        Assert.Same(main.Editor!.RollerToolCommand, roller.Command);
        Assert.Equal(new Control[] { button, properties }, group.Children);
        Assert.Equal(roller.Bounds.Height, button.Bounds.Height);
        Assert.Equal(roller.TranslatePoint(default, bar)!.Value.Y, button.TranslatePoint(default, bar)!.Value.Y);
        Assert.Equal(group.Spacing, properties.Bounds.Left - button.Bounds.Right);
        var bounds = new Rect(button.TranslatePoint(default, bar)!.Value, button.Bounds.Size);
        Assert.True(new Rect(bar.Bounds.Size).Contains(bounds));
        var icon = Assert.Single(button.GetVisualDescendants().OfType<PathIcon>());
        Assert.Equal(new Size(24, 24), icon.Bounds.Size);
        Assert.Same(Application.Current.FindResource("FluentIcon.DocumentData"), icon.Data);
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
    [InlineData("de-DE", false)] [InlineData("de-DE", true)]
    [InlineData("en-US", false)] [InlineData("en-US", true)]
    public void ProjectPropertiesStayRightAlignedAndUseNormalToolbarStates(string language, bool dark)
    {
        using var environment = new DesktopControlEnvironment(); using var culture = new UiCultureScope(language);
        Application.Current!.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        var (window, main) = OpenWindow();
        window.RequestedThemeVariant = Application.Current.RequestedThemeVariant;
        try
        {
            Layout(window, 1100, 650);
            var view = Assert.Single(window.GetVisualDescendants().OfType<EditorView>());
            var bar = view.FindControl<Border>("EditorToolbar")!;
            var layout = Assert.IsType<Grid>(bar.Child);
            var left = Assert.IsType<StackPanel>(layout.Children[0]);
            var right = Assert.IsType<StackPanel>(layout.Children[1]);
            var report = view.FindControl<Button>("ReportToolbarButton")!;
            var button = view.FindControl<Button>("ProjectPropertiesToolbarButton")!;
            Assert.Same(main.Editor!.ChangeProjectCommand, button.Command);
            string label = language == "de-DE" ? "Projekteigenschaften" : "Project properties";
            Assert.Equal(label, Assert.Single(button.GetVisualDescendants().OfType<TextBlock>()).Text);
            Assert.Equal(label, AutomationProperties.GetName(button));
            Assert.Contains("toolbar", button.Classes); Assert.DoesNotContain("primary", button.Classes);
            Assert.Equal(new System.Windows.Input.ICommand[] { main.Editor.ForceToolCommand, main.Editor.MomentToolCommand,
                main.Editor.DistributedLoadToolCommand, main.Editor.FixedToolCommand, main.Editor.PinnedToolCommand,
                main.Editor.RollerToolCommand }, left.Children.OfType<Button>().Select(b => b.Command));
            Assert.Equal(new Control[] { report, button }, right.Children);
            var originalLeftBounds = left.Children.Select(c => c.Bounds).ToArray();
            double previousGap = 0;
            foreach (int width in new[] { 1100, 1250, 1600, 2400 })
            {
                window.Width = width;
                Layout(window, width, 800);
                var bounds = new Rect(button.TranslatePoint(default, layout)!.Value, button.Bounds.Size);
                var reportBounds = new Rect(report.TranslatePoint(default, layout)!.Value, report.Bounds.Size);
                Assert.Equal(layout.Bounds.Width, bounds.Right);
                Assert.True(new Rect(layout.Bounds.Size).Contains(bounds));
                Assert.Equal(reportBounds.Top, bounds.Top); Assert.Equal(reportBounds.Height, bounds.Height);
                Assert.True(new Rect(layout.Bounds.Size).Contains(reportBounds));
                Assert.Equal(right.Spacing, bounds.Left - reportBounds.Right);
                var lastTool = left.Children[^1];
                double gap = reportBounds.Left - lastTool.TranslatePoint(new(lastTool.Bounds.Width, 0), layout)!.Value.X;
                Assert.True(gap > previousGap);
                Assert.True(gap >= layout.ColumnSpacing);
                previousGap = gap;
                Assert.Equal(originalLeftBounds, left.Children.Select(c => c.Bounds));
            }
            var icon = Assert.Single(button.GetVisualDescendants().OfType<PathIcon>());
            Assert.Same(Application.Current.FindResource("FluentIcon.DocumentSettings"), icon.Data);
            Assert.Equal(new Size(20, 20), icon.Bounds.Size);
            var slot = Assert.IsType<Grid>(icon.Parent);
            Assert.Equal(Assert.IsType<Grid>(report.GetVisualDescendants().OfType<PathIcon>().Single().Parent).Bounds.Size, slot.Bounds.Size);
            Assert.Equal(new Point(slot.Bounds.Width / 2, slot.Bounds.Height / 2), icon.Bounds.Center);
            var path = Assert.Single(icon.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>());
            Assert.False(icon.IsHitTestVisible); Assert.False(icon.Focusable);
            Assert.Same(report.Background, button.Background); Assert.Same(report.Foreground, button.Foreground);
            Assert.Same(button.Foreground, icon.Foreground); Assert.Same(icon.Foreground, path.Fill);
            Assert.True(button.Focus(NavigationMethod.Tab)); Dispatcher.UIThread.RunJobs();
            Assert.True(Assert.Single(button.GetVisualDescendants().OfType<Border>(), b => b.Name == "FocusRing").IsVisible);
            ((IPseudoClasses)button.Classes).Set(":pointerover", true); Dispatcher.UIThread.RunJobs();
            Assert.Same(Application.Current.FindResource("SurfaceHover"), button.Background);
            Assert.Same(Application.Current.FindResource("TextPrimary"), button.Foreground);
            ((IPseudoClasses)button.Classes).Set(":pressed", true); Dispatcher.UIThread.RunJobs();
            Assert.Same(Application.Current.FindResource("SurfacePressed"), button.Background);
            button.IsEnabled = false; Dispatcher.UIThread.RunJobs();
            Assert.Same(Application.Current.FindResource("TextDisabled"), button.Foreground);
            Assert.Same(button.Foreground, icon.Foreground); Assert.Same(icon.Foreground, path.Fill);
            Assert.Equal(Colors.Transparent, Assert.IsAssignableFrom<ISolidColorBrush>(button.Background).Color);
            ((IPseudoClasses)button.Classes).Set(":pressed", false);
            ((IPseudoClasses)button.Classes).Set(":pointerover", false);
            button.IsEnabled = true; Dispatcher.UIThread.RunJobs();
            Assert.Same(report.Background, button.Background); Assert.Same(report.Foreground, button.Foreground);
        }
        finally { window.Close(); }
    }

    [Theory]
    [InlineData("click")] [InlineData("releaseOutside")] [InlineData("captureLost")]
    public void ReportPointerPressProtectsDraftsAndAlwaysReleasesTemporaryProtection(string gesture)
    {
        using var environment = new DesktopControlEnvironment(); using var culture = new UiCultureScope("de-DE");
        var (window, main) = OpenWindow();
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
            Assert.Equal(MainWorkspaceKind.ReportPreview, main.Workspace); Assert.False(main.IsBusy);
        }
        Dispatcher.UIThread.RunJobs(); Assert.Equal(gesture == "click", editor.IsReportOpen);
        if (gesture == "click")
        {
            Assert.True(editor.PreserveDrafts);
            Assert.Empty(window.OwnedWindows);
            Assert.Single(window.GetVisualDescendants().OfType<ReportPreviewView>());
            Assert.True(main.CloseReport()); Dispatcher.UIThread.RunJobs(); Assert.False(main.IsBusy);
        }
        else Assert.Equal(MainWorkspaceKind.BeamEditor, main.Workspace);
        Assert.False(editor.PreserveDrafts);
        Assert.Same(revision, main.Session.CurrentRevision); Assert.False(main.Session.IsDirty);
        Assert.True(editor.DimensionLength.IsEditing); Assert.Equal("9000", editor.DimensionLength.Text);
        window.Close();
    }

    [Theory]
    [InlineData("menu", "dimension", false)] [InlineData("keyboard", "support", true)]
    [InlineData("menu", "load", true)] [InlineData("keyboard", "distributed", false)]
    public void WorkspaceNavigationRetainsEditorDraftsAndClosesByButtonOrEscape(string route, string draft, bool escape)
    {
        using var environment = new DesktopControlEnvironment(); using var culture = new UiCultureScope("de-DE");
        var (window, main) = OpenWindow(); Layout(window, 1100, 650);
        var editor = main.Editor!; var session = main.Session!; var revision = session.CurrentRevision;
        object buffer;
        if (draft == "support")
        {
            Assert.True(editor.EditSupport(editor.Document.Supports[0].Id));
            buffer = editor.SupportDraft!; editor.SupportDraft!.PositionText = "unconfirmed";
        }
        else if (draft == "load")
        {
            Assert.True(editor.EditLoad(editor.Document.Loads[0].Id));
            buffer = editor.LoadDraft!; editor.LoadDraft!.PositionText = "unconfirmed";
        }
        else if (draft == "distributed")
        {
            Assert.True(editor.EditDistributedLoad(editor.Document.DistributedLoads[0].Id));
            buffer = editor.DistributedLoadDraft!; editor.DistributedLoadDraft!.IntensityText = "unconfirmed";
        }
        else
        {
            editor.DimensionLength.Begin(); editor.DimensionLength.Text = "9000"; buffer = editor.DimensionLength;
        }
        if (route == "menu") window.FindControl<MenuItem>("ReportMenuItem")!.Command!.Execute(null);
        else
        {
            var primary = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
            var key = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.P, KeyModifiers = primary };
            window.RaiseEvent(key); Assert.True(key.Handled);
        }
        Dispatcher.UIThread.RunJobs(); Layout(window, 1100, 650);
        Assert.Equal(MainWorkspaceKind.ReportPreview, main.Workspace); Assert.False(main.IsWorkspaceBusy);
        Assert.Empty(window.OwnedWindows); Assert.Same(session, main.Session);
        var reportView = Assert.Single(window.GetVisualDescendants().OfType<ReportPreviewView>());
        if (escape)
        {
            var key = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape };
            Assert.True(reportView.IsFocused);
            reportView.RaiseEvent(key); Assert.True(key.Handled);
        }
        else typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(reportView.FindControl<Button>("CloseButton"), null);
        Dispatcher.UIThread.RunJobs(); Layout(window, 1100, 650);
        Assert.Equal(MainWorkspaceKind.BeamEditor, main.Workspace); Assert.Same(editor, main.CurrentViewModel);
        Assert.Same(revision, session.CurrentRevision); Assert.False(session.IsDirty);
        Assert.Single(window.GetVisualDescendants().OfType<EditorView>());
        if (draft == "support") { Assert.Same(buffer, editor.SupportDraft); Assert.Equal("unconfirmed", editor.SupportDraft!.PositionText); }
        else if (draft == "load") { Assert.Same(buffer, editor.LoadDraft); Assert.Equal("unconfirmed", editor.LoadDraft!.PositionText); }
        else if (draft == "distributed") { Assert.Same(buffer, editor.DistributedLoadDraft); Assert.Equal("unconfirmed", editor.DistributedLoadDraft!.IntensityText); }
        else { Assert.True(editor.DimensionLength.IsEditing); Assert.Equal("9000", editor.DimensionLength.Text); }
        window.Close();
    }

    [Theory]
    [InlineData("toolbar")] [InlineData("menu")] [InlineData("keyboard")]
    public void PreviewFocusAllowsImmediateEscapeAndNormalToolbarTabNavigationAfterReopening(string route)
    {
        using var environment = new DesktopControlEnvironment(); using var culture = new UiCultureScope("de-DE");
        var (window, main) = OpenWindow(); Layout(window, 1100, 650);
        try
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                if (route == "toolbar")
                {
                    var reportButton = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ReportToolbarButton");
                    reportButton.Focus(NavigationMethod.Tab);
                    typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(reportButton, null);
                }
                else if (route == "menu")
                {
                    var reportMenu = window.FindControl<MenuItem>("ReportMenuItem")!;
                    reportMenu.Focus(NavigationMethod.Tab); reportMenu.Command!.Execute(null);
                }
                else
                {
                    var primary = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
                    var key = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.P, KeyModifiers = primary };
                    window.RaiseEvent(key); Assert.True(key.Handled);
                }
                Layout(window, 1100, 650);
                var preview = Assert.Single(window.GetVisualDescendants().OfType<ReportPreviewView>());
                Assert.Same(preview, window.FocusManager!.GetFocusedElement());
                var toolbar = preview.FindControl<Border>("ReportToolbar")!;
                Assert.All(toolbar.GetVisualDescendants().OfType<Button>(), button =>
                {
                    Assert.False(button.IsFocused); Assert.DoesNotContain("selected", button.Classes);
                    Assert.False(Assert.Single(button.GetVisualDescendants().OfType<Border>(), b => b.Name == "FocusRing").IsVisible);
                });
                InputElement focused = preview;
                if (attempt == 1)
                {
                    var names = new[] { "SavePdfButton", "PrintButton", "PreviousButton", "NextButton", "ZoomOutButton", "ZoomValueButton", "ZoomInButton",
                        "PageFitButton", "PageWidthButton", "CloseButton" };
                    foreach (var button in names.Select(name => preview.FindControl<Button>(name)!).Where(button => button.IsEffectivelyEnabled))
                    {
                        Assert.True(DesktopControlEnvironment.MoveFocus(window, focused, NavigationDirection.Next));
                        Assert.Same(button, window.FocusManager.GetFocusedElement());
                        Assert.True(Assert.Single(button.GetVisualDescendants().OfType<Border>(), b => b.Name == "FocusRing").IsVisible);
                        focused = button;
                    }
                }
                var escape = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape };
                focused.RaiseEvent(escape); Assert.True(escape.Handled);
                Layout(window, 1100, 650);
                Assert.Equal(MainWorkspaceKind.BeamEditor, main.Workspace);
            }
        }
        finally { window.Close(); }
    }

    private static (MainWindow Window, MainWindowViewModel Main) OpenWindow()
    {
        var files = new Files(); string path = TestPath("toolbar.spandraft");
        var state = State();
        files.Data[path] = ProjectFileCodec.Serialize(state with { Document = state.Document with { Length = M(1) } });
        var window = new MainWindow(new DesktopStartupContext(files, new LocalSettingsStore(files, "settings"), UserSettings.Default));
        window.Show(); Dispatcher.UIThread.RunJobs();
        var main = new MainWindowViewModel(files: files);
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
