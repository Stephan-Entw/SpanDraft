using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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

/// <summary>Layout invariants shared by the pages of the start and setup flow.</summary>
[Collection("Avalonia")]
public sealed class DesktopPageLayoutTests
{
    [Theory]
    [InlineData("de-DE", 1300, 800)] [InlineData("en-US", 1300, 800)]
    [InlineData("de-DE", 1100, 650)] [InlineData("en-US", 1100, 650)]
    public async Task NavigatingBetweenStartSetupSectionAndMaterialKeepsThePageHeaderInPlace(string culture, int width, int height)
    {
        using var environment = new DesktopControlEnvironment(); using var scope = new UiCultureScope(culture);
        var (window, main) = await Shell(width, height);
        var pages = new List<(string Page, Rect Header, Rect Title, Rect? Actions)> { Measure("start") };
        main.NewCommand.Execute(null); Arrange(window, width, height); pages.Add(Measure("setup"));
        main.Setup.ShowSections(); Arrange(window, width, height); pages.Add(Measure("section gallery"));
        main.Setup.Definition!.SelectForm(SectionEditorType.ISection); Arrange(window, width, height); pages.Add(Measure("section form"));
        main.Setup.ShowOverview(); main.Setup.ShowMaterials(); Arrange(window, width, height); pages.Add(Measure("material"));

        var first = pages[0];
        foreach (var page in pages)
        {
            Assert.True(first.Header == page.Header, $"{page.Page}: header {page.Header} differs from {first.Header}");
            Assert.True(Math.Abs(first.Title.Left - page.Title.Left) < 0.5 && Math.Abs(first.Title.Top - page.Title.Top) < 0.5
                && Math.Abs(first.Title.Height - page.Title.Height) < 0.5, $"{page.Page}: title {page.Title} differs from {first.Title}");
        }
        var actions = pages.Where(p => p.Actions is not null).Select(p => p.Actions!.Value).ToArray();
        Assert.Equal(4, actions.Length);
        Assert.All(actions, a =>
        {
            Assert.Equal(actions[0].Top, a.Top, 0.5); Assert.Equal(actions[0].Bottom, a.Bottom, 0.5); Assert.Equal(actions[0].Right, a.Right, 0.5);
            Assert.Equal(width - first.Title.Left, a.Right, 0.5);
        });
        window.Close();

        (string, Rect, Rect, Rect?) Measure(string page)
        {
            // One page title per page; its header is the bar it sits in, whatever that bar is built from.
            var title = Assert.Single(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Classes.Contains("pageTitle") && t.IsEffectivelyVisible);
            var header = title.GetVisualAncestors().OfType<Border>().First(b => b.BorderThickness.Bottom > 0);
            Assert.Single(title.TextLayout.TextLines);
            Assert.True(title.TextLayout.WidthIncludingTrailingWhitespace <= title.Bounds.Width + 0.5, $"{page}: title clipped");
            var buttons = header.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible && b.Content is string).Select(b => In(b, window)).ToArray();
            Rect? actions = buttons.Length == 0 ? null : buttons.Aggregate((a, b) => a.Union(b));
            if (actions is { } bounds) Assert.True(In(header, window).Contains(bounds), $"{page}: actions outside the header");
            return (page, In(header, window), In(title, window), actions);
        }
    }

    [Theory]
    [InlineData("de-DE", 1300, 800)] [InlineData("en-US", 1300, 800)]
    [InlineData("de-DE", 1100, 650)] [InlineData("en-US", 1100, 650)]
    public async Task MaterialCategoriesAndSourcesShareOneRowRhythmAndOneSelection(string culture, int width, int height)
    {
        using var environment = new DesktopControlEnvironment(); using var scope = new UiCultureScope(culture);
        var (window, main) = await Shell(width, height);
        main.NewCommand.Execute(null); main.Setup.ShowMaterials(); Arrange(window, width, height);
        var view = Assert.Single(window.GetVisualDescendants().OfType<MaterialSelectionView>());
        var selection = (MaterialSelectionViewModel)main.Setup.CurrentStep!;
        var categories = view.FindControl<ListBox>("MaterialFilters")!;
        var sources = view.FindControl<ListBox>("MaterialSources")!;
        Assert.Equal(categories.Classes.Where(c => !c.StartsWith(':')), sources.Classes.Where(c => !c.StartsWith(':')));
        var categoryRows = Rows(categories); var sourceRows = Rows(sources);
        Assert.Equal(selection.CategoryFilters.Count, categoryRows.Length);
        Assert.Equal(3, sourceRows.Length);
        foreach (var row in categoryRows.Concat(sourceRows))
        {
            var bounds = In(row, window); var text = Text(row);
            Assert.Equal(In(categoryRows[0], window).Height, bounds.Height, 0.5);
            Assert.Equal(In(categoryRows[0], window).Left, bounds.Left, 0.5);
            Assert.Equal(In(categoryRows[0], window).Right, bounds.Right, 0.5);
            Assert.Equal(In(Text(categoryRows[0]), window).Left, In(text, window).Left, 0.5);
            Assert.Single(text.TextLayout.TextLines);
        }
        // Each group heading sits the same distance above its first row.
        var headings = view.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("sectionTitle") && t.IsEffectivelyVisible)
            .Where(t => t.Text == Strings.MaterialCategories || t.Text == Strings.MaterialSource).OrderBy(t => In(t, window).Top).ToArray();
        Assert.Equal(2, headings.Length);
        Assert.Equal(In(categoryRows[0], window).Top - In(headings[0], window).Bottom, In(sourceRows[0], window).Top - In(headings[1], window).Bottom, 0.5);
        Assert.Equal(In(headings[0], window).Left, In(headings[1], window).Left, 0.5);
        // Natural German wording, no anglicism in the source list.
        Assert.Equal(new[] { Strings.MaterialCurrentEntry, Strings.BuiltInMaterials, Strings.MyMaterials }, sourceRows.Select(r => Text(r).Text));
        if (culture == "de-DE") Assert.DoesNotContain(sourceRows, r => Text(r).Text!.Contains("Built-in"));

        // One selection across both groups: a source deselects the categories and vice versa; arrow keys move within a group.
        sources.SelectedItem = selection.BuiltInFilter; Arrange(window, width, height);
        Assert.Same(selection.BuiltInFilter, selection.SelectedFilter); Assert.Null(categories.SelectedItem);
        Assert.True(((ListBoxItem)sources.ContainerFromItem(selection.BuiltInFilter)!).IsSelected);
        var focused = (ListBoxItem)sources.ContainerFromItem(selection.BuiltInFilter)!; focused.Focus();
        focused.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Down }); Arrange(window, width, height);
        Assert.Same(selection.UserFilter, selection.SelectedFilter);
        categories.SelectedItem = selection.CategoryFilters[1]; Arrange(window, width, height);
        Assert.Same(selection.CategoryFilters[1], selection.SelectedFilter); Assert.Null(sources.SelectedItem);
        Assert.All(sourceRows, r => Assert.False(((ListBoxItem)r).IsSelected));
        window.Close();

        static Control[] Rows(ListBox list) => list.GetVisualDescendants().OfType<ListBoxItem>().Where(i => i.IsEffectivelyVisible).ToArray<Control>();
        static TextBlock Text(Control row) => row.GetVisualDescendants().OfType<TextBlock>().First(t => !string.IsNullOrEmpty(t.Text));
    }

    [Theory]
    [InlineData("de-DE", 1300, 800)] [InlineData("en-US", 1300, 800)]
    [InlineData("de-DE", 1100, 650)] [InlineData("en-US", 1100, 650)]
    public async Task SectionGalleryStartsOnThePageGridBelowItsSingleTitle(string culture, int width, int height)
    {
        using var environment = new DesktopControlEnvironment(); using var scope = new UiCultureScope(culture);
        var (window, main) = await Shell(width, height);
        main.NewCommand.Execute(null); main.Setup.ShowSections(); Arrange(window, width, height);
        var view = Assert.Single(window.GetVisualDescendants().OfType<SectionDefinitionView>());
        var title = Assert.Single(view.GetVisualDescendants().OfType<TextBlock>(), t => t.Classes.Contains("pageTitle") && t.IsEffectivelyVisible);
        var chooser = view.FindControl<StackPanel>("FormChooser")!;
        var group = view.FindControl<TextBlock>("ParametricGroupTitle")!;
        Assert.Contains("sectionTitle", group.Classes);
        Assert.Equal(In(title, window).Left, In(chooser, window).Left, 0.5);
        Assert.Equal(In(title, window).Left, In(group, window).Left, 0.5);
        var grid = Assert.Single(chooser.GetVisualDescendants().OfType<UniformGrid>());
        Assert.Equal(4, grid.Columns);
        var tiles = grid.Children.OfType<Control>().Select(c => c.GetVisualDescendants().OfType<Button>().First()).ToArray();
        Assert.Equal(8, tiles.Length);
        var manual = view.FindControl<Button>("ManualFormButton")!;
        foreach (var tile in tiles)
        {
            Assert.Equal(tiles[0].Bounds.Size, tile.Bounds.Size);
            Assert.True(tile.Bounds.Width > tile.Bounds.Height, "tiles stay a landscape gallery, not a compact strip");
            foreach (var text in tile.GetVisualDescendants().OfType<TextBlock>().Where(t => !string.IsNullOrEmpty(t.Text)))
                Assert.True(text.TextLayout.Width <= text.Bounds.Width + 0.5);
        }
        // The custom entry is a different way in, but it shares the gallery's edges.
        Assert.Equal(In(tiles[0], window).Left, In(manual, window).Left, 0.5);
        Assert.Equal(In(tiles[3], window).Right, In(manual, window).Right, 1.0);
        Assert.True(manual.Bounds.Height < tiles[0].Bounds.Height);
        window.Close();
    }

    private static async Task<(Window Window, MainWindowViewModel Main)> Shell(int width, int height)
    {
        var files = new Files();
        var main = new MainWindowViewModel(files: files, materialStore: new(files, "materials"), sectionStore: new(files, "sections"));
        await main.InitializeLibrariesAsync();
        var shell = new MainWindow(); var content = shell.Content; shell.Content = null;
        var window = new Window { Content = content, DataContext = main, Width = width, Height = height };
        foreach (var template in shell.DataTemplates) window.DataTemplates.Add(template);
        window.Show(); Arrange(window, width, height);
        return (window, main);
    }

    private static Rect In(Visual visual, Visual root) => new Rect(visual.Bounds.Size).TransformToAABB(visual.TransformToVisual(root)!.Value);

    private static void Arrange(Window window, int width, int height)
    {
        window.ApplyTemplate();
        for (int i = 0; i < 3; i++)
        {
            window.Measure(new Size(width, height)); window.Arrange(new Rect(0, 0, width, height));
            var content = (Control)window.Content!;
            content.InvalidateMeasure(); content.Measure(new Size(width, height));
            content.InvalidateArrange(); content.Arrange(new Rect(0, 0, width, height));
            Dispatcher.UIThread.RunJobs();
        }
    }
}
