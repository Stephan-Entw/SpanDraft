using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SpanDraft.Desktop.ViewModels;

namespace SpanDraft.Desktop.Views;

public partial class BeamSetupCard : UserControl
{
    public BeamSetupCard() => InitializeComponent();
    private void LengthGotFocus(object? sender, RoutedEventArgs e)
    { if (DataContext is ProjectSetupViewModel { IsBusy: false } model) model.LengthInput.Begin(); }
    private void LengthLostFocus(object? sender, RoutedEventArgs e)
    { if (DataContext is ProjectSetupViewModel model) model.LengthInput.LoseFocus(); }
    private void LengthKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not ProjectSetupViewModel model || model.IsBusy || e.KeyModifiers != KeyModifiers.None) return;
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            if (model.LengthInput.Confirm()) model.LengthInput.Begin();
        }
        else if (e.Key == Key.Escape && model.LengthInput.IsEditing)
        { e.Handled = true; model.LengthInput.Cancel(); SectionCardButton.Focus(NavigationMethod.Tab); }
    }
}
