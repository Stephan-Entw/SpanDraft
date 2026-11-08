using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Layout;
using Avalonia.VisualTree;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Controls;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.Views;
using Xunit;

namespace SpanDraft.Tests;

public abstract class DesktopResultDiagramControlTestSupport
{
    protected static EditorDocument Document() => new(Length.FromMeters(1), ProjectTemplates.Material, ProjectTemplates.Section,
        [new(Guid.NewGuid(), Length.FromMeters(0), SupportType.Fixed, "A")],
        [new EditorPointForce(Guid.NewGuid(), Length.FromMeters(.5), Force.FromNewtons(-1000), "F1")]);

    protected static void Arrange(Window window, double width = 1250, double height = 768)
    {
        var view = (EditorView)window.Content!;
        window.ApplyTemplate();
        window.Measure(new Size(width, height));
        window.Arrange(new Rect(0, 0, width, height));
        view.Width = width; view.Height = height;
        for (int pass = 0; pass < 3; pass++)
        {
            foreach (var c in view.GetVisualDescendants().OfType<Control>())
            { c.InvalidateMeasure(); c.InvalidateArrange(); }
            view.InvalidateMeasure(); view.Measure(new Size(width, height));
            view.InvalidateArrange(); view.Arrange(new Rect(0, 0, width, height));
        }
    }

    protected static ResultDiagram[] Diagrams(EditorView view) => view.GetVisualDescendants().OfType<ResultDiagram>().ToArray();

    protected static ScrollBar VerticalBar(ScrollViewer viewer) =>
        Assert.Single(viewer.GetVisualDescendants().OfType<ScrollBar>(), b => b.Orientation == Orientation.Vertical);

    protected static void Render(ResultDiagram diagram)
    {
        var drawing = new DrawingGroup();
        using var context = drawing.Open();
        diagram.Render(context);
    }

    protected static void AssertReadableMarkers(ResultDiagram diagram)
    {
        Assert.Equal(diagram.Projection!.Markers.Count, diagram.MarkerLabels.Count);
        foreach (var label in diagram.MarkerLabels)
        {
            Assert.InRange(label.Bounds.Left, 2, diagram.Bounds.Width);
            Assert.InRange(label.Bounds.Right, 0, diagram.Bounds.Width - 2);
            Assert.InRange(label.Bounds.Top, 28, diagram.Bounds.Height);
            Assert.InRange(label.Bounds.Bottom, 0, diagram.Bounds.Height - 2);
            Assert.DoesNotContain(diagram.Projection.Markers, m => label.Bounds.Inflate(4).Contains(m.Screen));
        }
        for (int i = 1; i < diagram.MarkerLabels.Count; i++)
            Assert.False(diagram.MarkerLabels[i - 1].Bounds.Inflate(2).Intersects(diagram.MarkerLabels[i].Bounds.Inflate(2)));
        var drawing = new DrawingGroup();
        using (var context = drawing.Open()) diagram.Render(context);
        var dots = drawing.Children.OfType<GeometryDrawing>()
            .Where(d => d.Geometry?.Bounds.Size == new Size(6, 6)).ToArray();
        Assert.True(diagram.Projection.Markers.Select(m => m.Screen).Concat(diagram.Regions.Select(r => r.Extremum.Screen)).Distinct().Count() == dots.Length,
            string.Join(", ", drawing.Children.TakeLast(6).Select(d => d is GeometryDrawing geometry
                ? $"{geometry.Geometry?.GetType().Name}: {geometry.Geometry?.Bounds}" : d.GetType().Name)));
        foreach (var marker in diagram.Projection.Markers)
            Assert.Contains(dots, d => d.Geometry!.Bounds.Center == marker.Screen);
    }

    protected static void Snapshot(Control view, string name)
    {
        // Reviewable render artifacts live beside the ignored test build output.
        string directory = Path.Combine(AppContext.BaseDirectory, "TestResults", "diagrams");
        Directory.CreateDirectory(directory);
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)view.Bounds.Width, (int)view.Bounds.Height));
        bitmap.Render(view);
        bitmap.Save(Path.Combine(directory, name + ".png"), PngBitmapEncoderOptions.Default);
    }
}
