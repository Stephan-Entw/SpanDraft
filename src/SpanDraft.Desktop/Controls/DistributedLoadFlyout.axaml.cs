using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.ViewModels;

namespace SpanDraft.Desktop.Controls;

public partial class DistributedLoadFlyout : UserControl
{
    public DistributedLoadFlyout() => InitializeComponent();
    public void FocusStart() { StartInput.Focus(); StartInput.SelectAll(); }

    private void ToggleSign(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorViewModel { DistributedLoadDraft: { } draft })
            draft.IntensityText = LoadSignToggle.Apply(draft.IntensityText, !draft.HasIntensityError, draft.Preview.Intensity);
    }

    private void FlyoutKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not EditorViewModel editor) return;
        if (e.Key == Key.Escape) { editor.CancelDistributedLoadInteraction(); e.Handled = true; }
        else if (e.Key == Key.Enter && SignButton.IsFocused) { ToggleSign(sender, e); e.Handled = true; }
        else if (e.Key == Key.Enter) { editor.ConfirmDistributedLoad(); e.Handled = true; }
    }
}
