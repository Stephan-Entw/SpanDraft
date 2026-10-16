using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SpanDraft.Desktop.ViewModels;

namespace SpanDraft.Desktop.Views;

public partial class MaterialSelectionView : UserControl
{
    private MaterialSelectionViewModel? _model;
    private Vector _browseOffset;
    private MaterialSelectionItem? _browseSelection;
    public MaterialSelectionView()
    {
        InitializeComponent();
        MaterialEntries.AddHandler(KeyDownEvent, EntriesKeyDown, RoutingStrategies.Tunnel);
        DataContextChanged += (_, _) => Observe();
        AttachedToVisualTree += (_, _) => Observe();
        DetachedFromVisualTree += (_, _) => { if (_model is not null) _model.PropertyChanged -= ModelChanged; _model = null; };
    }
    private void Observe()
    {
        if (_model is not null) _model.PropertyChanged -= ModelChanged;
        _model = DataContext as MaterialSelectionViewModel;
        if (_model is not null) _model.PropertyChanged += ModelChanged;
    }
    private ScrollViewer? ListScroll => MaterialEntries.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MaterialSelectionViewModel.IsEditing) || _model is not { } model) return;
        if (model.IsEditing)
        {
            _browseOffset = ListScroll?.Offset ?? default;
            _browseSelection = model.SelectedEntry;
        }
        Dispatcher.UIThread.Post(() =>
        {
            if (_model != model || TopLevel.GetTopLevel(this) is null) return;
            if (model.IsEditing)
                this.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(t => t.Name == "MaterialNameInput")?.Focus(NavigationMethod.Tab);
            else
            {
                if (ReferenceEquals(_browseSelection, model.SelectedEntry) && ListScroll is { } scroll) scroll.Offset = _browseOffset;
                if (model.SelectedEntry is { } selected)
                {
                    if (!ReferenceEquals(_browseSelection, selected) && !selected.IsProject) MaterialEntries.ScrollIntoView(selected);
                    (selected.IsProject ? ProjectMaterialEntry : MaterialEntries.ContainerFromItem(selected) ?? MaterialEntries).Focus(NavigationMethod.Tab);
                }
                else MaterialSearchInput.Focus(NavigationMethod.Tab);
            }
        });
    }
    private void SearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.KeyModifiers != KeyModifiers.None || DataContext is not MaterialSelectionViewModel { IsBusy: false } model) return;
        e.Handled = true;
        model.SelectedEntry = model.FilteredEntries.FirstOrDefault();
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
