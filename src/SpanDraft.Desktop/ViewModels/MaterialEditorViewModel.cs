using SpanDraft.Core.Materials;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Resources;
using SpanDraft.Reporting.Notation;

namespace SpanDraft.Desktop.ViewModels;

public sealed class MaterialEditorViewModel : SetupStepViewModel
{
    private string _name, _number, _standards;
    private SetupChoice<MaterialCategory>? _category;
    private SetupChoice<MaterialStrengthType>? _strengthType;
    private string? _error, _saveError;
    private readonly string? _originalName;
    private readonly Func<Material, Task<string?>> _confirm;
    private bool _confirming;
    private bool _nameValidationVisible, _strengthTypeValidationVisible;

    public MaterialEditorViewModel(ProjectSetupViewModel setup, Material? material, SetupEditorMode mode,
        Func<Material, Task<string?>>? confirm = null, bool showNameConflict = false) : base(setup)
    {
        Mode = mode;
        _confirm = confirm ?? (_ => Task.FromResult<string?>(null));
        _originalName = IsLibraryEdit ? material?.Name : null;
        _name = material?.Name ?? "";
        Title = IsLibraryEdit ? Strings.EditUserMaterial : IsProjectDraft ? Strings.MaterialCustomize : Strings.MaterialDefine;
        _number = material?.MaterialNumber ?? ""; _standards = string.Join("; ", material?.OtherStandards ?? []);
        Categories = MaterialSelectionViewModel.Categories();
        _category = Categories.Single(c => c.Value == (material?.Category ?? MaterialCategory.Other));
        StrengthTypes =
        [
            new(MaterialStrengthType.Re, MaterialStrengthDisplay.Symbol(MaterialStrengthType.Re)) { DisplayText = TechnicalSymbols.YieldStrength },
            new(MaterialStrengthType.Rp02, MaterialStrengthDisplay.Symbol(MaterialStrengthType.Rp02)) { DisplayText = TechnicalSymbols.ProofStrength }
        ];
        _strengthType = StrengthTypes.SingleOrDefault(c => c.Value == material?.StrengthType);
        _nameValidationVisible = showNameConflict;
        Inputs =
        [
            new("E", Strings.MaterialYoungsModulus + " " + TechnicalSymbols.YoungsModulus, setup.ResultPresentation.Profile[QuantityKind.YoungsModulus], Validate, material?.YoungsModulus.Pascals),
            new("strength", material?.StrengthType is { } type ? MaterialStrengthDisplay.LabelText(type) : Strings.MaterialStrength, setup.ResultPresentation.Profile[QuantityKind.MaterialStrength], Validate, material?.YieldStrength.Pascals),
            new("rho", Strings.MaterialDensity + " " + TechnicalSymbols.Density, setup.ResultPresentation.Profile[QuantityKind.Density], Validate, material?.Density?.KilogramsPerCubicMeter, optional: true),
            new("nu", Strings.MaterialPoissonRatio + " " + TechnicalSymbols.PoissonRatio, setup.ResultPresentation.Profile[QuantityKind.PoissonRatio], Validate, material?.PoissonRatio?.Value, optional: true, signed: true)
        ];
        ConfirmCommand = new(ConfirmAsync, () => CanConfirm);
        CancelCommand = new(() => CloseRequested?.Invoke(false), () => !IsBusy);
        Validate();
    }
    public event Action<bool>? CloseRequested;
    public SetupEditorMode Mode { get; }
    public bool IsLibraryEdit => Mode == SetupEditorMode.UserLibraryEdit;
    public bool IsProjectDraft => Mode == SetupEditorMode.ProjectDraft;
    public new bool IsBusy => _confirming || base.IsBusy;
    public string Title { get; }
    public string Name { get => _name; set { if (!IsBusy && Set(ref _name, value)) Validate(); } }
    public string MaterialNumber { get => _number; set { if (!IsBusy && Set(ref _number, value)) Validate(); } }
    public string OtherStandards { get => _standards; set { if (!IsBusy && Set(ref _standards, value)) Validate(); } }
    public IReadOnlyList<SetupChoice<MaterialCategory>> Categories { get; }
    public SetupChoice<MaterialCategory>? Category { get => _category; set { if (!IsBusy && Set(ref _category, value)) Validate(); } }
    public IReadOnlyList<SetupNumberInput> Inputs { get; }
    public IReadOnlyList<SetupChoice<MaterialStrengthType>> StrengthTypes { get; }
    public SetupChoice<MaterialStrengthType>? StrengthType
    {
        get => _strengthType;
        set
        {
            if (IsBusy || !Set(ref _strengthType, value)) return;
            Inputs[1].SetLabel(value is not null && StrengthTypes.Contains(value)
                ? MaterialStrengthDisplay.LabelText(value.Value) : Strings.MaterialStrength);
            Validate();
        }
    }
    public string? StrengthTypeError => StrengthType is null || !StrengthTypes.Contains(StrengthType) ? Strings.MaterialStrengthRequired : null;
    public bool HasStrengthTypeError => StrengthTypeError is not null;
    public bool ShowStrengthTypeError => _strengthTypeValidationVisible && HasStrengthTypeError;
    public void CompleteStrengthTypeInput()
    {
        _strengthTypeValidationVisible = true;
        Notify(nameof(ShowStrengthTypeError));
    }
    public string? NameError => string.IsNullOrWhiteSpace(Name) ? Strings.MaterialNameRequired
        : !IsProjectDraft && Setup.Libraries?.UserMaterials.Find(Name.Trim()) is { } existing
            && !StringComparer.OrdinalIgnoreCase.Equals(existing.Name, _originalName) ? Strings.LibraryNameConflict : null;
    public bool HasNameError => NameError is not null;
    public bool ShowNameError => _nameValidationVisible && HasNameError;
    public void CompleteNameInput()
    {
        _nameValidationVisible = true;
        Notify(nameof(ShowNameError));
    }
    public string? CategoryError => Category is null || !Categories.Contains(Category) ? Strings.MaterialCategoryRequired : null;
    public bool HasCategoryError => CategoryError is not null;
    public Material? Draft { get; private set; }
    public bool CanConfirm => !IsBusy && Draft is not null && !HasNameError;
    public string? Error => _saveError ?? _error;
    public bool HasError => Error is not null;
    public bool HasFormError => _saveError is not null || _error is not null && !HasNameError && !HasCategoryError && !HasStrengthTypeError && Inputs.All(i => !i.HasError);
    public AsyncActionCommand ConfirmCommand { get; }
    public ActionCommand CancelCommand { get; }
    private void Validate()
    {
        _saveError = null; _error = null; Draft = null;
        if (HasNameError) _error = NameError;
        else if (HasCategoryError) _error = CategoryError;
        else if (HasStrengthTypeError) _error = StrengthTypeError;
        else if (Inputs.Any(i => !i.TryRead(out _))) _error = Strings.InvalidMaterialInputs;
        else
        {
            Inputs[0].TryRead(out var e); Inputs[1].TryRead(out var strength);
            Inputs[2].TryRead(out var rho); Inputs[3].TryRead(out var nu);
            PoissonRatio? poisson = null;
            try { if (nu is { } value) poisson = PoissonRatio.FromValue(value); }
            catch (ArgumentException) { _error = Strings.InvalidPoissonRatio; }
            if (_error is null)
            {
                try
                {
                    Draft = new Material(Name.Trim(), Pressure.FromPascals(e!.Value), Pressure.FromPascals(strength!.Value), StrengthType!.Value,
                        rho is { } density ? MassDensity.FromKilogramsPerCubicMeter(density) : null, poisson,
                        Category!.Value, MaterialNumber, OtherStandards.Split(';'));
                }
                catch (ArgumentException) { _error = Strings.InvalidMaterialInputs; }
            }
        }
        Refresh();
    }
    public async Task<bool> ConfirmAsync()
    {
        if (!CanConfirm || Draft is not { } material) return false;
        _confirming = true; Refresh();
        try
        {
            _saveError = await _confirm(material);
            if (_saveError is not null) return false;
            CloseRequested?.Invoke(true);
            return true;
        }
        finally { _confirming = false; Refresh(); }
    }
    public override void Refresh()
    {
        base.Refresh();
        foreach (var name in new[] { nameof(IsBusy), nameof(Error), nameof(HasError), nameof(HasFormError), nameof(Draft), nameof(CanConfirm),
            nameof(NameError), nameof(HasNameError), nameof(ShowNameError), nameof(CategoryError), nameof(HasCategoryError), nameof(StrengthTypeError), nameof(HasStrengthTypeError), nameof(ShowStrengthTypeError) }) Notify(name);
        ConfirmCommand.Refresh(); CancelCommand.Refresh();
    }
}
