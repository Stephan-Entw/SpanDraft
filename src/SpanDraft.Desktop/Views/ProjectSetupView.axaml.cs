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
    private ProjectSetupPage _observedPage;
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
        _observedPage = _model?.Page ?? ProjectSetupPage.Overview;
        if (_model is not null) _model.PropertyChanged += ModelChanged;
        FocusStep();
    }
    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ProjectSetupViewModel.Page) || _model is null) return;
        string? card = _model.IsOverview && _observedPage != ProjectSetupPage.Overview
            ? _observedPage == ProjectSetupPage.SectionDefinition ? "SectionCardButton" : "MaterialCardButton" : null;
        _observedPage = _model.Page;
        FocusStep(card);
    }
    private void FocusStep(string? preferred = null)
    {
        var model = _model; var page = model?.Page;
        Dispatcher.UIThread.Post(() =>
        {
            if (model is null || model != _model || page != model.Page) return;
            var controls = this.GetVisualDescendants().OfType<Control>().Where(c => c.IsEffectivelyVisible && c.IsEffectivelyEnabled).ToArray();
            var control = controls.FirstOrDefault(c => preferred is not null && c.Name == preferred)
                ?? controls.FirstOrDefault(c => c is TextBox) ?? controls.FirstOrDefault(c => c.Focusable);
            control?.Focus(NavigationMethod.Tab);
        });
    }
    private async void SetupKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || e.KeyModifiers != KeyModifiers.None || DataContext is not ProjectSetupViewModel model || model.IsSectionDefinition) return;
        if (e.Key == Key.Escape) { e.Handled = true; model.Escape(); }
        else if (e.Key == Key.Enter && !model.IsOverview && e.Source is not Button && e.Source is not ComboBox)
        { e.Handled = true; await model.ConfirmAsync(); }
    }
}
