using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Data;
using Avalonia.Diagnostics;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
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

namespace SpanDraft.Tests;

[Collection("Avalonia")]
public sealed class DesktopMenuControlTests
{
    [Theory]
    [InlineData("de-DE", 1100, 650)] [InlineData("en-US", 1100, 650)]
    [InlineData("de-DE", 1250, 800)] [InlineData("en-US", 1250, 800)]
    public void MainMenuRemainsAtTheSamePositionAcrossEveryWorkspace(string culture, double width, double height)
    {
        using var environment = new DesktopControlEnvironment();
        using var scope = new UiCultureScope(culture);
        var main = new MainWindowViewModel();
        // Transplant the production shell to avoid starting native windows and local recovery I/O.
        var shell = new MainWindow(); var content = shell.Content; shell.Content = null;
        var window = new Window { DataContext = main, Content = content, Width = width, Height = height };
        foreach (var template in shell.DataTemplates) window.DataTemplates.Add(template);
        window.Show(); Arrange(window, width, height);
        var project = shell.FindControl<Menu>("MainMenu")!;
        Assert.True(project.IsEffectivelyVisible); AssertChrome(project, width);
        Assert.Same(main.NewCommand, shell.FindControl<MenuItem>("NewMenuItem")!.Command);
        Assert.Same(main.OpenCommand, shell.FindControl<MenuItem>("OpenMenuItem")!.Command);
        Assert.Same(main.SaveCommand, shell.FindControl<MenuItem>("SaveMenuItem")!.Command);
        var report = shell.FindControl<MenuItem>("ReportMenuItem")!;
        Assert.Same(main.ReportCommand, report.Command);
        var closeProject = shell.FindControl<MenuItem>("CloseProjectMenuItem")!;
        Assert.Same(main.CloseProjectCommand, closeProject.Command);
        Assert.Equal(culture == "de-DE" ? "Projekt schließen" : "Close Project", closeProject.Header);
        Assert.Null(closeProject.InputGesture);
        var file = project.Items.OfType<MenuItem>().Single(i => Equals(i.Header, Strings.File));
        Assert.Single(file.GetVisualDescendants().OfType<Popup>()).ShouldUseOverlayLayer = true;
        Assert.Equal(new object?[] { Strings.NewProject, Strings.Open, null, Strings.Save, Strings.SaveAs,
            Strings.ReportCommand, Strings.CloseProject, null, Strings.Quit }, file.Items.Select(i => i is MenuItem item ? item.Header : null));
        Assert.Same(main.UndoCommand, shell.FindControl<MenuItem>("UndoMenuItem")!.Command);
        var primary = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        Assert.Equal(new KeyGesture(Key.N, primary), shell.FindControl<MenuItem>("NewMenuItem")!.InputGesture);
        Assert.Equal(new KeyGesture(Key.S, primary | KeyModifiers.Shift), shell.FindControl<MenuItem>("SaveAsMenuItem")!.InputGesture);
        Assert.Equal(new KeyGesture(Key.Z, primary), shell.FindControl<MenuItem>("UndoMenuItem")!.InputGesture);
        Assert.Equal(new KeyGesture(Key.P, primary), report.InputGesture);
        var projectTheme = project.ItemContainerTheme;

        double menuTop = project.Bounds.Top;
        CheckWorkspace(MainWorkspaceKind.Start);
        main.NewCommand.Execute(null); CheckWorkspace(MainWorkspaceKind.ProjectSetup);
        main.Setup.ShowMaterials(); CheckWorkspace(MainWorkspaceKind.MaterialSelection);
        var materials = (MaterialSelectionViewModel)main.Setup.CurrentStep!;
        main.Setup.SelectedMaterial = main.BuiltInMaterials!.All[0]; main.Setup.ShowOverview(); main.Setup.ShowMaterials();
        materials = (MaterialSelectionViewModel)main.Setup.CurrentStep!; materials.EditSelectedCommand.Execute(null); CheckWorkspace(MainWorkspaceKind.MaterialSelection);
        main.Setup.Escape(); main.Setup.ShowOverview();
        main.Setup.ShowSections(); CheckWorkspace(MainWorkspaceKind.SectionDefinition);
        var definition = Assert.Single(window.GetVisualDescendants().OfType<SectionDefinitionView>());
        Assert.Empty(definition.GetVisualDescendants().OfType<Menu>());
        main.Setup.Escape(); ProjectTestSupport.CompleteSetup(main); main.Setup.ApplyCommand.Execute(null); CheckWorkspace(MainWorkspaceKind.BeamEditor);
        main.Session!.MarkSaved(main.Session.CurrentRevision, TestPath("closed.spandraft"));
        closeProject.Command!.Execute(null); CheckWorkspace(MainWorkspaceKind.Start);
        Assert.True(window.IsVisible); Assert.Null(main.Session); Assert.Null(main.Editor);

        void CheckWorkspace(MainWorkspaceKind expected)
        {
            Arrange(window, width, height);
            Assert.Equal(expected, main.Workspace);
            file.IsSubMenuOpen = true; Arrange(window, width, height);
            Assert.Equal(expected == MainWorkspaceKind.BeamEditor, closeProject.IsEffectivelyEnabled);
            file.IsSubMenuOpen = false; Arrange(window, width, height);
            Assert.True(project.IsEffectivelyVisible); AssertChrome(project, width);
            Assert.Equal(menuTop, project.Bounds.Top);
            Assert.Single(window.GetVisualDescendants().OfType<Menu>());
            Assert.Equal(new[] { Strings.File, Strings.Edit, Strings.Help }, project.Items.OfType<MenuItem>().Select(i => i.Header));
            Assert.True(shell.FindControl<MenuItem>("UserManualMenuItem")!.IsEnabled);
            string directory = Path.Combine(AppContext.BaseDirectory, "TestResults", "shell"); Directory.CreateDirectory(directory);
            using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize((int)width, (int)height));
            bitmap.Render((Control)window.Content!);
            bitmap.Save(Path.Combine(directory, $"shell-{culture}-{width}x{height}-{expected}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }

        // A plain future Menu receives all decoration automatically, without a class or wrapper.
        var future = new Menu(); future.Items.Add(new MenuItem { Header = Strings.File });
        window.Content = future; Arrange(window, width, height);
        AssertChrome(future, width); Assert.Same(projectTheme, future.ItemContainerTheme);
        window.Close();
    }

    [Fact]
    public void GlobalChromeRetainsNativeMenuFocusArrowNavigationPopupAndCommandActivation()
    {
        using var environment = new DesktopControlEnvironment();
        int executed = 0;
        var command = new ActionCommand(() => executed++);
        var action = new MenuItem { Header = "_Run", Command = command, InputGesture = new(Key.R, KeyModifiers.Control) };
        var file = new MenuItem { Header = "_File" }; file.Items.Add(action);
        var edit = new MenuItem { Header = "_Edit" }; edit.Items.Add(new MenuItem { Header = "Other" });
        var menu = new Menu(); menu.Items.Add(file); menu.Items.Add(edit);
        var window = new Window { Content = menu, Width = 1100, Height = 650 };
        window.Show(); Arrange(window, 1100, 650);
        Assert.True(file.Focus(NavigationMethod.Tab)); Assert.True(file.IsFocused);
        Press(file, Key.Right); Assert.True(edit.IsFocused);
        Press(edit, Key.Left); Assert.True(file.IsFocused);
        var popup = Assert.Single(file.GetVisualDescendants().OfType<Popup>());
        Assert.True(popup.IsLightDismissEnabled);
        Assert.Equal(PlacementMode.BottomEdgeAlignedLeft, popup.Placement);
        // Use the existing template's popup in the inert platform, without native popup creation.
        popup.ShouldUseOverlayLayer = true;
        Press(file, Key.Enter); Arrange(window, 1100, 650);
        Assert.True(file.IsSubMenuOpen); Assert.True(popup.IsOpen); Assert.True(action.IsFocused);
        Assert.Same(command, action.Command); Assert.Equal(new KeyGesture(Key.R, KeyModifiers.Control), action.InputGesture);
        Press(action, Key.Escape);
        Assert.False(file.IsSubMenuOpen); Assert.False(popup.IsOpen); Assert.True(file.IsFocused); Assert.Equal(0, executed);
        Press(file, Key.Down); Arrange(window, 1100, 650);
        Assert.True(action.IsFocused); Press(action, Key.Enter);
        Assert.Equal(1, executed); Assert.False(file.IsSubMenuOpen); Assert.False(popup.IsOpen);
        window.Close();
    }

    [Fact]
    public void ProjectSetupTabsInCardOrderAndMainMenuUsesAltAndF10()
    {
        using var environment = new DesktopControlEnvironment();
        environment.EnableRoutedKeyboardNavigation();
        var files = new Files();
        var window = new MainWindow(TestHosts.Initialized(new DesktopApplicationHost(new(files, new(files, "settings"), UserSettings.Default))));
        DesktopControlEnvironment.ShowInitialized(window);
        var main = Assert.IsType<MainWindowViewModel>(window.DataContext);
        main.NewCommand.Execute(null);
        Arrange(window, 1250, 800);

        var menu = window.FindControl<Menu>("MainMenu")!;
        var file = Assert.IsType<MenuItem>(menu.Items[0]);
        var setup = Assert.Single(window.GetVisualDescendants().OfType<ProjectSetupView>());
        var card = setup.FirstCard();
        var title = setup.Part<TextBox>("CalculationTitleInput");
        var cardOrder = new Control[]
        {
            setup.Part<TextBox>("ReferenceInput"), card.Part<Button>("SegmentMenuButton"),
            card.Part<TextBox>("SetupLengthInput"), card.Part<Button>("SectionCardButton"),
            card.Part<Button>("MaterialCardButton"), setup.Part<Button>("AddSegmentButton")
        };

        Assert.True(title.Focus());
        var visited = new List<Control> { title };
        while (true)
        {
            SendKey(Key.Tab);
            var focused = Assert.IsAssignableFrom<Control>(window.FocusManager.GetFocusedElement());
            Assert.False(menu.IsKeyboardFocusWithin);
            if (ReferenceEquals(focused, title)) break;
            Assert.DoesNotContain(focused, visited);
            visited.Add(focused);
        }
        Assert.Equal(cardOrder, visited.Skip(visited.IndexOf(cardOrder[0])).Take(cardOrder.Length));
        SendKey(Key.Tab, KeyModifiers.Shift);
        Assert.True(visited[^1].IsFocused);
        SendKey(Key.Tab);
        Assert.True(title.IsFocused);

        SendKey(Key.LeftAlt, KeyModifiers.Alt);
        SendKey(Key.LeftAlt, routedEvent: InputElement.KeyUpEvent);
        Assert.True(menu.IsOpen);
        Assert.True(file.IsFocused);
        SendKey(Key.LeftAlt, KeyModifiers.Alt);
        SendKey(Key.LeftAlt, routedEvent: InputElement.KeyUpEvent);
        Assert.False(menu.IsOpen);
        Assert.True(title.IsFocused);

        SendKey(Key.F10);
        Assert.True(menu.IsOpen);
        Assert.True(file.IsFocused);
        menu.Close();
        window.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.False(window.IsVisible);

        void SendKey(Key key, KeyModifiers modifiers = KeyModifiers.None, RoutedEvent? routedEvent = null) =>
            ((Control)window.FocusManager.GetFocusedElement()!).RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = routedEvent ?? InputElement.KeyDownEvent,
                Key = key,
                KeyModifiers = modifiers
            });
    }

