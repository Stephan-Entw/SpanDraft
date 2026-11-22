using System.Globalization;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.Services;
using SpanDraft.Reporting.Notation;

namespace SpanDraft.Desktop.ViewModels;

/// <summary>One line of the number preview: an exact value and how it is displayed with the pending settings.</summary>
public sealed record NumberPreviewRow(string Label, string Exact, string Displayed);

public sealed class UnitSettingViewModel : ObservableObject
{
    private readonly SettingsViewModel _owner;
    public UnitSettingViewModel(SettingsViewModel owner, QuantityKind quantity, TechnicalText label)
    {
        _owner = owner; Quantity = quantity; LabelText = label;
        Units = UnitCatalog.All.Where(u => u.Dimension == UnitCatalog.DimensionOf(quantity)).ToArray();
    }
    public QuantityKind Quantity { get; }
    public TechnicalText LabelText { get; }
    public string Label => LabelText.PlainText;
    public IReadOnlyList<UnitDefinition> Units { get; }
    public bool IsEditable => Units.Count > 1;
    public UnitDefinition SelectedUnit
    {
        get => _owner.Pending.Profile[Quantity];
        set { if (value is not null && value.Id != SelectedUnit.Id) _owner.ChangeUnit(Quantity, value); }
    }
    internal void Refresh() => Notify(nameof(SelectedUnit));
}

/// <summary>The unit settings of one engineering topic, shown under a common heading.</summary>
public sealed record UnitSettingGroup(string Title, IReadOnlyList<UnitSettingViewModel> Units);

/// <summary>Independent dialog transaction; rejected saves retain the pending edits.</summary>
public sealed class SettingsViewModel : ObservableObject
{
    private readonly Func<SettingsPatch, Task<string?>> _apply;
    private UserSettings _applied;
    private UserSettings _pending;
    private bool _saving;
    private string? _error;

    public SettingsViewModel(UserSettings settings, Func<SettingsPatch, Task<string?>> apply)
    {
        _applied = _pending = settings; _apply = apply;
        // Grouped by engineering topic, so a unit is found where the quantity belongs instead of in a list of 18 rows.
        UnitGroups =
        [
            new(Strings.UnitGroupGeometry,
            [
                Row(QuantityKind.BeamLength, Strings.UnitBeamLength), Row(QuantityKind.SectionDimension, Strings.UnitSectionDimension),
                Row(QuantityKind.Area, Strings.UnitArea + " " + TechnicalSymbols.Area),
                Row(QuantityKind.SecondMomentOfArea, Strings.UnitInertia + " " + TechnicalSymbols.Inertia()),
                Row(QuantityKind.SectionModulus, Strings.UnitModulus + " " + TechnicalSymbols.Modulus())
            ]),
            new(Strings.UnitGroupLoads,
            [
                Row(QuantityKind.TransverseForce, Strings.UnitForce), Row(QuantityKind.AxialForce, Strings.UnitAxialForce),
                Row(QuantityKind.Moment, Strings.Moment), Row(QuantityKind.DistributedLoad, Strings.DistributedLoad)
            ]),
            new(Strings.UnitGroupDeformation,
            [
                Row(QuantityKind.TransverseDisplacement, Strings.UnitDeflection), Row(QuantityKind.AxialDisplacement, Strings.UnitAxialDisplacement),
                Row(QuantityKind.Rotation, Strings.UnitRotation)
            ]),
            new(Strings.UnitGroupMaterial,
            [
                Row(QuantityKind.YoungsModulus, Strings.MaterialYoungsModulus), Row(QuantityKind.MaterialStrength, Strings.MaterialStrength),
                Row(QuantityKind.Stress, Strings.UnitStress), Row(QuantityKind.Density, Strings.MaterialDensity)
            ]),
            new(Strings.UnitGroupSprings,
            [
                Row(QuantityKind.TranslationalStiffness, Strings.UnitTranslationalStiffness),
                Row(QuantityKind.RotationalStiffness, Strings.UnitRotationalStiffness)
            ])
        ];
        UiLanguages =
        [
            new(UiLanguagePreference.System, Strings.SystemLanguage),
            new(UiLanguagePreference.German, Strings.GermanLanguage),
            new(UiLanguagePreference.English, Strings.EnglishLanguage)
        ];
        RegionalFormats =
        [
            new(RegionalFormatPreference.System, Strings.SystemRegionalFormat),
            new(RegionalFormatPreference.GermanGermany, Strings.RegionalFormatGermanGermany),
            new(RegionalFormatPreference.EnglishUnitedStates, Strings.RegionalFormatEnglishUnitedStates)
        ];
    }

