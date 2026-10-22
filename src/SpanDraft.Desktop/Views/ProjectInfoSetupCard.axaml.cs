using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SpanDraft.Desktop.ViewModels;

namespace SpanDraft.Desktop.Views;

public partial class ProjectInfoSetupCard : UserControl
{
    public ProjectInfoSetupCard() => InitializeComponent();
    private void InputGotFocus(object? sender, RoutedEventArgs e)
    { if (sender is TextBox { DataContext: ProjectInfoInput input }) input.Begin(); }
    private void InputLostFocus(object? sender, RoutedEventArgs e)
    { if (sender is TextBox { DataContext: ProjectInfoInput input }) input.LoseFocus(); }
    private void InputKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: ProjectInfoInput input } || e.KeyModifiers != KeyModifiers.None) return;
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            if (input.Confirm()) input.Begin();
        }
        else if (e.Key == Key.Escape && input.IsEditing)
        { e.Handled = true; input.Cancel(); }
    }
}
