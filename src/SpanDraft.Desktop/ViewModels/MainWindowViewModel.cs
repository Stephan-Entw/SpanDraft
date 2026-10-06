using SpanDraft.Analysis;
using SpanDraft.Core.Beams;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.State;

namespace SpanDraft.Desktop.ViewModels;

public enum MainViewMode { ProjectSetup, Editor }

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly Func<BeamModel, BeamAnalysisOutcome>? _analyze;
    private MainViewMode _mode;
    private ProjectSetupViewModel _setup;
    private EditorViewModel? _editor;

    public MainWindowViewModel(Func<BeamModel, BeamAnalysisOutcome>? analyze = null)
    {
        _analyze = analyze;
        _setup = NewSetup();
    }

    public MainViewMode Mode => _mode;
    public ProjectSetupViewModel Setup => _setup;
    public EditorViewModel? Editor => _editor;
    public object CurrentViewModel => Mode == MainViewMode.ProjectSetup ? Setup : Editor!;
    private ProjectSetupViewModel NewSetup() => new(ProjectSetupMode.Create,
        ProjectTemplates.Section, ProjectTemplates.Material, ApplySetup, CancelSetup);

    public void EditProject()
    {
        if (Editor is null) return;
        Editor.CancelEditorInteraction();
        _setup = new(ProjectSetupMode.Edit, Editor.Document.Section, Editor.Document.Material,
            ApplySetup, CancelSetup);
        Navigate(MainViewMode.ProjectSetup);
    }

    private void ApplySetup(ProjectSetupViewModel setup)
    {
        if (setup.Mode == ProjectSetupMode.Create)
            _editor = new(new(Length.FromMillimeters(1000), setup.SelectedMaterial, setup.SelectedSection),
                EditProject, _analyze);
        else
            Editor!.ApplySetup(setup.SelectedSection, setup.SelectedMaterial);
        Navigate(MainViewMode.Editor);
    }

    private void CancelSetup()
    {
        if (Setup.IsEdit) Navigate(MainViewMode.Editor);
    }

    private void Navigate(MainViewMode mode)
    {
        _mode = mode;
        Notify(nameof(Mode));
        Notify(nameof(Setup));
        Notify(nameof(Editor));
        Notify(nameof(CurrentViewModel));
    }
}
