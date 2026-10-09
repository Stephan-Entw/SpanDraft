using SpanDraft.Core.Materials;
using SpanDraft.Core.Sections;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.State;

namespace SpanDraft.Desktop.ViewModels;

public enum ProjectSetupMode { Create, Edit }
public enum ProjectSetupPage { Overview, MaterialSelection, MaterialEditor, SectionSelection, SectionEditor }

public sealed class ProjectSetupViewModel : ObservableObject
{
    private ISectionDefinition _selectedSection;
    private Material? _selectedMaterial;
    private SectionAxisDesignation _bendingAxis;
    private ResultPresentationOptions _resultPresentation;
    private ProjectSetupPage _page;
    private object? _step;
    private IReadOnlyList<AxisChoice> _axes;
    private bool _updatingSection;

    public ProjectSetupViewModel(ProjectSetupMode mode, Section section, Material material,
        Action<ProjectSetupViewModel> apply, Action cancel, ResultPresentationOptions? resultPresentation = null)
        : this(mode, section, SectionAxisDesignation.Y, material, apply, cancel, resultPresentation) { }

    public ProjectSetupViewModel(ProjectSetupMode mode, ISectionDefinition section, SectionAxisDesignation bendingAxis,
        Material? material, Action<ProjectSetupViewModel> apply, Action cancel,
        ResultPresentationOptions? resultPresentation = null, MainWindowViewModel? libraries = null)
    {
        ArgumentNullException.ThrowIfNull(section);
        if (mode == ProjectSetupMode.Edit) ArgumentNullException.ThrowIfNull(material);
        section.GetAxis(bendingAxis);
        _bendingAxis = bendingAxis; Mode = mode; _selectedSection = section; _selectedMaterial = material;
        _axes = section.Axes.Select(a => new AxisChoice(a.AxisDesignation)).ToArray();
        OriginalSection = IsEdit ? section : null; OriginalMaterial = IsEdit ? material : null;
        _resultPresentation = resultPresentation ?? ResultPresentationOptions.Default;
        Libraries = libraries;
        ApplyCommand = new(() => apply(this), () => CanApply);
        CancelCommand = new(cancel, () => IsEdit && !IsBusy);
        ChangeMaterialCommand = new(ShowMaterials, () => !IsBusy);
        ChangeSectionCommand = new(ShowSections, () => !IsBusy);
    }
    internal MainWindowViewModel? Libraries { get; }
    public ProjectSetupMode Mode { get; }
    public bool IsEdit => Mode == ProjectSetupMode.Edit;
    public bool IsBusy => Libraries?.IsBusy == true;
    public ProjectSetupPage Page => _page;
    public object? CurrentStep => _step;
    public bool IsOverview => Page == ProjectSetupPage.Overview;
    public string Title => IsEdit ? Strings.EditProject : Strings.NewProject;
    public string PrimaryAction => IsEdit ? Strings.Apply : Strings.CreateProject;
    public ISectionDefinition? OriginalSection { get; }
    public Material? OriginalMaterial { get; }
    public bool CanApply => IsOverview && !IsBusy && SelectedMaterial is not null;
    public ISectionDefinition SelectedSection
    {
        get => _selectedSection;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            var axis = value.Axes.Any(a => a.AxisDesignation == _bendingAxis) ? _bendingAxis : value.Axes[0].AxisDesignation;
            _selectedSection = value; _bendingAxis = axis;
            _axes = value.Axes.Select(a => new AxisChoice(a.AxisDesignation)).ToArray();
            _updatingSection = true;
            try { Notify(nameof(AvailableAxes)); }
            finally { _updatingSection = false; }
            Notify(nameof(SelectedSection)); RefreshSection();
        }
    }
    public SectionAxisDesignation BendingAxis
    {
        get => _bendingAxis;
        set { SelectedSection.GetAxis(value); if (Set(ref _bendingAxis, value)) RefreshSection(); }
    }
    public IReadOnlyList<AxisChoice> AvailableAxes => _axes;
    public AxisChoice SelectedAxisChoice
    {
        get => new(BendingAxis);
        set { if (value is not null && !_updatingSection) BendingAxis = value.Value; }
    }
    private SectionAxisProperties SelectedAxis => SelectedSection.GetAxis(BendingAxis);
    public Material? SelectedMaterial
    {
        get => _selectedMaterial;
        set
        {
            if (!Set(ref _selectedMaterial, value)) return;
            foreach (var name in new[] { nameof(MaterialName), nameof(YoungsModulus), nameof(YieldStrength),
                nameof(Density), nameof(PoissonRatio), nameof(HasDensity), nameof(HasPoissonRatio), nameof(CanApply) }) Notify(name);
            ApplyCommand.Refresh();
        }
    }
    public string MaterialName => SelectedMaterial?.Name ?? Strings.SelectMaterialRequired;
    public string? StartupError => Libraries?.BuiltInError;
    public bool HasStartupError => StartupError is not null;
    public string SectionName => SectionDisplay.Name(SelectedSection, ResultPresentation.Profile);
    public ResultPresentationOptions ResultPresentation => _resultPresentation;
    public string InertiaLabel => "I" + BendingAxis.ToString().ToLowerInvariant();
    public string ModulusLabel => "W" + BendingAxis.ToString().ToLowerInvariant();
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
    public ActionCommand ApplyCommand { get; }
    public ActionCommand CancelCommand { get; }
    public ActionCommand ChangeMaterialCommand { get; }
    public ActionCommand ChangeSectionCommand { get; }
    public void ShowMaterials() { if (!IsBusy) Navigate(ProjectSetupPage.MaterialSelection, new MaterialSelectionViewModel(this)); }
    public void ShowSections() { if (!IsBusy) Navigate(ProjectSetupPage.SectionSelection, new SectionSelectionViewModel(this)); }
    public void ShowOverview() { if (!IsBusy) Navigate(ProjectSetupPage.Overview, null); }
    internal void EditMaterial(SpanDraft.Desktop.Libraries.MaterialLibraryEntry? entry, SetupEditorMode mode) =>
        Navigate(ProjectSetupPage.MaterialEditor, new MaterialEditorViewModel(this, entry, mode));
    internal void EditCurrentMaterial() => Navigate(ProjectSetupPage.MaterialEditor,
        new MaterialEditorViewModel(this, null, SetupEditorMode.ProjectDraft, OriginalMaterial));
    internal void EditSection(ISectionDefinition? section, string? presetName, SetupEditorMode mode) =>
        Navigate(ProjectSetupPage.SectionEditor, new SectionEditorViewModel(this, section, presetName, mode));
    public void Escape()
    {
        if (IsBusy) return;
        switch (Page)
        {
            case ProjectSetupPage.MaterialEditor: ShowMaterials(); break;
            case ProjectSetupPage.SectionEditor: ShowSections(); break;
            case ProjectSetupPage.MaterialSelection or ProjectSetupPage.SectionSelection: ShowOverview(); break;
        }
    }
    public async Task<bool> ConfirmAsync() => CurrentStep switch
    {
        MaterialEditorViewModel m => await m.ConfirmAsync(),
        SectionEditorViewModel s => await s.ConfirmAsync(),
        _ => ConfirmOverview()
    };
    private bool ConfirmOverview() { if (!CanApply) return false; ApplyCommand.Execute(null); return true; }
    private void Navigate(ProjectSetupPage page, object? step)
    {
        _page = page; _step = step;
        Notify(nameof(Page)); Notify(nameof(CurrentStep)); Notify(nameof(IsOverview)); Notify(nameof(CanApply));
        ApplyCommand.Refresh();
    }
    public bool CanApplyInputUnits(UnitProfile profile) => CurrentStep is not SectionEditorViewModel s || s.CanApplyInputUnits(profile);
    internal void ApplyResultPresentation(ResultPresentationOptions options)
    {
        if (!Set(ref _resultPresentation, options, nameof(ResultPresentation))) return;
        RefreshSection();
        if (CurrentStep is SectionEditorViewModel s) s.RefreshPresentation();
        if (CurrentStep is SectionSelectionViewModel selection) selection.Refresh();
    }
    private void RefreshSection()
    {
        foreach (var name in new[] { nameof(SectionName), nameof(SelectedAxisChoice), nameof(BendingAxis),
            nameof(Area), nameof(Inertia), nameof(Modulus), nameof(InertiaLabel), nameof(ModulusLabel),
            nameof(IsAsymmetric), nameof(PositiveModulus), nameof(NegativeModulus) }) Notify(name);
    }
    internal void RefreshLibraryState()
    {
        Notify(nameof(IsBusy)); Notify(nameof(CanApply));
        ApplyCommand.Refresh(); CancelCommand.Refresh(); ChangeMaterialCommand.Refresh(); ChangeSectionCommand.Refresh();
        if (CurrentStep is SetupStepViewModel step) step.Refresh();
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
