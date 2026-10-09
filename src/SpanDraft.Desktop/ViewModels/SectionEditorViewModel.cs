using SpanDraft.Core.Sections;
using SpanDraft.Core.Sections.Parametric;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Libraries;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Resources;

namespace SpanDraft.Desktop.ViewModels;

public enum SectionEditorType { Rectangle, RectangularHollow, Circle, CircularHollow, ISection, USection, TSection, Angle, Manual }
public enum ManualAxisConfiguration { Y, Z, U, V, YZ, UV }

public sealed class SectionEditorViewModel : SetupStepViewModel
{
    private SetupChoice<SectionEditorType> _type;
    private SetupChoice<ManualAxisConfiguration> _configuration;
    private readonly Dictionary<string, double> _initial = [];
    private readonly Dictionary<string, SetupNumberInput> _manualBuffers = [];
    private UnitProfile _inputProfile;
    private readonly string? _originalName;
    private string _presetName;
    private bool _save;
    private string? _error, _saveError;
    private IReadOnlyList<SetupNumberInput> _parameters = [];

    public SectionEditorViewModel(ProjectSetupViewModel setup, ISectionDefinition? section, string? name, SetupEditorMode mode) : base(setup)
    {
        Mode = mode; _originalName = mode == SetupEditorMode.UserLibraryEdit ? name : null; _presetName = name ?? "";
        _inputProfile = setup.ResultPresentation.Profile;
        Types =
        [
            new(SectionEditorType.Rectangle, Strings.ShapeRectangle), new(SectionEditorType.RectangularHollow, Strings.ShapeRectangularHollow),
            new(SectionEditorType.Circle, Strings.ShapeCircle), new(SectionEditorType.CircularHollow, Strings.ShapeCircularHollow),
            new(SectionEditorType.ISection, Strings.ShapeI), new(SectionEditorType.USection, Strings.ShapeU),
            new(SectionEditorType.TSection, Strings.ShapeT), new(SectionEditorType.Angle, Strings.ShapeAngle),
            new(SectionEditorType.Manual, Strings.ShapeManual)
        ];
        Configurations =
        [
            new(ManualAxisConfiguration.Y, "y-y"), new(ManualAxisConfiguration.Z, "z-z"),
            new(ManualAxisConfiguration.U, "u-u"), new(ManualAxisConfiguration.V, "v-v"),
            new(ManualAxisConfiguration.YZ, "y-y / z-z"), new(ManualAxisConfiguration.UV, "u-u / v-v")
        ];
        _type = Types[0]; _configuration = Configurations[0];
        ReadSection(section);
        ConfirmCommand = new(ConfirmAsync, () => CanConfirm);
        CancelCommand = new(setup.ShowSections, () => !IsBusy);
        BuildInputs();
    }
    public SetupEditorMode Mode { get; }
    public bool IsLibraryEdit => Mode == SetupEditorMode.UserLibraryEdit;
    public bool IsProjectDraft => !IsLibraryEdit;
    public string Title => IsLibraryEdit ? Strings.EditUserSection : Strings.CreateSection;
    public string PrimaryAction => IsLibraryEdit ? Strings.Save : Strings.Use;
    public IReadOnlyList<SetupChoice<SectionEditorType>> Types { get; }
    public SetupChoice<SectionEditorType> Type
    {
        get => _type;
        set
        {
            if (value is null || !Types.Contains(value) || !Set(ref _type, value)) return;
            _initial.Clear(); _manualBuffers.Clear(); _inputProfile = Setup.ResultPresentation.Profile; BuildInputs(); Notify(nameof(IsManual));
        }
    }
    public bool IsManual => Type.Value == SectionEditorType.Manual;
    public IReadOnlyList<SetupChoice<ManualAxisConfiguration>> Configurations { get; }
    public SetupChoice<ManualAxisConfiguration> Configuration
    {
        get => _configuration;
        set
        {
            if (value is null || !Configurations.Contains(value) || !Set(ref _configuration, value)) return;
            foreach (var input in Parameters) _manualBuffers[input.Key] = input;
            BuildInputs();
        }
    }
    public string PresetName { get => _presetName; set { if (Set(ref _presetName, value)) Validate(); } }
    public bool SaveToLibrary { get => _save; set { if (Set(ref _save, value)) { Notify(nameof(NeedsPresetName)); Validate(); } } }
    public bool NeedsPresetName => IsLibraryEdit || SaveToLibrary;
    public bool CanSaveToLibrary => Setup.Libraries?.CanSaveSections == true;
    public string? LibraryError => Setup.Libraries?.SectionLibraryError;
    public bool HasLibraryError => LibraryError is not null;
    public IReadOnlyList<SetupNumberInput> Parameters => _parameters;
    public ISectionDefinition? Draft { get; private set; }
    public bool IsValid => Draft is not null;
    public string Area => Draft is { } s ? QuantityFormatter.Format(s.Area.SquareMeters, QuantityKind.Area,
        Setup.ResultPresentation.Profile, Setup.ResultPresentation.Mode) : "";
    public IReadOnlyList<SectionAxisValue> Axes => Draft is { } s ? SectionAxisValue.From(s, Setup.ResultPresentation) : [];
    public bool CanConfirm => !IsBusy && Draft is not null && (!NeedsPresetName || CanSaveToLibrary && !string.IsNullOrWhiteSpace(PresetName)) && _error is null;
    public string? Error => _saveError ?? _error;
    public bool HasError => Error is not null;
    public AsyncActionCommand ConfirmCommand { get; }
    public ActionCommand CancelCommand { get; }

