using System.ComponentModel;
using SpanDraft.Core.Materials;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Sections.Parametric;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.Sections;
using SpanDraft.Desktop.State;

namespace SpanDraft.Desktop.ViewModels;

public enum ProjectSetupMode { Create, Edit }
public enum ProjectSetupPage { Overview, MaterialSelection, SectionDefinition }

public sealed class ProjectSetupViewModel : ObservableObject
{
    private readonly Func<ProjectSetupViewModel, Task<bool>> _apply;
    private readonly EditorDocument? _originalDocument;
    private ISectionDefinition _selectedSection;
    private Material? _selectedMaterial;
    private SectionAxisDesignation _bendingAxis;
    private string? _sectionDisplayName;
    private Length _length;
    private ResultPresentationOptions _resultPresentation;
    private ProjectSetupPage _page;
    private object? _step;
    private MaterialSelectionViewModel? _materials;
    private bool _definitionBusy;

    public ProjectSetupViewModel(ProjectSetupMode mode, Section section, Material material,
        Action<ProjectSetupViewModel> apply, Action cancel, ResultPresentationOptions? resultPresentation = null)
        : this(mode, section, SectionAxisDesignation.Y, material, apply, cancel, resultPresentation) { }

    public ProjectSetupViewModel(ProjectSetupMode mode, ISectionDefinition section, SectionAxisDesignation bendingAxis,
        Material? material, Action<ProjectSetupViewModel> apply, Action cancel,
        ResultPresentationOptions? resultPresentation = null, MainWindowViewModel? libraries = null)
        : this(mode, section, bendingAxis, material, setup => { apply(setup); return Task.FromResult(true); },
            cancel, resultPresentation, libraries) { }

