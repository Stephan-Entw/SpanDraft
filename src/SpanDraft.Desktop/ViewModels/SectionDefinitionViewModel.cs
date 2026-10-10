using System.Globalization;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Sections.Parametric;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Libraries;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.Sections;

namespace SpanDraft.Desktop.ViewModels;

public sealed record SectionDefinitionForm(SectionEditorType Value, string Label);

public sealed record SectionDefinitionAxisValue(SectionAxisDesignation Axis, string Label, string InertiaLabel,
    string PositiveLabel, string NegativeLabel, string Inertia, string PositiveModulus, string NegativeModulus, bool IsSelected);

public sealed class SectionDefinitionViewModel : ObservableObject
{
    private sealed class FormDraft
    {
        public Dictionary<string, SectionDefinitionInput> Inputs { get; } = [];
        public Dictionary<ManualAxisConfiguration, SectionAxisDesignation> ManualAxisSelections { get; } = [];
        public ManualAxisConfiguration Configuration { get; set; } = ManualAxisConfiguration.Y;
        public SectionAxisDesignation Axis { get; set; } = SectionAxisDesignation.Y;
        public SectionPreviewGeometry? Preview { get; set; }
    }
    private readonly Dictionary<SectionEditorType, FormDraft> _drafts = [];
    private readonly ISectionDefinitionLibrary? _library;
    private readonly ResultPresentationOptions _presentation;
    private SectionEditorType? _type;
    private bool _choosing = true, _save, _busy, _editingName, _customName;
    private string _name, _pendingName = "";
    private string? _nameError, _saveError, _highlight;
    private ISectionDefinition? _section;

