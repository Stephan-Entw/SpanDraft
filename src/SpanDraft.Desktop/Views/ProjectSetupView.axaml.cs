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
    private bool _definitionOpen;
    public ProjectSetupView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Observe();
        AttachedToVisualTree += (_, _) => Observe();
        DetachedFromVisualTree += (_, _) => { if (_model is not null) _model.PropertyChanged -= ModelChanged; _model = null; };
        AddHandler(KeyDownEvent, SetupKeyDown, RoutingStrategies.Bubble);
    }
    private void TestSectionDefinition(object? sender, RoutedEventArgs e)
    {
        if (_definitionOpen || _model is not { Mode: ProjectSetupMode.Create, IsBusy: false } model
            || TopLevel.GetTopLevel(this) is not Window owner) return;
        var library = model.Libraries is { } main ? new Sections.SectionDefinitionLibrary(main) : null;
        var definition = new SectionDefinitionViewModel(model.ResultPresentation, library);
        var previousContent = owner.Content;
        var preview = new SectionDefinitionView { DataContext = definition };
        _definitionOpen = true;
        definition.CloseRequested += ReturnToSetup;
        owner.Closing += GuardSave;
        owner.Closed += OwnerClosed;
        // Keep the actual application window, its native chrome and its current geometry.
        // This remains a preview: accepting a section does not change project/setup state.
        owner.Content = preview;

        void ReturnToSetup(Sections.SectionDefinitionResult? result)
        {
            Cleanup();
            owner.Content = previousContent;
        }
        void GuardSave(object? sender, WindowClosingEventArgs args)
        { if (definition.IsBusy) args.Cancel = true; }
        void OwnerClosed(object? sender, EventArgs args) => Cleanup();
        void Cleanup()
        {
            definition.CloseRequested -= ReturnToSetup;
            owner.Closing -= GuardSave;
            owner.Closed -= OwnerClosed;
            _definitionOpen = false;
        }
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
