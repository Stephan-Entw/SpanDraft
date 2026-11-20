using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Xunit;

namespace SpanDraft.Tests;

[Collection("Avalonia")]
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

        var app = Application.Current!;
        var normalBrush = app.FindResource("TextMuted");
        Assert.Same(normalBrush, grip.Background);
        ((IPseudoClasses)thumb.Classes).Set(":pointerover", true);
        Assert.Same(app.FindResource("TextSecondary"), grip.Background);
        ((IPseudoClasses)thumb.Classes).Set(":pressed", true);
        Assert.Same(app.FindResource("TextPrimary"), grip.Background);
        ((IPseudoClasses)thumb.Classes).Set(":pressed", false);
        ((IPseudoClasses)thumb.Classes).Set(":pointerover", false);
        Assert.Same(normalBrush, grip.Background);
        thumb.IsEnabled = false;
        Assert.InRange(grip.Opacity, 0.1, 0.9);
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
