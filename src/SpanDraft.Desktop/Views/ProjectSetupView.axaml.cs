using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SpanDraft.Desktop.ViewModels;
using System.ComponentModel;

namespace SpanDraft.Desktop.Views;

public partial class ProjectSetupView : UserControl
{
    private ProjectSetupViewModel? _model;
    public ProjectSetupView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Observe();
        AttachedToVisualTree += (_, _) => Observe();
        DetachedFromVisualTree += (_, _) => { if (_model is not null) _model.PropertyChanged -= ModelChanged; _model = null; };
        AddHandler(KeyDownEvent, SetupKeyDown, RoutingStrategies.Bubble);
    }
    private void Observe()
    {
        if (_model is not null) _model.PropertyChanged -= ModelChanged;
        _model = DataContext as ProjectSetupViewModel;
        if (_model is not null) _model.PropertyChanged += ModelChanged;
        FocusStep();
    }
    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName == nameof(ProjectSetupViewModel.Page)) FocusStep(); }
    private void FocusStep() => Dispatcher.UIThread.Post(() =>
    {
        var control = this.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.IsEffectivelyVisible && c.IsEffectivelyEnabled && c is TextBox)
            ?? this.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.IsEffectivelyVisible && c.IsEffectivelyEnabled && c.Focusable);
        control?.Focus(NavigationMethod.Tab);
    });
    private async void SetupKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || e.KeyModifiers != KeyModifiers.None || DataContext is not ProjectSetupViewModel model) return;
        if (e.Key == Key.Escape) { e.Handled = true; model.Escape(); }
        else if (e.Key == Key.Enter && e.Source is not Button && e.Source is not ComboBox)
        { e.Handled = true; await model.ConfirmAsync(); }
    }
}
