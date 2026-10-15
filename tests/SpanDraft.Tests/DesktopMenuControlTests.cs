using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Data;
using Avalonia.Diagnostics;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SpanDraft.Desktop;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.ViewModels;
using SpanDraft.Desktop.Views;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

[Collection("Schematic text")]
public sealed class DesktopMenuControlTests
{
    [Theory]
    [InlineData("de-DE", 1100, 650)] [InlineData("en-US", 1100, 650)]
    [InlineData("de-DE", 1250, 800)] [InlineData("en-US", 1250, 800)]
    public void ProjectSectionAndFutureMenusShareRenderedChromeWithoutLocalDecoration(string culture, double width, double height)
    {
        using var environment = new DesktopControlEnvironment();
        using var scope = new UiCultureScope(culture);
        var main = new MainWindowViewModel();
        // Transplant the production shell to avoid starting native windows and local recovery I/O.
        var shell = new MainWindow(); var content = shell.Content; shell.Content = null;
        var window = new Window { DataContext = main, Content = content, Width = width, Height = height };
        foreach (var template in shell.DataTemplates) window.DataTemplates.Add(template);
        window.Show(); Arrange(window, width, height);
        var project = shell.FindControl<Menu>("ProjectMenu")!;
        Assert.True(project.IsEffectivelyVisible); AssertChrome(project, width);
        Assert.Same(main.NewCommand, shell.FindControl<MenuItem>("NewMenuItem")!.Command);
        Assert.Same(main.OpenCommand, shell.FindControl<MenuItem>("OpenMenuItem")!.Command);
        Assert.Same(main.SaveCommand, shell.FindControl<MenuItem>("SaveMenuItem")!.Command);
        Assert.Same(main.UndoCommand, shell.FindControl<MenuItem>("UndoMenuItem")!.Command);
        var primary = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        Assert.Equal(new KeyGesture(Key.N, primary), shell.FindControl<MenuItem>("NewMenuItem")!.InputGesture);
        Assert.Equal(new KeyGesture(Key.S, primary | KeyModifiers.Shift), shell.FindControl<MenuItem>("SaveAsMenuItem")!.InputGesture);
        Assert.Equal(new KeyGesture(Key.Z, primary), shell.FindControl<MenuItem>("UndoMenuItem")!.InputGesture);
        var projectTheme = project.ItemContainerTheme;

        main.Setup.ShowSections(); Arrange(window, width, height);
        Assert.False(project.IsEffectivelyVisible);
        var definition = Assert.Single(window.GetVisualDescendants().OfType<SectionDefinitionView>());
        var section = definition.FindControl<Menu>("SectionMenu")!;
        Assert.True(section.IsEffectivelyVisible); Assert.IsType<Grid>(section.Parent);
        AssertChrome(section, width); Assert.Same(projectTheme, section.ItemContainerTheme);
        Assert.Same(main.Setup.Definition!.SaveLibraryCommand, definition.FindControl<MenuItem>("SectionSaveMenuItem")!.Command);
        Assert.Same(main.Setup.Definition.ChangeFormCommand, definition.FindControl<MenuItem>("SectionChangeFormMenuItem")!.Command);

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
    public void MenuLessDialogsDoNotAcquireAMenuRow()
    {
        using var environment = new DesktopControlEnvironment();
        foreach (var dialog in new Window[] { new AboutWindow(), new SettingsWindow(), new LicenseWindow() })
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
