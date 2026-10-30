using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.ViewModels;

namespace SpanDraft.Desktop.Controls;

public partial class PointLoadFlyout : UserControl
{
    public PointLoadFlyout() => InitializeComponent();
    public void FocusPosition() { PositionInput.Focus(); PositionInput.SelectAll(); }

    private void ToggleSign(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorViewModel { LoadDraft: { } draft })
            draft.ValueText = LoadSignToggle.Apply(draft.ValueText, !draft.HasValueError, draft.Preview.Value);
    }

    private void FlyoutKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not EditorViewModel editor) return;
        if (e.Key == Key.Escape) { editor.CancelLoadInteraction(); e.Handled = true; }
        else if (e.Key == Key.Enter && SignButton.IsFocused) { ToggleSign(sender, e); e.Handled = true; }
        else if (e.Key == Key.Enter) { editor.ConfirmLoad(); e.Handled = true; }
    }
}
