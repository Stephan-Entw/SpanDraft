using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SpanDraft.Desktop.ViewModels;

namespace SpanDraft.Desktop.Views;

public partial class SegmentSetupCard : UserControl
{
    public SegmentSetupCard() => InitializeComponent();

    // The split position and the segment start are two inline length inputs with the same session handling as the beam length.
    private LengthInputViewModel? InputOf(object? sender) => DataContext is not ProjectSetupViewModel model || sender is not Control control ? null
        : control.Name == nameof(SplitPositionInput) ? model.SplitInput : model.BoundaryInput;

    private void InputGotFocus(object? sender, RoutedEventArgs e)
    { if (DataContext is ProjectSetupViewModel { IsBusy: false } && InputOf(sender) is { } input) input.Begin(); }

    private void InputLostFocus(object? sender, RoutedEventArgs e) => InputOf(sender)?.LoseFocus();

    private void InputKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not ProjectSetupViewModel { IsBusy: false } || InputOf(sender) is not { } input || e.KeyModifiers != KeyModifiers.None) return;
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            if (input.Confirm()) input.Begin();
        }
        else if (e.Key == Key.Escape && input.IsEditing)
        { e.Handled = true; input.Cancel(); SegmentList.Focus(NavigationMethod.Tab); }
    }
}
