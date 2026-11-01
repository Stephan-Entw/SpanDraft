using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using ColorTextBlock.Avalonia;
using Markdown.Avalonia;
using SpanDraft.Desktop;
using System.Globalization;
using Xunit;

namespace SpanDraft.Tests;

[Collection("Schematic text")]
public sealed class DesktopScrollBarTests
{
    [Theory]
    [InlineData(Orientation.Vertical)]
    [InlineData(Orientation.Horizontal)]
    public void ApplicationDefaultsKeepScrollBarsExpandedWithCompactChrome(Orientation orientation)
    {
        using var environment = new DesktopControlEnvironment();
        var viewer = new ScrollViewer
        {
            HorizontalScrollBarVisibility = orientation == Orientation.Horizontal ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = orientation == Orientation.Vertical ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled,
            Content = new Border { Width = 1600, Height = 1800 }
        };
        Layout(new Window { Content = viewer }, new Size(640, 560));

        Assert.False(viewer.AllowAutoHide);
        var bar = Assert.Single(viewer.GetVisualDescendants().OfType<ScrollBar>(), b => b.IsVisible);
        Assert.Equal(orientation, bar.Orientation);
        Assert.False(bar.AllowAutoHide);
        Assert.True(bar.IsExpanded);
        var thickness = orientation == Orientation.Vertical ? bar.Bounds.Width : bar.Bounds.Height;
        Assert.InRange(thickness, 12, 20);

        var thumb = Assert.Single(bar.GetVisualDescendants().OfType<Thumb>());
        var grip = Assert.Single(thumb.GetVisualDescendants().OfType<Border>());
        Assert.True(grip.CornerRadius.TopLeft > 0);
        var gripThickness = orientation == Orientation.Vertical ? grip.Bounds.Width : grip.Bounds.Height;
        Assert.InRange(gripThickness, 4, thickness - 1);
        Assert.All(bar.GetVisualDescendants().OfType<RepeatButton>(), button =>
            Assert.DoesNotContain(button.GetVisualDescendants(), control => control.Name == "DraftContent"));

        var initial = bar.Value;
        ScrollButton(bar, "PART_LineDownButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(bar.Value > initial);
        ScrollButton(bar, "PART_LineUpButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(initial, bar.Value);
        ScrollButton(bar, "PART_PageDownButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(bar.Value > initial);
        ScrollButton(bar, "PART_PageUpButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(initial, bar.Value);

        var normalBrush = grip.Background;
        ((IPseudoClasses)thumb.Classes).Set(":pointerover", true);
        Assert.NotEqual(normalBrush, grip.Background);
        var hoverBrush = grip.Background;
        ((IPseudoClasses)thumb.Classes).Set(":pressed", true);
        Assert.NotEqual(hoverBrush, grip.Background);
        ((IPseudoClasses)thumb.Classes).Set(":pressed", false);
        ((IPseudoClasses)thumb.Classes).Set(":pointerover", false);
        Assert.Equal(normalBrush, grip.Background);
        thumb.IsEnabled = false;
        Assert.InRange(grip.Opacity, 0.1, 0.9);
    }

    [Fact]
    public void LicenseScrollBarFillsRightEdgeWhileTextRetainsItsInset()
    {
        using var environment = new DesktopControlEnvironment();
        var window = new LicenseWindow();
        var size = new Size(window.Width, window.Height);
        Layout(window, size);
        var viewer = Assert.IsType<ScrollViewer>(window.Content);
        var text = Assert.IsType<SelectableTextBlock>(viewer.Content);
        var bar = Assert.Single(viewer.GetVisualDescendants().OfType<ScrollBar>(), b => b.IsVisible);
        Assert.Equal(default, viewer.Margin);
        Assert.Equal(size.Width, viewer.Bounds.Width);
        Assert.Equal(size.Height, viewer.Bounds.Height);
        Assert.Equal(viewer.Bounds.Width, bar.Bounds.Right, 6);
        Assert.True(text.Margin.Left > 0 && text.Margin.Top > 0);
    }

    [Theory]
    [InlineData("de-DE", "Datenschutzerklärung für SpanDraft", "Anbieter und Kontakt", "Projekt- und Berechnungsdaten")]
    [InlineData("en-US", "Privacy Policy for SpanDraft", "Provider and Contact", "project and calculation data")]
    public void PrivacyRendersLocalizedMarkdownInAnInsetScrollableViewer(string culture, string title, string section, string listItem)
    {
        var previousCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            using var environment = new DesktopControlEnvironment();
            var window = new PrivacyWindow();
            Layout(window, new Size(window.Width, window.Height));
            var markdown = Assert.IsType<MarkdownScrollViewer>(window.Content);
            Assert.True(markdown.SelectionEnabled);
            Assert.True(markdown.Padding.Left > 0 && markdown.Padding.Top > 0);
            var viewer = Assert.Single(markdown.GetVisualDescendants().OfType<ScrollViewer>());
            Assert.Equal(ScrollBarVisibility.Disabled, viewer.HorizontalScrollBarVisibility);
            Assert.True(viewer.Extent.Height > viewer.Viewport.Height);
            Assert.True(viewer.Extent.Width <= viewer.Viewport.Width + 0.5);
            var blocks = markdown.GetVisualDescendants().OfType<CTextBlock>().ToArray();
            var heading = Assert.Single(blocks, block => block.Classes.Contains("Heading1") && block.Text == title);
            var subheading = Assert.Single(blocks, block => block.Classes.Contains("Heading2") && block.Text.Contains(section));
            var item = Assert.Single(blocks, block => block.Text.Contains(listItem));
            Assert.True(heading.FontSize > subheading.FontSize && subheading.FontSize > item.FontSize);
            Assert.Equal(FontWeight.SemiBold, heading.FontWeight);
            Assert.Equal(FontWeight.SemiBold, subheading.FontWeight);
            Assert.Equal(TextWrapping.Wrap, item.TextWrapping);
            Assert.Contains(markdown.GetVisualDescendants().OfType<Grid>(), grid => grid.Classes.Contains("List"));
        }
        finally { CultureInfo.CurrentUICulture = previousCulture; }
    }

    private static RepeatButton ScrollButton(ScrollBar bar, string name) =>
        Assert.Single(bar.GetVisualDescendants().OfType<RepeatButton>(), b => b.Name == name);

    private static void Layout(Window window, Size size)
    {
        window.ApplyTemplate();
        window.Measure(size);
        window.Arrange(new Rect(size));
        var content = (Control)window.Content!;
        content.InvalidateMeasure();
        content.Measure(size);
        content.InvalidateArrange();
        content.Arrange(new Rect(size));
    }
}
