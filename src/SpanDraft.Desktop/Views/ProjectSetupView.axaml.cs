using Avalonia.Controls;
using Avalonia.Controls.Templates;
using SpanDraft.Core.Sections;
using SpanDraft.Desktop.Resources;
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
        DetachedFromVisualTree += (_, _) =>
        {
            if (_model is not null) _model.PropertyChanged -= ModelChanged;
            _model = null;
        };
        RefreshSectionTemplate();
    }

    private void Observe()
    {
        if (_model is not null) _model.PropertyChanged -= ModelChanged;
        _model = DataContext as ProjectSetupViewModel;
        if (_model is not null) _model.PropertyChanged += ModelChanged;
        RefreshSectionTemplate();
    }

    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProjectSetupViewModel.ResultPresentation)) RefreshSectionTemplate();
    }

    private void RefreshSectionTemplate()
    {
        SectionPicker.ItemTemplate = new FuncDataTemplate<ISectionDefinition>((section, _) =>
            new TextBlock { Text = section is null ? null : SectionDisplay.Name(section, _model?.ResultPresentation.Profile) });
    }
}
