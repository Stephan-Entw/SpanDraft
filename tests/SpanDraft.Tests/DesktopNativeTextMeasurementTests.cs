using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.TextFormatting;
using Avalonia.Platform;
using Avalonia.Skia;
using Avalonia.VisualTree;
using SpanDraft.Desktop.Controls;
using SpanDraft.Desktop.Layout;
using SpanDraft.Desktop.State;
using Xunit;

namespace SpanDraft.Tests;

[CollectionDefinition("Schematic text", DisableParallelization = true)]
public sealed class SchematicTextCollection : ICollectionFixture<SchematicRenderingFixture>;

public sealed class SchematicRenderingFixture
{
    public SchematicRenderingFixture() => AppBuilder.Configure<Application>()
        // Initialize runtime services and the real Skia text/geometry backend
        // shared by measurement and recorded drawing tests, without windows.
        .UseWindowingSubsystem(() => { }, "Schematic rendering tests")
        .UseStandardRuntimePlatformSubsystem().UseSkia().UseHarfBuzz().SetupWithoutStarting();
}

[Collection("Schematic text")]
public sealed class DesktopNativeTextMeasurementTests
{
    [Theory]
    [InlineData("Ändern", true)]
    [InlineData("Übernehmen", true)]
    [InlineData("Öffnen", true)]
    [InlineData("Rückgängig", false)]
    [InlineData("Löschen", false)]
    [InlineData("Balkenlänge", false)]
    [InlineData("Maßgebendes Moment", false)]
    public void StyledTextAndButtonCaptionsPreserveUnicodePixelsWithoutClippingAccents(string text, bool button)
    {
        using var environment = new DesktopControlEnvironment();
        Control control = button ? new Button { Content = text, Classes = { "ghost" } } : new TextBlock { Text = text };
        control.Margin = new Thickness(8);
        control.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        control.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        var host = new Panel { Children = { control } };
        var window = new Window { Content = host };
        window.ApplyTemplate();
        window.Measure(new Size(400, 80));
        window.Arrange(new Rect(0, 0, 400, 80));
        host.Measure(new Size(400, 80));
        host.Arrange(new Rect(0, 0, 400, 80));
        var caption = button ? Assert.Single(control.GetVisualDescendants().OfType<AccessText>()) : (TextBlock)control;
        Assert.Equal(text, caption.Text);
        Assert.False(caption.ClipToBounds);

        var line = Assert.Single(caption.TextLayout.TextLines);
        var runs = line.TextRuns.OfType<ShapedTextRun>().ToArray();
        Assert.NotEmpty(runs);
        Assert.Equal(text, string.Concat(runs.Select(run => run.Text.ToString())));
        foreach (var run in runs)
        {
            Assert.NotEmpty(run.GlyphRun.GlyphInfos);
            foreach (var glyph in run.GlyphRun.GlyphInfos)
                Assert.NotEqual(0, glyph.GlyphIndex);
        }

        // Use the rendered glyphs' ink bounds in line coordinates: platform fonts
        // differ in whether accents extend above the line box at all.
        double inkTop = runs.Min(run =>
            run.GlyphRun.InkBounds.Top + line.Baseline - run.GlyphRun.BaselineOrigin.Y);
        var origin = caption.TranslatePoint(default, host)!.Value;
        int top = (int)Math.Floor(origin.Y * 2);
        int left = (int)Math.Floor(origin.X * 2);
        int right = (int)Math.Ceiling((origin.X + caption.Bounds.Width) * 2);
        var visible = RenderCaption();
        Assert.Contains(visible.Pixels.Where((_, i) => i % 4 == 3), alpha => alpha != 0);
        var reference = RenderCaption(drawLayoutOnly: true);
        Assert.Equal(reference.Pixels, visible.Pixels);
        if (button)
        {
            caption.ClipToBounds = true;
            var clipped = RenderCaption();
            Assert.Contains(clipped.Pixels.Where((_, i) => i % 4 == 3), alpha => alpha != 0);
            Assert.True(visible.Above >= clipped.Above);
            // InkBounds are conservative. Require a difference only when the
            // unclipped layout also proves that visible ink exceeds the line box.
            if (inkTop < 0 && reference.Above > 0)
                Assert.True(visible.Above > clipped.Above,
                    $"No additional accent pixels for {text} with ink top {inkTop}: {visible.Above} vs {clipped.Above}");
            caption.ClipToBounds = false;
        }

        // Diacritics must render differently from ASCII even when they fit
        // entirely inside the platform font's line box.
        caption.Text = text.Replace("Ä", "A").Replace("Ö", "O").Replace("Ü", "U")
            .Replace("ü", "u").Replace("ä", "a").Replace("ö", "o").Replace("ß", "ss");
        var ascii = RenderCaption();
        Assert.NotEqual(visible.Pixels, ascii.Pixels);

        (byte[] Pixels, int Above) RenderCaption(bool drawLayoutOnly = false)
        {
            using var bitmap = new RenderTargetBitmap(new PixelSize(800, 160), new Vector(192, 192));
            if (drawLayoutOnly)
            {
                using var context = bitmap.CreateDrawingContext();
                caption.TextLayout.Draw(context, origin);
            }
            else
                bitmap.Render(host);
            using var pixels = new WriteableBitmap(bitmap.PixelSize, bitmap.Dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
            using var buffer = pixels.Lock();
            bitmap.CopyPixels(buffer);
            var bytes = new byte[buffer.RowBytes * buffer.Size.Height];
            Marshal.Copy(buffer.Address, bytes, 0, bytes.Length);
            int ink = 0;
            for (int y = Math.Max(0, top - 8); y < top; y++)
                for (int x = left; x < right; x++)
                    if (bytes[y * buffer.RowBytes + x * 4 + 3] != 0) ink++;
            return (bytes, ink);
        }
    }

    [Theory]
    [InlineData("Ändern Übernehmen Öffnen Rückgängig Löschen Balkenlänge Maßgebendes Moment")]
    [InlineData("äöüÄÖÜß")]
    [InlineData("13,3·10³ 123·10⁻⁶ 4,94·10⁻³²⁴ 180·10³⁰⁶")]
    public void PlatformTextShapingPreservesUnicodeAndHasNoMissingGlyphs(string text)
    {
        using var layout = new TextLayout(text, Typeface.Default, 13, Brushes.Black);
        var runs = layout.TextLines.SelectMany(l => l.TextRuns).OfType<ShapedTextRun>().ToArray();
        Assert.NotEmpty(runs);
        Assert.Equal(text, string.Concat(runs.Select(r => r.Text.ToString())));
        foreach (var run in runs)
            foreach (var glyph in run.GlyphRun.GlyphInfos)
                Assert.NotEqual(0, glyph.GlyphIndex);
    }

    [Theory]
    [InlineData("Ä", "A")]
    [InlineData("ö", "o")]
    [InlineData("ü", "u")]
    [InlineData("³", "3")]
    public void DiacriticsAndSuperscriptsAreDistinctRenderedGlyphs(string unicode, string plain)
    {
        using var unicodeLayout = new TextLayout(unicode, Typeface.Default, 13, Brushes.Black);
        using var plainLayout = new TextLayout(plain, Typeface.Default, 13, Brushes.Black);
        var unicodeRun = Assert.IsType<ShapedTextRun>(Assert.Single(Assert.Single(unicodeLayout.TextLines).TextRuns));
        var plainRun = Assert.IsType<ShapedTextRun>(Assert.Single(Assert.Single(plainLayout.TextLines).TextRuns));
        Assert.NotEqual(plainRun.GlyphRun.GlyphInfos[0].GlyphIndex, unicodeRun.GlyphRun.GlyphInfos[0].GlyphIndex);
        if (unicode == "³")
            Assert.True(unicodeRun.GlyphRun.InkBounds.Bottom < plainRun.GlyphRun.InkBounds.Bottom);
        else
            Assert.True(unicodeRun.GlyphRun.InkBounds.Top < plainRun.GlyphRun.InkBounds.Top);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("en-US")]
    public void FormattingThenRealMeasurementFeedsPurePackerWithFiniteBounds(string culture)
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            var layout = StationLayout.Compute(1.4005, 72, 900,
                [new(1.2, 4.75, 4.75), new(1.25, 4.75, 4.75), new(1.3, 4.75, 4.75)]);
            var measured = new List<(string Text, Size Size)>();
            Size Measure(string text)
            {
                var size = SchematicText.Measure(text, Typeface.Default, 13);
                measured.Add((text, size));
                return size;
            }
            var axis = CoordinateAxisLayout.Create(layout, 1000, Measure);
            Assert.Equal(layout.Stations.Count, measured.Count);
            Assert.Equal(layout.Stations.Select(s => UiNumbers.Compact(s.PhysicalX * 1000)), measured.Select(m => m.Text));
            Assert.All(measured, m =>
            {
                Assert.True(double.IsFinite(m.Size.Width) && m.Size.Width > 0);
                Assert.True(double.IsFinite(m.Size.Height) && m.Size.Height > 0);
            });
            Assert.All(axis.Packing.Labels, label =>
            {
                Assert.True(double.IsFinite(label.Left) && double.IsFinite(label.Right));
                int index = checked((int)label.Requirement.StableOrderKey);
                Assert.Equal(measured[index].Size.Width, label.Requirement.MeasuredWidth);
            });
            Assert.True(axis.Packing.LaneCount >= 1 && axis.Packing.LaneCount <= layout.Stations.Count);
            Assert.True(double.IsFinite(axis.PaneHeight) && axis.PaneHeight > 0);
            Assert.Same(layout.Transform, axis.StationLayout.Transform);
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }

    [Fact]
    public void RealMeasuredEntityLabelsHaveFiniteBoundsAndCarryStableNames()
    {
        var load = EditorPointLoad.Create(Guid.NewGuid(), SpanDraft.Core.Units.Length.FromMillimeters(500.5),
            PointLoadKind.Force, -1000.25, "Motorlast");
        var document = DesktopLayoutFixture.Document().WithLoads([load]);
        var frame = new BeamLayoutState().Update(document, 1100, 600)!;
        var scene = BeamRenderState.Create(document, frame, text => SchematicText.Measure(text, Typeface.Default, 13));
        var label = Assert.Single(scene.Annotations);
        Assert.Equal("Motorlast = " + UiNumbers.Compact(load.Value) + " N", label.Text);
        Assert.True(double.IsFinite(label.Bounds.X) && double.IsFinite(label.Bounds.Y));
        Assert.True(double.IsFinite(label.Bounds.Width) && label.Bounds.Width > 0);
        Assert.True(double.IsFinite(label.Bounds.Height) && label.Bounds.Height > 0);
        Assert.Equal(load.Id, scene.HitTestLabel(label.Bounds.Center.X, label.Bounds.Center.Y)!.Id);
    }
}
