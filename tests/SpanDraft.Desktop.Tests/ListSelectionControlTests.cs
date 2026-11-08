using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace SpanDraft.Tests;

[Collection("Avalonia")]
public sealed class ListSelectionControlTests
{

    [Fact]
    public void ListSelectionIsOnlyASubtleSurfaceAndDiffersFromHover()
    {
        using var environment = new DesktopControlEnvironment();
        var list = new ListBox { Classes = { "materialEntries" }, ItemsSource = new[] { "Steel", "Aluminium" } };
        var window = new Window { Content = list, Width = 400, Height = 180 };
        window.Show(); Dispatcher.UIThread.RunJobs();
        var row = Assert.IsType<ListBoxItem>(list.ContainerFromIndex(0));
        var content = Assert.Single(row.GetVisualDescendants().OfType<ContentPresenter>());
        var origin = content.Bounds;
        list.SelectedIndex = 0; Dispatcher.UIThread.RunJobs();
        Assert.Empty(row.GetVisualDescendants().OfType<Border>());
        Assert.Equal(origin, content.Bounds);
        Assert.Equal(new CornerRadius(4), row.CornerRadius);
        Assert.Same(Application.Current!.FindResource("AccentSubtle"), content.Background);
        ((IPseudoClasses)row.Classes).Set(":pointerover", true); Dispatcher.UIThread.RunJobs();
        Assert.Same(Application.Current!.FindResource("AccentSubtle"), content.Background);
        Assert.Equal(origin, content.Bounds);
        Assert.Empty(row.GetVisualDescendants().OfType<Border>());
        list.SelectedIndex = 1; Dispatcher.UIThread.RunJobs();
        Assert.NotSame(Application.Current!.FindResource("AccentSubtle"), content.Background);
        Assert.Same(Application.Current!.FindResource("SurfaceHover"), content.Background);
        window.Close();
    }
}