    public ProjectSetupViewModel(ProjectSetupMode mode, ISectionDefinition section, SectionAxisDesignation bendingAxis,
        Material? material, Func<ProjectSetupViewModel, Task<bool>> apply, Action cancel,
        ResultPresentationOptions? resultPresentation = null, MainWindowViewModel? libraries = null,
        EditorDocument? originalDocument = null)
    {
        ArgumentNullException.ThrowIfNull(section);
        if (mode == ProjectSetupMode.Edit) ArgumentNullException.ThrowIfNull(material);
        section.GetAxis(bendingAxis);
        Mode = mode; _selectedSection = section; _bendingAxis = bendingAxis; _selectedMaterial = material;
        _originalDocument = originalDocument;
        _length = originalDocument?.Length ?? Length.FromMillimeters(1000);
        _sectionDisplayName = originalDocument?.SectionDisplayName;
        OriginalMaterial = IsEdit ? material : null;
        _resultPresentation = resultPresentation ?? ResultPresentationOptions.Default;
        Libraries = libraries; _apply = apply;
        LengthInput = new(() => SelectedLength, CommitLength, () => IsBusy || Libraries?.PreserveSetupInputs == true,
            () => ResultPresentation.Profile[QuantityKind.BeamLength], retainRejectedOnBlur: true);
        LengthInput.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(LengthInputViewModel.Text) or nameof(LengthInputViewModel.IsEditing)) RefreshLength();
        };
        ApplyCommand = new(ConfirmAsync, () => CanApply);
        CancelCommand = new(cancel, () => CanCancel && !IsBusy);
        ChangeMaterialCommand = new(ShowMaterials, () => !IsBusy);
        ChangeSectionCommand = new(ShowSections, () => !IsBusy);
    }
    internal MainWindowViewModel? Libraries { get; }
    public ProjectSetupMode Mode { get; }
    public bool IsEdit => Mode == ProjectSetupMode.Edit;
    public bool CanCancel => IsEdit || Libraries?.Session is not null;
    public bool IsBusy => Libraries?.IsBusy == true || Definition?.IsBusy == true;
    public ProjectSetupPage Page => _page;
    public object? CurrentStep => _step;
    public SectionDefinitionViewModel? Definition => CurrentStep as SectionDefinitionViewModel;
    public bool IsOverview => Page == ProjectSetupPage.Overview;
    public bool IsSectionDefinition => Page == ProjectSetupPage.SectionDefinition;
    public string Title => IsEdit ? Strings.EditProject : Strings.NewProject;
    public string PrimaryAction => IsEdit ? Strings.Apply : Strings.CreateProject;
    public Material? OriginalMaterial { get; }
    public Length SelectedLength => _length;
    public LengthInputViewModel LengthInput { get; }
    public string? LengthError => !LengthInput.TryGetLength(out var length) ? Strings.InvalidLength
        : _originalDocument is { } document ? BeamLengthConstraints.Error(document, length) : null;
    public bool HasLengthError => LengthError is not null;
    public bool CanApply => IsOverview && !IsBusy && SelectedMaterial is not null && !HasLengthError;
    public string? SectionDisplayName => _sectionDisplayName;
    public ISectionDefinition SelectedSection
    {
        get => _selectedSection;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _selectedSection = value;
            if (!value.Axes.Any(a => a.AxisDesignation == _bendingAxis)) _bendingAxis = value.Axes[0].AxisDesignation;
            Notify(nameof(SelectedSection)); RefreshSection();
        }
    }
    public SectionAxisDesignation BendingAxis
    {
        get => _bendingAxis;
        set { SelectedSection.GetAxis(value); if (Set(ref _bendingAxis, value)) RefreshSection(); }
    }
    private SectionAxisProperties SelectedAxis => SelectedSection.GetAxis(BendingAxis);
    public Material? SelectedMaterial
    {
        get => _selectedMaterial;
        set
        {
            if (!Set(ref _selectedMaterial, value)) return;
            foreach (var name in new[] { nameof(MaterialName), nameof(MaterialSummary), nameof(YoungsModulus), nameof(YieldStrength),
                nameof(Density), nameof(PoissonRatio), nameof(HasDensity), nameof(HasPoissonRatio), nameof(CanApply) }) Notify(name);
            ApplyCommand.Refresh();
        }
    }
    public string MaterialName => SelectedMaterial?.Name ?? Strings.SelectMaterialRequired;
    internal void SelectMaterial(SpanDraft.Desktop.Libraries.MaterialLibraryEntry entry) => SelectedMaterial = entry.Material;
    public string MaterialSummary => SelectedMaterial is null ? "" : "E " + YoungsModulus + " · fy " + YieldStrength;
    public string? StartupError => Libraries?.BuiltInError;
    public bool HasStartupError => StartupError is not null;
    public string SectionName => SectionDisplayName ?? SectionDisplay.Name(SelectedSection, ResultPresentation.Profile);
    public string AxisSummary => Strings.BendingAxis + " " + new AxisChoice(BendingAxis).Label
        + (SelectedSection.Axes.Count == 2 && SectionDefinitionViewModel.HaveEquivalentProperties(SelectedSection.Axes[0], SelectedSection.Axes[1])
            ? " · " + Strings.SectionDefinitionEquivalent : "");
    public SectionEditorType SectionType => SectionDefinitionCompatibility.Normalize(SelectedSection) is IParametricSectionDefinition p
        ? Enum.Parse<SectionEditorType>(p.ShapeKind.ToString()) : SectionEditorType.Manual;
    public ResultPresentationOptions ResultPresentation => _resultPresentation;
    public string Area => Format(SelectedSection.Area.SquareMeters, QuantityKind.Area);
    public string Inertia => Format(SelectedAxis.SecondMomentOfArea.MetersToTheFourth, QuantityKind.SecondMomentOfArea);
    public string Modulus => !IsAsymmetric ? PositiveModulus : "W+ = " + PositiveModulus + "; W− = " + NegativeModulus;
    public bool IsAsymmetric => SelectedAxis.PositiveSectionModulus != SelectedAxis.NegativeSectionModulus;
    public string PositiveModulus => Format(SelectedAxis.PositiveSectionModulus.CubicMeters, QuantityKind.SectionModulus);
    public string NegativeModulus => Format(SelectedAxis.NegativeSectionModulus.CubicMeters, QuantityKind.SectionModulus);
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
        _length = length; Notify(nameof(SelectedLength)); RefreshLength();
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
            initialState: new(SelectedSection, BendingAxis, SectionDisplayName),
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
            Notify(nameof(SelectedSection)); RefreshSection();
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
    internal void ReturnToMaterials(string? replacement = null)
    {
        _materials?.EndEdit(replacement);
    }
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
        if (!IsOverview || IsBusy || !LengthInput.Confirm() || !CanApply) return false;
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
        foreach (var name in new[] { nameof(SectionName), nameof(SectionDisplayName), nameof(BendingAxis), nameof(AxisSummary), nameof(SectionType),
            nameof(Area), nameof(Inertia), nameof(Modulus), nameof(IsAsymmetric), nameof(PositiveModulus), nameof(NegativeModulus) }) Notify(name);
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
