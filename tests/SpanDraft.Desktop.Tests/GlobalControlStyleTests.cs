using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Diagnostics;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace SpanDraft.Tests;

/// <summary>Retained Fluent controls and popups follow the application tokens instead of Fluent or platform defaults.</summary>
[Collection("Avalonia")]
public sealed class GlobalControlStyleTests
{
    private static object Token(string key) => Application.Current!.FindResource(key)!;
    private static Color TokenColor(string key) => ((ISolidColorBrush)Token(key)).Color;
    private static Color ColorOf(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    private static Window Open(Control content)
    {
        var window = new Window { Width = 600, Height = 400, Content = content };
        window.Show(); Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static void AssertAppText(TextBlock text)
    {
        Assert.Equal(Token("AppFont"), text.FontFamily);
        Assert.Equal((double)Token("BodySize"), text.FontSize);
        Assert.Equal(TokenColor("TextPrimary"), ColorOf(text.Foreground));
    }

    [Fact]
    public void PopupContentInheritsApplicationTypographyLikeAWindow()
    {
        using var environment = new DesktopControlEnvironment();
        // Editor flyouts are bare popups: their content inherits from the popup host, not from the window.
        var text = new TextBlock { Text = "N/mm" };
        var popup = new Popup { Child = new Border { Child = text } };
        var window = Open(new Panel { Children = { popup } });
        popup.IsOpen = true; Dispatcher.UIThread.RunJobs();

        var host = Assert.Single(text.GetVisualAncestors().OfType<ContentControl>(), a => a is OverlayPopupHost or PopupRoot);
        Assert.Equal(window.FontFamily, host.FontFamily);
        Assert.Equal(window.FontSize, host.FontSize);
        Assert.Equal(window.FontFeatures, host.FontFeatures);
        AssertAppText(text);
        window.Close();
    }

    [Fact]
    public void DropdownEntriesAreCenteredOnTheApplicationSurfaceWithVisibleHover()
    {
        using var environment = new DesktopControlEnvironment();
        var combo = new ComboBox { ItemsSource = new[] { "mm", "ft", "in" }, SelectedIndex = 0 };
        var window = Open(combo);
        combo.IsDropDownOpen = true; Dispatcher.UIThread.RunJobs();

        var item = Assert.IsType<ComboBoxItem>(combo.ContainerFromIndex(1));
        var popupBorder = Assert.Single(item.GetVisualAncestors().OfType<Border>(), b => b.Name == "PopupBorder");
        Assert.Same(Token("Surface"), popupBorder.Background);
        var presenter = Assert.Single(item.GetVisualDescendants().OfType<ContentPresenter>(), p => p.Name == "PART_ContentPresenter");
        var text = Assert.Single(item.GetVisualDescendants().OfType<TextBlock>());
        AssertAppText(text);

        // The text sits in the middle of the entry's content area, not at its top.
        var top = text.TranslatePoint(new Point(), presenter)!.Value.Y - presenter.Padding.Top;
        var bottom = presenter.Bounds.Height - presenter.Padding.Bottom - text.TranslatePoint(new Point(), presenter)!.Value.Y - text.Bounds.Height;
        Assert.True(top > 0, $"Entry text is not vertically centered: {top} above, {bottom} below.");
        Assert.InRange(Math.Abs(top - bottom), 0, 1);

        ((IPseudoClasses)item.Classes).Set(":pointerover", true); Dispatcher.UIThread.RunJobs();
        Assert.Same(Token("SurfaceHover"), presenter.Background);
        Assert.NotEqual(ColorOf(popupBorder.Background), ColorOf(presenter.Background));
        window.Close();
    }

    [Fact]
    public void HoverAndSelectionFillsAreSquareInMenusDropdownsAndLists()
    {
        using var environment = new DesktopControlEnvironment();
        var menuItem = new MenuItem { Header = "Datei" };
        var combo = new ComboBox { ItemsSource = new[] { "mm", "ft" }, SelectedIndex = 0 };
        var entries = new ListBox { Classes = { "materialEntries" }, ItemsSource = new[] { "S235JR" }, SelectedIndex = 0 };
        var filters = new ListBox { Classes = { "materialFilters" }, ItemsSource = new[] { "Stahl" }, SelectedIndex = 0 };
        var window = Open(new StackPanel { Children = { new Menu { Items = { menuItem } }, combo, entries, filters } });
        combo.IsDropDownOpen = true; Dispatcher.UIThread.RunJobs();

        // Fills meet header lines, pane edges and neighbouring rows; rounded corners would leave light wedges there.
        var menuFill = Assert.Single(menuItem.GetVisualDescendants().OfType<Border>(), b => b.Name == "PART_LayoutRoot");
        Assert.Equal(default, menuFill.CornerRadius);
        foreach (var item in new Control[] { combo.ContainerFromIndex(1)!, entries.ContainerFromIndex(0)!, filters.ContainerFromIndex(0)! })
        {
            var fill = Assert.Single(item.GetVisualDescendants().OfType<ContentPresenter>(), p => p.Name == "PART_ContentPresenter");
            Assert.Equal(default, fill.CornerRadius);
        }
        window.Close();
    }

    [Fact]
    public void ToolTipsUseApplicationTypographySurfaceAndBalancedPadding()
    {
        using var environment = new DesktopControlEnvironment();
        var button = new Button { Content = "F" };
        ToolTip.SetTip(button, "Punktkraft");
        var window = Open(button);
        ToolTip.SetIsOpen(button, true); Dispatcher.UIThread.RunJobs();

        var tip = Assert.IsType<ToolTip>(button.GetValue(ToolTipDiagnostics.ToolTipProperty));
        var text = Assert.Single(tip.GetVisualDescendants().OfType<TextBlock>());
        Assert.Equal(Token("AppFont"), text.FontFamily);
        Assert.Equal((double)Token("SecondarySize"), text.FontSize);
        Assert.Equal(TokenColor("TextPrimary"), ColorOf(text.Foreground));
        Assert.Same(Token("Surface"), tip.Background);
        Assert.Same(Token("BorderStrong"), tip.BorderBrush);
        Assert.Equal(tip.Padding.Top, tip.Padding.Bottom);
        ToolTip.SetIsOpen(button, false);
        window.Close();
    }

    [Fact]
    public void CheckBoxAndRadioButtonUseTheApplicationAccentAndInputFrames()
    {
        using var environment = new DesktopControlEnvironment();
        var on = new CheckBox { Content = "on", IsChecked = true };
        var off = new CheckBox { Content = "off" };
        var disabled = new CheckBox { Content = "disabled", IsEnabled = false };
        var radio = new RadioButton { Content = "radio", IsChecked = true };
        var radioOff = new RadioButton { Content = "radio off", GroupName = "other" };
        var window = Open(new StackPanel { Children = { on, off, disabled, radio, radioOff } });

        static Border Box(CheckBox box) => Assert.Single(box.GetVisualDescendants().OfType<Border>(), b => b.Name == "NormalRectangle");
        static Ellipse Ellipse(RadioButton button, string name) => Assert.Single(button.GetVisualDescendants().OfType<Ellipse>(), e => e.Name == name);
        // The platform accent of the test environment differs from the application accent.
        Assert.Equal(TokenColor("Accent"), ColorOf(Box(on).Background));
        Assert.Equal(TokenColor("Accent"), ColorOf(Ellipse(radio, "CheckOuterEllipse").Fill));
        Assert.Same(Token("InputBorder"), Box(off).BorderBrush);
        Assert.Same(Token("InputBorder"), Ellipse(radioOff, "OuterEllipse").Stroke);
        Assert.Equal(TokenColor("TextPrimary"), ColorOf(Assert.Single(on.GetVisualDescendants().OfType<TextBlock>()).Foreground));
        Assert.Same(Token("TextDisabled"), Assert.Single(disabled.GetVisualDescendants().OfType<TextBlock>()).Foreground);

        ((IPseudoClasses)on.Classes).Set(":pointerover", true); Dispatcher.UIThread.RunJobs();
        Assert.Equal(TokenColor("AccentHover"), ColorOf(Box(on).Background));
        window.Close();
    }

    [Fact]
    public void PlaceholderIsReadableAndDisabledStatesDifferFromQuietText()
    {
        using var environment = new DesktopControlEnvironment();
        var search = new TextBox { PlaceholderText = "Werkstoffe durchsuchen" };
        var disabledSearch = new TextBox { PlaceholderText = "Werkstoffe durchsuchen", IsEnabled = false };
        var delete = new Button { Content = "Löschen", Classes = { "ghost", "dangerGhost" } };
        var disabledDelete = new Button { Content = "Löschen", Classes = { "ghost", "dangerGhost" }, IsEnabled = false };
        var window = Open(new StackPanel { Children = { search, disabledSearch, delete, disabledDelete } });

        static TextBlock Placeholder(TextBox box) => Assert.Single(box.GetVisualDescendants().OfType<TextBlock>(), t => t.Name == "PART_Placeholder");
        Assert.Same(Token("TextMuted"), Placeholder(search).Foreground);
        Assert.Equal(1, Placeholder(search).Opacity);
        Assert.Same(Token("TextDisabled"), Placeholder(disabledSearch).Foreground);
        Assert.Same(Token("Error"), delete.Foreground);
        Assert.Same(Token("TextDisabled"), disabledDelete.Foreground);
        // Unavailable must not look like quiet-but-available secondary text.
        Assert.True(Contrast(TokenColor("TextDisabled"), TokenColor("TextSecondary")) >= 1.8);
        window.Close();
    }

    private static double Contrast(Color a, Color b)
    {
        static double Luminance(Color c)
        {
            static double Channel(byte v) { double x = v / 255.0; return x <= 0.03928 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4); }
            return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
        }
        double l1 = Luminance(a), l2 = Luminance(b);
        return (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
    }
}
