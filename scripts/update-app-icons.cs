#:package Svg.Skia@5.1.1
#:package SkiaSharp@3.119.4

using System.Diagnostics;
using System.Runtime.CompilerServices;
using SkiaSharp;
using Svg.Skia;

if (args.Length > 1 || (args.Length == 1 && args[0] != "--check"))
{
    Console.Error.WriteLine("Usage: dotnet run --file scripts/update-app-icons.cs [-- --check]");
    return 1;
}
bool check = args.Length == 1;
string root = Path.GetFullPath(Path.Combine(ScriptDirectory(), ".."));
string assets = Path.Combine(root, "src", "SpanDraft.Desktop", "Assets", "AppIcon");
string temporary = Path.Combine(Path.GetTempPath(), "spandraft-app-icons-" + Guid.NewGuid().ToString("N"));
int[] sizes = [16, 24, 32, 48, 64, 128, 256];
try
{
    Directory.CreateDirectory(temporary);
    using var master = new SKSvg();
    using var small = new SKSvg();
    master.Load(Path.Combine(assets, "Source", "app-icon.svg"));
    small.Load(Path.Combine(assets, "Source", "app-icon-small.svg"));
    if (master.Picture is null || small.Picture is null) throw new InvalidOperationException("Cannot load app SVG masters.");
    var images = sizes.ToDictionary(size => size, Png);
    var outputs = new Dictionary<string, byte[]> { ["app-icon-1024.png"] = Png(1024) };
    using (var stream = new MemoryStream())
    {
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length);
        int offset = 6 + 16 * sizes.Length;
        foreach (int size in sizes)
        {
            writer.Write((byte)(size == 256 ? 0 : size)); writer.Write((byte)(size == 256 ? 0 : size));
            writer.Write((byte)0); writer.Write((byte)0); writer.Write((ushort)1); writer.Write((ushort)32);
            writer.Write(images[size].Length); writer.Write(offset); offset += images[size].Length;
        }
        foreach (int size in sizes) writer.Write(images[size]);
        outputs.Add("SpanDraft.ico", stream.ToArray());
    }
    if (OperatingSystem.IsMacOS())
    {
        string iconset = Path.Combine(temporary, "SpanDraft.iconset");
        Directory.CreateDirectory(iconset);
        foreach (int size in new[] { 16, 32, 128, 256, 512 })
        {
            await File.WriteAllBytesAsync(Path.Combine(iconset, $"icon_{size}x{size}.png"), Png(size));
            await File.WriteAllBytesAsync(Path.Combine(iconset, $"icon_{size}x{size}@2x.png"), Png(size * 2));
        }
        string icns = Path.Combine(temporary, "SpanDraft.icns");
        var start = new ProcessStartInfo("/usr/bin/iconutil") { UseShellExecute = false };
        foreach (string argument in new[] { "--convert", "icns", "--output", icns, iconset }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start iconutil.");
        await process.WaitForExitAsync();
        if (process.ExitCode != 0) throw new InvalidOperationException("iconutil failed.");
        outputs.Add("SpanDraft.icns", await File.ReadAllBytesAsync(icns));
    }
    bool outdated = false;
    foreach (var (name, bytes) in outputs)
    {
        string destination = Path.Combine(assets, name);
        if (File.Exists(destination) && (await File.ReadAllBytesAsync(destination)).AsSpan().SequenceEqual(bytes)) continue;
        if (check) { Console.Error.WriteLine("Outdated or missing: " + Path.GetRelativePath(root, destination)); outdated = true; }
        else { await File.WriteAllBytesAsync(destination, bytes); Console.WriteLine("Updated " + Path.GetRelativePath(root, destination)); }
    }
    if (outdated) return 1;
    Console.WriteLine("App icons are current." + (OperatingSystem.IsMacOS() ? "" : " ICNS generation/check requires macOS iconutil."));
    return 0;

    byte[] Png(int size)
    {
        var picture = size <= 24 ? small.Picture! : master.Picture!;
        using var surface = SKSurface.Create(new SKImageInfo(size * 4, size * 4, SKColorType.Rgba8888, SKAlphaType.Premul));
        surface.Canvas.Clear(SKColors.Transparent);
        surface.Canvas.Scale(size * 4 / picture.CullRect.Width, size * 4 / picture.CullRect.Height);
        surface.Canvas.Translate(-picture.CullRect.Left, -picture.CullRect.Top);
        surface.Canvas.DrawPicture(picture);
        using var image = surface.Snapshot();
        using var pixels = new SKBitmap(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
        if (!image.ScalePixels(pixels.PeekPixels(), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear)))
            throw new InvalidOperationException("Cannot resize app icon.");
        using var output = SKImage.FromBitmap(pixels);
        using var png = output.Encode(SKEncodedImageFormat.Png, 100);
        return png.ToArray();
    }
}
catch (Exception error)
{
    Console.Error.WriteLine("App icon generation failed: " + error.Message);
    return 1;
}
finally { Directory.Delete(temporary, recursive: true); }

static string ScriptDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
