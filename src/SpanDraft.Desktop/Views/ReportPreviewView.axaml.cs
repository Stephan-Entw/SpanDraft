using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Reporting;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.ViewModels;

namespace SpanDraft.Desktop.Views;

public partial class ReportPreviewView : UserControl
{
    private ReportPreviewViewModel? model;
    private double wheelDelta;
    private bool wheelZooming;
    private (Point Relative, Point Target)? zoomAnchor;

    public ReportPreviewView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Observe();
        AttachedToVisualTree += (_, _) =>
        {
            Observe();
            Dispatcher.UIThread.Post(() => { if (TopLevel.GetTopLevel(this) is not null) PageFitButton.Focus(); });
        };
        DetachedFromVisualTree += (_, _) =>
        {
            ResetWheel();
            if (model is not null) model.PropertyChanged -= ModelChanged;
            model = null; PageView.Page = null;
        };
        PageScroll.PropertyChanged += (_, e) =>
        {
            if (e.Property == ScrollViewer.ViewportProperty) UpdateViewport();
        };
        PageScroll.AddHandler(PointerWheelChangedEvent, PreviewWheelChanged, RoutingStrategies.Tunnel);
        LayoutUpdated += (_, _) => RestoreZoomAnchor();
    }

    private void Observe()
    {
        ResetWheel();
        if (model is not null) model.PropertyChanged -= ModelChanged;
        model = DataContext as ReportPreviewViewModel;
        if (model is not null) model.PropertyChanged += ModelChanged;
        PageView.Page = model?.Picture;
        UpdateViewport();
    }

    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ReportPreviewViewModel.Picture)) PageView.Page = model?.Picture;
        if (e.PropertyName == nameof(ReportPreviewViewModel.PageIndex)) { ResetWheel(); PageScroll.Offset = default; }
        if (e.PropertyName == nameof(ReportPreviewViewModel.Zoom) && !wheelZooming
            || e.PropertyName == nameof(ReportPreviewViewModel.IsBusy)) ResetWheel();
    }

    private void ResetWheel() { wheelDelta = 0; zoomAnchor = null; }

    private void PreviewWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (e.Handled) return;
        if (model?.CanOutput != true || !IsEffectivelyEnabled) { ResetWheel(); e.Handled = true; return; }
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) { ResetWheel(); return; }
        e.Handled = true;
        if (PageView.Bounds.Width <= 0 || PageView.Bounds.Height <= 0) return;
        if (e.Delta.Y > 0 && !model.ZoomInCommand.CanExecute(null)
            || e.Delta.Y < 0 && !model.ZoomOutCommand.CanExecute(null)) { wheelDelta = 0; return; }
        wheelDelta += e.Delta.Y;
        var position = e.GetPosition(PageView);
        var relative = new Point(Math.Clamp(position.X / PageView.Bounds.Width, 0, 1),
            Math.Clamp(position.Y / PageView.Bounds.Height, 0, 1));
        var target = PageView.TranslatePoint(new(relative.X * PageView.Bounds.Width, relative.Y * PageView.Bounds.Height), PageScroll);
        double before = model.Zoom;
        wheelZooming = true;
        try
        {
            while (Math.Abs(wheelDelta) >= 1 - 1e-9)
            {
                var command = wheelDelta > 0 ? model.ZoomInCommand : model.ZoomOutCommand;
                if (!command.CanExecute(null)) { wheelDelta = 0; break; }
                double direction = Math.Sign(wheelDelta);
                command.Execute(null); wheelDelta -= direction;
            }
        }
        finally { wheelZooming = false; }
        if (model.Zoom != before && target is { } point) zoomAnchor = (relative, point);
    }

    private void RestoreZoomAnchor()
    {
        if (zoomAnchor is not { } anchor) return;
        zoomAnchor = null;
        if (model?.CanOutput != true) return;
        // Translate after layout to include paper margins, centering and scrollbar changes.
        var point = PageView.TranslatePoint(new(anchor.Relative.X * PageView.Bounds.Width,
            anchor.Relative.Y * PageView.Bounds.Height), PageScroll);
        if (point is { } actual) PageScroll.Offset += actual - anchor.Target;
    }

    private void UpdateViewport() => model?.UpdateViewport(PageScroll.Viewport.Width, PageScroll.Viewport.Height);

    private void PreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || e.Key != Key.Escape || e.KeyModifiers != KeyModifiers.None || model?.CloseCommand.CanExecute(null) != true) return;
        e.Handled = true; model.CloseCommand.Execute(null);
    }

    private async void SavePdf(object? sender, RoutedEventArgs e)
    {
        if (model is not { } preview) return;
        await preview.OutputAsync(async () =>
        {
            var owner = TopLevel.GetTopLevel(this) as Window ?? throw new InvalidOperationException("The report needs a workspace window.");
            using var folder = preview.ProjectPath is null ? null
                : await owner.StorageProvider.TryGetFolderFromPathAsync(Path.GetDirectoryName(preview.ProjectPath)!);
            using var destination = await owner.StorageProvider.SaveFilePickerAsync(new()
            {
                Title = Strings.ReportSavePdf, DefaultExtension = "pdf", ShowOverwritePrompt = true,
                FileTypeChoices = [new FilePickerFileType("PDF") { Patterns = ["*.pdf"] }], SuggestedStartLocation = folder,
                SuggestedFileName = Path.ChangeExtension(ProjectSaveFileName.Suggest(null, preview.Report.Data.Title, Strings.Untitled), ".pdf")
            });
            if (destination is not null)
                await preview.SavePdfAsync(destination.TryGetLocalPath() ?? throw new IOException(Strings.LocalFilesOnly));
        });
    }

    private async void PrintReport(object? sender, RoutedEventArgs e)
    {
        if (!OperatingSystem.IsWindows() || model is not { } preview) return;
        await preview.OutputAsync(async () =>
        {
            var owner = TopLevel.GetTopLevel(this) as Window ?? throw new InvalidOperationException("The report needs a workspace window.");
            if (OperatingSystem.IsWindows()) await WindowsReportPrinter.PrintAsync(owner, preview.Report);
        });
    }
}
