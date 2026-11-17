using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SpanDraft.Desktop;
using SpanDraft.Desktop.Controls;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.ViewModels;
using SpanDraft.Desktop.Views;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

[Collection("Avalonia")]
public sealed class DesktopAdaptiveToolbarTests
{
    private static void Layout(Window window, int width, int height)
    {
        window.ApplyTemplate(); window.Measure(new(width, height)); window.Arrange(new Rect(0, 0, width, height));
        var content = (Control)window.Content!;
        foreach (var child in content.GetVisualDescendants().OfType<Control>())
        { child.InvalidateMeasure(); child.InvalidateArrange(); }
        content.InvalidateMeasure(); content.Measure(new(width, height));
        content.InvalidateArrange(); content.Arrange(new Rect(0, 0, width, height));
        Dispatcher.UIThread.RunJobs();
    }

    private static (MainWindow Window, EditorView View, AdaptiveToolStrip Tools) Open(int width)
    {
        var main = new MainWindowViewModel(files: new Files());
        main.NewCommand.Execute(null); CompleteSetup(main); main.Setup.ApplyCommand.Execute(null);
        var window = new MainWindow { DataContext = main };
        Layout(window, width, 650);
        var view = Assert.Single(window.GetVisualDescendants().OfType<EditorView>());
        var grid = Assert.IsType<Grid>(view.FindControl<Border>("EditorToolbar")!.Child);
        return (window, view, Assert.IsType<AdaptiveToolStrip>(grid.Children[0]));
    }

    private static TextBlock[] Captions(Control tools) =>
        tools.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("toolbarCaption")).ToArray();

    private static TextBlock[] GroupLabels(Control tools) =>
        tools.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("toolbarGroupLabel")).ToArray();

    [Theory]
    [InlineData("de-DE")]
    [InlineData("en-US")]
    public void TheMinimumWidthShowsIconsWithGroupLabelsAndWideWindowsShowCaptions(string culture)
    {
        using var environment = new DesktopControlEnvironment(); using var scope = new UiCultureScope(culture);
        var (narrow, narrowView, narrowTools) = Open(1100);
        Assert.True(narrowTools.IsCompact);
        Assert.Equal(10, Captions(narrowTools).Length);
        Assert.All(Captions(narrowTools), c => Assert.Equal(0, c.DesiredSize.Width));
        Assert.Equal(new[] { Strings.Loads, Strings.ToolbarSupportsAndHinges }, GroupLabels(narrowTools).Select(l => l.Text));
        Assert.All(GroupLabels(narrowTools), l => Assert.True(l.IsVisible && l.Bounds.Width > 0));
        var compactHeight = narrowTools.Bounds.Height;
        // The tools without a caption of their own stay named and explained for assistive technology and the pointer.
        foreach (var button in narrowTools.Children.OfType<ToggleButton>().Where(b => b.Classes.Contains("captioned")))
        {
            Assert.False(string.IsNullOrEmpty(Avalonia.Automation.AutomationProperties.GetName(button)));
            Assert.Equal(Avalonia.Automation.AutomationProperties.GetName(button), ToolTip.GetTip(button));
        }
        // The probe and the report group keep their captions in every mode.
        Assert.True(narrowView.FindControl<ToggleButton>("ResultProbeToolButton")!.GetVisualDescendants().OfType<TextBlock>().Single().IsVisible);
        Assert.True(narrowView.FindControl<Button>("ReportToolbarButton")!.GetVisualDescendants().OfType<TextBlock>().Single().IsVisible);
        Assert.True(narrowView.FindControl<Button>("ProjectPropertiesToolbarButton")!.GetVisualDescendants().OfType<TextBlock>().Single().IsVisible);
        // All icon-only tools are equally wide.
        var widths = narrowTools.Children.OfType<ToggleButton>().Where(b => b.Classes.Contains("captioned")).Select(b => b.Bounds.Width).Distinct().ToArray();
        Assert.Single(widths);
        narrow.Close();

        var (wide, _, wideTools) = Open(1920);
        Assert.False(wideTools.IsCompact);
        Assert.All(Captions(wideTools), c => Assert.True(c.IsVisible && c.Bounds.Width > 0));
        Assert.All(GroupLabels(wideTools), l => Assert.False(l.IsVisible));
        Assert.Equal(compactHeight, wideTools.Bounds.Height);
        wide.Close();
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("en-US")]
    public void TheDefaultWindowWidthShowsTheCompactToolbarWithoutClippingAndTheCaptionsReturnWithMoreRoom(string culture)
    {
        using var environment = new DesktopControlEnvironment(); using var scope = new UiCultureScope(culture);
        int defaultWidth = (int)new MainWindow().Width;
        var (window, view, tools) = Open(defaultWidth);
        var grid = Assert.IsType<Grid>(view.FindControl<Border>("EditorToolbar")!.Child);
        // With two spring tools the captioned row no longer fits the default width: the icons stay, the group labels name them.
        Assert.True(tools.IsCompact);
        Assert.All(Captions(tools), c => Assert.False(c.IsVisible));
        Assert.All(GroupLabels(tools), l => Assert.True(l.IsVisible));
        Assert.True(tools.DesiredSize.Width <= tools.Bounds.Width + 0.01);
        Assert.True(tools.Bounds.Width + grid.ColumnSpacing + grid.Children[1].Bounds.Width <= grid.Bounds.Width + 0.01);
        window.Close();
        var (wide, _, wideTools) = Open(defaultWidth + 300);
        Assert.False(wideTools.IsCompact);
        wide.Close();
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("en-US")]
    public void TheToolbarSwitchesOnceAndAtTheSamePointWhileGrowingAndNeverClipsOrChangesItsHeight(string culture)
    {
        using var environment = new DesktopControlEnvironment(); using var scope = new UiCultureScope(culture);
        bool? previous = null; int switches = 0; double? height = null; int firstWide = 0;
        for (int width = 1100; width <= 2000; width += 25)
        {
            var (window, view, tools) = Open(width);
            var bar = view.FindControl<Border>("EditorToolbar")!;
            var grid = Assert.IsType<Grid>(bar.Child);
            Assert.True(tools.DesiredSize.Width <= tools.Bounds.Width + 0.01, $"The tools need {tools.DesiredSize.Width} of {tools.Bounds.Width} at {width}.");
            var right = grid.Children[1];
            Assert.True(tools.Bounds.Width + grid.ColumnSpacing + right.Bounds.Width <= grid.Bounds.Width + 0.01);
            var visible = tools.Children.Where(c => c.IsVisible).ToArray();
            for (int i = 1; i < visible.Length; i++) Assert.True(visible[i].Bounds.Left >= visible[i - 1].Bounds.Right - 0.01, $"Overlap at {width}.");
            Assert.True(visible[^1].Bounds.Right <= tools.Bounds.Width + 0.01);
            height ??= bar.Bounds.Height; Assert.Equal(height, bar.Bounds.Height);
            if (previous is bool was && was != tools.IsCompact) { switches++; firstWide = width; }
            previous = tools.IsCompact;
            window.Close();
        }
        Assert.Equal(1, switches);
        Assert.False(previous);
        Assert.InRange(firstWide, 1100, 2000);
    }
}
