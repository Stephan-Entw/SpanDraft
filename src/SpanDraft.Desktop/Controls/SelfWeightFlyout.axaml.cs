using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SpanDraft.Desktop.ViewModels;

namespace SpanDraft.Desktop.Controls;

public partial class SelfWeightFlyout : UserControl
{
    public SelfWeightFlyout() => InitializeComponent();
    public void FocusAction() => VisibilityAction.Focus();
    private void Close(object? sender, RoutedEventArgs e) => (DataContext as EditorViewModel)?.CancelSelfWeightInteraction();
    private void FlyoutKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || DataContext is not EditorViewModel editor) return;
        editor.CancelSelfWeightInteraction();
        e.Handled = true;
    }
}
