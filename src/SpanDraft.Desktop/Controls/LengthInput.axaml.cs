using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SpanDraft.Desktop.ViewModels;

namespace SpanDraft.Desktop.Controls;

public partial class LengthInput : UserControl
{
    public LengthInput() => InitializeComponent();
    private LengthInputViewModel? Model => DataContext as LengthInputViewModel;

    public void FocusInput()
    {
        InputBox.Focus();
        InputBox.SelectAll();
    }

    private void InputGotFocus(object? sender, RoutedEventArgs e)
    {
        Model?.Begin();
    }

    private void InputLostFocus(object? sender, RoutedEventArgs e) => Model?.LoseFocus();

    private void InputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (Model?.Confirm() == true) Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Model?.Cancel();
            Focus();
            e.Handled = true;
        }
    }
}
