using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SpanDraft.Desktop.ViewModels;
using SpanDraft.Desktop.Views;
using Xunit;
using Path = System.IO.Path;

namespace SpanDraft.Tests;

[Collection("Schematic text")]
public sealed class DesktopIconTests
{
    private static readonly string[] Toolbar = ["PointLoad", "PointMoment", "DistributedLoad", "SelfWeight", "FixedSupport", "PinnedSupport", "RollerSupport", "Probe"];
    private static readonly string[] Fluent = ["DocumentAdd", "FolderOpen", "BookOpen", "Document", "Info", "Edit", "Star", "ErrorCircle", "Delete",
        "ZoomIn", "ZoomOut", "Add", "Subtract", "Print", "DocumentPdf", "ChevronLeft", "ChevronRight", "CheckmarkCircle", "LockClosed", "Eye", "EyeOff"];
    private static readonly string[] Fluent24 = ["DocumentFit", "DocumentWidth", "DocumentData"];

    [Fact]
    public void ToolbarSourcesHaveNoTextFontsOrExternalReferences()
    {
        var files = Directory.GetFiles(Path.Combine(Repository(), "src", "SpanDraft.Desktop", "Assets", "Icons", "Toolbar"), "*.svg");
        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            var svg = XDocument.Load(file);
            Assert.DoesNotContain(svg.Descendants(), e => e.Name.LocalName is "text" or "tspan" or "textPath" ||
                e.Name.LocalName.StartsWith("font", StringComparison.OrdinalIgnoreCase));
            var content = string.Join("\n", svg.Descendants().Attributes().Where(a => !a.IsNamespaceDeclaration).Select(a => a.ToString())
                .Concat(svg.DescendantNodes().OfType<XText>().Select(t => t.Value)));
            Assert.DoesNotMatch(@"(?i)\bfont\b|(?:https?:)?//", content);
        }

        static string Repository([CallerFilePath] string source = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "../.."));
    }

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
    public void ForegroundIsInheritedAndChangesOnExistingNativeIcons()
    {
        using var environment = new DesktopControlEnvironment();
        var icon = Icon("Info");
        var parent = new ContentControl { Content = icon, Foreground = Brushes.Red };
        var window = new Window { Content = parent };
        window.Show();
        Render(parent, null, 16, 16);
        var path = Assert.Single(icon.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>());
        Assert.Same(Brushes.Red, path.Fill);
        parent.Foreground = Brushes.Blue;
        Dispatcher.UIThread.RunJobs(); Assert.Same(Brushes.Blue, path.Fill);
        icon.Foreground = Brushes.Green;
        Dispatcher.UIThread.RunJobs(); Assert.Same(Brushes.Green, path.Fill);
        Application.Current!.Resources.ThemeDictionaries[ThemeVariant.Light] = new ResourceDictionary { ["IconTestBrush"] = Brushes.Red };
        Application.Current.Resources.ThemeDictionaries[ThemeVariant.Dark] = new ResourceDictionary { ["IconTestBrush"] = Brushes.Blue };
        icon.Bind(TemplatedControl.ForegroundProperty, new DynamicResourceExtension("IconTestBrush"));
        window.RequestedThemeVariant = ThemeVariant.Light;
        Dispatcher.UIThread.RunJobs(); Assert.Same(Brushes.Red, path.Fill);
        window.RequestedThemeVariant = ThemeVariant.Dark;
        Dispatcher.UIThread.RunJobs(); Assert.Same(Brushes.Blue, path.Fill);
        window.Close();
    }

    [Fact]
    public void SelfWeightPreservesTheSourceBaselineWithTheDescenderAboveTheArrowTip()
    {
        using var environment = new DesktopControlEnvironment();
        var drawings = Geometries(Resource<DrawingImage>("ToolbarIcon.SelfWeight").Drawing!).ToArray();
        var glyph = Assert.Single(drawings, d => d.Brush is ISolidColorBrush { Color: var color } && color == Colors.Black);
        var arrow = Assert.Single(drawings, d => d.Brush is ISolidColorBrush { Color: var color } && color == Color.Parse("#0073FE"));
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

    [Fact]
    public void ProductContainsNativeResourcesWithoutSvgAssembliesOrEmbeddedSources()
    {
        var desktop = typeof(SpanDraft.Desktop.App).Assembly;
        Assert.DoesNotContain(desktop.GetReferencedAssemblies(), a => a.Name!.StartsWith("Svg", StringComparison.OrdinalIgnoreCase));
        var assets = AssetLoader.GetAssets(new Uri("avares://SpanDraft.Desktop/Assets/Icons/"), null).ToArray();
        Assert.DoesNotContain(assets, uri => uri.AbsolutePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase));
    }

    private static PathIcon Icon(string key, int size = 16)
    {
        var icon = new PathIcon { Data = Resource<Geometry>("FluentIcon." + key), Classes = { "fluentIcon" } };
        if (size == 20) icon.Classes.Add("fluentIcon20");
        if (size == 24) icon.Classes.Add("fluentIcon24");
        return icon;
    }
    private static T Resource<T>(string key) => Assert.IsAssignableFrom<T>(Application.Current!.FindResource(key));

    private static void Render(Control control, string? name, int width, int height)
    {
        Window? host = null;
        if (control.Parent is null)
        {
            host = new Window { Content = control, Width = width, Height = height };
            host.Show();
        }
        control.Measure(new Size(width, height)); control.Arrange(new Rect(0, 0, width, height));
        Dispatcher.UIThread.RunJobs();
        using var bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
        bitmap.Render(control);
        if (control is PathIcon icon)
        {
            Assert.False(icon.IsHitTestVisible); Assert.False(icon.Focusable);
        }
        using var pixels = new WriteableBitmap(bitmap.PixelSize, bitmap.Dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var buffer = pixels.Lock(); bitmap.CopyPixels(buffer);
        var bytes = new byte[buffer.RowBytes * height]; Marshal.Copy(buffer.Address, bytes, 0, bytes.Length);
        Assert.Contains(Enumerable.Range(0, width * height), p => bytes[p * 4 + 3] != 0);
        if (name is not null && Environment.GetEnvironmentVariable("SPANDRAFT_VISUAL_TEST_OUTPUT") is { Length: > 0 } output)
        {
            Directory.CreateDirectory(output);
            bitmap.Save(Path.Combine(output, name + ".png"), PngBitmapEncoderOptions.Default);
        }
        host?.Close();
    }

    private static string Slug(string name, char separator) => string.Concat(name.Select((c, i) =>
        char.IsUpper(c) && i != 0 ? separator + char.ToLowerInvariant(c).ToString() : char.ToLowerInvariant(c).ToString()));
}
