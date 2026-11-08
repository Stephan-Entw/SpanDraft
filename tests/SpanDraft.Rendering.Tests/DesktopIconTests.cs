using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SpanDraft.Desktop.ViewModels;
using SpanDraft.Desktop.Views;
using Xunit;

namespace SpanDraft.Tests;

[Collection("Avalonia")]
public sealed class DesktopIconTests : DesktopIconTestSupport
{
    private static readonly string[] Fluent = ["DocumentAdd", "FolderOpen", "BookOpen", "Document", "Info", "Edit", "Star", "ErrorCircle", "Delete",
        "ZoomIn", "ZoomOut", "Add", "Subtract", "Print", "DocumentPdf", "ChevronLeft", "ChevronRight", "CheckmarkCircle", "LockClosed", "Eye", "EyeOff"];
    private static readonly string[] Fluent24 = ["DocumentFit", "DocumentWidth", "DocumentData"];

    private static string Slug(string name, char separator) => string.Concat(name.Select((c, i) =>
        char.IsUpper(c) && i != 0 ? separator + char.ToLowerInvariant(c).ToString() : char.ToLowerInvariant(c).ToString()));

    [Fact]
    public void NativeArtworkKeepsArtboardsAndRendersInBothThemes()
    {
        using var environment = new DesktopControlEnvironment();
        foreach (string key in Toolbar)
        {
            var image = Resource<DrawingImage>("ToolbarIcon." + key);
            Assert.Equal(new Size(48, 48), image.Size);
            foreach (int size in new[] { 28, 48 })
                Render(new Image { Source = image, Stretch = Stretch.Uniform, Width = size, Height = size },
                    Slug(key, '-') + "-" + size, size, size);
        }
        foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            Application.Current!.RequestedThemeVariant = theme;
            foreach (var (key, size) in Fluent.Select(key => (key, 16)).Append(("DocumentSettings", 20)).Concat(Fluent24.Select(key => (key, 24))))
                foreach (var (state, color) in new[] { ("primary", "TextPrimary"), ("secondary", "TextSecondary"), ("disabled", "TextDisabled"), ("error", "Error") })
                {
                    Assert.True(Application.Current.TryFindResource(color, theme, out var brush));
                    var icon = Icon(key, size); icon.Foreground = (IBrush)brush!;
                    Render(icon, $"fluent-{Slug(key, '_')}-{theme}-{state}", size, size);
                    var path = Assert.Single(icon.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>());
                    Assert.Same(icon.Foreground, path.Fill);
                    Assert.Equal(Stretch.None, path.Stretch);
                    Assert.Equal(new Size(size, size), path.Bounds.Size);
                    Assert.Equal(new Size(size, size), Assert.IsType<Canvas>(path.Parent).Bounds.Size);
                }
            var view = new EditorView { DataContext = new EditorViewModel(DesktopLayoutFixture.Document(), () => { }) };
            var window = new Window { Content = view };
            window.Show();
            window.Measure(new Size(1250, 800)); window.Arrange(new Rect(0, 0, 1250, 800));
            view.Measure(new Size(1250, 800)); view.Arrange(new Rect(0, 0, 1250, 800));
            Dispatcher.UIThread.RunJobs();
            var bar = view.FindControl<Border>("EditorToolbar")!;
            Render(bar, $"toolbar-{theme}", (int)bar.Bounds.Width, (int)bar.Bounds.Height);
            var images = bar.GetVisualDescendants().OfType<Image>().Where(i => i.Classes.Contains("toolbarIcon")).ToArray();
            Assert.Equal(Toolbar.Length, images.Length);
            foreach (var image in images)
            {
                Assert.Equal(new Size(28, 28), image.Bounds.Size);
                bool enabled = image.GetVisualAncestors().OfType<Button>().First().IsEffectivelyEnabled;
                Assert.Equal(enabled ? 1 : 0.45, image.Opacity);
                Assert.False(image.IsHitTestVisible); Assert.False(image.Focusable);
            }
            bar.IsEnabled = false; Dispatcher.UIThread.RunJobs();
            Assert.All(images, image => Assert.Equal(0.45, image.Opacity));
            window.Close();
        }
    }

    [Fact]
    public void SelfWeightPreservesTheSourceBaselineWithTheDescenderAboveTheArrowTip()
    {
        using var environment = new DesktopControlEnvironment();
        var drawings = Geometries(Resource<DrawingImage>("ToolbarIcon.SelfWeight").Drawing!).ToArray();
        var glyph = Assert.Single(drawings, d => d.Brush is ISolidColorBrush { Color: var color } && color == ((ISolidColorBrush)Application.Current!.FindResource("SelfWeightStroke")!).Color);
        var arrow = Assert.Single(drawings, d => d.Brush is ISolidColorBrush { Color: var color } && color == ((ISolidColorBrush)Application.Current!.FindResource("Accent")!).Color);
        var textBounds = glyph.Geometry!.Bounds;
        var arrowBounds = arrow.Geometry!.Bounds;
        Assert.True(textBounds.Right < arrowBounds.Left);
        Assert.True(textBounds.Top < arrowBounds.Top);
        Assert.True(textBounds.Bottom > arrowBounds.Top && textBounds.Bottom < arrowBounds.Bottom);
        Render(new Border { Background = Brushes.White, Child = new Image
            { Source = Resource<DrawingImage>("ToolbarIcon.SelfWeight"), Width = 96, Height = 96 } },
            "self-weight-source-baseline", 96, 96);

        static IEnumerable<GeometryDrawing> Geometries(Drawing drawing) => drawing switch
        {
            GeometryDrawing geometry => [geometry],
            DrawingGroup group => group.Children.SelectMany(Geometries),
            _ => []
        };
    }
}
