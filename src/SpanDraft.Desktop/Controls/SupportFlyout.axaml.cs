using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using SpanDraft.Desktop.ViewModels;

namespace SpanDraft.Desktop.Controls;

public partial class SupportFlyout : UserControl
{
    public SupportFlyout() => InitializeComponent();
    public void FocusPosition() { PositionInput.Focus(); PositionInput.SelectAll(); }

    private void TypeDropDownClosed(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (DataContext is EditorViewModel { SupportDraft: not null }) TypeInput.Focus();
        }, DispatcherPriority.Input);

    private void FlyoutKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not EditorViewModel editor) return;
        if (e.Key == Key.Escape) { editor.CancelSupportInteraction(); e.Handled = true; }
        else if (e.Key == Key.Enter) { editor.ConfirmSupport(); e.Handled = true; }
    }
}
