using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SpanDraft.Desktop.ViewModels;

namespace SpanDraft.Desktop.Views;

public partial class MaterialSelectionView : UserControl
{
    public MaterialSelectionView()
    {
        InitializeComponent();
        MaterialEntries.AddHandler(KeyDownEvent, EntriesKeyDown, RoutingStrategies.Tunnel);
    }
    private void SearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.KeyModifiers != KeyModifiers.None || DataContext is not MaterialSelectionViewModel { IsBusy: false } model) return;
        e.Handled = true;
        model.SelectedEntry ??= model.VisibleEntries.FirstOrDefault();
        if (model.SelectedEntry is not { } selected) { MaterialEntries.Focus(NavigationMethod.Tab); return; }
        MaterialEntries.ScrollIntoView(selected);
        Dispatcher.UIThread.Post(() => (MaterialEntries.ContainerFromItem(selected) ?? MaterialEntries).Focus(NavigationMethod.Tab));
    }
    private void EntriesKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.KeyModifiers != KeyModifiers.None) return;
        e.Handled = true; UseSelected();
    }
    private void EntriesDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is not Control source || (source as ListBoxItem ?? source.FindAncestorOfType<ListBoxItem>()) is null) return;
        e.Handled = true; UseSelected();
    }
    private void UseSelected()
    {
        if (DataContext is MaterialSelectionViewModel model && model.UseSelectedCommand.CanExecute(null)) model.UseSelectedCommand.Execute(null);
    }
}
