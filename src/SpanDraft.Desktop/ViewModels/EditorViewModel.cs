using SpanDraft.Analysis;
using SpanDraft.Core.Beams;
using SpanDraft.Core.Materials;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.State;

namespace SpanDraft.Desktop.ViewModels;

public sealed class EditorViewModel : ObservableObject
{
    private readonly Func<BeamModel, BeamAnalysisOutcome> _analyze;
    private EditorDocument _document;
    private AnalysisPresentationState _presentation;

    public EditorViewModel(EditorDocument document, Action changeProject,
        Func<BeamModel, BeamAnalysisOutcome>? analyze = null)
    {
        _document = document;
        _analyze = analyze ?? BeamAnalysis.Analyze;
        _presentation = AnalysisPresentationState.FromOutcome(_analyze(document.ToBeamModel()));
        DimensionLength = new(() => Document.Length, ChangeLength);
        ChangeProjectCommand = new(changeProject);
    }

    public EditorDocument Document => _document;
    public AnalysisPresentationState Presentation => _presentation;
    public string ProjectInfo => Strings.SectionTemplateName + " · " + Document.Material.Name;
    public LengthInputViewModel DimensionLength { get; }
    public ActionCommand ChangeProjectCommand { get; }

    private void ChangeLength(Length length)
    {
        if (length == Document.Length) return;
        Commit(Document with { Length = length });
    }

    public void ApplySetup(Section section, Material material) =>
        Commit(Document with { Section = section, Material = material });

    private void Commit(EditorDocument document)
    {
        _document = document;
        _presentation = AnalysisPresentationState.FromOutcome(_analyze(document.ToBeamModel()));
        DimensionLength.Refresh();
        Notify(nameof(Document));
        Notify(nameof(Presentation));
        Notify(nameof(ProjectInfo));
    }
}
