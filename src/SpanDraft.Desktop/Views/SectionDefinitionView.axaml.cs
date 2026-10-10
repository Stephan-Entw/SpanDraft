using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using SpanDraft.Desktop.ViewModels;

namespace SpanDraft.Desktop.Views;

public partial class SectionDefinitionView : UserControl
{
    private readonly string _groupSuffix = Guid.NewGuid().ToString("N");
    private SectionDefinitionViewModel? _model;
    public SectionDefinitionView()
    {
        InitializeComponent();
        DataContextChanged += (_,_) => Observe();
        AttachedToVisualTree += (_,_) => Observe();
        DetachedFromVisualTree += (_,_) => { if (_model is not null) _model.PropertyChanged -= ModelChanged; _model = null; };
        AddHandler(KeyDownEvent, ViewKeyDown, RoutingStrategies.Bubble);
    }
    private void Observe()
    {
        if (_model is not null) _model.PropertyChanged -= ModelChanged;
        _model = DataContext as SectionDefinitionViewModel;
        if (_model is null) return;
        _model.PropertyChanged += ModelChanged;
        BuildConfigurationOptions(); BuildAxisOptions(); Reflow();
    }
    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_model is null) return;
        if (e.PropertyName == nameof(SectionDefinitionViewModel.Type)) Reflow();
        if (e.PropertyName == nameof(SectionDefinitionViewModel.AvailableAxes)) BuildAxisOptions();
        if (e.PropertyName == nameof(SectionDefinitionViewModel.SelectedAxis))
            foreach (var radio in AxisOptions.Children.OfType<RadioButton>()) radio.IsChecked = Equals(radio.Tag,_model.SelectedAxis);
        if (e.PropertyName == nameof(SectionDefinitionViewModel.Configuration))
            foreach (var radio in ConfigurationOptions.Children.OfType<RadioButton>()) radio.IsChecked = Equals(radio.Tag,_model.Configuration.Value);
        if (e.PropertyName == nameof(SectionDefinitionViewModel.IsEditingName) && _model.IsEditingName)
            Dispatcher.UIThread.Post(() => { if (_model?.IsEditingName == true) { NameInput.Focus(); NameInput.SelectAll(); } });
    }
    private void Reflow()
    {
        if (_model is null) return;
        DefinitionColumns.ColumnDefinitions = new ColumnDefinitions(_model.IsManual ? "34*,66*" : "28*,44*,28*");
        Grid.SetColumn(ValuesColumn,_model.IsManual ? 1 : 2);
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
    private void BuildAxisOptions()
    {
        if (_model is null) return;
        var axes=_model.AvailableAxes;
        if (AxisOptions.Children.OfType<RadioButton>().Select(r=>r.Tag).SequenceEqual(axes.Select(a=>(object)a.Value))) return;
        AxisOptions.Children.Clear();
        foreach (var choice in axes)
        {
            var radio = new RadioButton { Content=choice.Label,Tag=choice.Value,GroupName="DefinitionBendingAxis"+_groupSuffix,IsChecked=choice.Value==_model.SelectedAxis };
            Avalonia.Automation.AutomationProperties.SetName(radio,choice.Label);
            radio.Click += (_,_) => { if (_model is not null) _model.SelectedAxis=choice.Value; };
            AxisOptions.Children.Add(radio);
        }
    }
    private void SelectForm(object? sender,RoutedEventArgs e) { if (sender is Button { Tag: SectionEditorType type }) _model?.SelectForm(type); }
    private void SelectManual(object? sender,RoutedEventArgs e) => _model?.SelectForm(SectionEditorType.Manual);
    private void InputKeyDown(object? sender,KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None && sender is TextBox { DataContext: SectionDefinitionInput input })
        { e.Handled=true; input.Commit(); }
    }
    private void InputLostFocus(object? sender,RoutedEventArgs e)
    {
        if (_model?.IsBusy != false || sender is not TextBox { DataContext: SectionDefinitionInput input }) return;
        // Untouched fields must remain neutral; a normal visit followed by blur is an interaction.
        input.Commit(); _model.HighlightedParameter=null;
    }
    private void InputGotFocus(object? sender,RoutedEventArgs e)
    { if (sender is TextBox { DataContext: SectionDefinitionInput input } && _model is not null) _model.HighlightedParameter=input.Key; }
    private void NameKeyDown(object? sender,KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { e.Handled=true; _model?.CommitName(); }
        else if (e.Key == Key.Escape) { e.Handled=true; _model?.CancelNameEdit(); }
    }
    private void NameLostFocus(object? sender,RoutedEventArgs e) => _model?.CommitName();
    private void ViewKeyDown(object? sender,KeyEventArgs e)
    {
        if (e.Handled || e.Key != Key.Escape || _model is null) return;
        e.Handled=true;
        if (_model.IsEditingName) _model.CancelNameEdit(); else _model.CancelCommand.Execute(null);
    }
}
