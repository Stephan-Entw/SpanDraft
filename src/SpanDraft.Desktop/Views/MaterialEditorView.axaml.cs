using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SpanDraft.Desktop.ViewModels;

namespace SpanDraft.Desktop.Views;

/// <summary>Shared form hosted by the owned material editor window.</summary>
public partial class MaterialEditorView : UserControl
{
    public MaterialEditorView() => InitializeComponent();

    private void NameLostFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MaterialEditorViewModel model) model.CompleteNameInput();
    }

    private void NumberLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: SetupNumberInput input }) input.CompleteInput();
    }

    private void StrengthTypeLostFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MaterialEditorViewModel model) model.CompleteStrengthTypeInput();
    }

    private void NameKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None)
        {
            // Enter in the designation continues editing rather than applying the draft.
            e.Handled = true;
            MaterialCategoryInput.Focus(NavigationMethod.Tab);
        }
    }
}