    public SectionDefinitionViewModel(ResultPresentationOptions? presentation = null, ISectionDefinitionLibrary? library = null,
        bool showLibraryOption = true)
    {
        _presentation = presentation ?? ResultPresentationOptions.Default; _library = library;
        ShowLibraryOption = showLibraryOption; _name = Strings.SectionDefinitionTitle;
        Forms = new[]
        {
            new SectionDefinitionForm(SectionEditorType.Rectangle, Strings.ShapeRectangle),
            new(SectionEditorType.RectangularHollow, Strings.ShapeRectangularHollow), new(SectionEditorType.Circle, Strings.ShapeCircle),
            new(SectionEditorType.CircularHollow, Strings.ShapeCircularHollow), new(SectionEditorType.ISection, Strings.ShapeI),
            new(SectionEditorType.USection, Strings.ShapeU), new(SectionEditorType.TSection, Strings.ShapeT), new(SectionEditorType.Angle, Strings.ShapeAngle)
        };
        Configurations = new[]
        {
            new SetupChoice<ManualAxisConfiguration>(ManualAxisConfiguration.Y, "y-y"), new(ManualAxisConfiguration.Z, "z-z"),
            new(ManualAxisConfiguration.U, "u-u"), new(ManualAxisConfiguration.V, "v-v"),
            new(ManualAxisConfiguration.YZ, "y-y / z-z"), new(ManualAxisConfiguration.UV, "u-u / v-v")
        };
        ChangeFormCommand = new(() => { _choosing = true; Refresh(); }, () => !IsBusy);
        EditNameCommand = new(BeginNameEdit, () => !IsBusy);
        ConfirmCommand = new(ConfirmAsync, () => CanConfirm);
        CancelCommand = new(() => CloseRequested?.Invoke(null), () => !IsBusy);
    }
    public event Action<SectionDefinitionResult?>? CloseRequested;
    public ResultPresentationOptions Presentation => _presentation;
    public IReadOnlyList<SectionDefinitionForm> Forms { get; }
    public IReadOnlyList<SetupChoice<ManualAxisConfiguration>> Configurations { get; }
    public SectionEditorType? Type => _type;
    public bool IsChoosingForm => _choosing;
    public bool HasDefinition => _type is not null && !_choosing;
    public bool IsManual => _type == SectionEditorType.Manual;
    public bool HasRadius => !IsManual && Parameters.Any(p => p.Key == "r");
    public bool HasPreview => Preview is not null && !IsManual;
    public string ValuesTitle => IsManual ? Strings.SectionDefinitionManualProperties : Strings.SectionDefinitionProperties;
    public string SelectedFormName => IsManual ? Strings.SectionDefinitionManualChoice : Forms.FirstOrDefault(f => f.Value == _type)?.Label ?? "";
    public bool IsBusy => _busy;
    public bool IsEditingName => _editingName;
    public string DisplayName => _name;
    public string PendingName { get => _pendingName; set { if (Set(ref _pendingName, value)) { _nameError = null; Notify(nameof(NameError)); Notify(nameof(HasNameError)); } } }
    public string? NameError => _nameError;
    public bool HasNameError => NameError is not null;
    public bool ShowLibraryOption { get; }
    public bool SaveToLibrary { get => _save; set { if (ShowLibraryOption && Set(ref _save, value)) { _saveError = null; Refresh(); } } }
    public string? LibraryError => _saveError ?? (SaveToLibrary ? _library?.Error
        ?? (_library?.CanSave == true ? null : Strings.SectionLibraryUnavailable) : null)
        ?? (SaveToLibrary && _library?.ContainsName(DisplayName) == true ? Strings.LibraryNameConflict : null);
    public bool HasLibraryError => LibraryError is not null;
    private FormDraft Current => _drafts[_type!.Value];
    public SetupChoice<ManualAxisConfiguration> Configuration
    {
        get => Configurations.Single(c => c.Value == (_type is null ? ManualAxisConfiguration.Y : Current.Configuration));
        set
        {
            if (!IsManual || value is null || !Configurations.Contains(value) || Current.Configuration == value.Value || IsBusy) return;
            Current.ManualAxisSelections[Current.Configuration] = Current.Axis;
            Current.Configuration = value.Value;
            Current.Axis = Current.ManualAxisSelections.GetValueOrDefault(value.Value, AxesForCurrent()[0]);
            EnsureInputs(); NormalizeAxis(); RefreshInputs(); Recalculate(null);
        }
    }
    public IReadOnlyList<AxisChoice> AvailableAxes => _type is null ? [] : AxesForCurrent().Select(a => new AxisChoice(a)).ToArray();
    public SectionAxisDesignation SelectedAxis
    {
        get => _type is null ? SectionAxisDesignation.Y : Current.Axis;
        set { if (_type is null || IsBusy || !AxesForCurrent().Contains(value) || Current.Axis == value) return; Current.Axis = value; if (IsManual) Current.ManualAxisSelections[Current.Configuration] = value; Refresh(); }
    }
    public string? HighlightedParameter { get => _highlight; set => Set(ref _highlight, value); }
    public IReadOnlyList<SectionDefinitionInput> Parameters => _type is null ? [] : Keys().Select(k => Current.Inputs[k]).ToArray();
    public IReadOnlyList<SectionDefinitionInput> GeometryInputs => IsManual ? [] : Parameters;
    public IReadOnlyList<SectionDefinitionInput> ManualInputs => IsManual ? Parameters : [];
    public SectionPreviewGeometry? Preview => _type is null ? null : Current.Preview;
    public ISectionDefinition? Section => _section;
    public bool HasValidSection => _section is not null;
    public string Area => _section is null ? "—" : Format(_section.Area.SquareMeters, QuantityKind.Area);
    public IReadOnlyList<SectionDefinitionAxisValue> AxisValues => AvailableAxes.Select(a =>
    {
        var p = _section?.GetAxis(a.Value);
        string letter = a.Value.ToString().ToLowerInvariant();
        return new SectionDefinitionAxisValue(a.Value, a.Label, "I"+letter, "W"+letter+"+", "W"+letter+"−",
            p is null ? "—" : Format(p.SecondMomentOfArea.MetersToTheFourth, QuantityKind.SecondMomentOfArea),
            p is null ? "—" : Format(p.PositiveSectionModulus.CubicMeters, QuantityKind.SectionModulus),
            p is null ? "—" : Format(p.NegativeSectionModulus.CubicMeters, QuantityKind.SectionModulus), a.Value == SelectedAxis);
    }).ToArray();
    public bool CanConfirm => HasDefinition && !IsBusy && !_editingName && _section is not null && !string.IsNullOrWhiteSpace(DisplayName)
        && Parameters.All(p => !p.IsPending && !p.HasError) && (!SaveToLibrary || _library?.CanSave == true && !_library.ContainsName(DisplayName));
    public ActionCommand ChangeFormCommand { get; }
    public ActionCommand EditNameCommand { get; }
    public AsyncActionCommand ConfirmCommand { get; }
    public ActionCommand CancelCommand { get; }

