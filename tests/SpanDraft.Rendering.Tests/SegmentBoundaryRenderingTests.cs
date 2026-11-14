using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SkiaSharp;
using SpanDraft.Core.Materials;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Sections.Parametric;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Controls;
using SpanDraft.Desktop.Layout;
using SpanDraft.Desktop.State;
using Xunit;

namespace SpanDraft.Tests;

[Collection("Avalonia")]
public sealed class SegmentBoundaryRenderingTests
{
    private static Length M(double value) => Length.FromMeters(value);

    private static SKBitmap Render(EditorDocument document, int scale, out BeamLayoutFrame frame)
    {
        frame = new BeamLayoutFrame(StationLayout.Compute(document.Length.Meters, 30, 170,
            StationRequirementBuilder.FromDocument(document)), new(200, 100, 35));
        var canvas = new BeamCanvas { Width = 200, Height = 100,
            Scene = BeamRenderState.Create(document, frame, DesktopLayoutFixture.Measure) with { Annotations = [] },
            BeamBrush = Brushes.Black, CanvasBackgroundBrush = Brushes.Transparent };
        var export = new Viewbox { Width = 200 * scale, Height = 100 * scale, Child = canvas };
        export.Measure(new Size(export.Width, export.Height)); export.Arrange(new Rect(0, 0, export.Width, export.Height));
        using var bitmap = new RenderTargetBitmap(new PixelSize(200 * scale, 100 * scale));
        bitmap.Render(export);
        using var stream = new MemoryStream();
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
        stream.Position = 0;
        return SKBitmap.Decode(stream);
    }

    private static EditorDocument Document(params double[] boundaries) => new(M(1), boundaries.Select(M),
        Enumerable.Range(0, boundaries.Length + 1).Select(_ => new EditorSegment(new("Steel", Pressure.FromPascals(210e9),
            Pressure.FromPascals(235e6), MaterialStrengthType.Re), new RectangleSectionGeometry(M(0.1), M(0.2)),
            SectionAxisDesignation.Y)), [new(Guid.NewGuid(), M(0), SupportType.Fixed, "A")]);

    private static int Extent(SKBitmap pixels, int x, int beamY, int direction)
    {
        int count = 0;
        for (int y = beamY + direction * 3; y >= 0 && y < pixels.Height && pixels.GetPixel(x, y).Alpha > 0; y += direction) count++;
        return count;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void ABoundaryIsAThinCrossLineOfEqualExtentAboveAndBelowTheBeam(int scale)
    {
        using var pixels = Render(Document(0.5), scale, out var frame);
        int x = (int)Math.Round(frame.Layout.Transform.PhysicalToScreen(0.5) * scale), y = 35 * scale;

        int above = Extent(pixels, x, y, -1), below = Extent(pixels, x, y, 1);
        Assert.True(above > 0 && below > 0);
        Assert.InRange(Math.Abs(above - below), 0, scale);
        // It is thin: the neighbouring columns three pixels away stay empty off the beam.
        Assert.Equal(0, pixels.GetPixel(x + 4 * scale, y - 6 * scale).Alpha);
        Assert.Equal(0, pixels.GetPixel(x - 4 * scale, y + 6 * scale).Alpha);
    }

    [Fact]
    public void ABeamWithoutBoundariesHasNoCrossLinesAndTheMiddleOfTheBeamStaysClean()
    {
        using var plain = Render(Document(), 1, out var frame);
        using var split = Render(Document(0.5), 1, out _);
        int x = (int)Math.Round(frame.Layout.Transform.PhysicalToScreen(0.5)), y = 35;

        Assert.Equal(0, Extent(plain, x, y, -1));
        Assert.Equal(0, Extent(plain, x, y, 1));
        Assert.True(Extent(split, x, y, -1) > 0);
    }
}