    private void ReadSection(ISectionDefinition? section)
    {
        void Values(SectionEditorType type, params (string Key, double Value)[] values)
        { _type = Types.Single(t => t.Value == type); foreach (var v in values) _initial[v.Key] = v.Value; }
        switch (section)
        {
            case RectangleSectionGeometry s: Values(SectionEditorType.Rectangle, ("b", s.Width.Meters), ("h", s.Height.Meters)); break;
            case RectangleSection s: Values(SectionEditorType.Rectangle, ("b", s.Width.Meters), ("h", s.Height.Meters)); break;
            case RectangularHollowSectionGeometry s: Values(SectionEditorType.RectangularHollow, ("b", s.Width.Meters), ("h", s.Height.Meters), ("t", s.WallThickness.Meters), ("r", s.OuterRadius.Meters)); break;
            case RectangularHollowSection s: Values(SectionEditorType.RectangularHollow, ("b", s.Width.Meters), ("h", s.Height.Meters), ("t", s.WallThickness.Meters), ("r", 0)); break;
            case CircleSectionGeometry s: Values(SectionEditorType.Circle, ("d", s.Diameter.Meters)); break;
            case CircleSection s: Values(SectionEditorType.Circle, ("d", s.Diameter.Meters)); break;
            case CircularHollowSectionGeometry s: Values(SectionEditorType.CircularHollow, ("D", s.OuterDiameter.Meters), ("t", s.WallThickness.Meters)); break;
            case CircularHollowSection s: Values(SectionEditorType.CircularHollow, ("D", s.OuterDiameter.Meters), ("t", s.WallThickness.Meters)); break;
            case ISectionGeometry s: Values(SectionEditorType.ISection, ("h", s.Height.Meters), ("b", s.Width.Meters), ("tw", s.WebThickness.Meters), ("tf", s.FlangeThickness.Meters), ("r", s.Radius.Meters)); break;
            case USectionGeometry s: Values(SectionEditorType.USection, ("h", s.Height.Meters), ("b", s.Width.Meters), ("tw", s.WebThickness.Meters), ("tf", s.FlangeThickness.Meters), ("r", s.Radius.Meters)); break;
            case TSectionGeometry s: Values(SectionEditorType.TSection, ("h", s.Height.Meters), ("b", s.Width.Meters), ("tw", s.WebThickness.Meters), ("tf", s.FlangeThickness.Meters), ("r", s.Radius.Meters)); break;
            case AngleSectionGeometry s: Values(SectionEditorType.Angle, ("b", s.Width.Meters), ("h", s.Height.Meters), ("t", s.Thickness.Meters), ("r", s.InnerRadius.Meters)); break;
            case ManualSectionDefinition or CustomSection:
                Values(SectionEditorType.Manual, ("A", section.Area.SquareMeters));
                var axes = section.Axes;
                var config = axes.Count == 2 ? axes[0].AxisDesignation == SectionAxisDesignation.Y ? ManualAxisConfiguration.YZ : ManualAxisConfiguration.UV
                    : Enum.Parse<ManualAxisConfiguration>(axes[0].AxisDesignation.ToString());
                _configuration = Configurations.Single(c => c.Value == config);
                foreach (var a in axes)
                {
                    _initial["I" + a.AxisDesignation] = a.SecondMomentOfArea.MetersToTheFourth;
                    _initial["W" + a.AxisDesignation] = a.PositiveSectionModulus.CubicMeters;
                }
                break;
        }
    }
    private IReadOnlyList<SectionAxisDesignation> ManualAxes => Configuration.Value switch
    {
        ManualAxisConfiguration.Y => [SectionAxisDesignation.Y], ManualAxisConfiguration.Z => [SectionAxisDesignation.Z],
        ManualAxisConfiguration.U => [SectionAxisDesignation.U], ManualAxisConfiguration.V => [SectionAxisDesignation.V],
        ManualAxisConfiguration.YZ => [SectionAxisDesignation.Y, SectionAxisDesignation.Z],
        ManualAxisConfiguration.UV => [SectionAxisDesignation.U, SectionAxisDesignation.V],
        _ => []
    };
    private void BuildInputs()
    {
        var inputs = new List<SetupNumberInput>();
        void Add(string key, string label, QuantityKind kind, bool radius = false, string? hint = null)
        {
            if (IsManual && _manualBuffers.TryGetValue(key, out var buffer)) inputs.Add(buffer);
            else inputs.Add(new(key, label, _inputProfile[kind], Validate,
                _initial.TryGetValue(key, out var value) ? value : radius ? 0 : null, nonnegative: radius, hint: hint));
        }
        if (IsManual)
        {
            Add("A", "A", QuantityKind.Area);
            foreach (var axis in ManualAxes)
            {
                Add("I" + axis, "I · " + new AxisChoice(axis).Label, QuantityKind.SecondMomentOfArea);
                Add("W" + axis, "W · " + new AxisChoice(axis).Label, QuantityKind.SectionModulus);
            }
        }
        else
        {
            string[] keys = Type.Value switch
            {
                SectionEditorType.Rectangle => ["b", "h"], SectionEditorType.RectangularHollow => ["b", "h", "t", "r"],
                SectionEditorType.Circle => ["d"], SectionEditorType.CircularHollow => ["D", "t"],
                SectionEditorType.ISection or SectionEditorType.USection or SectionEditorType.TSection => ["h", "b", "tw", "tf", "r"],
                SectionEditorType.Angle => ["b", "h", "t", "r"], _ => []
            };
            foreach (var key in keys)
                Add(key, key, QuantityKind.SectionDimension, key == "r",
                    key != "r" ? null : Type.Value == SectionEditorType.RectangularHollow ? Strings.OuterRadiusHint
                        : Type.Value == SectionEditorType.Angle ? Strings.InnerRadiusHint : Strings.FilletRadiusHint);
        }
        _parameters = inputs; Notify(nameof(Parameters)); Validate();
    }
    private void Validate()
    {
        _saveError = null; _error = null; Draft = null;
        if (Parameters.Any(p => !p.TryRead(out _))) _error = Strings.InvalidSectionInputs;
        else
        {
            double V(string key) { Parameters.Single(p => p.Key == key).TryRead(out var v); return v!.Value; }
            Length L(string key) => Length.FromMeters(V(key));
            try
            {
                Draft = Type.Value switch
                {
                    SectionEditorType.Rectangle => new RectangleSectionGeometry(L("b"), L("h")),
                    SectionEditorType.RectangularHollow => new RectangularHollowSectionGeometry(L("b"), L("h"), L("t"), L("r")),
                    SectionEditorType.Circle => new CircleSectionGeometry(L("d")),
                    SectionEditorType.CircularHollow => new CircularHollowSectionGeometry(L("D"), L("t")),
                    SectionEditorType.ISection => new ISectionGeometry(L("h"), L("b"), L("tw"), L("tf"), L("r")),
                    SectionEditorType.USection => new USectionGeometry(L("h"), L("b"), L("tw"), L("tf"), L("r")),
                    SectionEditorType.TSection => new TSectionGeometry(L("h"), L("b"), L("tw"), L("tf"), L("r")),
                    SectionEditorType.Angle => new AngleSectionGeometry(L("b"), L("h"), L("t"), L("r")),
                    SectionEditorType.Manual => new ManualSectionDefinition(SpanDraft.Core.Units.Area.FromSquareMeters(V("A")),
                        Axis(ManualAxes[0]), ManualAxes.Count == 2 ? Axis(ManualAxes[1]) : null),
                    _ => null
                };
                ManualSectionAxis Axis(SectionAxisDesignation a) => new(a,
                    SecondMomentOfArea.FromMetersToTheFourth(V("I" + a)), SectionModulus.FromCubicMeters(V("W" + a)));
            }
            catch (ArgumentException) { _error = Strings.InvalidSectionGeometry; }
        }
        if (NeedsPresetName)
        {
            if (string.IsNullOrWhiteSpace(PresetName)) _error = Strings.PresetNameRequired;
            else
            {
                var existing = Setup.Libraries?.UserSections.Find(PresetName.Trim());
                if (existing is not null && !StringComparer.OrdinalIgnoreCase.Equals(existing.Name, _originalName))
                    _error = Strings.LibraryNameConflict;
            }
        }
        Refresh();
    }
    public async Task<bool> ConfirmAsync()
    {
        if (!CanConfirm || Draft is not { } section) return false;
        if (NeedsPresetName)
        {
            _saveError = await Setup.Libraries!.SaveSectionAsync(new SectionLibraryEntry(PresetName.Trim(), section), _originalName);
            if (_saveError is not null) { Refresh(); return false; }
        }
        if (IsLibraryEdit) Setup.ShowSections();
        else { Setup.SelectedSection = section; Setup.ShowOverview(); }
        return true;
    }
    public bool CanApplyInputUnits(UnitProfile profile) => (IsManual
        ? new[] { QuantityKind.Area, QuantityKind.SecondMomentOfArea, QuantityKind.SectionModulus }
        : [QuantityKind.SectionDimension]).All(q => profile[q].Id == _inputProfile[q].Id);
    public void RefreshPresentation() { Notify(nameof(Area)); Notify(nameof(Axes)); }
    public override void Refresh()
    {
        base.Refresh();
        foreach (var name in new[] { nameof(Error), nameof(HasError), nameof(Draft), nameof(IsValid), nameof(CanConfirm),
            nameof(CanSaveToLibrary), nameof(LibraryError), nameof(HasLibraryError) }) Notify(name);
        RefreshPresentation(); ConfirmCommand.Refresh(); CancelCommand.Refresh();
    }
}
