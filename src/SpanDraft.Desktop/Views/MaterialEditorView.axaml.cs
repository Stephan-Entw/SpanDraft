using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using SpanDraft.Desktop.ViewModels;

namespace SpanDraft.Desktop.Views;

public partial class MaterialEditorView : UserControl
{
    private MaterialEditorViewModel? _model;
    public MaterialEditorView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Observe();
        DetachedFromVisualTree += (_, _) => { if (_model is not null) _model.PropertyChanged -= ModelChanged; _model = null; };
        AttachedToVisualTree += (_, _) => Observe();
    }
    private void Observe()
    {
        if (_model is not null) _model.PropertyChanged -= ModelChanged;
        _model = DataContext as MaterialEditorViewModel;
        if (_model is not null) _model.PropertyChanged += ModelChanged;
    }
    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MaterialEditorViewModel.IsEditingName) || _model?.IsEditingName != true) return;
        Dispatcher.UIThread.Post(() => { if (_model?.IsEditingName == true) MaterialNameInput.Focus(NavigationMethod.Tab); });
    }
    private void NameLostFocus(object? sender, RoutedEventArgs e)
    { if (_model is { IsBusy: false, IsEditingName: true }) _model.EndNameEdit(); }
    private void NameKeyDown(object? sender, KeyEventArgs e)
    {
        if (_model is not { IsBusy: false } || e.KeyModifiers != KeyModifiers.None) return;
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            if (_model.EndNameEdit()) MaterialNameButton.Focus(NavigationMethod.Tab);
        }
        else if (e.Key == Key.Escape && _model.IsEditingName)
        {
            e.Handled = true; _model.CancelNameEdit();
            if (!_model.ShowNameInput) MaterialNameButton.Focus(NavigationMethod.Tab);
        }
    }
}