    public void SelectForm(SectionEditorType type)
    {
        if (IsBusy || !Enum.IsDefined(type)) return;
        _type = type; _choosing = false; _saveError = null; _highlight = null;
        if (!_drafts.ContainsKey(type)) _drafts[type] = new();
        EnsureInputs(); NormalizeAxis(); RefreshInputs(); Recalculate(null);
    }
    private void NormalizeAxis() { if (!AxesForCurrent().Contains(Current.Axis)) Current.Axis = AxesForCurrent()[0]; }
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
            Current.Inputs[key] = new(key, Presentation.Profile[quantity], InputChanged, key == "r");
        }
    }
    private void InputChanged(SectionDefinitionInput input, bool committed)
    {
        if (committed) { _saveError = null; Recalculate(input); }
        else { Notify(nameof(CanConfirm)); ConfirmCommand.Refresh(); }
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
        if (_customName) return;
        if (IsManual) { _name = Strings.SectionDefinitionCustomName; return; }
        string[] main = SectionPreviewGeometryResolver.ParameterKeys(_type!.Value).Where(k => k != "r").ToArray();
        _name = SelectedFormName;
        if (main.All(values.ContainsKey))
        {
            var unit = Presentation.Profile[QuantityKind.SectionDimension];
            _name += " " + (_type is SectionEditorType.Circle or SectionEditorType.CircularHollow ? "Ø" : "")
                + string.Join(" × ", main.Select(k => InputQuantityFormatter.Display(values[k], unit))) + " " + unit.Symbol;
            if (values.TryGetValue("r", out double radius) && radius > 0)
                _name += string.Format(CultureInfo.CurrentUICulture, Strings.SectionDefinitionRadiusSuffix, InputQuantityFormatter.Display(radius, unit), unit.Symbol);
        }
    }
    public void BeginNameEdit() { if (IsBusy) return; _pendingName = DisplayName; _editingName = true; _nameError = null; Refresh(); }
    public bool CommitName()
    {
        if (!IsEditingName) return true;
        if (string.IsNullOrWhiteSpace(PendingName)) { _nameError = Strings.SectionDefinitionNameRequired; Refresh(); return false; }
        var name = PendingName.Trim();
        if (name != DisplayName) { _name = name; _customName = true; }
        _editingName = false; _nameError = null; _saveError = null; Refresh(); return true;
    }
    public void CancelNameEdit() { _editingName = false; _nameError = null; Refresh(); }
    public async Task<bool> ConfirmAsync()
    {
        if (!CanConfirm || _section is not { } section) return false;
        var result = new SectionDefinitionResult(section, SelectedAxis, DisplayName);
        _busy = true; _saveError = null; Refresh();
        try
        {
            if (SaveToLibrary)
            {
                _saveError = await _library!.SaveAsync(new SectionLibraryEntry(result.DisplayName, section));
                if (_saveError is not null) return false;
            }
        }
        finally { _busy = false; Refresh(); }
        CloseRequested?.Invoke(result); return true;
    }
    private string Format(double value, QuantityKind kind) => QuantityFormatter.Format(value, kind, Presentation.Profile, Presentation.Mode);
    private void RefreshInputs()
    { Notify(nameof(Parameters)); Notify(nameof(GeometryInputs)); Notify(nameof(ManualInputs)); }
    private void Refresh()
    {
        foreach (var property in new[] { nameof(Type), nameof(IsChoosingForm), nameof(HasDefinition), nameof(IsManual), nameof(HasPreview),
            nameof(ValuesTitle), nameof(SelectedFormName), nameof(IsBusy), nameof(IsEditingName), nameof(DisplayName), nameof(PendingName), nameof(NameError),
            nameof(HasNameError), nameof(LibraryError), nameof(HasLibraryError), nameof(Configuration), nameof(AvailableAxes), nameof(SelectedAxis),
            nameof(HasRadius), nameof(Preview), nameof(Section), nameof(HasValidSection),
            nameof(Area), nameof(AxisValues), nameof(CanConfirm) }) Notify(property);
        ChangeFormCommand.Refresh(); EditNameCommand.Refresh(); ConfirmCommand.Refresh(); CancelCommand.Refresh();
    }
}
