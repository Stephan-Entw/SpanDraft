using SpanDraft.Core.Materials;
using SpanDraft.Core.Sections;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.State;

namespace SpanDraft.Desktop.ViewModels;

public enum ProjectSetupMode { Create, Edit }

public sealed class ProjectSetupViewModel : ObservableObject
{
    private ISectionDefinition _selectedSection;
    private Material _selectedMaterial;
    private ResultPresentationOptions _resultPresentation;

    public ProjectSetupViewModel(ProjectSetupMode mode, Section section, Material material,
        Action<ProjectSetupViewModel> apply, Action cancel, ResultPresentationOptions? resultPresentation = null)
        : this(mode, section, SectionAxisDesignation.Y, material, apply, cancel, resultPresentation)
    {
    }

    public ProjectSetupViewModel(ProjectSetupMode mode, ISectionDefinition section, SectionAxisDesignation bendingAxis,
        Material material, Action<ProjectSetupViewModel> apply, Action cancel,
        ResultPresentationOptions? resultPresentation = null)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(material);
        section.GetAxis(bendingAxis);
        BendingAxis = bendingAxis;
        Mode = mode;
        _selectedSection = section;
        _selectedMaterial = material;
        _resultPresentation = resultPresentation ?? ResultPresentationOptions.Default;
        Sections = [section];
        Materials = [material];
        ApplyCommand = new(() => apply(this));
        CancelCommand = new(cancel);
    }

    public ProjectSetupMode Mode { get; }
    public bool IsEdit => Mode == ProjectSetupMode.Edit;
    public string Title => IsEdit ? Strings.EditProject : Strings.NewProject;
    public string PrimaryAction => IsEdit ? Strings.Apply : Strings.CreateProject;
    public IReadOnlyList<ISectionDefinition> Sections { get; }
    public IReadOnlyList<Material> Materials { get; }
    public ISectionDefinition SelectedSection
    {
        get => _selectedSection;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            value.GetAxis(BendingAxis);
            if (Set(ref _selectedSection, value))
            { Notify(nameof(Area)); Notify(nameof(Inertia)); Notify(nameof(Modulus)); }
        }
    }
    public SectionAxisDesignation BendingAxis { get; }
    private SectionAxisProperties SelectedAxis => SelectedSection.GetAxis(BendingAxis);

    public Material SelectedMaterial
    {
        get => _selectedMaterial;
        set { if (Set(ref _selectedMaterial, value)) { Notify(nameof(YoungsModulus)); Notify(nameof(YieldStrength)); } }
    }
    public ResultPresentationOptions ResultPresentation => _resultPresentation;
    public string InertiaLabel => "I" + BendingAxis.ToString().ToLowerInvariant();
    public string ModulusLabel => "W" + BendingAxis.ToString().ToLowerInvariant();
    public string Area => Format(SelectedSection.Area.SquareMeters, QuantityKind.Area);
    public string Inertia => Format(SelectedAxis.SecondMomentOfArea.MetersToTheFourth, QuantityKind.SecondMomentOfArea);
    // A single W is truthful only for equal positive/negative section moduli.
    public string Modulus => SelectedAxis.PositiveSectionModulus == SelectedAxis.NegativeSectionModulus
        ? Format(SelectedAxis.PositiveSectionModulus.CubicMeters, QuantityKind.SectionModulus)
        : "W+ = " + Format(SelectedAxis.PositiveSectionModulus.CubicMeters, QuantityKind.SectionModulus)
            + "; W− = " + Format(SelectedAxis.NegativeSectionModulus.CubicMeters, QuantityKind.SectionModulus);
    public string YoungsModulus => UiNumbers.Indicator(SelectedMaterial.YoungsModulus.Pascals / 1e9) + " GPa";
    public string YieldStrength => UiNumbers.Indicator(SelectedMaterial.YieldStrength.Megapascals) + " MPa";
    public ActionCommand ApplyCommand { get; }
    public ActionCommand CancelCommand { get; }

    internal void ApplyResultPresentation(ResultPresentationOptions options)
    {
        if (!Set(ref _resultPresentation, options, nameof(ResultPresentation))) return;
        Notify(nameof(Area)); Notify(nameof(Inertia)); Notify(nameof(Modulus));
    }

    private string Format(double siValue, QuantityKind kind) =>
        QuantityFormatter.Format(siValue, kind, ResultPresentation.Profile, ResultPresentation.Mode);
}
