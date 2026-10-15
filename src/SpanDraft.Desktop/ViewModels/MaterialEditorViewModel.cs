using SpanDraft.Core.Materials;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Libraries;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Resources;

namespace SpanDraft.Desktop.ViewModels;

public sealed class MaterialEditorViewModel : SetupStepViewModel
{
    private static readonly UnitDefinition GPa = new("GPa", "GPa", UnitDimension.Pressure, PresentationNumber.Parse("1e9"));
    private string _name, _number, _standards, _nameBeforeEdit;
    private SetupChoice<MaterialCategory>? _category;
    private bool _editingName;
    private string? _error, _saveError;
    private readonly string? _originalName;
    private MaterialLibraryEntry? _savedEntry;

    public MaterialEditorViewModel(ProjectSetupViewModel setup, MaterialLibraryEntry? entry, SetupEditorMode mode,
        Material? projectSnapshot = null) : base(setup)
    {
        var material = entry?.Material ?? projectSnapshot;
        Mode = mode; _originalName = IsLibraryEdit ? entry?.Material.Name : null;
        _name = _nameBeforeEdit = material?.Name ?? ""; _editingName = material is null;
        _number = entry?.MaterialNumber ?? ""; _standards = string.Join("; ", entry?.OtherStandards ?? []);
        Categories = MaterialSelectionViewModel.Categories();
        _category = Categories.Single(c => c.Value == (entry?.Category ?? MaterialCategory.Other));
        Inputs =
        [
            new("E", Strings.MaterialYoungsModulus, GPa, Validate, material?.YoungsModulus.Pascals),
            new("fy", Strings.MaterialYieldStrength, UnitCatalog.Megapascal, Validate, material?.YieldStrength.Pascals),
            new("rho", Strings.MaterialDensity, UnitCatalog.Dimensionless, Validate, material?.Density?.KilogramsPerCubicMeter, optional: true, hint: "kg/m³"),
            new("nu", Strings.MaterialPoissonRatio, UnitCatalog.Dimensionless, Validate, material?.PoissonRatio?.Value, optional: true, signed: true)
        ];
        ApplyCommand = new(ApplyAsync, () => CanApply);
        AddLibraryCommand = new(AddLibraryAsync, () => CanAddLibrary);
        ReplaceLibraryCommand = new(ReplaceLibraryAsync, () => CanReplaceLibrary);
        CancelCommand = new(() => setup.ReturnToMaterials(), () => !IsBusy);
        EditNameCommand = new(BeginNameEdit, () => !IsBusy);
        Validate();
    }
    public SetupEditorMode Mode { get; }
    public bool IsLibraryEdit => Mode == SetupEditorMode.UserLibraryEdit;
    public bool IsProjectDraft => !IsLibraryEdit;
    public string PrimaryAction => IsLibraryEdit ? Strings.Save : Strings.Apply;
    public string HeaderName => string.IsNullOrWhiteSpace(Name) ? Strings.NewMaterial : Name;
    public string CancelLabel => IsLibraryEdit ? Strings.Back : Strings.SectionDefinitionDiscard;
    public string Name { get => _name; set { if (!IsBusy && Set(ref _name, value)) Validate(); } }
    public string MaterialNumber { get => _number; set { if (!IsBusy && Set(ref _number, value)) Validate(); } }
    public string OtherStandards { get => _standards; set { if (!IsBusy && Set(ref _standards, value)) Validate(); } }
    public IReadOnlyList<SetupChoice<MaterialCategory>> Categories { get; }
    public SetupChoice<MaterialCategory>? Category { get => _category; set { if (!IsBusy && Set(ref _category, value)) Validate(); } }
    public IReadOnlyList<SetupNumberInput> Inputs { get; }
    public bool IsEditingName => _editingName;
    public bool ShowNameInput => _editingName || string.IsNullOrWhiteSpace(Name);
    public string? NameError => string.IsNullOrWhiteSpace(Name) ? Strings.MaterialNameRequired : null;
    public bool HasNameError => NameError is not null;
    public string? CategoryError => Category is null || !Categories.Contains(Category) ? Strings.MaterialCategoryRequired : null;
    public bool HasCategoryError => CategoryError is not null;
    public bool CanSaveToLibrary => Setup.Libraries?.CanSaveMaterials == true;
    private bool HasLibraryNameConflict => Draft is { } entry && Setup.Libraries?.UserMaterials.Find(entry.Material.Name) is { } existing
        && !IsInLibrary && !StringComparer.OrdinalIgnoreCase.Equals(existing.Material.Name, _originalName);
    public string? LibraryError => _saveError ?? Setup.Libraries?.MaterialLibraryError
        ?? (HasLibraryNameConflict ? Strings.LibraryNameConflict : null);
    public bool HasLibraryError => LibraryError is not null;
    public MaterialLibraryEntry? Draft { get; private set; }
    public bool IsInLibrary => _savedEntry is not null && Draft is { } entry && MaterialSnapshotComparison.Equals(_savedEntry, entry)
        && Setup.Libraries?.UserMaterials.Find(entry.Material.Name) is { } saved && MaterialSnapshotComparison.Equals(saved, entry);
    public string LibraryActionLabel => IsInLibrary ? Strings.MaterialInLibrary : Strings.SectionDefinitionAddLibrary;
    public bool CanApply => IsProjectDraft && !IsBusy && Draft is not null;
    public bool CanAddLibrary => IsProjectDraft && !IsBusy && Draft is not null && CanSaveToLibrary && !IsInLibrary && !HasLibraryNameConflict;
    public bool CanReplaceLibrary => IsLibraryEdit && !IsBusy && Draft is not null && CanSaveToLibrary && !HasLibraryNameConflict;
    public bool CanConfirm => IsLibraryEdit ? CanReplaceLibrary : CanApply;
    public string? Error => _error ?? (IsLibraryEdit ? LibraryError : null);
    public bool HasError => Error is not null;
    public AsyncActionCommand ApplyCommand { get; }
    public AsyncActionCommand AddLibraryCommand { get; }
    public AsyncActionCommand ReplaceLibraryCommand { get; }
    public AsyncActionCommand ConfirmCommand => IsLibraryEdit ? ReplaceLibraryCommand : ApplyCommand;
    public ActionCommand CancelCommand { get; }
    public ActionCommand EditNameCommand { get; }

