using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using SpanDraft.Desktop.ViewModels;

namespace SpanDraft.Desktop.Controls;

public partial class BeamEditorSurface : UserControl
{
    public BeamEditorSurface() => InitializeComponent();

    private void SurfaceSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        var geometry = BeamViewport.Fit(Bounds.Width, Bounds.Height, 1);
        Canvas.SetLeft(DimensionOverlay, geometry.Midpoint - DimensionOverlay.Width / 2);
        Canvas.SetTop(DimensionOverlay, geometry.DimensionY - DimensionButton.Height / 2);
    }

    private void BeginDimensionEdit(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not EditorViewModel editor) return;
        editor.DimensionLength.Begin();
        Dispatcher.UIThread.Post(DimensionInput.FocusInput, DispatcherPriority.Input);
    }
}
