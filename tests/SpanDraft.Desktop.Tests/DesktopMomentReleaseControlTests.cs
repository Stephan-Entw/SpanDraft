using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop;
using SpanDraft.Desktop.Controls;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using SpanDraft.Desktop.Views;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

[Collection("Avalonia")]
public sealed class DesktopMomentReleaseControlTests
{
    private static Point Origin(Control control, Control ancestor) => control.TranslatePoint(new Point(), ancestor)!.Value;

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

    private static (EditorViewModel Editor, MomentReleaseFlyout Flyout, Window Window) Open()
    {
        var editor = new EditorViewModel(DesktopLayoutFixture.Document(), () => { });
        editor.ToggleMomentReleaseTool(); editor.HoverMomentReleasePlacement(Length.FromMeters(.5));
        Assert.True(editor.PlaceMomentRelease());
        var flyout = new MomentReleaseFlyout { DataContext = editor };
        var window = new Window { Content = flyout, Width = 360, Height = 320 };
        window.Show(); window.Measure(new Size(360, 320)); window.Arrange(new Rect(0, 0, 360, 320));
        Dispatcher.UIThread.RunJobs();
        return (editor, flyout, window);
    }

    [Theory]
    [InlineData("de-DE", 1100)]
    [InlineData("en-US", 1100)]
    [InlineData("de-DE", 1600)]
    public void TheToolbarKeepsEveryToolVisibleAtTheMinimumWidthWithTheHingeToolAfterTheSupports(string culture, int width)
    {
        using var environment = new DesktopControlEnvironment(); using var scope = new UiCultureScope(culture);
        var main = new MainWindowViewModel(files: new Files());
        main.NewCommand.Execute(null); CompleteSetup(main); main.Setup.ApplyCommand.Execute(null);
        var window = new MainWindow { DataContext = main };
        Layout(window, width, 650);
        var view = Assert.Single(window.GetVisualDescendants().OfType<EditorView>());
        var grid = Assert.IsType<Grid>(view.FindControl<Border>("EditorToolbar")!.Child);
        var tools = Assert.IsAssignableFrom<StackPanel>(grid.Children[0]);
        var hinge = view.FindControl<ToggleButton>("MomentReleaseToolButton")!;
        Assert.Same(main.Editor!.MomentReleaseToolCommand, hinge.Command);
        Assert.Equal(Strings.MomentRelease, Avalonia.Automation.AutomationProperties.GetName(hinge));
        Assert.Equal(Strings.MomentRelease, ToolTip.GetTip(hinge));
        Assert.Same(Application.Current!.FindResource("ToolbarIcon.MomentRelease"), Assert.Single(hinge.GetVisualDescendants().OfType<Image>()).Source);
        // The hinge tool closes the group of structural tools, directly before the separator of the probe.
        int index = tools.Children.IndexOf(hinge);
        Assert.IsType<Avalonia.Controls.Shapes.Rectangle>(tools.Children[index + 1]);
        Assert.Same(view.FindControl<ToggleButton>("ResultProbeToolButton"), tools.Children[index + 2]);
        Assert.Equal(10, tools.Children.OfType<ToggleButton>().Count(b => b.Command is not null));

        // No tool is clipped: the row needs no more width than the toolbar grants it, and the tools follow each other without overlap.
        Assert.True(tools.DesiredSize.Width <= tools.Bounds.Width + 0.01,
            $"The toolbar needs {tools.DesiredSize.Width} but only {tools.Bounds.Width} are available.");
        var bounds = tools.Children.Where(c => c.IsVisible).Select(c => new Rect(Origin(c, tools), c.Bounds.Size)).ToArray();
        for (int i = 1; i < bounds.Length; i++)
            Assert.True(bounds[i].Left >= bounds[i - 1].Right - 0.01, $"Tool {i} overlaps its predecessor.");
        Assert.True(bounds[^1].Right <= tools.Bounds.Width + 0.01);
        var rightGroup = grid.Children[1];
        Assert.True(Origin(tools, grid).X + tools.Bounds.Width + grid.ColumnSpacing <= Origin(rightGroup, grid).X + 0.01);
        window.Close();
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("en-US")]
    public void TheFlyoutHasNameAndPositionRowsAlignedLikeTheSupportFlyoutAndANoClippingFooter(string culture)
    {
        using var environment = new DesktopControlEnvironment(); using var scope = new UiCultureScope(culture);
        var (editor, flyout, window) = Open();
        var name = flyout.FindControl<TextBox>("NameInput")!;
        var position = flyout.FindControl<TextBox>("PositionInput")!;
        Assert.Equal(name.Bounds.Width, position.Bounds.Width, 3);
        Assert.Equal(Origin(name, flyout).X, Origin(position, flyout).X, 3);
        Assert.True(Origin(name, flyout).Y < Origin(position, flyout).Y);
        Assert.Equal(Origin(position, flyout).X + position.Bounds.Width + 8, Origin(flyout.FindControl<Control>("PositionUnit")!, flyout).X, 3);
        var footer = flyout.FindControl<Control>("Footer")!;
        Assert.True(Origin(position, flyout).Y + position.Bounds.Height <= Origin(footer, flyout).Y);
        Assert.True(Origin(footer, flyout).Y + footer.Bounds.Height <= flyout.Bounds.Height + 0.5);
        Assert.False(flyout.FindControl<Button>("DeleteAction")!.IsVisible);
        Assert.Equal("G1", name.Text);
        window.Close();
    }

