using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SkiaSharp;
using SpanDraft.Core.Supports;
using SpanDraft.Desktop.Controls;
using SpanDraft.Desktop.Layout;
using Xunit;

namespace SpanDraft.Tests;

[Collection("Avalonia")]
public sealed class HingedSupportRenderingTests : DesktopHingedSupportTestSupport
{

    [Theory]
    [InlineData(SupportType.Pinned, 1)]
    [InlineData(SupportType.Roller, 1)]
    [InlineData(SupportType.Pinned, 2)]
    [InlineData(SupportType.Roller, 2)]
    public void TransparentExportLeavesJointEmptyAndKeepsBeamAndSymbolAligned(SupportType type, int scale)
    {
        var document = Document(Support(type));
        var frame = new BeamLayoutFrame(StationLayout.Compute(document.Length.Meters, 30, 170, []), new(200, 100, 35));
        var canvas = new BeamCanvas { Width = 200, Height = 100, Scene = Scene(document, frame) with { Annotations = [] },
            BeamBrush = Brushes.Black, CanvasBackgroundBrush = Brushes.Transparent };
        var export = new Viewbox { Width = 200 * scale, Height = 100 * scale, Child = canvas };
        export.Measure(new Size(export.Width, export.Height)); export.Arrange(new Rect(0, 0, export.Width, export.Height));
        using var bitmap = new RenderTargetBitmap(new PixelSize(200 * scale, 100 * scale));
        bitmap.Render(export);
        using var stream = new MemoryStream(); bitmap.Save(stream, PngBitmapEncoderOptions.Default); stream.Position = 0;
        using var pixels = SKBitmap.Decode(stream);
        double supportX = frame.Layout.Transform.PhysicalToScreen(.5);
        int x = (int)(supportX * scale), y = 35 * scale;
        Assert.Equal(0, pixels.GetPixel(x, y).Alpha);
        Assert.Equal(0, pixels.GetPixel(x - scale, y).Alpha);
        Assert.Equal(0, pixels.GetPixel(0, 0).Alpha);
        Assert.True(pixels.GetPixel(x - 10 * scale, y).Alpha > 0);
        Assert.True(pixels.GetPixel(x + 10 * scale, y).Alpha > 0);
        Assert.True(pixels.GetPixel(x, (35 + 20) * scale).Alpha > 0);
    }
}
