using System.Globalization;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Sections.Parametric;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Libraries;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.Sections;
using SpanDraft.Desktop.State;

namespace SpanDraft.Desktop.ViewModels;

public sealed record SectionDefinitionForm(SectionEditorType Value, string Label);

public sealed record SectionDefinitionPreset(SectionLibraryEntry Entry, string Summary)
{
    public string Name => Entry.Name;
}

public enum SectionDefinitionLayout { FormSelection, ParametricDraft, ParametricSection, Manual }

public sealed class SectionDefinitionViewModel : ObservableObject
{
    private sealed class FormDraft
    {
        public string AutomaticName { get; set; } = "";
        public string? CustomName { get; set; }
        public Dictionary<string, SectionDefinitionInput> Inputs { get; } = [];
        public Dictionary<ManualAxisConfiguration, SectionAxisDesignation?> ManualAxisSelections { get; } = [];
        public ManualAxisConfiguration Configuration { get; set; } = ManualAxisConfiguration.Y;
        public SectionAxisDesignation? Axis { get; set; }
        public Dictionary<string, SectionDefinitionAxisValue> Blocks { get; } = [];
        public SectionPreviewGeometry? Preview { get; set; }
    }
    private readonly Dictionary<SectionEditorType, FormDraft> _drafts = [];
    private readonly ISectionDefinitionLibrary? _library;
    private readonly ResultPresentationOptions _presentation;
    private SectionEditorType? _type;
    private bool _choosing = true, _busy, _editingName;
    private string _pendingName = "";
    private string? _nameError, _saveError, _highlight;
    private ISectionDefinition? _section;
    private IReadOnlyList<SectionDefinitionAxisValue> _axisValues = [];
    private IReadOnlyList<SectionDefinitionPreset> _libraryEntries = [];