    [Fact]
    public void AboutLinksShareUnderliningAndAccentStatesWithVisibleKeyboardFocus()
    {
        using var environment = new DesktopControlEnvironment();
        var window = new AboutWindow { WindowStartupLocation = WindowStartupLocation.Manual };
        window.Show(); Arrange(window, 380, 400);

        var app = Application.Current!;
        var links = ((Control)window.Content!).GetVisualDescendants().OfType<Button>().ToArray();
        Assert.Equal(new[] { ApplicationMetadata.LicenseShortName, Strings.Privacy, Strings.GitHubLink },
            links.Select(link => Assert.IsType<TextBlock>(link.Content).Text));
        Assert.Equal(new object[] { ApplicationMetadata.License, Strings.AboutPrivacyToolTip, Strings.AboutGitHubToolTip },
            links.Select(ToolTip.GetTip));
        Assert.Equal(ApplicationMetadata.RepositoryUrl, links[2].Tag);
        foreach (var link in links)
        {
            var caption = Assert.IsType<TextBlock>(link.Content);
            Assert.Equal(TextDecorationLocation.Underline, Assert.Single(caption.TextDecorations!).Location);
            Assert.Equal(app.FindResource("Accent"), caption.Foreground);
            var states = (IPseudoClasses)link.Classes;
            states.Set(":pointerover", true);
            Assert.Equal(app.FindResource("AccentHover"), caption.Foreground);
            states.Set(":pressed", true);
            Assert.Equal(app.FindResource("AccentPressed"), caption.Foreground);
            states.Set(":pressed", false);
            states.Set(":pointerover", false);

            Assert.True(link.Focus(NavigationMethod.Tab));
            var ring = Assert.Single(link.GetVisualDescendants().OfType<Border>(), border => border.Name == "FocusRing");
            Assert.True(ring.IsVisible);
            Assert.Equal(app.FindResource("Accent"), ring.BorderBrush);
        }
        window.Close();
    }

