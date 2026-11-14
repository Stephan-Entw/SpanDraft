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
public sealed class MomentReleaseRenderingTests : DesktopHingedSupportTestSupport
{
    private static SKBitmap Render(EditorDocument document, int scale, out double releaseX, IBrush? background = null)
    {
        var frame = new BeamLayoutFrame(StationLayout.Compute(document.Length.Meters, 30, 170, []), new(200, 100, 35));
        var canvas = new BeamCanvas { Width = 200, Height = 100, Scene = Scene(document, frame) with { Annotations = [] },
            BeamBrush = Brushes.Black, CanvasBackgroundBrush = background ?? Brushes.Transparent };
        var export = new Viewbox { Width = 200 * scale, Height = 100 * scale, Child = canvas };
        export.Measure(new Size(export.Width, export.Height)); export.Arrange(new Rect(0, 0, export.Width, export.Height));
        using var bitmap = new RenderTargetBitmap(new PixelSize(200 * scale, 100 * scale));
        bitmap.Render(export);
        using var stream = new MemoryStream();
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
        stream.Position = 0;
        releaseX = frame.Layout.Transform.PhysicalToScreen(document.MomentReleases[0].Position.Meters);
        return SKBitmap.Decode(stream);
    }

    private static EditorDocument WithRelease(double meters = .5) => DesktopLayoutFixture.Document()
        .WithSupports([new(Guid.NewGuid(), Length.FromMeters(0), SupportType.Pinned, "A"),
            new(Guid.NewGuid(), Length.FromMeters(1), SupportType.Roller, "B")])
        .WithMomentReleases([new(Guid.NewGuid(), Length.FromMeters(meters), "G1")]);

    private static bool AnyInk(SKBitmap pixels, int x0, int y0, int x1, int y1)
    {
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                if (pixels.GetPixel(x, y).Alpha > 0) return true;
        return false;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void TheRingLeavesItsCentreEmptyWhileTheBeamPassesOnBothSidesAndNothingHangsBelowIt(int scale)
    {
        using var pixels = Render(WithRelease(), scale, out double x0);
        int x = (int)(x0 * scale), y = 35 * scale;
        // The beam is cut out inside the ring and runs on both sides of it.
        Assert.Equal(0, pixels.GetPixel(x, y).Alpha);
        Assert.True(pixels.GetPixel(x - 12 * scale, y).Alpha > 0);
        Assert.True(pixels.GetPixel(x + 12 * scale, y).Alpha > 0);
        // The ring itself is ink at its radius above and below the axis.
        Assert.True(AnyInk(pixels, x - scale, (35 - 6) * scale, x + scale, (35 - 4) * scale));
        Assert.True(AnyInk(pixels, x - scale, (35 + 4) * scale, x + scale, (35 + 6) * scale));
        // It is no support: no ground line and nothing further away than the ring.
        Assert.False(AnyInk(pixels, x - 3 * scale, (35 + 9) * scale, x + 3 * scale, (35 + 30) * scale));
        Assert.False(AnyInk(pixels, x - 3 * scale, (35 - 30) * scale, x + 3 * scale, (35 - 9) * scale));
    }

    [Fact]
    public void AnOpaqueBackgroundFillsTheRingInsteadOfClippingTheBeam()
    {
        using var pixels = Render(WithRelease(), 2, out double x0, Brushes.White);
        int x = (int)(x0 * 2), y = 35 * 2;
        var centre = pixels.GetPixel(x, y);
        Assert.Equal((255, 255, 255), (centre.Red, centre.Green, centre.Blue));
        Assert.True(pixels.GetPixel(x - 12 * 2, y).Red < 128);
    }

    [Fact]
    public void ARingOnASupportKeepsTheJointOfTheSupportAndAddsItsOwnRing()
    {
        var document = DesktopLayoutFixture.Document().WithSupports([new(Guid.NewGuid(), Length.FromMeters(.5), SupportType.Roller, "A")])
            .WithMomentReleases([new(Guid.NewGuid(), Length.FromMeters(.5), "G1")]);
        using var pixels = Render(document, 2, out double x0);
        int x = (int)(x0 * 2), y = 35 * 2;
        Assert.True(AnyInk(pixels, x - 6 * 2, (35 + 8) * 2, x + 6 * 2, (35 + 24) * 2));
        Assert.True(AnyInk(pixels, x - 6 * 2, (35 - 7) * 2, x + 6 * 2, (35 - 4) * 2));
        Assert.True(pixels.GetPixel(x - 12 * 2, y).Alpha > 0);
    }
}
