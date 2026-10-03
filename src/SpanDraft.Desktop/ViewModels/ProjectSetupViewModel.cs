using SpanDraft.Core.Materials;
using SpanDraft.Core.Sections;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.State;

namespace SpanDraft.Desktop.ViewModels;

public enum ProjectSetupMode { Create, Edit }

public sealed class ProjectSetupViewModel : ObservableObject
{
    private Section _selectedSection;
    private Material _selectedMaterial;

    public ProjectSetupViewModel(ProjectSetupMode mode, Section section, Material material,
        Action<ProjectSetupViewModel> apply, Action cancel)
    {
        Mode = mode;
        _selectedSection = section;
        _selectedMaterial = material;
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
    public string Area => UiNumbers.Indicator(SelectedSection.Area.SquareMillimeters) + " mm²";
    public string Inertia => UiNumbers.Indicator(SelectedSection.SecondMomentOfArea.MillimetersToTheFourth) + " mm⁴";
    public string Modulus => UiNumbers.Indicator(SelectedSection.SectionModulus.CubicMillimeters) + " mm³";
    public string YoungsModulus => UiNumbers.Indicator(SelectedMaterial.YoungsModulus.Pascals / 1e9) + " GPa";
    public string YieldStrength => UiNumbers.Indicator(SelectedMaterial.YieldStrength.Megapascals) + " MPa";
    public ActionCommand ApplyCommand { get; }
    public ActionCommand CancelCommand { get; }
}
