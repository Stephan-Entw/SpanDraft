using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using SpanDraft.Desktop.ViewModels;

namespace SpanDraft.Desktop.Views;

public partial class EditorView : UserControl
{
    public EditorView()
    {
        InitializeComponent();
        EditorScroll.PropertyChanged += ScrollRangeChanged;
        DiagramScroll.PropertyChanged += ScrollRangeChanged;
    }

    private void ScrollRangeChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != ScrollViewer.ScrollBarMaximumProperty) return;
        var visibility = EditorScroll.ScrollBarMaximum.Y > 0 || DiagramScroll.ScrollBarMaximum.Y > 0
            ? ScrollBarVisibility.Visible : ScrollBarVisibility.Hidden;
        EditorScroll.VerticalScrollBarVisibility = visibility;
        DiagramScroll.VerticalScrollBarVisibility = visibility;
    }

    private void WorkspaceSizeChanged(object? sender, SizeChangedEventArgs e) =>
        EditorScroll.MaxHeight = e.NewSize.Height * 0.6;

    private void EditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || DataContext is not EditorViewModel editor) return;
        editor.CancelEditorInteraction();
        if (editor.DimensionLength.IsEditing || editor.DimensionLength.HasError || editor.ConstraintConflict is not null)
            editor.DimensionLength.Cancel();
        e.Handled = true;
    }
}
