using Avalonia.Controls;
using Avalonia.Input;
using SpanDraft.Desktop.ViewModels;

namespace SpanDraft.Desktop.Controls;

public partial class MomentReleaseFlyout : UserControl
{
    public MomentReleaseFlyout() => InitializeComponent();
    public void FocusPosition() { PositionInput.Focus(); PositionInput.SelectAll(); }

    private void FlyoutKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not EditorViewModel editor) return;
        if (e.Key == Key.Escape) { editor.CancelMomentReleaseInteraction(); e.Handled = true; }
        else if (e.Key == Key.Enter) { editor.ConfirmMomentRelease(); e.Handled = true; }
    }
}
