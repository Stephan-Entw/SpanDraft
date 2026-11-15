using System.Globalization;
using SpanDraft.Core.Materials;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.Sections;
using SpanDraft.Reporting.Notation;

namespace SpanDraft.Desktop.ViewModels;

/// <summary>
/// One segment of the beam in the project setup: its length, section and material, and the actions on it. The card
/// keeps its identity, and with it its input session, for as long as its segment exists.
/// </summary>
public sealed class SetupSegmentCard : ObservableObject
{
    private readonly ProjectSetupViewModel _setup;
    private int _number = 1;
    private bool _hasSeveralSegments, _isSelected;
    private ISectionDefinition? _section;
    private string _sectionName = "", _axisSummary = "", _materialName = "";
    private SectionEditorType _sectionType = SectionEditorType.Manual;
    private bool _hasMaterial;
    private TechnicalText _materialSummary = "";

    internal SetupSegmentCard(ProjectSetupViewModel setup, Guid id, LengthInputViewModel lengthInput)
    {
        _setup = setup; Id = id; LengthInput = lengthInput;
        LengthInput.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(LengthInputViewModel.HasError) or nameof(LengthInputViewModel.ErrorText))
            { Notify(nameof(LengthError)); Notify(nameof(HasLengthError)); }
        };
        ChangeSectionCommand = new(() => { _setup.Activate(Id); _setup.ShowSections(); }, () => !_setup.IsBusy);
        ChangeMaterialCommand = new(() => { _setup.Activate(Id); _setup.ShowMaterials(); }, () => !_setup.IsBusy);
        InsertBeforeCommand = new(() => _setup.InsertSegment(Index), () => !_setup.IsBusy);
        InsertAfterCommand = new(() => _setup.InsertSegment(Index + 1), () => !_setup.IsBusy);
        DuplicateCommand = new(() => _setup.DuplicateSegment(Id), () => !_setup.IsBusy);
        RemoveCommand = new(() => _setup.RemoveSegment(Id), () => !_setup.IsBusy && HasSeveralSegments);
    }

    public Guid Id { get; }
    public LengthInputViewModel LengthInput { get; }
    public int Number { get => _number; private set { if (Set(ref _number, value)) { Notify(nameof(Title)); Notify(nameof(Index)); } } }
    private int Index => _number - 1;
    public string Title => string.Format(CultureInfo.CurrentUICulture, Strings.SegmentSelected, Number);

    /// <summary>The title is only worth showing when there is more than one segment to tell apart.</summary>
    public bool HasSeveralSegments
    {
        get => _hasSeveralSegments;
        private set { if (Set(ref _hasSeveralSegments, value)) { Notify(nameof(CanRemove)); RemoveCommand.Refresh(); } }
    }
    public bool CanRemove => HasSeveralSegments;

    /// <summary>True for the segment whose section or material the setup steps currently edit.</summary>
    public bool IsSelected { get => _isSelected; private set => Set(ref _isSelected, value); }

    public string? LengthError => LengthInput.HasError ? LengthInput.ErrorText : null;
    public bool HasLengthError => LengthError is not null;
    public bool HasSection => _section is not null;
    public string SectionName { get => _sectionName; private set => Set(ref _sectionName, value); }
    public string AxisSummary { get => _axisSummary; private set => Set(ref _axisSummary, value); }
    public SectionEditorType SectionType { get => _sectionType; private set => Set(ref _sectionType, value); }
    public bool HasMaterial { get => _hasMaterial; private set => Set(ref _hasMaterial, value); }
    public string MaterialName { get => _materialName; private set => Set(ref _materialName, value); }
    public TechnicalText MaterialSummaryText { get => _materialSummary; private set { if (Set(ref _materialSummary, value)) Notify(nameof(MaterialSummary)); } }
    public string MaterialSummary => MaterialSummaryText.PlainText;

    public ActionCommand ChangeSectionCommand { get; }
    public ActionCommand ChangeMaterialCommand { get; }
    public ActionCommand InsertBeforeCommand { get; }
    public ActionCommand InsertAfterCommand { get; }
    public ActionCommand DuplicateCommand { get; }
    public ActionCommand RemoveCommand { get; }

    internal void Update(int number, int count, bool selected, SegmentDraft draft, ResultPresentationOptions presentation)
    {
        Number = number; HasSeveralSegments = count > 1; IsSelected = selected;
        if (!ReferenceEquals(_section, draft.Section)) { _section = draft.Section; Notify(nameof(HasSection)); }
        SectionName = draft.Section is { } section ? draft.Name ?? SectionDisplay.Name(section, presentation) : Strings.SectionSelection;
        AxisSummary = ProjectSetupViewModel.AxisSummaryOf(draft.Section, draft.Axis);
        SectionType = ProjectSetupViewModel.SectionTypeOf(draft.Section);
        HasMaterial = draft.Material is not null;
        MaterialName = draft.Material is { } material ? MaterialDisplay.Format(material) : Strings.SelectMaterialRequired;
        MaterialSummaryText = draft.Material is { } m ? ProjectSetupViewModel.MaterialSummaryTextOf(m, presentation) : (TechnicalText)"";
        Notify(nameof(LengthError)); Notify(nameof(HasLengthError));
        foreach (var command in new[] { ChangeSectionCommand, ChangeMaterialCommand, InsertBeforeCommand, InsertAfterCommand,
            DuplicateCommand, RemoveCommand }) command.Refresh();
    }
}
