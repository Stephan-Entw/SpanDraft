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

    // The menu is built when it opens: a flyout is not part of the card's visual tree, so its items are given
    // the card's commands directly instead of binding to a data context they would not inherit.
    private void MenuClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || CardOf(sender) is not { } card) return;
        var flyout = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        flyout.Items.Add(new MenuItem { Header = Strings.SegmentInsertBefore, Command = card.InsertBeforeCommand });
        flyout.Items.Add(new MenuItem { Header = Strings.SegmentInsertAfter, Command = card.InsertAfterCommand });
        flyout.Items.Add(new MenuItem { Header = Strings.SegmentDuplicate, Command = card.DuplicateCommand });
        flyout.Items.Add(new MenuItem { Header = Strings.SegmentRemove, Command = card.RemoveCommand });
        flyout.ShowAt(button);
    }
}
