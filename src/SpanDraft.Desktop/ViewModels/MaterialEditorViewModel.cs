using SpanDraft.Core.Materials;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Libraries;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Resources;

namespace SpanDraft.Desktop.ViewModels;

public sealed class MaterialEditorViewModel : SetupStepViewModel
{
    private static readonly UnitDefinition GPa = new("GPa", "GPa", UnitDimension.Pressure, PresentationNumber.Parse("1e9"));
    private string _name, _number, _standards;
    private SetupChoice<MaterialCategory>? _category;
    private bool _save;
    private string? _error, _saveError;
    private readonly string? _originalName;
    public MaterialEditorViewModel(ProjectSetupViewModel setup, MaterialLibraryEntry? entry, SetupEditorMode mode,
        Material? projectSnapshot = null) : base(setup)
    {
        var m = entry?.Material ?? projectSnapshot;
        Mode = mode; _originalName = mode == SetupEditorMode.UserLibraryEdit ? entry?.Material.Name : null;
        _name = m?.Name ?? ""; _number = entry?.MaterialNumber ?? "";
        _standards = string.Join("; ", entry?.OtherStandards ?? []);
        Categories = MaterialSelectionViewModel.Categories();
        _category = Categories.Single(c => c.Value == (entry?.Category ?? MaterialCategory.Other));
        Inputs =
        [
            new("E", "E *", GPa, Validate, m?.YoungsModulus.Pascals),
            new("fy", "fy *", UnitCatalog.Megapascal, Validate, m?.YieldStrength.Pascals),
            new("rho", "ρ", UnitCatalog.Dimensionless, Validate, m?.Density?.KilogramsPerCubicMeter, optional: true, hint: "kg/m³"),
            new("nu", "ν", UnitCatalog.Dimensionless, Validate, m?.PoissonRatio?.Value, optional: true, signed: true)
        ];
        ConfirmCommand = new(ConfirmAsync, () => CanConfirm);
        CancelCommand = new(setup.ShowMaterials, () => !IsBusy);
        Validate();
    }
    public SetupEditorMode Mode { get; }
    public bool IsLibraryEdit => Mode == SetupEditorMode.UserLibraryEdit;
    public bool IsProjectDraft => !IsLibraryEdit;
    public string Title => IsLibraryEdit ? Strings.EditUserMaterial : Strings.CreateMaterial;
    public string PrimaryAction => IsLibraryEdit ? Strings.Save : Strings.Use;
    public string Name { get => _name; set { if (Set(ref _name, value)) Validate(); } }
    public string MaterialNumber { get => _number; set { if (Set(ref _number, value)) Validate(); } }
    public string OtherStandards { get => _standards; set { if (Set(ref _standards, value)) Validate(); } }
    public IReadOnlyList<SetupChoice<MaterialCategory>> Categories { get; }
    public SetupChoice<MaterialCategory>? Category { get => _category; set { if (Set(ref _category, value)) Validate(); } }
    public IReadOnlyList<SetupNumberInput> Inputs { get; }
    public bool SaveToLibrary { get => _save; set { if (Set(ref _save, value)) Validate(); } }
    public bool CanSaveToLibrary => Setup.Libraries?.CanSaveMaterials == true;
    public string? LibraryError => Setup.Libraries?.MaterialLibraryError;
    public bool HasLibraryError => LibraryError is not null;
    public MaterialLibraryEntry? Draft { get; private set; }
    public bool CanConfirm => !IsBusy && Draft is not null && (!(IsLibraryEdit || SaveToLibrary) || CanSaveToLibrary);
    public string? Error => _saveError ?? _error;
    public bool HasError => Error is not null;
    public AsyncActionCommand ConfirmCommand { get; }
    public ActionCommand CancelCommand { get; }
    private void Validate()
    {
        _saveError = null; _error = null; Draft = null;
        if (string.IsNullOrWhiteSpace(Name)) _error = Strings.MaterialNameRequired;
        else if (Category is null || !Categories.Contains(Category)) _error = Strings.MaterialCategoryRequired;
        else if (Inputs.Any(i => !i.TryRead(out _))) _error = Strings.InvalidMaterialInputs;
        else
        {
            Inputs[0].TryRead(out var e); Inputs[1].TryRead(out var fy);
            Inputs[2].TryRead(out var rho); Inputs[3].TryRead(out var nu);
            PoissonRatio? poisson = null;
            try { if (nu is { } value) poisson = PoissonRatio.FromValue(value); }
            catch (ArgumentException) { _error = Strings.InvalidPoissonRatio; }
            if (_error is null)
            {
                try
                {
                    Draft = new(new Material(Name.Trim(), Pressure.FromPascals(e!.Value), Pressure.FromPascals(fy!.Value),
                        rho is { } density ? MassDensity.FromKilogramsPerCubicMeter(density) : null, poisson),
                        Category!.Value, MaterialNumber, OtherStandards.Split(';').Select(s => s.Trim()).Where(s => s.Length > 0));
                }
                catch (ArgumentException) { _error = Strings.InvalidMaterialInputs; }
            }
        }
        if (Draft is not null && (IsLibraryEdit || SaveToLibrary))
        {
            var existing = Setup.Libraries?.UserMaterials.Find(Draft.Material.Name);
            if (existing is not null && !StringComparer.OrdinalIgnoreCase.Equals(existing.Material.Name, _originalName))
            { Draft = null; _error = Strings.LibraryNameConflict; }
        }
        Refresh();
    }
    public async Task<bool> ConfirmAsync()
    {
        if (!CanConfirm || Draft is not { } entry) return false;
        if (IsLibraryEdit || SaveToLibrary)
        {
            _saveError = await Setup.Libraries!.SaveMaterialAsync(entry, _originalName);
            if (_saveError is not null) { Refresh(); return false; }
        }
        if (IsLibraryEdit) Setup.ShowMaterials();
        else { Setup.SelectedMaterial = entry.Material; Setup.ShowOverview(); }
        return true;
    }
    public override void Refresh()
    {
        base.Refresh();
        foreach (var name in new[] { nameof(Error), nameof(HasError), nameof(Draft), nameof(CanConfirm),
            nameof(CanSaveToLibrary), nameof(LibraryError), nameof(HasLibraryError) }) Notify(name);
        ConfirmCommand.Refresh(); CancelCommand.Refresh();
    }
}
