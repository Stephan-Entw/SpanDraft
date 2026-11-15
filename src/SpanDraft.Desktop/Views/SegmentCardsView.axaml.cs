using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.ViewModels;

namespace SpanDraft.Desktop.Views;

/// <summary>The segment cards of the project setup: one per segment, with the length, section, material and a menu.</summary>
public partial class SegmentCardsView : UserControl
{
    public SegmentCardsView() => InitializeComponent();

    private static SetupSegmentCard? CardOf(object? sender) => (sender as Control)?.DataContext as SetupSegmentCard;
    private bool Busy => DataContext is ProjectSetupViewModel { IsBusy: true };

    private void LengthGotFocus(object? sender, RoutedEventArgs e)
    { if (!Busy) CardOf(sender)?.LengthInput.Begin(); }

    private void LengthLostFocus(object? sender, RoutedEventArgs e) => CardOf(sender)?.LengthInput.LoseFocus();

    private void LengthKeyDown(object? sender, KeyEventArgs e)
    {
        if (Busy || CardOf(sender) is not { } card || e.KeyModifiers != KeyModifiers.None) return;
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            if (card.LengthInput.Confirm()) card.LengthInput.Begin();
        }
        else if (e.Key == Key.Escape && card.LengthInput.IsEditing)
        {
            e.Handled = true; card.LengthInput.Cancel();
            (sender as Control)?.FindAncestorOfType<Border>()?.GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(b => b.Name == "SectionCardButton")?.Focus(NavigationMethod.Tab);
        }
    }
}