    public SectionDefinitionViewModel(ResultPresentationOptions? presentation = null, ISectionDefinitionLibrary? library = null,
        bool showLibraryOption = true)
    {
        _presentation = presentation ?? ResultPresentationOptions.Default; _library = library;
        ShowLibraryOption = showLibraryOption;
        Forms = new[]
        {
            new SectionDefinitionForm(SectionEditorType.Rectangle, Strings.ShapeRectangle),
            new(SectionEditorType.RectangularHollow, Strings.ShapeRectangularHollow), new(SectionEditorType.Circle, Strings.ShapeCircle),
            new(SectionEditorType.CircularHollow, Strings.ShapeCircularHollow), new(SectionEditorType.ISection, Strings.SectionDefinitionShapeI),
            new(SectionEditorType.USection, Strings.ShapeU), new(SectionEditorType.TSection, Strings.ShapeT), new(SectionEditorType.Angle, Strings.ShapeAngle)
        };
        Configurations = new[]
        {
            new SetupChoice<ManualAxisConfiguration>(ManualAxisConfiguration.Y, "y-y"), new(ManualAxisConfiguration.Z, "z-z"),
            new(ManualAxisConfiguration.U, "u-u"), new(ManualAxisConfiguration.V, "v-v"),
            new(ManualAxisConfiguration.YZ, "y-y / z-z"), new(ManualAxisConfiguration.UV, "u-u / v-v")
        };
        ChangeFormCommand = new(() => { if (CommitName()) { _choosing = true; Refresh(); } }, () => !IsBusy);
        EditNameCommand = new(BeginNameEdit, () => HasDefinition && !IsBusy);
        ResetNameCommand = new(ResetName, () => HasDefinition && !IsBusy);
        SaveLibraryCommand = new(SaveLibraryAsync, () => CanSaveLibrary);
        ConfirmCommand = new(ConfirmAsync, () => CanConfirm);
        CancelCommand = new(() => CloseRequested?.Invoke(null), () => !IsBusy);
    }
    public event Action<SectionDefinitionResult?>? CloseRequested;
    public ResultPresentationOptions Presentation => _presentation;
    public IReadOnlyList<SectionDefinitionForm> Forms { get; }
    public IReadOnlyList<SetupChoice<ManualAxisConfiguration>> Configurations { get; }
    public SectionEditorType? Type => _type;
    public SectionEditorType SelectedFormType => _type ?? SectionEditorType.Rectangle;
    public bool IsChoosingForm => _choosing;
    public bool HasDefinition => _type is not null && !_choosing;
    public bool IsManual => _type == SectionEditorType.Manual;
    public bool HasRadius => !IsManual && Parameters.Any(p => p.Key == "r");
    public SectionDefinitionLayout LayoutState => !HasDefinition ? SectionDefinitionLayout.FormSelection
        : IsManual ? SectionDefinitionLayout.Manual
        : HasValidSection ? SectionDefinitionLayout.ParametricSection : SectionDefinitionLayout.ParametricDraft;
    public bool ShowPreview => LayoutState is SectionDefinitionLayout.ParametricDraft or SectionDefinitionLayout.ParametricSection;
    public bool ShowValues => LayoutState is SectionDefinitionLayout.ParametricSection or SectionDefinitionLayout.Manual;
    public bool ShowConfirm => HasDefinition;
    public bool ShowLibraryAction => ShowLibraryOption && HasCommittedInputs && !string.IsNullOrWhiteSpace(DisplayName);
    public bool HasPreview => ShowPreview && Preview is not null;
    public string ValuesTitle => IsManual ? Strings.SectionDefinitionManualProperties : Strings.SectionDefinitionProperties;
    public string SelectedFormName => IsManual ? Strings.SectionDefinitionManualChoice : Forms.FirstOrDefault(f => f.Value == _type)?.Label ?? "";
    public bool IsBusy => _busy;
    public bool IsEditingName => _editingName;
    public string DisplayName => _type is null ? Strings.SectionDefinitionTitle : Current.CustomName ?? Current.AutomaticName;
    public string PendingName
    {
        get => _pendingName;
        set
        {
            if (!Set(ref _pendingName, value)) return;
            _nameError = null; Notify(nameof(NameError)); Notify(nameof(HasNameError));
            Notify(nameof(CanSaveLibrary)); SaveLibraryCommand.Refresh();
        }
    }
    public string? NameError => _nameError;
    public bool HasNameError => NameError is not null;
    public bool ShowLibraryOption { get; }
    public bool HasCustomName => _type is not null && Current.CustomName is not null;
    public bool ShowResetName => HasDefinition && IsEditingName;
    public string HeaderTitle => IsChoosingForm ? Strings.SectionDefinitionChooseTitle : DisplayName;
    public string? LibraryError => _saveError;
    public bool HasLibraryError => LibraryError is not null;
    public IReadOnlyList<SectionDefinitionPreset> LibraryEntries => _libraryEntries;
    public bool IsLibraryEmpty => _libraryEntries.Count == 0 && _library?.Error is null && _library is not null;
    private bool HasCommittedInputs => HasDefinition && _section is not null
        && Parameters.All(p => !p.IsPending && !p.HasError);
    public bool CanSaveLibrary => ShowLibraryAction && HasCommittedInputs && !IsBusy
        && (!IsEditingName || !string.IsNullOrWhiteSpace(PendingName));
    private FormDraft Current => _drafts[_type!.Value];
    public SetupChoice<ManualAxisConfiguration> Configuration
    {
        get => Configurations.Single(c => c.Value == (_type is null ? ManualAxisConfiguration.Y : Current.Configuration));
        set
        {
            if (!IsManual || value is null || !Configurations.Contains(value) || Current.Configuration == value.Value || IsBusy) return;
            Current.ManualAxisSelections[Current.Configuration] = Current.Axis;
            Current.Configuration = value.Value;
            Current.Axis = Current.ManualAxisSelections.GetValueOrDefault(value.Value);
            EnsureInputs(); NormalizeAxis(); RefreshInputs(); Recalculate(null);
        }
    }
    public IReadOnlyList<AxisChoice> AvailableAxes => _type is null ? [] : AxesForCurrent().Select(a => new AxisChoice(a)).ToArray();
    public SectionAxisDesignation? SelectedAxis
    {
        get => _type is null ? null : Current.Axis;
        set
        {
            if (!HasDefinition || IsBusy || !RequiresAxisSelection || value is null || !AxesForCurrent().Contains(value.Value) || Current.Axis == value) return;
            Current.Axis = value;
            if (IsManual) Current.ManualAxisSelections[Current.Configuration] = value;
            Refresh();
        }
    }
    public SectionAxisDesignation ResultAxis => RequiresAxisSelection && SelectedAxis is { } selected ? selected : AxesForCurrent()[0];
    public bool AxesEquivalent => HasValidSection && _section!.Axes.Count == 2 && HaveEquivalentProperties(_section.Axes[0], _section.Axes[1]);
    public bool RequiresAxisSelection => HasValidSection && _section!.Axes.Count > 1 && !AxesEquivalent;
    public static bool NearlyEqual(double first, double second) => first == second || Math.Abs(first - second) / Math.Max(Math.Abs(first), Math.Abs(second)) <= 1e-9;
    public static bool HaveEquivalentProperties(SectionAxisProperties first, SectionAxisProperties second) =>
        NearlyEqual(first.SecondMomentOfArea.MetersToTheFourth, second.SecondMomentOfArea.MetersToTheFourth)
        && NearlyEqual(first.PositiveSectionModulus.CubicMeters, second.PositiveSectionModulus.CubicMeters)
        && NearlyEqual(first.NegativeSectionModulus.CubicMeters, second.NegativeSectionModulus.CubicMeters);
    public string? HighlightedParameter { get => _highlight; set => Set(ref _highlight, value); }
    public IReadOnlyList<SectionDefinitionInput> Parameters => _type is null ? [] : Keys().Select(k => Current.Inputs[k]).ToArray();
    public IReadOnlyList<SectionDefinitionInput> GeometryInputs => IsManual ? [] : Parameters.OrderBy(p =>
        _type is SectionEditorType.Rectangle or SectionEditorType.RectangularHollow ? p.Key == "h" ? 0 : p.Key == "b" ? 1 : 2 : 0).ToArray();
    public IReadOnlyList<SectionDefinitionInput> ManualAreaInputs => IsManual ? [Current.Inputs["A"]] : [];
    public IReadOnlyList<SectionDefinitionInput> ManualInputs => IsManual ? Parameters : [];
    public SectionPreviewGeometry? Preview => _type is null ? null : Current.Preview;
    public ISectionDefinition? Section => _section;
    public bool HasValidSection => HasDefinition && _section is not null;
    public string AreaLabel => Strings.SectionDefinitionArea + " A";
    public string Area => _section is null ? "—" : Format(_section.Area.SquareMeters, QuantityKind.Area);
    public IReadOnlyList<SectionDefinitionAxisValue> AxisValues => _axisValues;
    private void RefreshAxisValues()
    {
        var blocks = new List<SectionDefinitionAxisValue>();
        if (ShowValues)
        {
            bool combined = !IsManual && AxesEquivalent;
            foreach (var axis in combined ? AxesForCurrent().Take(1) : AxesForCurrent())
            {
                string key = (IsManual ? "manual-" : "calculated-") + axis + (combined ? "-combined" : "");
                if (!Current.Blocks.TryGetValue(key, out var block))
                {
                    block = new(axis, IsManual ? [Current.Inputs["I" + axis], Current.Inputs["W" + axis]] : []);
                    Current.Blocks[key] = block;
                }
                var properties = _section?.GetAxis(axis);
                string letter = combined ? "" : axis.ToString().ToLowerInvariant();
                string heading = new AxisChoice(axis).Label;
                if (combined) heading = string.Join(" / ", AvailableAxes.Select(a => a.Label)) + " · " + Strings.SectionDefinitionEquivalent;
                else if (AxesEquivalent) heading += " · " + Strings.SectionDefinitionEquivalent;
                else if (HasValidSection && _section!.Axes.Count == 2)
                {
                    var other = _section.Axes.Single(a => a.AxisDesignation != axis);
                    double inertia = properties!.SecondMomentOfArea.MetersToTheFourth, otherInertia = other.SecondMomentOfArea.MetersToTheFourth;
                    if (!NearlyEqual(inertia, otherInertia)) heading += " · " + (inertia > otherInertia ? Strings.SectionDefinitionStrongAxis : Strings.SectionDefinitionWeakAxis);
                }
                block.Update(heading, Strings.SectionDefinitionInertia + " I" + letter,
                    Strings.SectionDefinitionModulus + " W" + letter + "+", Strings.SectionDefinitionModulus + " W" + letter + "−",
                    properties is null ? "—" : Format(properties.SecondMomentOfArea.MetersToTheFourth, QuantityKind.SecondMomentOfArea),
                    properties is null ? "—" : Format(properties.PositiveSectionModulus.CubicMeters, QuantityKind.SectionModulus),
                    properties is null ? "—" : Format(properties.NegativeSectionModulus.CubicMeters, QuantityKind.SectionModulus),
                    RequiresAxisSelection && SelectedAxis == axis, RequiresAxisSelection);
                blocks.Add(block);
            }
        }
        if (!_axisValues.SequenceEqual(blocks)) { _axisValues = blocks; Notify(nameof(AxisValues)); }
    }
    public bool CanConfirm => HasDefinition && !IsBusy && !_editingName && _section is not null && !string.IsNullOrWhiteSpace(DisplayName)
        && (!RequiresAxisSelection || SelectedAxis is not null)
        && Parameters.All(p => !p.IsPending && !p.HasError);
    public ActionCommand ChangeFormCommand { get; }
    public ActionCommand EditNameCommand { get; }
    public ActionCommand ResetNameCommand { get; }
    public AsyncActionCommand SaveLibraryCommand { get; }
    public AsyncActionCommand ConfirmCommand { get; }
    public ActionCommand CancelCommand { get; }

