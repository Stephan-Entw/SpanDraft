using System.ComponentModel;
using System.Globalization;
using SpanDraft.Core.Materials;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Sections.Parametric;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.Sections;
using SpanDraft.Desktop.State;
using SpanDraft.Reporting.Notation;

namespace SpanDraft.Desktop.ViewModels;

public enum ProjectSetupMode { Create, Edit }

/// <summary>
/// One row of the segment list: its range in the display unit and the summaries of section, material and axis.
/// Rows are updated in place so that the list keeps its selection while a segment is edited.
/// </summary>
public sealed class SetupSegmentRow(int number) : ObservableObject
{
    private string _range = "", _section = "", _material = "", _axis = "";
    public int Number { get; } = number;
    public string Range { get => _range; internal set => Set(ref _range, value); }
    public string Section { get => _section; internal set => Set(ref _section, value); }
    public string Material { get => _material; internal set => Set(ref _material, value); }
    public string Axis { get => _axis; internal set => Set(ref _axis, value); }
}

/// <summary>The properties of one segment in the setup; the selected segment's working copy lives in the view model.</summary>
internal sealed record SegmentDraft(Material? Material, ISectionDefinition? Section, SectionAxisDesignation Axis, string? Name);
public enum ProjectSetupPage { Overview, MaterialSelection, SectionDefinition }

public sealed class ProjectSetupViewModel : ObservableObject
{
    private readonly Func<ProjectSetupViewModel, Task<bool>> _apply;
    private readonly EditorDocument? _originalDocument;
    private readonly ProjectInfo? _originalInfo;
    private ISectionDefinition? _selectedSection;
    private Material? _selectedMaterial;
    private SectionAxisDesignation _bendingAxis;
    private string? _sectionDisplayName;
    private Length? _length;
    private ResultPresentationOptions _resultPresentation;
    private ProjectSetupPage _page;
    private object? _step;
    private MaterialSelectionViewModel? _materials;
    private bool _definitionBusy;
    private readonly bool _canCancel;
    private ProjectSetupDraft _confirmedDraft;
    // All segments of an Edit setup. The working fields below hold the selected segment and are authoritative for it.
    private readonly List<SegmentDraft> _segments = [];
    private readonly List<Length> _boundaries = [];
    private int _selectedSegment;

    public ProjectSetupViewModel(ProjectSetupMode mode, Section section, Material material,
        Action<ProjectSetupViewModel> apply, Action cancel, ResultPresentationOptions? resultPresentation = null)
        : this(mode, section, SectionAxisDesignation.Y, material, apply, cancel, resultPresentation) { }

    public ProjectSetupViewModel(ProjectSetupMode mode, ISectionDefinition? section, SectionAxisDesignation bendingAxis,
        Material? material, Action<ProjectSetupViewModel> apply, Action cancel,
        ResultPresentationOptions? resultPresentation = null, MainWindowViewModel? libraries = null)
        : this(mode, section, bendingAxis, material, setup => { apply(setup); return Task.FromResult(true); },
            cancel, resultPresentation, libraries) { }

