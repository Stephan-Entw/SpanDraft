using Avalonia.Controls;
using Avalonia.Input;
using SpanDraft.Desktop.ViewModels;

namespace SpanDraft.Desktop.Views;

public partial class EditorView : UserControl
{
    public EditorView() => InitializeComponent();

    private void EditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || DataContext is not EditorViewModel editor) return;
        editor.CancelEditorInteraction();
        if (editor.DimensionLength.IsEditing || editor.DimensionLength.HasError || editor.ConstraintConflict is not null)
            editor.DimensionLength.Cancel();
        e.Handled = true;
    }
}
