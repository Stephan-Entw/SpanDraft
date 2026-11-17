using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SkiaSharp;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Controls;
using SpanDraft.Desktop.Layout;
using SpanDraft.Desktop.State;
using Xunit;

namespace SpanDraft.Tests;

[Collection("Avalonia")]
public sealed class SpringSupportRenderingTests : DesktopHingedSupportTestSupport
{
    private static SKBitmap Render(EditorSupport support, int scale, out double supportX)
    {
        var document = Document(support);
        var frame = new BeamLayoutFrame(StationLayout.Compute(document.Length.Meters, 30, 170, []), new(200, 100, 35));
        var canvas = new BeamCanvas { Width = 200, Height = 100, Scene = Scene(document, frame) with { Annotations = [] },
            BeamBrush = Brushes.Black, CanvasBackgroundBrush = Brushes.Transparent };
        var export = new Viewbox { Width = 200 * scale, Height = 100 * scale, Child = canvas };
        export.Measure(new Size(export.Width, export.Height)); export.Arrange(new Rect(0, 0, export.Width, export.Height));
        using var bitmap = new RenderTargetBitmap(new PixelSize(200 * scale, 100 * scale));
        bitmap.Render(export);
        using var stream = new MemoryStream();
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
        stream.Position = 0;
        supportX = frame.Layout.Transform.PhysicalToScreen(support.Position.Meters);
        return SKBitmap.Decode(stream);
    }

    private static EditorSupport Spring(SpringData spring) =>
        new(Guid.NewGuid(), Length.FromMeters(.5), SupportType.Spring, "A", spring);

    private static bool AnyInk(SKBitmap pixels, int x0, int y0, int x1, int y1)
    {
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                if (pixels.GetPixel(x, y).Alpha > 0) return true;
        return false;
    }

