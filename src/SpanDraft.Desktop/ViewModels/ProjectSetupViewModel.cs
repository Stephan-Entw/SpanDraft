using System.ComponentModel;
using System.Globalization;
using SpanDraft.Core.Materials;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Sections.Parametric;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Diagnostics;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.Sections;
using SpanDraft.Desktop.State;
using SpanDraft.Reporting.Notation;

namespace SpanDraft.Desktop.ViewModels;

public enum ProjectSetupMode { Create, Edit }

/// <summary>The properties of one segment in the setup; the selected segment's working copy lives in the view model.</summary>
internal sealed record SegmentDraft(Guid Id, Length? Length, Material? Material, ISectionDefinition? Section,
    SectionAxisDesignation Axis, string? Name);
public enum ProjectSetupPage { Overview, MaterialSelection, SectionDefinition }

public sealed class ProjectSetupViewModel : ObservableObject
{
    private readonly Func<ProjectSetupViewModel, Task<bool>> _apply;
    private readonly InteractionPerformance? _performance;
    private readonly EditorDocument? _originalDocument;
    private readonly ProjectInfo? _originalInfo;
    private ISectionDefinition? _selectedSection;
    private Material? _selectedMaterial;
    private SectionAxisDesignation _bendingAxis;
    private string? _sectionDisplayName;
    private ResultPresentationOptions _resultPresentation;
    private ProjectSetupPage _page;
    private object? _step;
    private MaterialSelectionViewModel? _materials;
    private bool _definitionBusy;
    private readonly bool _canCancel;
    private ProjectSetupDraft _confirmedDraft;
    // All segments of the setup. The working fields above hold the selected segment's material, section, axis and
    // name and are authoritative for it; the length of every segment lives in its draft.
    private readonly List<SegmentDraft> _segments = [];
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
        ProjectInfo? originalInfo = null, string? suggestedAuthor = null,
        InteractionPerformance? performance = null)
    {
        if (mode == ProjectSetupMode.Edit)
        {
            ArgumentNullException.ThrowIfNull(section);
            ArgumentNullException.ThrowIfNull(material);
        }
        section?.GetAxis(bendingAxis);
        Mode = mode; _selectedSection = section; _bendingAxis = bendingAxis; _selectedMaterial = material;
        _canCancel = canCancel;
        _performance = performance;
        _originalDocument = originalDocument;
        _sectionDisplayName = recoveredDraft?.SectionDisplayName ?? originalDocument?.Segments[0].SectionDisplayName;
        if (originalDocument is not null)
            _segments.AddRange(originalDocument.Segments.Select(g => new SegmentDraft(g.Id, g.Length, g.Material, g.Section,
                g.BendingAxis, g.SectionDisplayName)));
        else
        {
            // The first segment is the selected one; its working fields are the constructor arguments.
            _segments.Add(new(Guid.NewGuid(), recoveredDraft?.Length ?? (mode == ProjectSetupMode.Edit ? Length.FromMillimeters(1000) : null),
                null, null, SectionAxisDesignation.Y, null));
            foreach (var further in recoveredDraft?.FurtherSegments ?? [])
                _segments.Add(new(Guid.NewGuid(), further.Length, further.Material, further.Section, further.BendingAxis, further.SectionDisplayName));
        }
        _originalInfo = originalInfo;
        CalculationTitleInput = new(this, recoveredDraft is not null ? recoveredDraft.CalculationTitle : originalInfo?.CalculationTitle, Strings.CalculationTitleRequired);
        ProjectTitleInput = new(this, recoveredDraft is not null ? recoveredDraft.ProjectTitle : originalInfo?.ProjectTitle);
        ReferenceInput = new(this, recoveredDraft is not null ? recoveredDraft.Reference : originalInfo?.Reference);
        AuthorInput = new(this, recoveredDraft is not null ? recoveredDraft.Author : IsEdit ? originalInfo?.Author : suggestedAuthor);
        _confirmedDraft = Draft;
        OriginalMaterial = IsEdit ? material : null;
        _resultPresentation = resultPresentation ?? ResultPresentationOptions.Default;
        Libraries = libraries; _apply = apply;
        RefreshCards();
        ApplyCommand = new(ConfirmAsync, () => CanApply);
        CancelCommand = new(cancel, () => CanCancel && !IsBusy);
        ChangeMaterialCommand = new(ShowMaterials, () => !IsBusy);
        ChangeSectionCommand = new(ShowSections, () => !IsBusy);
    }
    public event Action<ProjectSetupViewModel>? ConfirmedDraftChanged;
    public ProjectSetupDraft Draft
    {
        get
        {
            var all = AllSegments();
            var first = all[0];
            return new(first.Length, first.Material, first.Section, first.Axis, first.Name,
                CalculationTitleInput.ConfirmedValue, ProjectTitleInput.ConfirmedValue, ReferenceInput.ConfirmedValue,
                AuthorInput.ConfirmedValue, all.Skip(1).Select(g => new SetupSegmentDraft(g.Length, g.Material, g.Section, g.Axis, g.Name)).ToArray());
        }
    }
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
    /// <summary>The length of the first segment, which for a beam of one segment is the beam length.</summary>
    public Length? SelectedLength => _segments[0].Length;
    /// <summary>The length input of the first segment.</summary>
    public LengthInputViewModel LengthInput => Cards[0].LengthInput;
    public string? LengthError => LengthInput.HasError ? LengthInput.ErrorText : null;
    public bool HasLengthError => LengthError is not null;
    /// <summary>The beam length, the sum of the lengths entered so far; null while none is.</summary>
    public Length? TotalLength => _segments.Any(g => g.Length is not null)
        ? Length.FromMeters(_segments.Sum(g => g.Length?.Meters ?? 0)) : null;
    public bool CanApply => IsOverview && !IsBusy && HasValidInfoInput
        && AllSegments().All(g => g.Section is not null && g.Material?.StrengthType is not null
            && ValidateProjectMaterial(g.Material) is null)
        && Cards.All(card => card.LengthInput.TryGetLength(out var length) && length.Meters > 0 && !card.LengthInput.HasError)
        && ChainError(ChainWithBuffers()) is null;

    // The chain with the lengths as they stand in the input buffers, committed or not.
    private List<SegmentDraft> ChainWithBuffers() => AllSegments().Select(g =>
        Cards.FirstOrDefault(card => card.Id == g.Id) is { } card && card.LengthInput.TryGetLength(out var length)
            ? g with { Length = length } : g).ToList();

    // ---- Segments: the beam is a chain of segments, each with its own card (Create and Edit) ----
    public bool HasSeveralSegments => _segments.Count > 1;
    public string SelectedSegmentTitle => string.Format(CultureInfo.CurrentUICulture, Strings.SegmentSelected, _selectedSegment + 1);
    public System.Collections.ObjectModel.ObservableCollection<SetupSegmentCard> Cards { get; } = [];
    public ActionCommand AddSegmentCommand => _addSegment ??= new(() => InsertSegment(_segments.Count), () => !IsBusy);
    private ActionCommand? _addSegment;
    private string? _segmentError;

    /// <summary>Why the last change of the segment chain was rejected; null when there is nothing to say.</summary>
    public string? SegmentError
    {
        get => _segmentError;
        private set { if (Set(ref _segmentError, value)) Notify(nameof(HasSegmentError)); }
    }
    public bool HasSegmentError => _segmentError is not null;

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

    private SegmentDraft CurrentDraft() => _segments[_selectedSegment] with
    { Material = _selectedMaterial, Section = _selectedSection, Axis = _bendingAxis, Name = _sectionDisplayName };
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
        RefreshSegments();
        RefreshSection();
        foreach (var name in new[] { nameof(SelectedMaterial), nameof(HasMaterial), nameof(MaterialName), nameof(MaterialSummary),
            nameof(MaterialSummaryText), nameof(YoungsModulus), nameof(YieldStrength), nameof(Density), nameof(PoissonRatio),
            nameof(HasDensity), nameof(HasPoissonRatio), nameof(MaterialError), nameof(HasMaterialError), nameof(SelectedSection) }) Notify(name);
    }
    private void RefreshSegments()
    {
        RefreshCards();
        foreach (var name in new[] { nameof(SelectedSegmentIndex), nameof(SelectedSegmentTitle), nameof(HasSeveralSegments),
            nameof(TotalLength), nameof(SelectedLength), nameof(CanApply) }) Notify(name);
        RefreshLength();
    }

    // Brings the cards up to date. A card keeps its identity and its input session for as long as its segment exists.
    private void RefreshCards()
    {
        var all = AllSegments();
        for (int i = 0; i < all.Count; i++)
        {
            int at = Cards.Select(c => c.Id).ToList().IndexOf(all[i].Id);
            if (at < 0) Cards.Insert(i, NewCard(all[i].Id));
            else if (at != i) Cards.Move(at, i);
        }
        while (Cards.Count > all.Count) Cards.RemoveAt(Cards.Count - 1);
        for (int i = 0; i < all.Count; i++) Cards[i].Update(i + 1, all.Count, i == _selectedSegment, all[i], ResultPresentation);
    }

    private SetupSegmentCard NewCard(Guid id)
    {
        var card = new SetupSegmentCard(this, id, new LengthInputViewModel(() => _segments.FirstOrDefault(g => g.Id == id)?.Length,
            length => CommitSegmentLength(id, length), () => IsBusy || Libraries?.PreserveSetupInputs == true,
            () => ResultPresentation.Profile[QuantityKind.BeamLength], retainRejectedOnBlur: true,
            readPresentation: () => ResultPresentation));
        card.LengthInput.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(LengthInputViewModel.Text) or nameof(LengthInputViewModel.IsEditing)
                or nameof(LengthInputViewModel.HasError) or nameof(LengthInputViewModel.ErrorText)) RefreshLength();
        };
        return card;
    }

    /// <summary>The segments of the setup in order, with the working copy of the selected one.</summary>
    internal IReadOnlyList<EditorSegment> BuildSegments() => Array.AsReadOnly(AllSegments().Select(g => new EditorSegment(
        g.Material ?? throw new InvalidOperationException("A segment has no material."),
        g.Section ?? throw new InvalidOperationException("A segment has no section."), g.Axis, g.Name)
        { Id = g.Id, Length = g.Length ?? throw new InvalidOperationException("A segment has no length.") }).ToArray());

    // The chain checked against the project being edited: what would become invalid is named, never corrected. A
    // segment that has no length yet takes no part, and the checks need no material or section, so the first
    // segment's stand in for those that are not chosen yet.
    private string? ChainError(IReadOnlyList<SegmentDraft> chain)
    {
        if (_originalDocument is not { } document) return null;
        var placeholder = document.Segments[0];
        var segments = chain.Where(g => g.Length is { Meters: > 0 }).Select(g => new EditorSegment(
            g.Material ?? placeholder.Material, g.Section ?? placeholder.Section, g.Section is null ? placeholder.BendingAxis : g.Axis)
            { Id = g.Id, Length = g.Length!.Value }).ToArray();
        if (segments.Length == 0) return null;
        var change = document.WithSegmentChain(segments);
        if (change.IsAccepted) return null;
        // A segment without density is reported by its own material (ValidateProjectMaterial) and blocks Apply there;
        // it is no reason to reject a length, a copy or a removal of some other segment.
        if (change.Rejection == GeometryRejection.SelfWeightDensity) return null;
        // The established message for a beam that has become too short for elements that are all measured from its start.
        var elements = document.Supports.Cast<object>().Concat(document.Loads).Concat(document.DistributedLoads).Concat(document.MomentReleases);
        if (change.Conflicts.All(c => c.Kind == GeometryConflictKind.RuleViolation) && elements.All(IsFixedFromStart)
            && BeamLengthConstraints.Error(document, Length.FromMeters(segments.Sum(g => g.Length.Meters)), []) is { } legacy)
            return legacy;
        return string.Format(CultureInfo.CurrentCulture, Strings.SegmentChangeBlocked,
            string.Join(", ", change.Conflicts.Select(c => c.EntityName)));
    }
    private static bool IsFixedFromStart(object entity) => entity switch
    {
        EditorSupport s => s.Reference is null,
        EditorPointLoad l => l.Reference is null,
        EditorMomentRelease r => r.Reference is null,
        EditorDistributedLoad d => d.StartReference is null && d.EndReference is null,
        _ => true
    };

    private LengthCommitResult CommitSegmentLength(Guid id, Length length)
    {
        if (IsBusy) return new(false, Strings.ProjectBusy);
        if (!(length.Meters > 0)) return new(false, Strings.InvalidLength);
        var all = AllSegments();
        int index = all.FindIndex(g => g.Id == id);
        if (index < 0) return new(false, Strings.InvalidLength);
        all[index] = all[index] with { Length = length };
        if (ChainError(all) is { } error) { SegmentError = error; return new(false, error); }
        SegmentError = null;
        _segments[index] = _segments[index] with { Length = length };
        RefreshSegments(); DraftCommitted();
        return LengthCommitResult.Success;
    }

    // Replaces the chain with the given one and keeps the segment that is selected now selected.
    private void ReplaceSegments(List<SegmentDraft> chain, Guid keepSelected)
    {
        _segments.Clear(); _segments.AddRange(chain);
        int selected = Math.Max(0, chain.FindIndex(g => g.Id == keepSelected));
        Load(selected);
        DraftCommitted();
    }

    /// <summary>Inserts an empty segment so that it gets this place in the chain; it counts once it has a length.</summary>
    internal void InsertSegment(int index)
    {
        if (IsBusy || index < 0 || index > _segments.Count) return;
        var all = AllSegments();
        all.Insert(index, new(Guid.NewGuid(), null, null, null, SectionAxisDesignation.Y, null));
        SegmentError = null;
        ReplaceSegments(all, all[Math.Min(_selectedSegment + (index <= _selectedSegment ? 1 : 0), all.Count - 1)].Id);
    }

    internal void DuplicateSegment(Guid id)
    {
        if (IsBusy) return;
        var all = AllSegments();
        int index = all.FindIndex(g => g.Id == id);
        if (index < 0) return;
        var keep = all[_selectedSegment].Id;
        all.Insert(index + 1, all[index] with { Id = Guid.NewGuid() });
        if (ChainError(all) is { } error) { SegmentError = error; return; }
        SegmentError = null;
        ReplaceSegments(all, keep);
    }

    internal void RemoveSegment(Guid id)
    {
        if (IsBusy || _segments.Count < 2) return;
        var all = AllSegments();
        int index = all.FindIndex(g => g.Id == id);
        if (index < 0) return;
        var selected = all[_selectedSegment].Id;
        all.RemoveAt(index);
        if (ChainError(all) is { } error) { SegmentError = error; return; }
        SegmentError = null;
        // The neighbour in front of the removed segment (or the first) takes over the selection.
        ReplaceSegments(all, selected == id ? all[Math.Max(0, index - 1)].Id : selected);
    }

    /// <summary>Makes the segment the one whose section and material the steps edit.</summary>
    internal void Activate(Guid id)
    {
        int index = _segments.FindIndex(g => g.Id == id);
        if (index >= 0) SelectedSegmentIndex = index;
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
            RefreshCards(); ApplyCommand.Refresh(); DraftCommitted();
        }
    }
    public bool HasMaterial => SelectedMaterial is not null;
    public string MaterialName => SelectedMaterial is { } material ? MaterialDisplay.Format(material) : Strings.SelectMaterialRequired;
    public TechnicalText MaterialSummaryText => SelectedMaterial is null ? (TechnicalText)"" : MaterialSummaryTextOf(SelectedMaterial, ResultPresentation);
    internal static TechnicalText MaterialSummaryTextOf(Material material, ResultPresentationOptions presentation) =>
        TechnicalSymbols.YoungsModulus + " " + QuantityFormatter.FormatInput(material.YoungsModulus.Pascals,
            presentation.Profile[QuantityKind.YoungsModulus], presentation) + " · "
        + MaterialStrengthDisplay.SummaryText(material, presentation) + " " + presentation.Profile[QuantityKind.MaterialStrength].Symbol;
    public string MaterialSummary => MaterialSummaryText.PlainText;
    public string? StartupError => Libraries?.BuiltInError;
    public bool HasStartupError => StartupError is not null;
    public string SectionName => SelectedSection is { } section
        ? SectionDisplayName ?? SectionDisplay.Name(section, ResultPresentation) : Strings.SectionSelection;
    public string AxisSummary => AxisSummaryOf(SelectedSection, BendingAxis);
    internal static string AxisSummaryOf(ISectionDefinition? section, SectionAxisDesignation axis) => section is null ? ""
        : Strings.BendingAxis + " " + new AxisChoice(axis).Label
        + (section.Axes.Count == 2 && SectionDefinitionViewModel.HaveEquivalentProperties(section.Axes[0], section.Axes[1])
            ? " · " + Strings.SectionDefinitionEquivalent : "");
    public SectionEditorType SectionType => SectionTypeOf(SelectedSection);
    internal static SectionEditorType SectionTypeOf(ISectionDefinition? section) =>
        section is not null && SectionDefinitionCompatibility.Normalize(section) is IParametricSectionDefinition p
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
        return await ConfirmOverviewAsync();
    }

    private Task<bool> ConfirmOverviewAsync()
    {
        if (!IsOverview || IsBusy) return Task.FromResult(false);
        // The context ends with synchronous confirmation, before any asynchronous setup work can yield.
        using var measurement = IsEdit ? _performance?.Begin(InteractionAction.ProjectSetup) : null;
        bool infoAccepted = true;
        foreach (var input in InfoInputs) infoAccepted &= input.Confirm();
        foreach (var card in Cards) infoAccepted &= card.LengthInput.Confirm();
        if (!infoAccepted || !CanApply) return Task.FromResult(false);
        var result = _apply(this);
        measurement?.Complete();
        return result;
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
    public bool CanApplyInputUnits(UnitProfile profile) => (Cards.All(card => !card.LengthInput.IsEditing)
        || profile[QuantityKind.BeamLength].Id == ResultPresentation.Profile[QuantityKind.BeamLength].Id)
        && (Definition?.CanApplyInputUnits(profile) ?? true)
        && (CurrentStep is not MaterialSelectionViewModel { Editor: not null }
            || new[] { QuantityKind.YoungsModulus, QuantityKind.MaterialStrength, QuantityKind.Density, QuantityKind.PoissonRatio }
                .All(kind => profile[kind].Id == ResultPresentation.Profile[kind].Id));
    internal void ApplyResultPresentation(ResultPresentationOptions options)
    {
        if (!Set(ref _resultPresentation, options, nameof(ResultPresentation))) return;
        foreach (var card in Cards) card.LengthInput.Refresh(preserveError: card.LengthInput.IsEditing);
        RefreshLength(); RefreshSection();
        Definition?.ApplyResultPresentation(options);
        foreach (var name in new[] { nameof(MaterialSummary), nameof(MaterialSummaryText), nameof(YoungsModulus), nameof(YieldStrength), nameof(Density), nameof(PoissonRatio) }) Notify(name);
        if (CurrentStep is MaterialSelectionViewModel materials) materials.Refresh();
    }
    private void RefreshSection()
    {
        RefreshCards();
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
