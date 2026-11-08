using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.TextFormatting;
using Avalonia.Platform;
using Avalonia.Skia.Helpers;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SkiaSharp;
using SpanDraft.Desktop.Controls;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Reporting;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.ViewModels;
using SpanDraft.Desktop.Views;
using SpanDraft.Reporting;
using Xunit;

// Control rendering must stay on the owning Avalonia dispatcher thread.
#pragma warning disable xUnit1031
namespace SpanDraft.Tests;

[Collection("Avalonia")]
public sealed class ReportToolbarRenderingTests : ProjectReportTestSupport
{

    [Theory]
    [InlineData("de-DE", false, 1100, 650)] [InlineData("en-US", true, 1100, 650)]
    [InlineData("de-DE", true, 1100, 650)] [InlineData("en-US", false, 1100, 650)]
    [InlineData("de-DE", true, 1250, 800)] [InlineData("en-US", false, 1250, 800)]
    [InlineData("de-DE", false, 1250, 800)] [InlineData("en-US", true, 1250, 800)]
    public void PreviewToolbarFitsTheMainWorkspaceInBothThemes(string language, bool dark, int width, int height)
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        Application.Current!.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        var (main, files) = Open(ReportTestProjects.Create(true));
        var (window, view) = ShowReportWorkspace(main, files, width, height);
        try
        {
            window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(window.RequestedThemeVariant, view.ActualThemeVariant);
            var bar = view.FindControl<Border>("ReportToolbar")!;
            var buttons = new[] { "PreviousButton", "NextButton", "ZoomOutButton", "ZoomInButton", "PageFitButton", "PageWidthButton", "SavePdfButton", "PrintButton", "CloseButton", "ZoomValueButton" }
                .Select(n => view.FindControl<Button>(n)!).ToArray();
            Assert.All(buttons, button =>
            {
                var top = button.TranslatePoint(default, bar)!.Value;
                Assert.True(new Rect(bar.Bounds.Size).Contains(new Rect(top, button.Bounds.Size)), button.Name);
                Assert.Equal(buttons[0].Bounds.Height, button.Bounds.Height);
                Assert.Equal(buttons[0].TranslatePoint(default, bar)!.Value.Y, top.Y);
                Assert.False(button.IsFocused); Assert.DoesNotContain("selected", button.Classes);
            });
            Assert.All(buttons.Take(6), button =>
            {
                Assert.False(string.IsNullOrWhiteSpace(Avalonia.Automation.AutomationProperties.GetName(button)));
                Assert.NotNull(ToolTip.GetTip(button));
                if (button.Name is "ZoomOutButton" or "ZoomInButton")
                {
                    Assert.Equal(buttons[0].Bounds.Width, button.Bounds.Width);
                    var symbol = Assert.Single(button.GetVisualDescendants().OfType<PathIcon>());
                    Assert.Equal(new Size(16, 16), symbol.Bounds.Size);
                    Assert.Same(button.Foreground, symbol.Foreground);
                    Assert.Same(Application.Current!.FindResource(button.Name == "ZoomOutButton" ? "FluentIcon.Subtract" : "FluentIcon.Add"), symbol.Data);
                    return;
                }
                bool navigation = button.Name is "PreviousButton" or "NextButton";
                if (navigation) Assert.True(button.Bounds.Width < buttons[4].Bounds.Width);
                else Assert.Equal(new Size(36, 36), button.Bounds.Size);
                var icon = Assert.Single(button.GetVisualDescendants().OfType<PathIcon>());
                Assert.Same(button.Foreground, icon.Foreground);
                int size = navigation ? 20 : 24;
                Assert.Equal(new Size(size, size), icon.Bounds.Size);
            });
            Assert.False(buttons[0].IsEffectivelyEnabled); Assert.True(buttons[1].IsEffectivelyEnabled);
            Assert.Equal(OperatingSystem.IsWindows(), buttons[7].IsEnabled);
            Assert.Equal(language == "de-DE" ? "Ganze Seite anzeigen" : "Show whole page", ToolTip.GetTip(buttons[4]));
            Assert.Equal(language == "de-DE" ? "An Seitenbreite anpassen" : "Fit to page width", ToolTip.GetTip(buttons[5]));
            Assert.All(buttons.Skip(4).Take(2), button =>
                Assert.Equal(ToolTip.GetTip(button), Avalonia.Automation.AutomationProperties.GetName(button)));
            Assert.Contains("secondary", buttons[8].Classes);
            Assert.DoesNotContain("toolbar", buttons[8].Classes); Assert.DoesNotContain("primary", buttons[8].Classes);
            Assert.DoesNotContain("…", Strings.ReportSavePdf); Assert.DoesNotContain("…", Strings.ReportPrint);
            var ordered = new[] { "SavePdfButton", "PrintButton", "PreviousButton", "PageValue", "NextButton",
                "ZoomOutButton", "ZoomValue", "ZoomInButton", "PageFitButton", "PageWidthButton", "CloseButton" }
                .Select(name => view.FindControl<Control>(name)!).ToArray();
            var bounds = ordered.Select(control => new Rect(control.TranslatePoint(default, bar)!.Value, control.Bounds.Size)).ToArray();
            Assert.All(bounds, rect => Assert.True(new Rect(bar.Bounds.Size).Contains(rect)));
            for (int i = 1; i < bounds.Length; i++) Assert.True(bounds[i - 1].Right <= bounds[i].Left, ordered[i].Name);
            Assert.Equal(bar.Padding.Left, bounds[0].Left);
            Assert.Equal(bar.Bounds.Width - bar.Padding.Right, bounds[^1].Right);
            var center = view.FindControl<StackPanel>("PreviewControls")!;
            Assert.Equal(bar.Bounds.Width / 2, center.TranslatePoint(new(center.Bounds.Width / 2, 0), bar)!.Value.X, .5);
            Assert.Empty(bar.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Rectangle>());
            Assert.Null(center.Background);
            Assert.True(bounds[5].Left - bounds[4].Right > bounds[6].Left - bounds[5].Right);
            Assert.Equal(bounds[5].Right, bounds[6].Left); Assert.Equal(bounds[6].Right, bounds[7].Left);
            Assert.Equal(bounds[2].Right, bounds[3].Left); Assert.Equal(bounds[3].Right, bounds[4].Left);
            double pageStep = bounds[3].Center.X - bounds[2].Center.X;
            double zoomStep = bounds[6].Center.X - bounds[5].Center.X;
            Assert.Equal(pageStep, bounds[4].Center.X - bounds[3].Center.X);
            Assert.Equal(zoomStep, bounds[7].Center.X - bounds[6].Center.X);
            Assert.True(bounds[5].Center.X - bounds[4].Center.X > Math.Max(pageStep, zoomStep));
            Assert.True(bounds[8].Center.X - bounds[7].Center.X > Math.Max(pageStep, zoomStep));
            Assert.Equal(bounds[5].Left - bounds[4].Right, bounds[8].Left - bounds[7].Right);
            var zoomLabel = view.FindControl<TextBlock>("ZoomLabel")!;
            double baseline = zoomLabel.TranslatePoint(new(0, zoomLabel.TextLayout.Baseline), bar)!.Value.Y;
            Assert.All(buttons.Skip(6).Take(3), button =>
            {
                var caption = Assert.Single(button.GetVisualDescendants().OfType<TextBlock>());
                Assert.Equal(baseline, caption.TranslatePoint(new(0, caption.TextLayout.Baseline), bar)!.Value.Y, .01);
                Assert.Equal(caption.Text, Avalonia.Automation.AutomationProperties.GetName(button));
                Assert.True(new Rect(button.Bounds.Size).Contains(new Rect(caption.TranslatePoint(default, button)!.Value, caption.Bounds.Size)));
            });
            Assert.IsType<ToolbarTextPresenter>(zoomLabel.Parent);
            Assert.IsType<ToolbarTextPresenter>(view.FindControl<TextBlock>("PageLabel")!.Parent);
            Assert.Equal((bounds[5].Center.X + bounds[7].Center.X) / 2, bounds[6].Center.X, .5);
            Assert.All(new[] { ordered[3], ordered[6] }, label =>
                Assert.Equal(bounds[0].Center.Y, label.TranslatePoint(new(0, label.Bounds.Height / 2), bar)!.Value.Y, .5));
            var preview = main.ReportPreview!;
            var pageLabel = view.FindControl<TextBlock>("PageLabel")!;
            var pageValue = view.FindControl<ToolbarTextPresenter>("PageValue")!;
            Assert.True(double.IsFinite(pageValue.Width));
            Assert.Equal(pageValue.Width, pageValue.Bounds.Width);
            var labelSize = pageValue.Bounds.Size;
            Assert.Equal("1 / 2", pageLabel.Text);
            preview.NextCommand.Execute(null); Dispatcher.UIThread.RunJobs(); Assert.Equal("2 / 2", pageLabel.Text);
            preview.PreviousCommand.Execute(null); Dispatcher.UIThread.RunJobs(); Assert.Equal("1 / 2", pageLabel.Text);
            Assert.Equal(labelSize, pageValue.Bounds.Size);
            Assert.DoesNotContain(view.GetVisualDescendants(), c => c is ComboBox || c is TextBox { IsVisible: true });
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == Strings.ReportHeading);
            Assert.Empty(window.OwnedWindows); Assert.Equal(main.WindowTitle, window.Title);
            var page = view.FindControl<ReportPageControl>("PageView")!;
            var scroll = view.FindControl<ScrollViewer>("PageScroll")!;
            Assert.True(page.Bounds.Width + 32 <= scroll.Viewport.Width + 1);
            Assert.True(page.Bounds.Height + 32 <= scroll.Viewport.Height + 1);
            string directory = Path.Combine(RepositoryRoot(), "output", "pdf", "production"); Directory.CreateDirectory(directory);
            using var image = new RenderTargetBitmap(new(width, height), new Vector(96, 96)); image.Render(window);
            image.Save(Path.Combine(directory, $"workspace-{language}-{dark}-{width}.png"), PngBitmapEncoderOptions.Default);
            using var toolbarImage = new RenderTargetBitmap(new((int)bar.Bounds.Width, (int)bar.Bounds.Height), new(96, 96)); toolbarImage.Render(bar);
            toolbarImage.Save(Path.Combine(directory, $"preview-toolbar-{language}-{dark}-{width}.png"), PngBitmapEncoderOptions.Default);
        }
        finally { window.Close(); }
    }

    [Theory]
    [InlineData("Arial", 1)] [InlineData("Arial", 2)]
    [InlineData("Helvetica Neue", 1)] [InlineData("Helvetica Neue", 2)]
    [InlineData("Segoe UI", 1)] [InlineData("Segoe UI", 2)]
    public void ReportToolbarKeepsSharedTextBaselineAndCentersIconsAndReferenceDigit(string font, int scaling)
    {
        var (main, files) = Open(ReportTestProjects.Create());
        var (window, view) = ShowReportWorkspace(main, files);
        try
        {
            view.FontFamily = new FontFamily(font);
            var controls = view.FindControl<Border>("ReportToolbar")!;
            var page = view.FindControl<TextBlock>("PageLabel")!;
            var zoom = view.FindControl<TextBlock>("ZoomLabel")!;
            var captions = new[] { "SavePdfButton", "PrintButton", "CloseButton" }
                .Select(name => Assert.Single(view.FindControl<Button>(name)!.GetVisualDescendants().OfType<TextBlock>())).ToArray();
            double? initialBaseline = null;
            foreach (int percent in new[] { 25, 50, 61, 75, 125, 400 })
            {
                Assert.True(main.ReportPreview!.TrySetZoom(percent.ToString(CultureInfo.InvariantCulture)));
                page.Text = percent < 100 ? "1 / 1" : "99 / 99";
                Dispatcher.UIThread.RunJobs();
                double baseline = page.TranslatePoint(new(0, page.TextLayout.Baseline), controls)!.Value.Y;
                if (initialBaseline is { } previous) Assert.Equal(previous, baseline, .01);
                initialBaseline = baseline;
                Assert.All(captions.Append(zoom).Append(page), text =>
                {
                    Assert.Equal(baseline, text.TranslatePoint(new(0, text.TextLayout.Baseline), controls)!.Value.Y, .01);
                    Assert.Null(text.RenderTransform);
                    var presenter = Assert.IsType<ToolbarTextPresenter>(text.Parent);
                    var origin = text.TranslatePoint(default, presenter)!.Value;
                    Assert.True(origin.X >= -.5 && origin.Y >= -.5);
                    Assert.True(origin.X + text.Bounds.Width <= presenter.Bounds.Width + .5);
                    Assert.True(origin.Y + text.Bounds.Height <= presenter.Bounds.Height + .5);
                });
                using var image = new RenderTargetBitmap(new((int)Math.Ceiling(controls.Bounds.Width * scaling),
                    (int)Math.Ceiling(controls.Bounds.Height * scaling)), new(96 * scaling, 96 * scaling));
                image.Render(controls);
                using var pixels = new WriteableBitmap(image.PixelSize, image.Dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
                using var buffer = pixels.Lock(); image.CopyPixels(buffer);
                var bytes = new byte[buffer.RowBytes * buffer.Size.Height];
                System.Runtime.InteropServices.Marshal.Copy(buffer.Address, bytes, 0, bytes.Length);
                var minus = Ink(view.FindControl<Button>("ZoomOutButton")!);
                var label = Ink(view.FindControl<Grid>("ZoomValue")!);
                var plus = Ink(view.FindControl<Button>("ZoomInButton")!);
                int leftGap = label.Left - minus.Right - 1, rightGap = plus.Left - label.Right - 1;
                Assert.True(leftGap > 0 && rightGap > 0);
                // Text alignment uses font geometry; raster bounds also depend on pixel coverage.
                var zoomValue = view.FindControl<Grid>("ZoomValue")!;
                double zoomCenter = zoomValue.TranslatePoint(new(zoomValue.Bounds.Width / 2, 0), controls)!.Value.X * scaling;
                double left = double.PositiveInfinity, right = double.NegativeInfinity, advance = 0;
                foreach (var run in Assert.Single(zoom.TextLayout.TextLines).TextRuns.OfType<ShapedTextRun>())
                {
                    var glyphs = run.GlyphRun;
                    left = Math.Min(left, advance + glyphs.InkBounds.Left - glyphs.BaselineOrigin.X);
                    right = Math.Max(right, advance + glyphs.InkBounds.Right - glyphs.BaselineOrigin.X);
                    advance += run.Size.Width;
                }
                Assert.True(double.IsFinite(left) && double.IsFinite(right));
                double textCenter = zoom.TranslatePoint(new((left + right) / 2, 0), controls)!.Value.X * scaling;
                Assert.True(Math.Abs(zoomCenter - textCenter) <= scaling,
                    $"{font}, {percent} %, {scaling}×: text geometry center {textCenter}px vs zoom control center {zoomCenter}px");
                // Fluent Add and Subtract have different ink widths; compare their rendered centers.
                double symbolMidpoint = (minus.Left + minus.Right + plus.Left + plus.Right + 2) / 4d;
                Assert.True(Math.Abs(zoomCenter - symbolMidpoint) <= scaling,
                    $"{font}, {percent} %, {scaling}×: zoom control center {zoomCenter}px vs symbol midpoint {symbolMidpoint}px");
                var iconNames = new[] { "PreviousButton", "NextButton", "ZoomOutButton", "ZoomInButton", "PageFitButton", "PageWidthButton" };
                double axis = view.FindControl<Button>("CloseButton")!.TranslatePoint(new(0,
                    view.FindControl<Button>("CloseButton")!.Bounds.Height / 2), controls)!.Value.Y * scaling;
                foreach (string name in iconNames)
                {
                    var ink = Ink(view.FindControl<Control>(name)!);
                    Assert.True(Math.Abs((ink.Top + ink.Bottom + 1) / 2d - axis) <= scaling,
                        $"{font}, {percent} %, {scaling}×: {name} ink center {(ink.Top + ink.Bottom + 1) / 2d}px vs axis {axis}px");
                }
                foreach (string name in new[] { "PageValue", "ZoomValue", "CloseButton" })
                {
                    var control = view.FindControl<Control>(name)!;
                    double center = control.TranslatePoint(new(0, control.Bounds.Height / 2), controls)!.Value.Y * scaling;
                    Assert.Equal(axis, center, .01);
                }
                if (font == "Helvetica Neue" && scaling == 2 && percent == 61)
                {
                    string directory = Path.Combine(RepositoryRoot(), "output", "pdf", "production"); Directory.CreateDirectory(directory);
                    image.Save(Path.Combine(directory, "report-toolbar-aligned-2x.png"), PngBitmapEncoderOptions.Default);
                }

                // Text shares a baseline based on digit height. A slash or descender can
                // change a string's ink bounds without changing that alignment.
                string? pageText = page.Text;
                page.Text = "0"; Dispatcher.UIThread.RunJobs();
                Assert.Equal(baseline, page.TranslatePoint(new(0, page.TextLayout.Baseline), controls)!.Value.Y, .01);
                using var reference = new RenderTargetBitmap(image.PixelSize, image.Dpi);
                reference.Render(controls); reference.CopyPixels(buffer);
                System.Runtime.InteropServices.Marshal.Copy(buffer.Address, bytes, 0, bytes.Length);
                var digit = Ink(view.FindControl<ToolbarTextPresenter>("PageValue")!);
                double digitCenter = (digit.Top + digit.Bottom + 1) / 2d;
                Assert.True(Math.Abs(digitCenter - axis) <= scaling,
                    $"{font}, {percent} %, {scaling}×: reference digit center {digitCenter}px vs axis {axis}px");
                page.Text = pageText; Dispatcher.UIThread.RunJobs();

                (int Left, int Top, int Right, int Bottom) Ink(Control control)
                {
                    var origin = control.TranslatePoint(default, controls)!.Value;
                    var foreground = (ISolidColorBrush)(control.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault()?.Foreground ??
                        control.GetVisualDescendants().OfType<PathIcon>().First().Foreground)!;
                    var color = foreground.Color;
                    int threshold = (255 + Math.Max(color.R, Math.Max(color.G, color.B)) + 1) / 2;
                    int left = image.PixelSize.Width, top = image.PixelSize.Height, right = -1, bottom = -1;
                    for (int x = (int)Math.Floor(origin.X * scaling); x < Math.Ceiling((origin.X + control.Bounds.Width) * scaling); x++)
                        for (int y = 0; y < image.PixelSize.Height; y++)
                            // Count at least half-covered glyph pixels against the light surface, excluding the button border.
                            if (bytes[y * buffer.RowBytes + x * 4 + 3] >= 128 &&
                                Math.Max(bytes[y * buffer.RowBytes + x * 4], Math.Max(bytes[y * buffer.RowBytes + x * 4 + 1], bytes[y * buffer.RowBytes + x * 4 + 2])) <= threshold)
                            {
                                left = Math.Min(left, x); right = Math.Max(right, x);
                                top = Math.Min(top, y); bottom = Math.Max(bottom, y);
                            }
                    Assert.True(right >= left, control.Name);
                    return (left, top, right, bottom);
                }
            }
        }
        finally { window.Close(); }
    }
}
