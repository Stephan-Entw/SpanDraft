using System.Globalization;
using SkiaSharp;
using SpanDraft.Desktop.Controls;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Resources;
using SpanDraft.Reporting;

namespace SpanDraft.Desktop.ViewModels;

public sealed class ReportPreviewViewModel : ObservableObject, IDisposable
{
    private static readonly double[] ZoomSteps = [.25, .5, .75, 1, 1.25, 1.5, 2, 3, 4];
    private readonly IProjectFileStore files;
    private readonly Dictionary<int, ReportPagePicture> pictures = [];
    private bool busy, disposed;
    private enum FitMode { Manual, Page, Width }
    private FitMode fitMode = FitMode.Page;
    private double viewportWidth, viewportHeight;
    internal ReportDocument Report { get; }
    internal string? ProjectPath { get; }
    internal ReportPagePicture? Picture { get; private set; }
    public int PageIndex { get; private set; }
    public string PageLabel => string.Format(CultureInfo.CurrentUICulture, Report.Data.Labels.PageFormat, PageIndex + 1, Report.Pages.Count);
    public bool IsBusy => busy;
    public bool CanOutput => !busy && !disposed;
    public bool CanPrint => CanOutput && OperatingSystem.IsWindows();
    public string? Error { get; private set; }
    public bool HasError => Error is not null;
    public bool IsPageFit => fitMode == FitMode.Page;
    public bool IsPageWidthFit => fitMode == FitMode.Width;
    public double Zoom { get; private set; } = 1;
    public double PageWidth => ReportDocument.PageWidth * 96d / 72 * Zoom;
    public double PageHeight => ReportDocument.PageHeight * 96d / 72 * Zoom;
    public ActionCommand PreviousCommand { get; }
    public ActionCommand NextCommand { get; }
    public ActionCommand ZoomOutCommand { get; }
    public ActionCommand ZoomInCommand { get; }
    public ActionCommand PageFitCommand { get; }
    public ActionCommand PageWidthCommand { get; }
    public ActionCommand CloseCommand { get; }

    internal ReportPreviewViewModel(ReportDocument report, string? projectPath, IProjectFileStore files, Action close)
    {
        Report = report; ProjectPath = projectPath; this.files = files;
        PreviousCommand = new(() => ShowPage(PageIndex - 1), () => CanOutput && PageIndex > 0);
        NextCommand = new(() => ShowPage(PageIndex + 1), () => CanOutput && PageIndex + 1 < Report.Pages.Count);
        ZoomOutCommand = new(() => SetZoom(ZoomSteps.Last(z => z < Zoom), FitMode.Manual),
            () => CanOutput && ZoomSteps.Any(z => z < Zoom));
        ZoomInCommand = new(() => SetZoom(ZoomSteps.First(z => z > Zoom), FitMode.Manual),
            () => CanOutput && ZoomSteps.Any(z => z > Zoom));
        PageFitCommand = new(() => SetZoom(FitZoom(FitMode.Page), FitMode.Page), () => CanOutput);
        PageWidthCommand = new(() => SetZoom(FitZoom(FitMode.Width), FitMode.Width), () => CanOutput);
        CloseCommand = new(close, () => CanOutput);
        ShowPage(0);
    }

    internal void ShowPage(int index)
    {
        if (!CanOutput || index < 0 || index >= Report.Pages.Count) return;
        if (!pictures.TryGetValue(index, out var picture))
        {
            using var recorder = new SKPictureRecorder();
            var canvas = recorder.BeginRecording(SKRect.Create(ReportDocument.PageWidth, ReportDocument.PageHeight));
            Report.DrawPage(canvas, index);
            picture = new ReportPagePicture(recorder.EndRecording());
            pictures.Add(index, picture);
        }
        Picture = picture; PageIndex = index;
        Notify(nameof(Picture)); Notify(nameof(PageIndex)); Notify(nameof(PageLabel));
        RefreshCommands();
    }

    internal void UpdateViewport(double width, double height)
    {
        if (disposed || width <= 0 || height <= 0) return;
        viewportWidth = width; viewportHeight = height;
        if (fitMode != FitMode.Manual) SetZoom(FitZoom(fitMode), fitMode);
    }

    private double FitZoom(FitMode mode)
    {
        if (viewportWidth <= 0 || viewportHeight <= 0) return Zoom;
        double width = Math.Max(1, viewportWidth - 32) / (ReportDocument.PageWidth * 96d / 72);
        return mode == FitMode.Width ? width : Math.Min(width,
            Math.Max(1, viewportHeight - 32) / (ReportDocument.PageHeight * 96d / 72));
    }

    private void SetZoom(double zoom, FitMode mode)
    {
        if (Zoom == zoom && fitMode == mode) return;
        Zoom = zoom; fitMode = mode;
        Notify(nameof(Zoom)); Notify(nameof(IsPageFit)); Notify(nameof(IsPageWidthFit)); Notify(nameof(PageWidth)); Notify(nameof(PageHeight));
        RefreshCommands();
    }

    internal async Task SavePdfAsync(string path)
    {
        path = Path.GetFullPath(path);
        if (string.Equals(Path.GetExtension(path), ".spandraft", StringComparison.OrdinalIgnoreCase)
            || ProjectPath is not null && string.Equals(path, Path.GetFullPath(ProjectPath),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new IOException(Strings.ReportProjectTarget);
        if (!string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase))
            throw new IOException(Strings.ReportPdfTarget);
        using var pdf = new MemoryStream();
        Report.WritePdf(pdf);
        await files.WriteAtomicAsync(path, pdf.ToArray());
    }

    internal async Task OutputAsync(Func<Task> action)
    {
        if (!CanOutput) return;
        busy = true; Notify(nameof(IsBusy)); Error = null; Notify(nameof(Error)); Notify(nameof(HasError)); RefreshCommands();
        try { await action(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException
            or System.ComponentModel.Win32Exception or System.Runtime.InteropServices.COMException)
        { Error = Strings.ReportError + "\n" + error.Message; Notify(nameof(Error)); Notify(nameof(HasError)); }
        finally { busy = false; Notify(nameof(IsBusy)); RefreshCommands(); }
    }

    private void RefreshCommands()
    {
        Notify(nameof(CanOutput)); Notify(nameof(CanPrint));
        PreviousCommand.Refresh(); NextCommand.Refresh(); ZoomOutCommand.Refresh(); ZoomInCommand.Refresh();
        PageFitCommand.Refresh(); PageWidthCommand.Refresh(); CloseCommand.Refresh();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true; Picture = null; Notify(nameof(Picture));
        foreach (var picture in pictures.Values) picture.Dispose();
        pictures.Clear(); Report.Dispose(); RefreshCommands();
    }
}
