using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using SpanDraft.Desktop.Controls;
using SpanDraft.Desktop.ViewModels;

namespace SpanDraft.Desktop.Views;

public partial class SectionDefinitionView : UserControl
{
    private readonly string _groupSuffix = Guid.NewGuid().ToString("N");
    private SectionDefinitionViewModel? _model;
    private SectionDefinitionLayout? _layoutState;
    private double _inputWidth = double.NaN, _valuesWidth = double.NaN;
    private bool _observedEditingName;
    private int _nameEditRevision;
    private string? _observedLibraryError;
    private bool _libraryVisibilityUpdatePending;
    public SectionDefinitionView()
    {
        InitializeComponent();
        DataContextChanged += (_,_) => Observe();
        AttachedToVisualTree += (_,_) => Observe();
        DetachedFromVisualTree += (_,_) =>
        {
            if (_model is not null) _model.PropertyChanged -= ModelChanged;
            _model = null;
        };
        AddHandler(KeyDownEvent, ViewKeyDown, RoutingStrategies.Bubble);
        NameEditor.AddHandler(LostFocusEvent, NameLostFocus, RoutingStrategies.Bubble);
        LayoutUpdated += (_,_) => ScheduleLibraryVisibilityUpdate();
    }
    private void Observe()
    {
        if (_model is not null) _model.PropertyChanged -= ModelChanged;
        _model = DataContext as SectionDefinitionViewModel;
        _nameEditRevision++;
        _observedEditingName = false;
        _observedLibraryError = null;
        if (_model is null) return;
        _model.PropertyChanged += ModelChanged;
        _layoutState = null; BuildConfigurationOptions(); Reflow();
    }
    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_model is null) return;
        if (e.PropertyName == nameof(SectionDefinitionViewModel.LayoutState)) Reflow();
        if (e.PropertyName == nameof(SectionDefinitionViewModel.Configuration))
            foreach (var radio in ConfigurationOptions.Children.OfType<RadioButton>()) radio.IsChecked = Equals(radio.Tag,_model.Configuration.Value);
        if (e.PropertyName == nameof(SectionDefinitionViewModel.IsEditingName) && _observedEditingName != _model.IsEditingName)
        {
            _observedEditingName = _model.IsEditingName;
            int revision = ++_nameEditRevision;
            if (_model.IsEditingName)
                Dispatcher.UIThread.Post(() =>
                {
                    if (_model?.IsEditingName == true && revision == _nameEditRevision) { NameInput.Focus(); NameInput.SelectAll(); }
                });
        }
        if (e.PropertyName == nameof(SectionDefinitionViewModel.LibraryError) && _observedLibraryError != _model.LibraryError)
        {
            _observedLibraryError = _model.LibraryError;
            var model = _model;
            var error = model.LibraryError;
            if (error is not null)
                Dispatcher.UIThread.Post(() =>
                {
                    if (_model == model && model.LibraryError == error && !model.IsEditingName)
                        LibraryErrorHint.Focus();
                });
        }
    }
    protected override Avalonia.Size MeasureOverride(Avalonia.Size availableSize)
    {
        Reflow(availableSize.Width);
        return base.MeasureOverride(availableSize);
    }
    protected override Avalonia.Size ArrangeOverride(Avalonia.Size finalSize)
    {
        InputWorkspace.Classes.Set("compactSectionInputs", finalSize.Height < 750);
        var arrangedSize = base.ArrangeOverride(finalSize);
        ScheduleLibraryVisibilityUpdate();
        return arrangedSize;
    }
    private void ScheduleLibraryVisibilityUpdate()
    {
        if (_libraryVisibilityUpdatePending || _model?.HasDefinition != true) return;
        _libraryVisibilityUpdatePending = true;
        // Changing visibility during Arrange can leave Grid's measured rows stale.
        // Coalesce updates and apply them after the current layout pass finishes.
        Dispatcher.UIThread.Post(() =>
        {
            _libraryVisibilityUpdatePending = false;
            UpdateLibraryVisibility();
        });
    }
    private void UpdateLibraryVisibility()
    {
        if (_model?.HasDefinition != true || InputWorkspace.Bounds.Width <= 0) return;
        // Measure the heading itself even when its containing library region is hidden.
        // This keeps the hypothetical scroll height current after resizing or text reflow.
        if (!LibraryQuickSelection.IsVisible)
            LibraryHeading.Measure(new Avalonia.Size(PinnedInputs.Bounds.Width, double.PositiveInfinity));
        double scrollHeight = InputWorkspace.Bounds.Height - PinnedInputs.DesiredSize.Height - LibraryHeading.DesiredSize.Height;
        bool visible = _model.ShowLibraryOption && scrollHeight >= 96;
        if (LibraryQuickSelection.IsVisible != visible) LibraryQuickSelection.IsVisible = visible;
    }
    private void Reflow(double? availableWidth = null)
    {
        if (_model is null) return;
        double width = availableWidth ?? Bounds.Width;
        if (!double.IsFinite(width) || width <= 0) return;
        double inputWidth = Math.Clamp(width * .2267, 310, 340);
        double valuesWidth = Math.Clamp(width * .2693, 360, 404);
        if (_layoutState == _model.LayoutState && _inputWidth == inputWidth && _valuesWidth == valuesWidth) return;
        _layoutState = _model.LayoutState; _inputWidth = inputWidth; _valuesWidth = valuesWidth;
        string input = inputWidth.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string values = valuesWidth.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string columns = _model.LayoutState switch
        {
            SectionDefinitionLayout.FormSelection => "*",
            // Keep the sketch in the same column while properties are hidden or shown.
            SectionDefinitionLayout.ParametricDraft or SectionDefinitionLayout.ParametricSection => $"{input},*,{values}",
            _ => $"{input},*"
        };
        DefinitionColumns.ColumnDefinitions = new ColumnDefinitions(columns);
        // Hidden columns belong to the sole initial cell. Moving a visible column also
        // invalidates Avalonia's cached cell assignments when the column count changes.
        Grid.SetColumn(PreviewColumn, _model.ShowPreview ? 1 : 0);
        Grid.SetColumn(ValuesColumn, _model.ShowValues ? _model.IsManual ? 1 : 2 : 0);
    }
    private void BuildConfigurationOptions()
    {
        ConfigurationOptions.Children.Clear();
        if (_model is null) return;
        foreach (var choice in _model.Configurations)
        {
            var radio = new RadioButton { Content=choice.Label, Tag=choice.Value, GroupName="ManualConfiguration"+_groupSuffix, IsChecked=choice.Value==_model.Configuration.Value };
            Avalonia.Automation.AutomationProperties.SetName(radio,choice.Label);
            radio.Click += (_,_) => { if (_model is not null) _model.Configuration=choice; };
            ConfigurationOptions.Children.Add(radio);
        }
    }
    private void AxisBlockSelected(object? sender, RoutedEventArgs e)
    {
        if (sender is SectionAxisBlock { DataContext: SectionDefinitionAxisValue block } && block.IsSelectable && _model is not null)
            _model.SelectedAxis = block.Axis;
    }
    private void SelectForm(object? sender,RoutedEventArgs e) { if (sender is Button { Tag: SectionEditorType type }) _model?.SelectForm(type); }
    private void LoadPreset(object? sender, RoutedEventArgs e)
    { if (sender is Button { DataContext: SectionDefinitionPreset preset }) _model?.LoadPreset(preset.Entry); }
    private void SelectManual(object? sender,RoutedEventArgs e) => _model?.SelectForm(SectionEditorType.Manual);
    private void InputKeyDown(object? sender,KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None && sender is TextBox { DataContext: SectionDefinitionInput input })
        { e.Handled=true; input.Commit(); }
    }
    private void InputLostFocus(object? sender,RoutedEventArgs e)
    {
        if (_model is null || _model.PreserveInputs || sender is not TextBox { DataContext: SectionDefinitionInput input }) return;
        // Untouched fields must remain neutral; a normal visit followed by blur is an interaction.
        input.Commit(); _model.HighlightedParameter=null;
    }
    private void InputGotFocus(object? sender,RoutedEventArgs e)
    { if (sender is TextBox { DataContext: SectionDefinitionInput input } && _model is not null) _model.HighlightedParameter=input.Key; }
    private void NameKeyDown(object? sender,KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled=true;
            if (_model is not null) { _model.PendingName = NameInput.Text ?? ""; _model.CommitName(); }
        }
        else if (e.Key == Key.Escape) { e.Handled=true; _model?.CancelNameEdit(); }
    }
    private void NameLostFocus(object? sender,RoutedEventArgs e)
    {
        var model = _model;
        int revision = _nameEditRevision;
        Dispatcher.UIThread.Post(() =>
        {
            if (_model == model && revision == _nameEditRevision && model?.IsEditingName == true && !model.PreserveInputs && !NameEditor.IsKeyboardFocusWithin)
                model.CommitName();
        });
    }
    private void ViewKeyDown(object? sender,KeyEventArgs e)
    {
        if (e.Handled || e.Key != Key.Escape || _model is null) return;
        e.Handled=true;
        if (_model.IsEditingName) _model.CancelNameEdit();
        else _model.CancelCommand.Execute(null);
    }
}
