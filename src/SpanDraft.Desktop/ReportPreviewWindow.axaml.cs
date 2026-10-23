using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Reporting;
using SpanDraft.Desktop.Resources;
using SpanDraft.Reporting;

namespace SpanDraft.Desktop;

public partial class ReportPreviewWindow : Window
{
    private readonly ReportDocument? report;
    private readonly IProjectFileStore files;
    private readonly string? projectPath;
    private Bitmap? bitmap;
    private int page;
    private bool busy;

    public ReportPreviewWindow() : this(null, null, new ProjectFileStore()) { }

    internal ReportPreviewWindow(ReportDocument? report, string? projectPath, IProjectFileStore files)
    {
        this.report = report; this.projectPath = projectPath; this.files = files;
        InitializeComponent();
        PrintButton.IsEnabled = OperatingSystem.IsWindows();
        Closed += (_, _) => { PageImage.Source = null; bitmap?.Dispose(); bitmap = null; };
        Closing += (_, e) => { if (busy) e.Cancel = true; };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && !busy) { e.Handled = true; Close(); }
        };
        if (report is not null) ShowPage(0);
    }

    internal void ShowPage(int index)
    {
        if (report is null || busy || index < 0 || index >= report.Pages.Count) return;
        using var png = new MemoryStream();
        report.WritePng(png, index); png.Position = 0;
        var next = new Bitmap(png);
        PageImage.Source = next;
        bitmap?.Dispose(); bitmap = next; page = index;
        PageLabel.Text = string.Format(CultureInfo.CurrentUICulture, report.Data.Labels.PageFormat, page + 1, report.Pages.Count);
        PreviousButton.IsEnabled = page > 0; NextButton.IsEnabled = page + 1 < report.Pages.Count;
        PageScroll.Offset = default;
        UpdateZoom();
    }

    private void UpdateZoom()
    {
        if (PageImage is null || ZoomPicker is null || PageScroll is null) return;
        double width = ReportDocument.PageWidth * 96 / 72, height = ReportDocument.PageHeight * 96 / 72;
        double scale = ZoomPicker.SelectedIndex == 0
            ? Math.Min(Math.Max(1, PageScroll.Bounds.Width - 40) / width, Math.Max(1, PageScroll.Bounds.Height - 40) / height) : 1;
        PageImage.Width = width * scale; PageImage.Height = height * scale;
    }

    private void ViewportChanged(object? sender, SizeChangedEventArgs e) => UpdateZoom();
    private void ZoomChanged(object? sender, SelectionChangedEventArgs e) => UpdateZoom();
    private void PreviousPage(object? sender, RoutedEventArgs e) => ShowPage(page - 1);
    private void NextPage(object? sender, RoutedEventArgs e) => ShowPage(page + 1);
    private void ClosePreview(object? sender, RoutedEventArgs e) { if (!busy) Close(); }

    internal async Task SavePdfAsync(string path)
    {
        if (report is null) return;
        path = Path.GetFullPath(path);
        if (string.Equals(Path.GetExtension(path), ".spandraft", StringComparison.OrdinalIgnoreCase)
            || projectPath is not null && string.Equals(path, Path.GetFullPath(projectPath),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new IOException(Strings.ReportProjectTarget);
        if (!string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase))
            throw new IOException(Strings.ReportPdfTarget);
        using var pdf = new MemoryStream();
        report.WritePdf(pdf);
        await files.WriteAtomicAsync(path, pdf.ToArray());
    }

    private async void SavePdf(object? sender, RoutedEventArgs e) => await OutputAsync(async () =>
    {
        using var folder = projectPath is null ? null
            : await StorageProvider.TryGetFolderFromPathAsync(Path.GetDirectoryName(projectPath)!);
        using var destination = await StorageProvider.SaveFilePickerAsync(new()
        {
            Title = Strings.ReportSavePdf, DefaultExtension = "pdf", ShowOverwritePrompt = true,
            FileTypeChoices = [new FilePickerFileType("PDF") { Patterns = ["*.pdf"] }], SuggestedStartLocation = folder,
            SuggestedFileName = Path.ChangeExtension(ProjectSaveFileName.Suggest(null, report?.Data.Title, Strings.Untitled), ".pdf")
        });
        if (destination is null) return;
        await SavePdfAsync(destination.TryGetLocalPath() ?? throw new IOException(Strings.LocalFilesOnly));
    });

    private async void PrintReport(object? sender, RoutedEventArgs e) => await OutputAsync(async () =>
    {
        if (OperatingSystem.IsWindows() && report is not null) await WindowsReportPrinter.PrintAsync(this, report);
    });

    private async Task OutputAsync(Func<Task> action)
    {
        if (busy) return;
        busy = true; ErrorText.IsVisible = false;
        SavePdfButton.IsEnabled = PrintButton.IsEnabled = PreviousButton.IsEnabled = NextButton.IsEnabled = CloseButton.IsEnabled = false;
        try { await action(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException
            or System.ComponentModel.Win32Exception or System.Runtime.InteropServices.COMException)
        { ErrorText.Text = Strings.ReportError + "\n" + error.Message; ErrorText.IsVisible = true; }
        finally
        {
            busy = false; SavePdfButton.IsEnabled = CloseButton.IsEnabled = true;
            PrintButton.IsEnabled = OperatingSystem.IsWindows();
            PreviousButton.IsEnabled = page > 0; NextButton.IsEnabled = report is not null && page + 1 < report.Pages.Count;
        }
    }
}
