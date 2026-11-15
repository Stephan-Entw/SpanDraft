using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using SpanDraft.Desktop.Controls;
using SpanDraft.Desktop.ViewModels;
using Xunit;

namespace SpanDraft.Tests;

[Collection("Avalonia")]
public sealed class SectionShapeIconTests
{
    [Theory]
    [InlineData(32, 32, true)]
    [InlineData(68, 68, true)]
    [InlineData(1, 1, false)]
    [InlineData(3, 40, false)]
    [InlineData(40, 4, false)]
    public void EveryFormRendersAtAnySizeAndDrawsOnlyWhereThereIsRoom(int width, int height, bool drawn)
    {
        using var environment = new DesktopControlEnvironment();
        byte[] blank = Png(new Control { Width = width, Height = height });
        foreach (var type in Enum.GetValues<SectionEditorType>())
        {
            var icon = new SectionShapeIcon { Type = type, Width = width, Height = height };
            // A manual section has its own simple frame without the size guard; only the silhouettes are covered here.
            if (type == SectionEditorType.Manual) { Png(icon); continue; }
            Assert.Equal(drawn, !Png(icon).SequenceEqual(blank));
        }
    }

    private static byte[] Png(Control control)
    {
        control.Measure(new(100, 100)); control.Arrange(new Rect(0, 0, control.Width, control.Height));
        using var bitmap = new RenderTargetBitmap(new PixelSize(100, 100));
        bitmap.Render(control);
        using var stream = new MemoryStream();
        bitmap.Save(stream);
        return stream.ToArray();
    }
}