    public ProjectSetupViewModel(ProjectSetupMode mode, ISectionDefinition? section, SectionAxisDesignation bendingAxis,
        Material? material, Func<ProjectSetupViewModel, Task<bool>> apply, Action cancel,
        ResultPresentationOptions? resultPresentation = null, MainWindowViewModel? libraries = null,
        EditorDocument? originalDocument = null, bool canCancel = false, ProjectSetupDraft? recoveredDraft = null,
        ProjectInfo? originalInfo = null, string? suggestedAuthor = null)
    {
        if (mode == ProjectSetupMode.Edit)
        {
            ArgumentNullException.ThrowIfNull(section);
            ArgumentNullException.ThrowIfNull(material);
        }
        section?.GetAxis(bendingAxis);
        Mode = mode; _selectedSection = section; _bendingAxis = bendingAxis; _selectedMaterial = material;
        _canCancel = canCancel;
        _originalDocument = originalDocument;
        _length = recoveredDraft?.Length ?? originalDocument?.Length ?? (mode == ProjectSetupMode.Edit ? Length.FromMillimeters(1000) : null);
        _sectionDisplayName = recoveredDraft?.SectionDisplayName ?? originalDocument?.Segments[0].SectionDisplayName;
        if (originalDocument is not null)
        {
            _boundaries.AddRange(originalDocument.SegmentBoundaries);
            _segments.AddRange(originalDocument.Segments.Select(g => new SegmentDraft(g.Material, g.Section, g.BendingAxis, g.SectionDisplayName)));
        }
        else
            _segments.Add(new(null, null, SectionAxisDesignation.Y, null));
        _originalInfo = originalInfo;
        CalculationTitleInput = new(this, recoveredDraft is not null ? recoveredDraft.CalculationTitle : originalInfo?.CalculationTitle, Strings.CalculationTitleRequired);
        ProjectTitleInput = new(this, recoveredDraft is not null ? recoveredDraft.ProjectTitle : originalInfo?.ProjectTitle);
        ReferenceInput = new(this, recoveredDraft is not null ? recoveredDraft.Reference : originalInfo?.Reference);
        AuthorInput = new(this, recoveredDraft is not null ? recoveredDraft.Author : IsEdit ? originalInfo?.Author : suggestedAuthor);
        _confirmedDraft = Draft;
        OriginalMaterial = IsEdit ? material : null;
        _resultPresentation = resultPresentation ?? ResultPresentationOptions.Default;
        Libraries = libraries; _apply = apply;
        LengthInput = new(() => SelectedLength, CommitLength, () => IsBusy || Libraries?.PreserveSetupInputs == true,
            () => ResultPresentation.Profile[QuantityKind.BeamLength], retainRejectedOnBlur: true, readPresentation: () => ResultPresentation);
        LengthInput.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(LengthInputViewModel.Text) or nameof(LengthInputViewModel.IsEditing)
                or nameof(LengthInputViewModel.HasError) or nameof(LengthInputViewModel.ErrorText)) RefreshLength();
        };
        ApplyCommand = new(ConfirmAsync, () => CanApply);
        CancelCommand = new(cancel, () => CanCancel && !IsBusy);
        ChangeMaterialCommand = new(ShowMaterials, () => !IsBusy);
        ChangeSectionCommand = new(ShowSections, () => !IsBusy);
        SplitInput = new(() => null, CommitSplit, () => IsBusy, () => ResultPresentation.Profile[QuantityKind.BeamLength],
            retainRejectedOnBlur: true, readPresentation: () => ResultPresentation);
        BoundaryInput = new(() => SelectedSegmentStart, CommitBoundary, () => IsBusy, () => ResultPresentation.Profile[QuantityKind.BeamLength],
            retainRejectedOnBlur: true, readPresentation: () => ResultPresentation);
        RemoveSegmentCommand = new(RemoveSegment, () => !IsBusy && _segments.Count > 1);
        SplitCommand = new(() => { if (!SplitInput.IsEditing) SplitInput.Begin(); SplitInput.Confirm(); }, () => !IsBusy);
        RefreshRows();
    }
    public event Action<ProjectSetupViewModel>? ConfirmedDraftChanged;
    public ProjectSetupDraft Draft => new(SelectedLength, SelectedMaterial, SelectedSection, BendingAxis, SectionDisplayName,
        CalculationTitleInput.ConfirmedValue, ProjectTitleInput.ConfirmedValue, ReferenceInput.ConfirmedValue, AuthorInput.ConfirmedValue);
    public ProjectInfoInput CalculationTitleInput { get; }
    public ProjectInfoInput ProjectTitleInput { get; }
    public ProjectInfoInput ReferenceInput { get; }
    public ProjectInfoInput AuthorInput { get; }
    public bool RequiresProjectInfo => !IsEdit || _originalInfo is not null
        || InfoInputs.Any(i => !string.IsNullOrWhiteSpace(i.Text));
    private IEnumerable<ProjectInfoInput> InfoInputs => [CalculationTitleInput, AuthorInput, ProjectTitleInput, ReferenceInput];
    public ProjectInfo? Info => CalculationTitleInput.ConfirmedValue is { } title
        ? new(title, ProjectTitleInput.ConfirmedValue, ReferenceInput.ConfirmedValue, AuthorInput.ConfirmedValue) : null;
    internal void RefreshInfo() { Notify(nameof(CanApply)); ApplyCommand?.Refresh(); }
    internal void InfoCommitted() { RefreshInfo(); DraftCommitted(); }
    private bool HasValidInfoInput => !RequiresProjectInfo
        || !string.IsNullOrWhiteSpace(CalculationTitleInput.Text);
    private void DraftCommitted()
    {
        if (IsEdit) return;
        var draft = Draft;
        if (_confirmedDraft.ContentEquals(draft)) return;
        _confirmedDraft = draft;
        ConfirmedDraftChanged?.Invoke(this);
    }
    internal MainWindowViewModel? Libraries { get; }
    public ProjectSetupMode Mode { get; }
    public bool IsEdit => Mode == ProjectSetupMode.Edit;
    public bool CanCancel => IsEdit || _canCancel;
    public bool IsBusy => Libraries?.IsBusy == true || Definition?.IsBusy == true;
    public ProjectSetupPage Page => _page;
    public object? CurrentStep => _step;
    public SectionDefinitionViewModel? Definition => CurrentStep as SectionDefinitionViewModel;
    public bool IsOverview => Page == ProjectSetupPage.Overview;
    public bool IsSectionDefinition => Page == ProjectSetupPage.SectionDefinition;
    public string Title => IsEdit ? Strings.EditProject : Strings.NewProject;
    public string PrimaryAction => IsEdit ? Strings.Apply : Strings.CreateProject;
    public Material? OriginalMaterial { get; }
    public Length? SelectedLength => _length;
    public LengthInputViewModel LengthInput { get; }
    public string? LengthError => LengthInput.HasError ? LengthInput.ErrorText : null;
    public bool HasLengthError => LengthError is not null;
    public bool CanApply => IsOverview && !IsBusy && HasValidInfoInput && HasSection && SelectedMaterial?.StrengthType is not null
        && MaterialError is null && AllSegments().All(g => g.Section is not null && g.Material?.StrengthType is not null
            && ValidateProjectMaterial(g.Material) is null)
        && LengthInput.TryGetLength(out var length) && !HasLengthError
        && (_originalDocument is not { } document || BeamLengthConstraints.Error(document, length, _boundaries) is null);

    // ---- Segments (Edit setup only; a new project starts with one segment) ----
    public bool ShowSegments => IsEdit;
    public bool HasSeveralSegments => _segments.Count > 1;
    public IReadOnlyList<Length> SegmentBoundaries => Array.AsReadOnly(_boundaries.ToArray());
    public LengthInputViewModel SplitInput { get; }
    public LengthInputViewModel BoundaryInput { get; }
    public ActionCommand RemoveSegmentCommand { get; }
    public ActionCommand SplitCommand { get; }
    public string SelectedSegmentTitle => string.Format(CultureInfo.CurrentUICulture, Strings.SegmentSelected, _selectedSegment + 1);
    public bool CanEditBoundary => _selectedSegment > 0;
    private Length? SelectedSegmentStart => _selectedSegment > 0 ? _boundaries[_selectedSegment - 1] : null;
    public int SelectedSegmentIndex
    {
        get => _selectedSegment;
        set
        {
            if (IsBusy || value < 0 || value >= _segments.Count || value == _selectedSegment) return;
            _segments[_selectedSegment] = CurrentDraft();
            Load(value);
        }
    }
    public System.Collections.ObjectModel.ObservableCollection<SetupSegmentRow> SegmentRows { get; } = [];

    // Brings the rows up to date; only a changed count adds or removes rows, so the selection survives edits.
    private void RefreshRows()
    {
        var all = AllSegments();
        while (SegmentRows.Count > all.Count) SegmentRows.RemoveAt(SegmentRows.Count - 1);
        while (SegmentRows.Count < all.Count) SegmentRows.Add(new(SegmentRows.Count + 1));
        for (int i = 0; i < all.Count; i++)
        {
            var g = all[i];
            var row = SegmentRows[i];
            row.Range = FormatPosition(i == 0 ? default : _boundaries[i - 1]) + "…"
                + (i < _boundaries.Count ? FormatPosition(_boundaries[i]) : _length is { } end ? FormatPosition(end) : "");
            row.Section = g.Section is null ? "" : g.Name ?? SectionDisplay.Name(g.Section, ResultPresentation);
            row.Material = g.Material is null ? "" : MaterialDisplay.Format(g.Material);
            row.Axis = new AxisChoice(g.Axis).Label;
        }
    }

    private SegmentDraft CurrentDraft() => new(_selectedMaterial, _selectedSection, _bendingAxis, _sectionDisplayName);
    private List<SegmentDraft> AllSegments()
    {
        var all = _segments.ToList();
        all[_selectedSegment] = CurrentDraft();
        return all;
    }
    private void Load(int index)
    {
        var draft = _segments[index];
        _selectedSegment = index;
        _selectedMaterial = draft.Material; _selectedSection = draft.Section; _bendingAxis = draft.Axis;
        _sectionDisplayName = draft.Name;
        BoundaryInput.Refresh();
        RefreshSegments();
        RefreshSection();
        foreach (var name in new[] { nameof(SelectedMaterial), nameof(HasMaterial), nameof(MaterialName), nameof(MaterialSummary),
            nameof(MaterialSummaryText), nameof(YoungsModulus), nameof(YieldStrength), nameof(Density), nameof(PoissonRatio),
            nameof(HasDensity), nameof(HasPoissonRatio), nameof(MaterialError), nameof(HasMaterialError), nameof(SelectedSection) }) Notify(name);
    }
    private void RefreshSegments()
    {
        RefreshRows();
        foreach (var name in new[] { nameof(SelectedSegmentIndex), nameof(SelectedSegmentTitle),
            nameof(HasSeveralSegments), nameof(CanEditBoundary), nameof(CanApply) }) Notify(name);
        RemoveSegmentCommand?.Refresh(); ApplyCommand?.Refresh();
    }
    private string FormatPosition(Length position) => QuantityFormatter.FormatInputNumber(position.Meters,
        ResultPresentation.Profile[QuantityKind.BeamLength], ResultPresentation);

    /// <summary>The segments of the setup in order, with the working copy of the selected one.</summary>
    internal IReadOnlyList<EditorSegment> BuildSegments() => Array.AsReadOnly(AllSegments().Select(g => new EditorSegment(
        g.Material ?? throw new InvalidOperationException("A segment has no material."),
        g.Section ?? throw new InvalidOperationException("A segment has no section."), g.Axis, g.Name)).ToArray());

    private LengthCommitResult CommitSplit(Length position)
    {
        if (IsBusy) return new(false, Strings.ProjectBusy);
        if (_length is not { } length || !(position.Meters > 0) || !(position.Meters < length.Meters)
            || _boundaries.Contains(position)) return new(false, Strings.SegmentSplitInvalid);
        var all = AllSegments();
        int index = _boundaries.Count(b => b.Meters <= position.Meters);
        all.Insert(index, all[index]);
        _boundaries.Insert(index, position);
        _segments.Clear(); _segments.AddRange(all);
        // Both halves keep every property of the original; the new right half is selected.
        Load(index + 1);
        return LengthCommitResult.Success;
    }

    private LengthCommitResult CommitBoundary(Length position)
    {
        if (IsBusy) return new(false, Strings.ProjectBusy);
        int k = _selectedSegment - 1;
        double previous = k > 0 ? _boundaries[k - 1].Meters : 0;
        double next = k < _boundaries.Count - 1 ? _boundaries[k + 1].Meters : _length?.Meters ?? double.PositiveInfinity;
        if (k < 0 || !(position.Meters > previous) || !(position.Meters < next)) return new(false, Strings.SegmentStartInvalid);
        _boundaries[k] = position;
        RefreshSegments();
        return LengthCommitResult.Success;
    }

    private void RemoveSegment()
    {
        if (IsBusy || _segments.Count < 2) return;
        var all = AllSegments();
        int k = _selectedSegment;
        // The neighbour to the left absorbs the segment; the first segment is absorbed by the one to its right.
        all.RemoveAt(k);
        _boundaries.RemoveAt(k > 0 ? k - 1 : 0);
        _segments.Clear(); _segments.AddRange(all);
        Load(Math.Max(0, k - 1));
    }
    public string? SectionDisplayName => _sectionDisplayName;
    internal string? ValidateProjectMaterial(Material? material) => _originalDocument?.SelfWeight?.Enabled == true
        && material?.Density is null ? Strings.SelfWeightMissingDensity : null;
    public string? MaterialError => ValidateProjectMaterial(SelectedMaterial);
    public bool HasMaterialError => MaterialError is not null;
    public bool HasSection => SelectedSection is not null;
    public ISectionDefinition? SelectedSection
    {
        get => _selectedSection;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _selectedSection = value;
            if (!value.Axes.Any(a => a.AxisDesignation == _bendingAxis)) _bendingAxis = value.Axes[0].AxisDesignation;
            Notify(nameof(SelectedSection)); RefreshSection(); DraftCommitted();
        }
    }
    public SectionAxisDesignation BendingAxis
    {
        get => _bendingAxis;
        set { SelectedSection?.GetAxis(value); if (Set(ref _bendingAxis, value)) { RefreshSection(); DraftCommitted(); } }
    }
    private SectionAxisProperties? SelectedAxis => SelectedSection?.GetAxis(BendingAxis);
    public Material? SelectedMaterial
    {
        get => _selectedMaterial;
        set
        {
            if (!Set(ref _selectedMaterial, value)) return;
            foreach (var name in new[] { nameof(HasMaterial), nameof(MaterialName), nameof(MaterialSummary), nameof(MaterialSummaryText), nameof(YoungsModulus), nameof(YieldStrength),
                nameof(Density), nameof(PoissonRatio), nameof(HasDensity), nameof(HasPoissonRatio), nameof(CanApply),
                nameof(MaterialError), nameof(HasMaterialError) }) Notify(name);
            RefreshRows(); ApplyCommand.Refresh(); DraftCommitted();
        }
    }
    public bool HasMaterial => SelectedMaterial is not null;
    public string MaterialName => SelectedMaterial is { } material ? MaterialDisplay.Format(material) : Strings.SelectMaterialRequired;
    public TechnicalText MaterialSummaryText => SelectedMaterial is null ? (TechnicalText)"" : TechnicalSymbols.YoungsModulus + " " + YoungsModulus + " · " + MaterialStrengthDisplay.SummaryText(SelectedMaterial, ResultPresentation) + " " + ResultPresentation.Profile[QuantityKind.MaterialStrength].Symbol;
    public string MaterialSummary => MaterialSummaryText.PlainText;
    public string? StartupError => Libraries?.BuiltInError;
    public bool HasStartupError => StartupError is not null;
    public string SectionName => SelectedSection is { } section
        ? SectionDisplayName ?? SectionDisplay.Name(section, ResultPresentation) : Strings.SectionSelection;
    public string AxisSummary => SelectedSection is not { } section ? "" : Strings.BendingAxis + " " + new AxisChoice(BendingAxis).Label
        + (section.Axes.Count == 2 && SectionDefinitionViewModel.HaveEquivalentProperties(section.Axes[0], section.Axes[1])
            ? " · " + Strings.SectionDefinitionEquivalent : "");
    public SectionEditorType SectionType => SelectedSection is { } section && SectionDefinitionCompatibility.Normalize(section) is IParametricSectionDefinition p
        ? Enum.Parse<SectionEditorType>(p.ShapeKind.ToString()) : SectionEditorType.Manual;
    public ResultPresentationOptions ResultPresentation => _resultPresentation;
    public string Area => SelectedSection is { } section ? SectionProperty(section.Area.SquareMeters, QuantityKind.Area) : "";
    public string Inertia => SelectedAxis is { } axis ? SectionProperty(axis.SecondMomentOfArea.MetersToTheFourth, QuantityKind.SecondMomentOfArea) : "";
    public string Modulus => !IsAsymmetric ? PositiveModulus : "W+ = " + PositiveModulus + "; W− = " + NegativeModulus;
    public bool IsAsymmetric => SelectedAxis is { } axis && axis.PositiveSectionModulus != axis.NegativeSectionModulus;
    public string PositiveModulus => SelectedAxis is { } axis ? SectionProperty(axis.PositiveSectionModulus.CubicMeters, QuantityKind.SectionModulus) : "";
    public string NegativeModulus => SelectedAxis is { } axis ? SectionProperty(axis.NegativeSectionModulus.CubicMeters, QuantityKind.SectionModulus) : "";
    public string YoungsModulus => SelectedMaterial is { } m ? Given(m.YoungsModulus.Pascals, QuantityKind.YoungsModulus) : "";
    public string YieldStrength => SelectedMaterial is { } m ? Given(m.YieldStrength.Pascals, QuantityKind.MaterialStrength) : "";
    public bool HasDensity => SelectedMaterial?.Density is not null;
    public bool HasPoissonRatio => SelectedMaterial?.PoissonRatio is not null;
    public string Density => SelectedMaterial?.Density is { } rho ? Given(rho.KilogramsPerCubicMeter, QuantityKind.Density) : "";
    public string PoissonRatio => SelectedMaterial?.PoissonRatio is { } nu ? Given(nu.Value, QuantityKind.PoissonRatio) : "";
    public AsyncActionCommand ApplyCommand { get; }
    public ActionCommand CancelCommand { get; }
    public ActionCommand ChangeMaterialCommand { get; }
    public ActionCommand ChangeSectionCommand { get; }

    private LengthCommitResult CommitLength(Length length)
    {
        if (IsBusy) return new(false, Strings.ProjectBusy);
        if (_originalDocument is { } document && BeamLengthConstraints.Error(document, length, _boundaries) is { } error) return new(false, error);
        if (_boundaries.Count > 0 && !(length.Meters > _boundaries[^1].Meters)) return new(false, Strings.LengthExcludesSegments);
        _length = length; Notify(nameof(SelectedLength)); RefreshLength(); DraftCommitted();
        RefreshRows();
        return LengthCommitResult.Success;
    }
    private void RefreshLength()
    {
        Notify(nameof(LengthError)); Notify(nameof(HasLengthError)); Notify(nameof(CanApply)); ApplyCommand?.Refresh();
    }
    public void ShowMaterials()
    {
        if (IsBusy) return;
        if (IsOverview || _materials is null) _materials = new MaterialSelectionViewModel(this);
        _materials.Refresh();
        Navigate(ProjectSetupPage.MaterialSelection, _materials);
    }
    public void ShowSections()
    {
        if (IsBusy || IsSectionDefinition) return;
        var library = Libraries is { } main ? new SectionDefinitionLibrary(main) : null;
        var definition = new SectionDefinitionViewModel(ResultPresentation, library,
            initialState: SelectedSection is { } section ? new(section, BendingAxis, SectionDisplayName) : null,
            preserveInputs: () => Libraries?.PreserveSetupInputs == true);
        definition.CloseRequested += FinishDefinition;
        definition.PropertyChanged += DefinitionChanged;
        Navigate(ProjectSetupPage.SectionDefinition, definition);
    }
    private void FinishDefinition(SectionDefinitionResult? result)
    {
        if (IsBusy) return;
        if (result is not null)
        {
            result.Section.GetAxis(result.BendingAxis);
            _selectedSection = result.Section; _bendingAxis = result.BendingAxis;
            _sectionDisplayName = result.IsAutomaticName ? null : result.DisplayName;
            Notify(nameof(SelectedSection)); RefreshSection(); DraftCommitted();
        }
        ShowOverview();
    }
    private void DefinitionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SectionDefinitionViewModel.CanUndo) or nameof(SectionDefinitionViewModel.CanRedo))
        { Libraries?.RefreshHistoryState(); return; }
        if (e.PropertyName != nameof(SectionDefinitionViewModel.IsBusy) || _definitionBusy == Definition?.IsBusy) return;
        _definitionBusy = Definition?.IsBusy == true;
        RefreshLibraryState(); Libraries?.RefreshWorkspaceState();
    }
    public void ShowOverview() { if (!IsBusy) Navigate(ProjectSetupPage.Overview, null); }
    public void Escape()
    {
        if (IsBusy) return;
        switch (Page)
        {
            case ProjectSetupPage.MaterialSelection:
                if (_materials?.IsEditing == true) _materials.EndEdit(); else ShowOverview();
                break;
            case ProjectSetupPage.SectionDefinition: Definition?.CancelCommand.Execute(null); break;
            default: CancelCommand.Execute(null); break;
        }
    }
    public async Task<bool> ConfirmAsync()
    {
        if (CurrentStep is MaterialSelectionViewModel { Editor: { } material }) return await material.ConfirmAsync();
        if (Definition is { } definition) return await definition.ConfirmAsync();
        if (!IsOverview || IsBusy) return false;
        bool infoAccepted = true;
        foreach (var input in InfoInputs) infoAccepted &= input.Confirm();
        if (!infoAccepted || !LengthInput.Confirm() || !CanApply) return false;
        // Read the current buffer even when a caller has not opened an input session.
        if (!LengthInput.TryGetLength(out var length) || !CommitLength(length).Accepted) return false;
        return await _apply(this);
    }
    private void Navigate(ProjectSetupPage page, object? step)
    {
        if (Definition is { } previous)
        {
            previous.CloseRequested -= FinishDefinition; previous.PropertyChanged -= DefinitionChanged;
        }
        _page = page; _step = step; _definitionBusy = false;
        foreach (var name in new[] { nameof(Page), nameof(CurrentStep), nameof(IsOverview), nameof(IsSectionDefinition), nameof(CanApply) }) Notify(name);
        ApplyCommand.Refresh(); Libraries?.RefreshWorkspaceState();
    }
    public bool CanApplyInputUnits(UnitProfile profile) => (!LengthInput.IsEditing
        || profile[QuantityKind.BeamLength].Id == ResultPresentation.Profile[QuantityKind.BeamLength].Id)
        && (Definition?.CanApplyInputUnits(profile) ?? true)
        && (CurrentStep is not MaterialSelectionViewModel { Editor: not null }
            || new[] { QuantityKind.YoungsModulus, QuantityKind.MaterialStrength, QuantityKind.Density, QuantityKind.PoissonRatio }
                .All(kind => profile[kind].Id == ResultPresentation.Profile[kind].Id));
    internal void ApplyResultPresentation(ResultPresentationOptions options)
    {
        if (!Set(ref _resultPresentation, options, nameof(ResultPresentation))) return;
        LengthInput.Refresh(preserveError: LengthInput.IsEditing); RefreshLength(); RefreshSection();
        Definition?.ApplyResultPresentation(options);
        foreach (var name in new[] { nameof(MaterialSummary), nameof(MaterialSummaryText), nameof(YoungsModulus), nameof(YieldStrength), nameof(Density), nameof(PoissonRatio) }) Notify(name);
        if (CurrentStep is MaterialSelectionViewModel materials) materials.Refresh();
    }
    private void RefreshSection()
    {
        RefreshRows();
        foreach (var name in new[] { nameof(HasSection), nameof(CanApply), nameof(SectionName), nameof(SectionDisplayName), nameof(BendingAxis), nameof(AxisSummary), nameof(SectionType),
            nameof(Area), nameof(Inertia), nameof(Modulus), nameof(IsAsymmetric), nameof(PositiveModulus), nameof(NegativeModulus) }) Notify(name);
        ApplyCommand.Refresh();
    }
    internal void RefreshLibraryState()
    {
        foreach (var name in new[] { nameof(IsBusy), nameof(CanApply), nameof(CanCancel), nameof(MaterialName), nameof(StartupError), nameof(HasStartupError) }) Notify(name);
        ApplyCommand.Refresh(); CancelCommand.Refresh(); ChangeMaterialCommand.Refresh(); ChangeSectionCommand.Refresh();
        if (CurrentStep is SetupStepViewModel step) step.Refresh();
        Definition?.RefreshLibraryState();
    }
    private string Given(double si, QuantityKind kind) => QuantityFormatter.FormatInput(si, ResultPresentation.Profile[kind], ResultPresentation);
    private string SectionProperty(double si, QuantityKind kind) => SectionDisplay.Property(SelectedSection!, si, kind, ResultPresentation);
}

public abstract class SetupStepViewModel(ProjectSetupViewModel setup) : ObservableObject
{
    protected ProjectSetupViewModel Setup { get; } = setup;
    public bool IsBusy => Setup.IsBusy;
    public virtual void Refresh() => Notify(nameof(IsBusy));
}