    public void BeginNameEdit()
    {
        if (IsBusy) return;
        _nameBeforeEdit = Name; _editingName = true; Refresh();
    }
    public bool EndNameEdit()
    {
        if (HasNameError) return false;
        _editingName = false; Refresh(); return true;
    }
    public void CancelNameEdit()
    {
        if (IsBusy) return;
        Name = _nameBeforeEdit; _editingName = false; Refresh();
    }
    private void Validate()
    {
        _saveError = null; _error = null; Draft = null;
        if (HasNameError) _error = NameError;
        else if (HasCategoryError) _error = CategoryError;
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
        Refresh();
    }
    public Task<bool> ConfirmAsync() => IsLibraryEdit ? ReplaceLibraryAsync() : ApplyAsync();
    public Task<bool> ApplyAsync()
    {
        if (!CanApply || Draft is not { } entry) return Task.FromResult(false);
        Setup.SelectedMaterial = entry.Material;
        Setup.ShowOverview();
        return Task.FromResult(true);
    }
    public async Task<bool> AddLibraryAsync()
    {
        if (!CanAddLibrary || Draft is not { } entry) return false;
        _saveError = await Setup.Libraries!.SaveMaterialAsync(entry);
        if (_saveError is null) _savedEntry = entry;
        Refresh(); return _saveError is null;
    }
    public async Task<bool> ReplaceLibraryAsync()
    {
        if (!CanReplaceLibrary || Draft is not { } entry) return false;
        _saveError = await Setup.Libraries!.SaveMaterialAsync(entry, _originalName);
        if (_saveError is not null) { Refresh(); return false; }
        Setup.ReturnToMaterials(entry.Material.Name);
        return true;
    }
    public override void Refresh()
    {
        base.Refresh();
        foreach (var name in new[] { nameof(Error), nameof(HasError), nameof(Draft), nameof(CanConfirm), nameof(CanApply), nameof(CanAddLibrary), nameof(CanReplaceLibrary),
            nameof(CanSaveToLibrary), nameof(LibraryError), nameof(HasLibraryError), nameof(IsInLibrary), nameof(LibraryActionLabel), nameof(HeaderName),
            nameof(IsEditingName), nameof(ShowNameInput), nameof(NameError), nameof(HasNameError), nameof(CategoryError), nameof(HasCategoryError) }) Notify(name);
        ApplyCommand.Refresh(); AddLibraryCommand.Refresh(); ReplaceLibraryCommand.Refresh(); CancelCommand.Refresh(); EditNameCommand.Refresh();
    }
}