    public UserSettings Pending => _pending;
    public bool HasChanges => !_pending.ContentEquals(_applied);
    public bool IsSaving => _saving;
    public bool CanApply => HasChanges && !IsSaving;
    public string? ErrorText => _error;
    public bool HasError => ErrorText is not null;
    public bool HasCustomProfile => _pending.HasCustomProfile;
    public int SignificantDigits
    {
        get => _pending.SignificantDigits;
        set => Update(_pending with { SignificantDigits = value });
    }
    /// <summary>
    /// Engineering notation as a yes/no choice. A check box, not a toggle switch: the dialog only applies its
    /// changes on OK, while a toggle switch signals an immediate effect.
    /// </summary>
    public bool UsesPowersOfTen
    {
        get => _pending.Notation == EngineeringNotation.Automatic;
        set => Update(_pending with { Notation = value ? EngineeringNotation.Automatic : EngineeringNotation.Off });
    }
    /// <summary>The thresholds of automatic engineering notation in the regional number format, with an example.</summary>
    public string PowersOfTenHint => string.Format(CultureInfo.CurrentUICulture, Strings.PowersOfTenHint,
        QuantityFormatter.Format(new DecimalNumber(1, -3)), QuantityFormatter.Format(new DecimalNumber(1, 6)),
        QuantityFormatter.FormatResult(1.23e6, QuantityKind.TransverseForce, UnitProfile.Default));
    /// <summary>
    /// Representative values in the pending units, digits and notation: a rounded given value (marked with ≈),
    /// a calculated value and a small calculated value that switches to powers of ten.
    /// </summary>
    public IReadOnlyList<NumberPreviewRow> PreviewRows
    {
        get
        {
            var options = _pending.Presentation;
            NumberPreviewRow Row(string label, double displayValue, QuantityKind kind, bool given)
            {
                var unit = options.Profile[kind];
                double si = unit.ToSi(displayValue);
                return new(label, QuantityFormatter.WithUnit(InputQuantityFormatter.FormatExact(si, unit), unit), given
                    ? QuantityFormatter.FormatInput(si, unit, options) : QuantityFormatter.FormatResult(si, kind, options));
            }
            return
            [
                Row(Strings.NumberPreviewInput, 1234.5678, QuantityKind.BeamLength, true),
                Row(Strings.NumberPreviewResult, 2.4583, QuantityKind.TransverseDisplacement, false),
                Row(Strings.NumberPreviewSmall, 0.00042, QuantityKind.TransverseDisplacement, false)
            ];
        }
    }
    public EngineeringNotation SelectedNotation
    {
        get => _pending.Notation;
        set => Update(_pending with { Notation = value });
    }
    public IReadOnlyList<UnitSettingGroup> UnitGroups { get; }
    /// <summary>The window shows the groups in two columns of about equal length: geometry and loads, then the rest.</summary>
    public IReadOnlyList<UnitSettingGroup> LeftUnitGroups => UnitGroups.Take(2).ToArray();
    public IReadOnlyList<UnitSettingGroup> RightUnitGroups => UnitGroups.Skip(2).ToArray();
    public IReadOnlyList<SetupChoice<UiLanguagePreference>> UiLanguages { get; }
    public IReadOnlyList<SetupChoice<RegionalFormatPreference>> RegionalFormats { get; }
    public UiLanguagePreference SelectedUiLanguage
    {
        get => _pending.UiLanguage;
        set => Update(_pending with { UiLanguage = value });
    }
    public RegionalFormatPreference SelectedRegionalFormat
    {
        get => _pending.RegionalFormat;
        set => Update(_pending with { RegionalFormat = value });
    }
    public bool IsMechanical { get => Kind == UnitProfileKind.MechanicalEngineering; set { if (value) Select(UnitProfileKind.MechanicalEngineering); } }
    public bool IsStructural { get => Kind == UnitProfileKind.StructuralEngineering; set { if (value) Select(UnitProfileKind.StructuralEngineering); } }
    public bool IsUnitedStates { get => Kind == UnitProfileKind.UnitedStates; set { if (value) Select(UnitProfileKind.UnitedStates); } }
    public bool IsCustom { get => Kind == UnitProfileKind.Custom; set { if (value) Select(UnitProfileKind.Custom); } }
    private UnitProfileKind Kind => _pending.Profile.Kind;
    private UnitSettingViewModel Row(QuantityKind q, TechnicalText label) => new(this, q, label);
    public void Select(UnitProfileKind kind) => Update(_pending.Select(kind));
    public void ChangeUnit(QuantityKind quantity, UnitDefinition unit) => Update(_pending.WithUnit(quantity, unit));
    public void DiscardPending()
    {
        if (IsSaving) return;
        Update(_applied);
    }

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
            // Only the editable preferences leave the dialog; fields it does not own are never written back.
            _error = await _apply(SettingsPatch.From(_pending));
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
            nameof(IsCustom), nameof(SignificantDigits), nameof(SelectedNotation), nameof(UsesPowersOfTen), nameof(PreviewRows),
            nameof(SelectedUiLanguage), nameof(SelectedRegionalFormat) }) Notify(name);
        foreach (var row in UnitGroups.SelectMany(g => g.Units)) row.Refresh();
    }
}
