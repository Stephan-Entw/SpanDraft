using SpanDraft.Core.Materials;
using SpanDraft.Core.Sections;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.State;

namespace SpanDraft.Desktop.ViewModels;

public enum ProjectSetupMode { Create, Edit }

public sealed class ProjectSetupViewModel : ObservableObject
{
    private Section _selectedSection;
    private Material _selectedMaterial;
    private ResultPresentationOptions _resultPresentation;

    public ProjectSetupViewModel(ProjectSetupMode mode, Section section, Material material,
        Action<ProjectSetupViewModel> apply, Action cancel, ResultPresentationOptions? resultPresentation = null)
    {
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
    public IReadOnlyList<Section> Sections { get; }
    public IReadOnlyList<Material> Materials { get; }
    public Section SelectedSection
    {
        get => _selectedSection;
        set { if (Set(ref _selectedSection, value)) { Notify(nameof(Area)); Notify(nameof(Inertia)); Notify(nameof(Modulus)); } }
    }
    public Material SelectedMaterial
    {
        get => _selectedMaterial;
        set { if (Set(ref _selectedMaterial, value)) { Notify(nameof(YoungsModulus)); Notify(nameof(YieldStrength)); } }
    }
    public ResultPresentationOptions ResultPresentation => _resultPresentation;
    public string Area => Format(SelectedSection.Area.SquareMeters, QuantityKind.Area);
    public string Inertia => Format(SelectedSection.SecondMomentOfArea.MetersToTheFourth, QuantityKind.SecondMomentOfArea);
    public string Modulus => Format(SelectedSection.SectionModulus.CubicMeters, QuantityKind.SectionModulus);
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