    public void SelectForm(SectionEditorType type)
    {
        if (IsBusy || !Enum.IsDefined(type) || !CommitName()) return;
        _type = type; _choosing = false; _highlight = null; _saveError = null;
        if (!_drafts.ContainsKey(type)) _drafts[type] = new();
        EnsureInputs(); NormalizeAxis(); RefreshInputs(); Recalculate(null);
    }
    private void NormalizeAxis() { if (Current.Axis is { } axis && !AxesForCurrent().Contains(axis)) Current.Axis = null; }
    private IReadOnlyList<SectionAxisDesignation> AxesForCurrent() => IsManual ? Current.Configuration switch
    {
        ManualAxisConfiguration.Y => [SectionAxisDesignation.Y], ManualAxisConfiguration.Z => [SectionAxisDesignation.Z],
        ManualAxisConfiguration.U => [SectionAxisDesignation.U], ManualAxisConfiguration.V => [SectionAxisDesignation.V],
        ManualAxisConfiguration.YZ => [SectionAxisDesignation.Y, SectionAxisDesignation.Z], _ => [SectionAxisDesignation.U, SectionAxisDesignation.V]
    } : _type == SectionEditorType.Angle ? [SectionAxisDesignation.U, SectionAxisDesignation.V] : [SectionAxisDesignation.Y, SectionAxisDesignation.Z];
    private string[] Keys() => IsManual ? new[] { "A" }.Concat(AxesForCurrent().SelectMany(a => new[] { "I"+a, "W"+a })).ToArray()
        : SectionPreviewGeometryResolver.ParameterKeys(_type!.Value);
    private void EnsureInputs()
    {
        foreach (var key in Keys())
        {
            if (Current.Inputs.ContainsKey(key)) continue;
            var quantity = !IsManual ? QuantityKind.SectionDimension : key == "A" ? QuantityKind.Area
                : key.StartsWith('I') ? QuantityKind.SecondMomentOfArea : QuantityKind.SectionModulus;
            Current.Inputs[key] = new(key, Presentation.Profile[quantity], InputChanged, key == "r", InputLabel(key));
        }
    }
    private string InputLabel(string key)
    {
        string symbol = key.Length > 1 && char.IsUpper(key[0]) ? key[..1] + key[1..].ToLowerInvariant() : key;
        string description = key switch
        {
            "A" => Strings.SectionDefinitionArea, _ when key.StartsWith('I') => Strings.SectionDefinitionInertia,
            _ when key.StartsWith('W') => Strings.SectionDefinitionModulus,
            "h" => Strings.SectionDefinitionHeight,
            "b" => _type == SectionEditorType.TSection ? Strings.SectionDefinitionFlangeWidth : Strings.SectionDefinitionWidth,
            "d" => Strings.SectionDefinitionDiameter, "D" => Strings.SectionDefinitionOuterDiameter,
            "t" => _type == SectionEditorType.Angle ? Strings.SectionDefinitionThickness : Strings.SectionDefinitionWallThickness,
            "tw" => Strings.SectionDefinitionWebThickness, "tf" => Strings.SectionDefinitionFlangeThickness,
            "r" => _type == SectionEditorType.RectangularHollow ? Strings.SectionDefinitionOuterRadius : Strings.SectionDefinitionInnerRadius,
            _ => symbol
        };
        return description + " " + symbol;
    }
    private void InputChanged(SectionDefinitionInput input, bool committed)
    {
        if (committed)
        {
            _saveError = null;
            if (input.LastCommitChangedValue)
            {
                Current.Axis = null;
                // A is shared by all configurations; axis inputs affect every configuration containing that axis.
                foreach (var configuration in Current.ManualAxisSelections.Keys.ToArray())
                    if (input.Key == "A" || configuration.ToString().Contains(input.Key[^1])) Current.ManualAxisSelections[configuration] = null;
            }
            Recalculate(input);
        }
        else
        {
            Notify(nameof(CanConfirm)); Notify(nameof(ShowLibraryAction)); Notify(nameof(CanSaveLibrary));
            ConfirmCommand.Refresh(); SaveLibraryCommand.Refresh();
        }
    }
    private void Recalculate(SectionDefinitionInput? source)
    {
        _section = null;
        foreach (var input in Parameters) input.SetGeometryError(null);
        var values = Parameters.Where(p => p.ConfirmedValue is not null).ToDictionary(p => p.Key, p => p.ConfirmedValue!.Value);
        if (Parameters.Any(p => p.HasParseError)) { Refresh(); return; }
        try
        {
            SectionPreviewGeometry? preview;
            if (Parameters.All(p => p.ConfirmedValue is not null))
            {
                _section = ConstructSection(values);
                preview = _section is IParametricSectionDefinition parametric
                    ? SectionPreviewGeometryResolver.FromSection(_type!.Value, values, parametric) : null;
            }
            else preview = IsManual ? null : SectionPreviewGeometryResolver.Resolve(_type!.Value, values);
            Current.Preview = preview;
            UpdateAutomaticName(values);
        }
        catch (PreviewGeometryException error) { MarkGeometryError(error.Key, source); }
        catch (ArgumentException error) { MarkGeometryError(error.ParamName, source); }
        Refresh();
    }
    private void MarkGeometryError(string? parameter, SectionDefinitionInput? source)
    {
        string? key = parameter switch
        {
            "width" => "b", "height" => "h", "diameter" => "d", "outerDiameter" => "D", "wallThickness" or "thickness" => "t",
            "webThickness" => "tw", "flangeThickness" => "tf", "radius" or "outerRadius" or "innerRadius" => "r", _ => parameter
        };
        var input = source ?? Parameters.FirstOrDefault(p => p.Key == key) ?? Parameters.FirstOrDefault(p => p.HasInteracted);
        if (input is null) return;
        var message = key switch
        {
            "t" => Strings.SectionDefinitionThicknessError, "tw" => Strings.SectionDefinitionWebError,
            "tf" => Strings.SectionDefinitionFlangeError, "r" => Strings.SectionDefinitionRadiusError,
            _ => Strings.InvalidSectionGeometry
        };
        input.SetGeometryError(message);
    }
    private ISectionDefinition ConstructSection(Dictionary<string, double> v)
    {
        Length L(string key) => Length.FromMeters(v[key]);
        ManualSectionAxis Axis(SectionAxisDesignation axis) => new(axis,
            SecondMomentOfArea.FromMetersToTheFourth(v["I"+axis]), SectionModulus.FromCubicMeters(v["W"+axis]));
        return _type switch
        {
            SectionEditorType.Rectangle => new RectangleSectionGeometry(L("b"),L("h")),
            SectionEditorType.RectangularHollow => new RectangularHollowSectionGeometry(L("b"),L("h"),L("t"),L("r")),
            SectionEditorType.Circle => new CircleSectionGeometry(L("d")),
            SectionEditorType.CircularHollow => new CircularHollowSectionGeometry(L("D"),L("t")),
            SectionEditorType.ISection => new ISectionGeometry(L("h"),L("b"),L("tw"),L("tf"),L("r")),
            SectionEditorType.USection => new USectionGeometry(L("h"),L("b"),L("tw"),L("tf"),L("r")),
            SectionEditorType.TSection => new TSectionGeometry(L("h"),L("b"),L("tw"),L("tf"),L("r")),
            SectionEditorType.Angle => new AngleSectionGeometry(L("b"),L("h"),L("t"),L("r")),
            SectionEditorType.Manual => new ManualSectionDefinition(SpanDraft.Core.Units.Area.FromSquareMeters(v["A"]),
                Axis(AxesForCurrent()[0]), AxesForCurrent().Count == 2 ? Axis(AxesForCurrent()[1]) : null),
            _ => throw new InvalidOperationException()
        };
    }
    private void UpdateAutomaticName(Dictionary<string, double> values)
    {
        if (IsManual) { Current.AutomaticName = Strings.SectionDefinitionCustomName; return; }
        string[] main = SectionPreviewGeometryResolver.ParameterKeys(_type!.Value).Where(k => k != "r").ToArray();
        var name = SelectedFormName;
        if (main.All(values.ContainsKey))
        {
            var unit = Presentation.Profile[QuantityKind.SectionDimension];
            name += " " + (_type is SectionEditorType.Circle or SectionEditorType.CircularHollow ? "Ø" : "")
                + string.Join(" × ", main.Select(k => InputQuantityFormatter.Display(values[k], unit))) + " " + unit.Symbol;
            if (values.TryGetValue("r", out double radius) && radius > 0)
                name += string.Format(CultureInfo.CurrentUICulture, Strings.SectionDefinitionRadiusSuffix, InputQuantityFormatter.Display(radius, unit), unit.Symbol);
        }
        Current.AutomaticName = name;
    }
    public void BeginNameEdit() { if (!HasDefinition || IsBusy) return; _pendingName = DisplayName; _editingName = true; _nameError = null; Refresh(); }
    public bool CommitName()
    {
        if (!IsEditingName) return true;
        string name = PendingName.Trim();
        if (string.IsNullOrWhiteSpace(name)) { _nameError = Strings.SectionDefinitionNameRequired; Refresh(); return false; }
        if (!string.Equals(name, DisplayName, StringComparison.Ordinal))
            Current.CustomName = name;
        _editingName = false; _nameError = null; _saveError = null; Refresh(); return true;
    }
    public void CancelNameEdit() { _editingName = false; _nameError = null; Refresh(); }
    public void ResetName()
    {
        if (!HasDefinition || IsBusy) return;
        Current.CustomName = null; _editingName = false; _nameError = null; _saveError = null;
        UpdateAutomaticName(Parameters.Where(p => p.ConfirmedValue is not null).ToDictionary(p => p.Key, p => p.ConfirmedValue!.Value));
        Refresh();
    }
    public Task<bool> ConfirmAsync()
    {
        if (!CanConfirm || _section is not { } section) return Task.FromResult(false);
        CloseRequested?.Invoke(new SectionDefinitionResult(section, ResultAxis, DisplayName));
        return Task.FromResult(true);
    }
    public async Task<bool> SaveLibraryAsync()
    {
        if (!CanSaveLibrary || !CommitName() || _section is not { } section) return false;
        string name = DisplayName;
        // Each attempt presents its own error, including repeated duplicate-name attempts.
        _saveError = null; Notify(nameof(LibraryError)); Notify(nameof(HasLibraryError));
        _saveError = _library?.CanSave != true ? _library?.Error ?? Strings.SectionLibraryUnavailable
            : _library.ContainsName(name) ? Strings.LibraryNameConflict : null;
        if (_saveError is not null) { Refresh(); return false; }
        _busy = true; Refresh();
        try
        {
            _saveError = await _library!.SaveAsync(new SectionLibraryEntry(name, section));
            return _saveError is null;
        }
        finally { _busy = false; Refresh(); }
    }
    public void LoadPreset(SectionLibraryEntry entry)
    {
        if (IsBusy || !HasDefinition || _library?.Entries.Contains(entry) != true) return;
        var section = SectionDefinitionCompatibility.Normalize(entry.Section);
        if (FormOf(section) != _type) return;
        var values = PresetValues(section);
        if (IsManual)
        {
            Current.ManualAxisSelections[Current.Configuration] = Current.Axis;
            Current.Configuration = ManualConfiguration(section);
        }
        EnsureInputs();
        foreach (var value in values) Current.Inputs[value.Key].Load(value.Value);
        NormalizeAxis();
        if (IsManual) Current.ManualAxisSelections[Current.Configuration] = Current.Axis;
        Current.CustomName = entry.Name;
        _editingName = false; _nameError = null;
        _highlight = null; _saveError = null;
        RefreshInputs(); Recalculate(null);
    }
    private static ManualAxisConfiguration ManualConfiguration(ISectionDefinition section) => section.Axes.Count == 2
        ? section.Axes[0].AxisDesignation == SectionAxisDesignation.Y ? ManualAxisConfiguration.YZ : ManualAxisConfiguration.UV
        : Enum.Parse<ManualAxisConfiguration>(section.Axes[0].AxisDesignation.ToString());
    private static SectionEditorType FormOf(ISectionDefinition section) => section is IParametricSectionDefinition p
        ? Enum.Parse<SectionEditorType>(p.ShapeKind.ToString()) : SectionEditorType.Manual;
    private static Dictionary<string, double> PresetValues(ISectionDefinition section)
    {
        (string Key, double Value)[] values = section switch
        {
            RectangleSectionGeometry s => [("b", s.Width.Meters), ("h", s.Height.Meters)],
            RectangularHollowSectionGeometry s => [("b", s.Width.Meters), ("h", s.Height.Meters), ("t", s.WallThickness.Meters), ("r", s.OuterRadius.Meters)],
            CircleSectionGeometry s => [("d", s.Diameter.Meters)],
            CircularHollowSectionGeometry s => [("D", s.OuterDiameter.Meters), ("t", s.WallThickness.Meters)],
            ISectionGeometry s => [("h", s.Height.Meters), ("b", s.Width.Meters), ("tw", s.WebThickness.Meters), ("tf", s.FlangeThickness.Meters), ("r", s.Radius.Meters)],
            USectionGeometry s => [("h", s.Height.Meters), ("b", s.Width.Meters), ("tw", s.WebThickness.Meters), ("tf", s.FlangeThickness.Meters), ("r", s.Radius.Meters)],
            TSectionGeometry s => [("h", s.Height.Meters), ("b", s.Width.Meters), ("tw", s.WebThickness.Meters), ("tf", s.FlangeThickness.Meters), ("r", s.Radius.Meters)],
            AngleSectionGeometry s => [("b", s.Width.Meters), ("h", s.Height.Meters), ("t", s.Thickness.Meters), ("r", s.InnerRadius.Meters)],
            _ => [("A", section.Area.SquareMeters)]
        };
        var result = values.ToDictionary(v => v.Key, v => v.Value);
        if (section is ManualSectionDefinition)
            foreach (var axis in section.Axes)
            {
                result["I" + axis.AxisDesignation] = axis.SecondMomentOfArea.MetersToTheFourth;
                result["W" + axis.AxisDesignation] = axis.PositiveSectionModulus.CubicMeters;
            }
        return result;
    }
    private void RefreshLibraryEntries()
    {
        var entries = (_library?.Entries ?? []).Where(e => FormOf(SectionDefinitionCompatibility.Normalize(e.Section)) == _type)
            .Take(5).Select(e =>
            {
                var section = SectionDefinitionCompatibility.Normalize(e.Section);
                var values = PresetValues(section);
                string summary = IsManual ? string.Join(" / ", section.Axes.Select(a => new AxisChoice(a.AxisDesignation).Label))
                    + " · " + Format(section.Area.SquareMeters, QuantityKind.Area)
                    : string.Join(" × ", values.Values.Select(v => InputQuantityFormatter.Display(v, Presentation.Profile[QuantityKind.SectionDimension])))
                        + " " + Presentation.Profile[QuantityKind.SectionDimension].Symbol;
                return new SectionDefinitionPreset(e, summary);
            }).ToArray();
        if (!_libraryEntries.SequenceEqual(entries)) { _libraryEntries = entries; Notify(nameof(LibraryEntries)); }
    }
    private string Format(double value, QuantityKind kind) => QuantityFormatter.Format(value, kind, Presentation.Profile, Presentation.Mode);
    private void RefreshInputs()
    { Notify(nameof(Parameters)); Notify(nameof(GeometryInputs)); Notify(nameof(ManualInputs)); Notify(nameof(ManualAreaInputs)); }
    private void Refresh()
    {
        RefreshAxisValues(); RefreshLibraryEntries();
        foreach (var property in new[] { nameof(Type), nameof(IsChoosingForm), nameof(HasDefinition), nameof(IsManual), nameof(HasPreview),
            nameof(ValuesTitle), nameof(SelectedFormName), nameof(SelectedFormType), nameof(IsBusy), nameof(IsEditingName), nameof(DisplayName), nameof(PendingName), nameof(NameError),
            nameof(HasNameError), nameof(LibraryError), nameof(HasLibraryError), nameof(Configuration), nameof(AvailableAxes), nameof(SelectedAxis),
            nameof(HasRadius), nameof(Preview), nameof(Section), nameof(HasValidSection),
            nameof(Area), nameof(CanConfirm), nameof(LayoutState), nameof(ShowPreview), nameof(ShowValues),
            nameof(ShowConfirm), nameof(ShowLibraryAction), nameof(CanSaveLibrary), nameof(IsLibraryEmpty),
            nameof(HasCustomName), nameof(ShowResetName), nameof(HeaderTitle), nameof(AxesEquivalent), nameof(RequiresAxisSelection) }) Notify(property);
        ChangeFormCommand.Refresh(); EditNameCommand.Refresh(); ResetNameCommand.Refresh();
        SaveLibraryCommand.Refresh(); ConfirmCommand.Refresh(); CancelCommand.Refresh();
    }
}