    [Fact]
    public void MenuLessDialogsDoNotAcquireAMenuRow()
    {
        using var environment = new DesktopControlEnvironment();
        foreach (var dialog in new Window[] { new AboutWindow(), new SettingsWindow(), new DocumentWindow("Document", "# Heading") })
        {
            Arrange(dialog, double.IsNaN(dialog.Width) ? 1100 : dialog.Width, double.IsNaN(dialog.Height) ? 650 : dialog.Height);
            Assert.Empty(dialog.GetVisualDescendants().OfType<Menu>());
            dialog.Close();
        }
    }

    private static void AssertChrome(Menu menu, double width)
    {
        foreach (var property in new AvaloniaProperty[] { Menu.BackgroundProperty, Menu.BorderThicknessProperty, Menu.BorderBrushProperty, Menu.HeightProperty })
            Assert.Equal(BindingPriority.Style, menu.GetDiagnostic(property).Priority);
        Assert.Equal(Color.Parse("#F8FAFC"), Assert.IsAssignableFrom<ISolidColorBrush>(menu.Background).Color);
        Assert.Equal(Color.Parse("#D9DFE7"), Assert.IsAssignableFrom<ISolidColorBrush>(menu.BorderBrush).Color);
        Assert.Equal(new Thickness(0, 0, 0, 1), menu.BorderThickness); Assert.Equal(default, menu.Padding);
        Assert.Equal(33, menu.Bounds.Height); Assert.Equal(width, menu.Bounds.Width);
        // Verify the Fluent template actually renders the separator, rather than only storing a setter.
        var border = Assert.Single(menu.GetVisualDescendants().OfType<Border>(), b => ReferenceEquals(b.TemplatedParent, menu));
        Assert.Same(menu.Background, border.Background); Assert.Same(menu.BorderBrush, border.BorderBrush);
        Assert.Equal(menu.BorderThickness, border.BorderThickness); Assert.Equal(menu.Bounds.Size, border.Bounds.Size);
        var presenter = Assert.IsType<ItemsPresenter>(border.Child);
        Assert.Equal(32, presenter.Bounds.Height); Assert.Equal(KeyboardNavigationMode.Continue, KeyboardNavigation.GetTabNavigation(presenter));
        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            Assert.Equal(new Thickness(12, 4), item.Padding); Assert.Equal(32, item.Bounds.Height);
            Assert.True(item.Focusable); Assert.True(item.IsTopLevel);
        }
    }

    private static void Press(Control control, Key key) =>
        control.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });

    private static void Arrange(Window window, double width, double height)
    {
        window.ApplyTemplate();
        for (int pass = 0; pass < 3; pass++)
        {
            window.Measure(new Size(width, height)); window.Arrange(new Rect(0, 0, width, height));
            var content = (Control)window.Content!;
            content.InvalidateMeasure(); content.Measure(new Size(width, height));
            content.InvalidateArrange(); content.Arrange(new Rect(0, 0, width, height));
            Dispatcher.UIThread.RunJobs();
        }
    }
}
