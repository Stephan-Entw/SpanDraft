using System.ComponentModel;
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
        _sectionDisplayName = recoveredDraft?.SectionDisplayName ?? originalDocument?.SectionDisplayName;
        _originalInfo = originalInfo;
        CalculationTitleInput = new(this, recoveredDraft is not null ? recoveredDraft.CalculationTitle : originalInfo?.CalculationTitle, Strings.CalculationTitleRequired);
        ProjectTitleInput = new(this, recoveredDraft is not null ? recoveredDraft.ProjectTitle : originalInfo?.ProjectTitle);
        ReferenceInput = new(this, recoveredDraft is not null ? recoveredDraft.Reference : originalInfo?.Reference);
        AuthorInput = new(this, recoveredDraft is not null ? recoveredDraft.Author : IsEdit ? originalInfo?.Author : suggestedAuthor, Strings.ProjectAuthorRequired);
        _confirmedDraft = Draft;
        OriginalMaterial = IsEdit ? material : null;
        _resultPresentation = resultPresentation ?? ResultPresentationOptions.Default;
        Libraries = libraries; _apply = apply;
        LengthInput = new(() => SelectedLength, CommitLength, () => IsBusy || Libraries?.PreserveSetupInputs == true,
            () => ResultPresentation.Profile[QuantityKind.BeamLength], retainRejectedOnBlur: true);
        LengthInput.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(LengthInputViewModel.Text) or nameof(LengthInputViewModel.IsEditing)
                or nameof(LengthInputViewModel.HasError) or nameof(LengthInputViewModel.ErrorText)) RefreshLength();
        };
        ApplyCommand = new(ConfirmAsync, () => CanApply);
        CancelCommand = new(cancel, () => CanCancel && !IsBusy);
        ChangeMaterialCommand = new(ShowMaterials, () => !IsBusy);
        ChangeSectionCommand = new(ShowSections, () => !IsBusy);
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
    public ProjectInfo? Info => CalculationTitleInput.ConfirmedValue is { } title && AuthorInput.ConfirmedValue is { } author
        ? new(title, ProjectTitleInput.ConfirmedValue, ReferenceInput.ConfirmedValue, author) : null;
    internal void RefreshInfo() { Notify(nameof(CanApply)); ApplyCommand?.Refresh(); }
    internal void InfoCommitted() { RefreshInfo(); DraftCommitted(); }
    private bool HasValidInfoInput => !RequiresProjectInfo
        || !string.IsNullOrWhiteSpace(CalculationTitleInput.Text) && !string.IsNullOrWhiteSpace(AuthorInput.Text);
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
        && LengthInput.TryGetLength(out var length) && !HasLengthError
        && (_originalDocument is not { } document || BeamLengthConstraints.Error(document, length) is null);
    public string? SectionDisplayName => _sectionDisplayName;
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
                nameof(Density), nameof(PoissonRatio), nameof(HasDensity), nameof(HasPoissonRatio), nameof(CanApply) }) Notify(name);
            ApplyCommand.Refresh(); DraftCommitted();
        }
    }
    public bool HasMaterial => SelectedMaterial is not null;
    public string MaterialName => SelectedMaterial is { } material ? MaterialDisplay.Format(material) : Strings.SelectMaterialRequired;
    public TechnicalText MaterialSummaryText => SelectedMaterial is null ? (TechnicalText)"" : TechnicalSymbols.YoungsModulus + " " + YoungsModulus + " · " + MaterialStrengthDisplay.SummaryText(SelectedMaterial) + " MPa";
    public string MaterialSummary => MaterialSummaryText.PlainText;
    public string? StartupError => Libraries?.BuiltInError;
    public bool HasStartupError => StartupError is not null;
    public string SectionName => SelectedSection is { } section
        ? SectionDisplayName ?? SectionDisplay.Name(section, ResultPresentation.Profile) : Strings.SectionSelection;
    public string AxisSummary => SelectedSection is not { } section ? "" : Strings.BendingAxis + " " + new AxisChoice(BendingAxis).Label
        + (section.Axes.Count == 2 && SectionDefinitionViewModel.HaveEquivalentProperties(section.Axes[0], section.Axes[1])
            ? " · " + Strings.SectionDefinitionEquivalent : "");
    public SectionEditorType SectionType => SelectedSection is { } section && SectionDefinitionCompatibility.Normalize(section) is IParametricSectionDefinition p
        ? Enum.Parse<SectionEditorType>(p.ShapeKind.ToString()) : SectionEditorType.Manual;
    public ResultPresentationOptions ResultPresentation => _resultPresentation;
    public string Area => SelectedSection is { } section ? Format(section.Area.SquareMeters, QuantityKind.Area) : "";
    public string Inertia => SelectedAxis is { } axis ? Format(axis.SecondMomentOfArea.MetersToTheFourth, QuantityKind.SecondMomentOfArea) : "";
    public string Modulus => !IsAsymmetric ? PositiveModulus : "W+ = " + PositiveModulus + "; W− = " + NegativeModulus;
    public bool IsAsymmetric => SelectedAxis is { } axis && axis.PositiveSectionModulus != axis.NegativeSectionModulus;
    public string PositiveModulus => SelectedAxis is { } axis ? Format(axis.PositiveSectionModulus.CubicMeters, QuantityKind.SectionModulus) : "";
    public string NegativeModulus => SelectedAxis is { } axis ? Format(axis.NegativeSectionModulus.CubicMeters, QuantityKind.SectionModulus) : "";
    public string YoungsModulus => SelectedMaterial is { } m ? UiNumbers.Indicator(m.YoungsModulus.Pascals / 1e9) + " GPa" : "";
    public string YieldStrength => SelectedMaterial is { } m ? UiNumbers.Indicator(m.YieldStrength.Megapascals) + " MPa" : "";
    public bool HasDensity => SelectedMaterial?.Density is not null;
    public bool HasPoissonRatio => SelectedMaterial?.PoissonRatio is not null;
    public string Density => SelectedMaterial?.Density is { } rho ? UiNumbers.Indicator(rho.KilogramsPerCubicMeter) + " kg/m³" : "";
    public string PoissonRatio => SelectedMaterial?.PoissonRatio is { } nu ? UiNumbers.Indicator(nu.Value) : "";
    public AsyncActionCommand ApplyCommand { get; }
    public ActionCommand CancelCommand { get; }
    public ActionCommand ChangeMaterialCommand { get; }
    public ActionCommand ChangeSectionCommand { get; }

    private LengthCommitResult CommitLength(Length length)
    {
        if (IsBusy) return new(false, Strings.ProjectBusy);
        if (_originalDocument is { } document && BeamLengthConstraints.Error(document, length) is { } error) return new(false, error);
        _length = length; Notify(nameof(SelectedLength)); RefreshLength(); DraftCommitted();
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
        && (Definition?.CanApplyInputUnits(profile) ?? true);
    internal void ApplyResultPresentation(ResultPresentationOptions options)
    {
        if (!Set(ref _resultPresentation, options, nameof(ResultPresentation))) return;
        LengthInput.Refresh(preserveError: LengthInput.IsEditing); RefreshLength(); RefreshSection();
        Definition?.ApplyResultPresentation(options);
    }
    private void RefreshSection()
    {
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
    private string Format(double si, QuantityKind kind) =>
        QuantityFormatter.Format(si, kind, ResultPresentation.Profile, ResultPresentation.Mode);
}

public abstract class SetupStepViewModel(ProjectSetupViewModel setup) : ObservableObject
{
    protected ProjectSetupViewModel Setup { get; } = setup;
    public bool IsBusy => Setup.IsBusy;
    public virtual void Refresh() => Notify(nameof(IsBusy));
}