    [Fact]
    public void AnInvalidPositionShowsItsMessageDisablesOkAndAValidOneCommitsViaTheButton()
    {
        using var environment = new DesktopControlEnvironment(); using var scope = new UiCultureScope("en-US");
        var (editor, flyout, window) = Open();
        var ok = flyout.FindControl<Button>("ConfirmAction")!;
        var position = flyout.FindControl<TextBox>("PositionInput")!;
        Assert.True(ok.IsEnabled);
        position.Text = "0"; Dispatcher.UIThread.RunJobs();
        Assert.Contains("error", position.Classes);
        Assert.False(ok.IsEnabled);
        Assert.Contains(flyout.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == Strings.MomentReleaseInsideBeam && t.IsVisible);
        position.Text = "750"; Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain("error", position.Classes);
        Assert.True(ok.IsEnabled);
        ok.Command!.Execute(null); Dispatcher.UIThread.RunJobs();
        Assert.Equal(Length.FromMeters(.75), Assert.Single(editor.Document.MomentReleases).Position);
        window.Close();
    }

    [Fact]
    public void AnExistingReleaseOffersDeleteAndEscapeCancelsWithoutCommit()
    {
        using var environment = new DesktopControlEnvironment(); using var scope = new UiCultureScope("en-US");
        var (editor, flyout, window) = Open();
        Assert.True(editor.ConfirmMomentRelease());
        Assert.True(editor.EditMomentRelease(editor.Document.MomentReleases[0].Id));
        Dispatcher.UIThread.RunJobs();
        Assert.True(flyout.FindControl<Button>("DeleteAction")!.IsVisible);
        flyout.FindControl<Button>("DeleteAction")!.Command!.Execute(null);
        Assert.Empty(editor.Document.MomentReleases);
        window.Close();
    }

    [Theory]
    [InlineData("de-DE", -50)]
    [InlineData("en-US", -50)]
    [InlineData("de-DE", -10000)]
    public void TheHingeQuantitiesKeepEveryRotationOnOneLineInsideItsColumnInTheFixedPanelWidth(string culture, double load)
    {
        using var environment = new DesktopControlEnvironment(); using var scope = new UiCultureScope(culture);
        var document = new EditorDocument(Length.FromMeters(8), ProjectTemplates.Material, ProjectTemplates.Section,
            new EditorSupport[] { new(Guid.NewGuid(), Length.FromMeters(0), SupportType.Pinned, "A"),
                new(Guid.NewGuid(), Length.FromMeters(4), SupportType.Roller, "B"), new(Guid.NewGuid(), Length.FromMeters(8), SupportType.Roller, "C") },
            [new EditorPointForce(Guid.NewGuid(), Length.FromMeters(7), Force.FromNewtons(load), "F1")])
            .WithMomentReleases([new(Guid.NewGuid(), Length.FromMeters(6), "G1")]);
        var editor = new EditorViewModel(document, () => { });
        var result = Assert.Single(editor.Overview.MomentReleaseResults);
        if (load == -50) Assert.True(SpanDraft.Reporting.Notation.TechnicalText.HasExponents(result.RotationLeft), result.RotationLeft);
        var view = new ProjectOverviewView { DataContext = editor, Width = 320 };
        var window = new Window { Content = view, Width = 320, Height = 800 };
        window.Show(); Layout(window, 320, 800);
        var table = view.FindControl<ItemsControl>("MomentReleaseResultTable")!;
        var row = Assert.Single(table.GetVisualDescendants().OfType<Grid>(), g => g.ColumnDefinitions.Count == 4);
        var header = view.FindControl<TextBlock>("RotationLeftHeader")!.GetVisualAncestors().OfType<Grid>().First();
        // Both rotation columns are as wide in the header as in the row, so every value sits under its caption.
        Assert.Equal(header.ColumnDefinitions.Select(c => c.Width.Value), row.ColumnDefinitions.Select(c => c.Width.Value));
        foreach (var (grid, column) in new[] { (header, 1), (header, 2), (row, 1), (row, 2) })
        {
            var cell = grid.Children.OfType<TextBlock>().Single(t => Grid.GetColumn(t) == column);
            Assert.Single(cell.TextLayout.TextLines);
            Assert.True(cell.DesiredSize.Width <= cell.Bounds.Width + 0.01, $"'{cell.Text ?? cell.Inlines?.Text}' needs {cell.DesiredSize.Width} of {cell.Bounds.Width}.");
        }
        window.Close();
    }
}