    private static bool AnyDark(SKBitmap pixels, int x0, int y0, int x1, int y1)
    {
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                if (pixels.GetPixel(x, y).Red < 128) return true;
        return false;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void TranslationalSpringLeavesTheJointEmptyAndDrawsSpringAndGroundBelowIt(int scale)
    {
        using var pixels = Render(Spring(new(TranslationalStiffness.FromNewtonsPerMeter(1e5), null)), scale, out double supportX);
        int x = (int)(supportX * scale), y = 35 * scale;
        Assert.Equal(0, pixels.GetPixel(x, y).Alpha);
        Assert.Equal(0, pixels.GetPixel(0, 0).Alpha);
        // The beam passes on both sides, the zigzag leaves the axis to both sides and the ground line closes the symbol.
        Assert.True(pixels.GetPixel(x - 10 * scale, y).Alpha > 0);
        Assert.True(pixels.GetPixel(x + 10 * scale, y).Alpha > 0);
        Assert.True(AnyInk(pixels, x - 6 * scale, (35 + 5) * scale, x - 3 * scale, (35 + 17) * scale));
        Assert.True(AnyInk(pixels, x + 3 * scale, (35 + 5) * scale, x + 6 * scale, (35 + 17) * scale));
        Assert.True(pixels.GetPixel(x - 15 * scale, (35 + 20) * scale).Alpha > 0);
        Assert.True(pixels.GetPixel(x + 15 * scale, (35 + 20) * scale).Alpha > 0);
        // Nothing is drawn above the beam.
        Assert.False(AnyInk(pixels, x - 8 * scale, (35 - 12) * scale, x + 8 * scale, (35 - 6) * scale));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void RotationalSpringAddsASpiralAroundTheJointAboveAndBelowTheBeam(int scale)
    {
        using var pixels = Render(Spring(new(null, RotationalStiffness.FromNewtonMetersPerRadian(1e4))), scale, out double supportX);
        int x = (int)(supportX * scale), y = 35 * scale;
        Assert.Equal(0, pixels.GetPixel(x, y).Alpha);
        Assert.True(AnyInk(pixels, x - 11 * scale, (35 - 11) * scale, x + 11 * scale, (35 - 5) * scale));
        Assert.True(AnyInk(pixels, x - 11 * scale, (35 + 5) * scale, x + 11 * scale, (35 + 11) * scale));
        Assert.True(pixels.GetPixel(x - 15 * scale, (35 + 20) * scale).Alpha > 0);
    }

    [Fact]
    public void PartialRestraintDrawsTheRollerTriangleWithTheSpiral()
    {
        using var partial = Render(Spring(new(null, RotationalStiffness.FromNewtonMetersPerRadian(1e4), true)), 2, out double supportX);
        int x = (int)(supportX * 2), y = 35 * 2;
        Assert.Equal(0, partial.GetPixel(x, y).Alpha);
        // The triangle flanks run diagonally from the apex to the base at 80 percent of the pinned size.
        Assert.True(AnyInk(partial, x - 12 * 2, (35 + 14) * 2, x - 8 * 2, (35 + 16) * 2));
        Assert.True(AnyInk(partial, x - 11 * 2, (35 - 11) * 2, x + 11 * 2, (35 - 5) * 2));
    }

    [Fact]
    public void ATranslationalAndARotationalSpringAtOneStationShareOneLabelBelowBothSymbols()
    {
        const int scale = 4;
        var w = new EditorSupport(Guid.NewGuid(), Length.FromMeters(.5), SupportType.Spring, "B",
            new(TranslationalStiffness.FromNewtonsPerMeter(1e5), null));
        var d = new EditorSupport(Guid.NewGuid(), Length.FromMeters(.5), SupportType.Spring, "C",
            new(null, RotationalStiffness.FromNewtonMetersPerRadian(1e4)));
        var document = DesktopLayoutFixture.Document().WithSupports([w, d]);
        var frame = new BeamLayoutFrame(StationLayout.Compute(document.Length.Meters, 30, 170, []), new(200, 140, 35));
        var scene = BeamRenderState.Create(document, frame, text => SchematicText.Measure(text, AppTypography.Typeface, 13));
        var label = Assert.Single(scene.Annotations);
        Assert.Equal("B + C", label.Text);
        Assert.Null(label.Id);
        Assert.Equal(new[] { w.Id, d.Id }, label.EntityIds);
        Assert.Equal(frame.Layout.Transform.PhysicalToScreen(.5), label.Bounds.Center.X);
        Assert.True(label.Bounds.Top > frame.Viewport.BeamY + SchematicMetrics.SupportGroundY);
        Assert.True(new Rect(0, 0, 200, 140).Contains(label.Bounds));
        var canvas = new BeamCanvas { Width = 200, Height = 140, Scene = scene,
            BeamBrush = Brushes.Black, CanvasBackgroundBrush = Brushes.White };
        var export = new Viewbox { Width = 200 * scale, Height = 140 * scale,
            Child = new Border { Width = 200, Height = 140, Background = Brushes.White, Child = canvas } };
        export.Measure(new Size(export.Width, export.Height)); export.Arrange(new Rect(0, 0, export.Width, export.Height));
        using var bitmap = new RenderTargetBitmap(new PixelSize(200 * scale, 140 * scale));
        bitmap.Render(export);
        if (Environment.GetEnvironmentVariable("SPANDRAFT_VISUAL_TEST_OUTPUT") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            using var file = File.Create(Path.Combine(directory, "spring-station-two-supports.png"));
            bitmap.Save(file, PngBitmapEncoderOptions.Default);
        }
        using var stream = new MemoryStream();
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
        stream.Position = 0;
        using var pixels = SKBitmap.Decode(stream);
        Assert.Equal(SKColors.White, pixels.GetPixel(0, 0));
        Assert.True(AnyDark(pixels, (int)(label.Bounds.Left * scale), (int)(label.Bounds.Top * scale),
            (int)(label.Bounds.Right * scale), (int)(label.Bounds.Bottom * scale)));
        int x = (int)(frame.Layout.Transform.PhysicalToScreen(.5) * scale), y = 35 * scale;
        // The zigzag below the joint and the spiral around it are both there.
        Assert.True(AnyDark(pixels, x - 6 * scale, (35 + 5) * scale, x + 6 * scale, (35 + 17) * scale));
        Assert.True(AnyDark(pixels, x - 11 * scale, (35 - 11) * scale, x + 11 * scale, (35 - 5) * scale));
        // The joint itself stays empty.
        Assert.False(pixels.GetPixel(x, y).Red < 128);
    }

    [Fact]
    public void SharedLabelUsesHighlightAndConflictFromEitherSupportWithErrorTakingPriority()
    {
        var w = Spring(new(TranslationalStiffness.FromNewtonsPerMeter(1e5), null)) with { Name = "B" };
        var d = Spring(new(null, RotationalStiffness.FromNewtonMetersPerRadian(1e4))) with { Name = "C" };
        var document = DesktopLayoutFixture.Document().WithSupports([w, d]);
        var scene = Scene(document, new BeamLayoutState().Update(document, 1100, 600)!);
        var canvas = new BeamCanvas { Scene = scene, BeamBrush = Brushes.Black, AccentBrush = Brushes.Blue, ErrorBrush = Brushes.Red };
        foreach (var id in new[] { w.Id, d.Id })
        {
            canvas.HighlightedSupportId = id;
            AssertLabelBrush(Brushes.Blue);
            canvas.ConflictEntityIds = [id == w.Id ? d.Id : w.Id];
            AssertLabelBrush(Brushes.Red);
            canvas.HighlightedSupportId = null;
            AssertLabelBrush(Brushes.Red);
            canvas.ConflictEntityIds = [];
            AssertLabelBrush(Brushes.Black);
        }

        void AssertLabelBrush(IBrush brush)
        {
            var drawing = new DrawingGroup();
            using (var context = drawing.Open()) canvas.Render(context);
            var runs = TextRuns(drawing).ToArray();
            Assert.Equal("B + C", string.Concat(runs.Select(run => run.GlyphRun!.Characters.ToString())));
            Assert.NotEmpty(runs);
            Assert.All(runs, run => Assert.Same(brush, run.Foreground));
        }
        static IEnumerable<GlyphRunDrawing> TextRuns(Drawing drawing) => drawing switch
        {
            DrawingGroup group => group.Children.SelectMany(TextRuns),
            GlyphRunDrawing run => [run],
            _ => []
        };
    }
}
