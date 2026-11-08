using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Xunit;
using Path = System.IO.Path;

namespace SpanDraft.Tests;

public abstract class DesktopIconTestSupport
{
    protected static readonly string[] Toolbar = ["PointLoad", "PointMoment", "DistributedLoad", "SelfWeight", "FixedSupport", "PinnedSupport", "RollerSupport", "Probe"];

    protected static PathIcon Icon(string key, int size = 16)
    {
        var icon = new PathIcon { Data = Resource<Geometry>("FluentIcon." + key), Classes = { "fluentIcon" } };
        if (size == 20) icon.Classes.Add("fluentIcon20");
        if (size == 24) icon.Classes.Add("fluentIcon24");
        return icon;
    }
    protected static T Resource<T>(string key) => Assert.IsAssignableFrom<T>(Application.Current!.FindResource(key));

    protected static void Render(Control control, string? name, int width, int height)
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
}
