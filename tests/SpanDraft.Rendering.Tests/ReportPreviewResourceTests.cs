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
public sealed class ReportPreviewResourceTests : ProjectReportTestSupport
{

    [Theory]
    [InlineData(0, 0, 1)] [InlineData(1, 0, 1)]
    [InlineData(0, 1, 1)] [InlineData(1, 1, 2)]
    public void PreviewSkiaRenderingMatchesTheComposedPageAtFitAndFullSize(int page, int zoom, int scaling)
    {
        var (main, files) = Open(ReportTestProjects.Create(true));
        var (window, view) = ShowReportWorkspace(main, files);
        var preview = main.ReportPreview!; var report = preview.Report;
        try
        {
            if (zoom == 1) while (preview.Zoom < 1) preview.ZoomInCommand.Execute(null);
            preview.ShowPage(page); Dispatcher.UIThread.RunJobs();
            var control = view.FindControl<ReportPageControl>("PageView")!;
            var size = control.Bounds.Size;
            Assert.Equal(ReportDocument.PageWidth / (double)ReportDocument.PageHeight, size.Width / size.Height, .003);
            if (zoom == 1)
            {
                Assert.Equal(ReportDocument.PageWidth * 96d / 72, control.Width, .001);
                Assert.Equal(ReportDocument.PageHeight * 96d / 72, control.Height, .001);
            }
            using var actual = new RenderTargetBitmap(new((int)Math.Ceiling(size.Width * scaling), (int)Math.Ceiling(size.Height * scaling)),
                new(96 * scaling, 96 * scaling));
            actual.Render(control);
            using var pixels = new WriteableBitmap(actual.PixelSize, actual.Dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
            using var buffer = pixels.Lock(); actual.CopyPixels(buffer);
            var bytes = new byte[buffer.RowBytes * buffer.Size.Height];
            System.Runtime.InteropServices.Marshal.Copy(buffer.Address, bytes, 0, bytes.Length);
            using var expected = new SKBitmap(new SKImageInfo(actual.PixelSize.Width, actual.PixelSize.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
            using (var canvas = new SKCanvas(expected))
            {
                canvas.Scale((float)(size.Width * scaling / ReportDocument.PageWidth), (float)(size.Height * scaling / ReportDocument.PageHeight));
                report.DrawPage(canvas, page);
            }
            var expectedBytes = expected.Bytes;
            int ink = 0, different = 0;
            for (int y = 0; y < expected.Height; y++)
            for (int x = 0; x < expected.Width; x++)
            {
                int a = y * buffer.RowBytes + x * 4, b = y * expected.RowBytes + x * 4;
                if (expectedBytes[b] < 240 || expectedBytes[b + 1] < 240 || expectedBytes[b + 2] < 240) ink++;
                if (Math.Abs(bytes[a] - expectedBytes[b]) > 24 || Math.Abs(bytes[a + 1] - expectedBytes[b + 1]) > 24
                    || Math.Abs(bytes[a + 2] - expectedBytes[b + 2]) > 24 || Math.Abs(bytes[a + 3] - expectedBytes[b + 3]) > 24) different++;
            }
            Assert.True(ink > 1000);
            Assert.True(different < ink * .05, $"{different} differing pixels for {ink} ink pixels, page {page}, zoom {zoom}, scaling {scaling}.");
        }
        finally { window.Close(); }
    }

    [Fact]
    public void PreviewCachesVisitedPagesAcrossZoomAndReleasesThemOnClose()
    {
        var (main, files) = Open(ReportTestProjects.Create(true)); using var original = ProjectReport.Capture(main.Editor!, Now);
        int draws = 0;
        var report = new ReportDocument(original.Data, new(130, (_, _, _) => draws++));
        using var preview = new ReportPreviewViewModel(report, main.Session!.FilePath, files, () => { });
        var view = new ReportPreviewView { DataContext = preview };
        var window = new Window { Content = view, Width = 1100, Height = 650 };
        window.Show(); Dispatcher.UIThread.RunJobs();
        var control = view.FindControl<ReportPageControl>("PageView")!;
        var first = Assert.IsType<ReportPagePicture>(control.Page); int recordedDraws = draws;
        preview.ShowPage(1); var second = Assert.IsType<ReportPagePicture>(control.Page);
        Assert.NotSame(first, second); Assert.Equal(report.TechnicalAreas(0).Count, recordedDraws);
        var delayed = new ReportPageControl.PageDrawOperation(first, new Rect(control.Bounds.Size));
        for (int i = 0; i < 20; i++)
        {
            (i % 2 == 0 ? preview.ZoomInCommand : preview.PageFitCommand).Execute(null);
            preview.ShowPage(0); Assert.Same(first, control.Page);
            preview.ShowPage(1); Assert.Same(second, control.Page);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.Equal(recordedDraws, draws);
        preview.ShowPage(-1); preview.ShowPage(report.Pages.Count); Assert.Same(second, control.Page);
        preview.Dispose(); window.Close(); Assert.Null(control.Page);
        preview.ShowPage(0); Assert.Null(control.Page);
        using var bitmap = new SKBitmap(400, 600);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Magenta);
        first.Draw(canvas); second.Draw(canvas);
        var pending = new PendingReportDraw(delayed);
        pending.Measure(new(400, 600)); pending.Arrange(new Rect(0, 0, 400, 600));
        DrawingContextHelper.RenderAsync(canvas, pending).GetAwaiter().GetResult();
        Assert.All(bitmap.Pixels, pixel => Assert.Equal(SKColors.Magenta, pixel));
        first.Dispose(); second.Dispose(); delayed.Dispose();
    }

    [Fact]
    public void PreviewPictureOwnerDisposesItsNativePictureExactlyOnce()
    {
        using var recorder = new SKPictureRecorder();
        recorder.BeginRecording(SKRect.Create(20, 20)).Clear(SKColors.White);
        var picture = recorder.EndRecording();
        var owner = new ReportPagePicture(picture);
        Assert.NotEqual(IntPtr.Zero, picture.Handle);
        owner.Dispose(); owner.Dispose();
        Assert.Equal(IntPtr.Zero, picture.Handle);
    }

    [Fact]
    public void PreviewRetriesFailedRecordingsWithoutReplacingTheCurrentPage()
    {
        var (main, files) = Open(ReportTestProjects.Create()); using var original = ProjectReport.Capture(main.Editor!, Now);
        bool fail = true; int attempts = 0;
        var report = new ReportDocument(original.Data with
        {
            Metadata = Enumerable.Range(0, 10).Select(i => ($"Field {i}", "Value")).ToArray()
        }, new(130, (_, _, _) => { attempts++; if (fail) throw new InvalidOperationException("Recording failed"); }));
        using var preview = new ReportPreviewViewModel(report, main.Session!.FilePath, files, () => { });
        var first = Assert.IsType<ReportPagePicture>(preview.Picture);
        int graphicsPage = report.Pages.Select((p, i) => (p, i)).First(p => p.p.HasGraphics).i;
        Assert.Throws<InvalidOperationException>(() => preview.ShowPage(graphicsPage));
        Assert.Same(first, preview.Picture); Assert.Equal(1, attempts);
        fail = false; preview.ShowPage(graphicsPage);
        Assert.NotSame(first, preview.Picture); Assert.Equal(1 + report.TechnicalAreas(graphicsPage).Count, attempts);
    }
}
