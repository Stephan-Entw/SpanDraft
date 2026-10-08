using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Resources;

namespace SpanDraft.Desktop.ViewModels;

public sealed class UnitSettingViewModel : ObservableObject
{
    private readonly SettingsViewModel _owner;
    public UnitSettingViewModel(SettingsViewModel owner, QuantityKind quantity, string label)
    {
        _owner = owner; Quantity = quantity; Label = label;
        Units = UnitCatalog.All.Where(u => u.Dimension == UnitCatalog.DimensionOf(quantity)).ToArray();
    }
    public QuantityKind Quantity { get; }
    public string Label { get; }
    public IReadOnlyList<UnitDefinition> Units { get; }
    public bool IsEditable => Units.Count > 1;
    public UnitDefinition SelectedUnit
    {
        get => _owner.Pending.Profile[Quantity];
        set { if (value is not null && value.Id != SelectedUnit.Id) _owner.ChangeUnit(Quantity, value); }
    }
    internal void Refresh() => Notify(nameof(SelectedUnit));
}

/// <summary>Independent dialog transaction; rejected saves retain the pending edits.</summary>
public sealed class SettingsViewModel : ObservableObject
{
    private readonly Func<UserSettings, Task<string?>> _apply;
    private UserSettings _applied;
    private UserSettings _pending;
    private bool _saving;
    private string? _error;

    public SettingsViewModel(UserSettings settings, Func<UserSettings, Task<string?>> apply)
    {
        _applied = _pending = settings; _apply = apply;
        LeftUnits = new[]
        {
            Row(QuantityKind.BeamLength, Strings.UnitBeamLength), Row(QuantityKind.SectionDimension, Strings.UnitSectionDimension),
            Row(QuantityKind.TransverseDisplacement, Strings.UnitDeflection), Row(QuantityKind.AxialDisplacement, Strings.UnitAxialDisplacement),
            Row(QuantityKind.Area, Strings.UnitArea), Row(QuantityKind.SecondMomentOfArea, Strings.UnitInertia), Row(QuantityKind.SectionModulus, Strings.UnitModulus)
        };
        RightUnits = new[]
        {
            Row(QuantityKind.TransverseForce, Strings.UnitForce), Row(QuantityKind.AxialForce, Strings.UnitAxialForce),
            Row(QuantityKind.Moment, Strings.Moment), Row(QuantityKind.DistributedLoad, Strings.DistributedLoad),
            Row(QuantityKind.Stress, Strings.UnitStress), Row(QuantityKind.Rotation, Strings.UnitRotation)
        };
    }

    public UserSettings Pending => _pending;
    public bool HasChanges => !_pending.ContentEquals(_applied);
    public bool IsSaving => _saving;
    public bool CanApply => HasChanges && !IsSaving;
    public string? ErrorText => _error;
    public bool HasError => ErrorText is not null;
    public bool HasCustomProfile => _pending.HasCustomProfile;
    public IReadOnlyList<UnitSettingViewModel> LeftUnits { get; }
    public IReadOnlyList<UnitSettingViewModel> RightUnits { get; }
    public bool IsMechanical { get => Kind == UnitProfileKind.MechanicalEngineering; set { if (value) Select(UnitProfileKind.MechanicalEngineering); } }
    public bool IsStructural { get => Kind == UnitProfileKind.StructuralEngineering; set { if (value) Select(UnitProfileKind.StructuralEngineering); } }
    public bool IsUnitedStates { get => Kind == UnitProfileKind.UnitedStates; set { if (value) Select(UnitProfileKind.UnitedStates); } }
    public bool IsCustom { get => Kind == UnitProfileKind.Custom; set { if (value) Select(UnitProfileKind.Custom); } }
    public bool IsStandard { get => _pending.Mode == PresentationMode.Standard; set { if (value) Update(_pending with { Mode = PresentationMode.Standard }); } }
    public bool IsDetailed { get => _pending.Mode == PresentationMode.Detailed; set { if (value) Update(_pending with { Mode = PresentationMode.Detailed }); } }
    private UnitProfileKind Kind => _pending.Profile.Kind;
    private UnitSettingViewModel Row(QuantityKind q, string label) => new(this, q, label);
    public void Select(UnitProfileKind kind) => Update(_pending.Select(kind));
    public void ChangeUnit(QuantityKind quantity, UnitDefinition unit) => Update(_pending.WithUnit(quantity, unit));
    public void DiscardPending() { if (!IsSaving) Update(_applied); }

    private void Update(UserSettings settings)
    {
        if (IsSaving || _pending.ContentEquals(settings)) return;
        _pending = settings; _error = null; Refresh();
    }

    public async Task<bool> ApplyAsync()
    {
        if (!CanApply) return false;
        _saving = true; _error = null; Refresh();
        try
        {
            _error = await _apply(_pending);
            if (_error is not null) return false;
            _applied = _pending;
            return true;
        }
        finally { _saving = false; Refresh(); }
    }

    private void Refresh()
    {
        foreach (string name in new[] { nameof(Pending), nameof(HasChanges), nameof(IsSaving), nameof(CanApply), nameof(ErrorText),
            nameof(HasError), nameof(HasCustomProfile), nameof(IsMechanical), nameof(IsStructural), nameof(IsUnitedStates),
            nameof(IsCustom), nameof(IsStandard), nameof(IsDetailed) }) Notify(name);
        foreach (var row in LeftUnits.Concat(RightUnits)) row.Refresh();
    }
}
