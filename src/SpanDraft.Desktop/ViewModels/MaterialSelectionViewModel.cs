using System.Globalization;
using SpanDraft.Core.Materials;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.Libraries;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Reporting.Notation;

namespace SpanDraft.Desktop.ViewModels;

public enum MaterialSelectionKind { BuiltIn, User, Project }
public enum MaterialFilterKind { All, Category, Project, BuiltIn, User }
public sealed record MaterialFilter(MaterialFilterKind Kind, string Label, MaterialCategory? Category = null);

/// <summary>Origin is transient browser state; all values belong to the material snapshot.</summary>
public sealed class MaterialSelectionItem(Material material, MaterialSelectionKind kind, Func<ResultPresentationOptions>? readPresentation = null) : ObservableObject
{
    private ResultPresentationOptions Presentation => readPresentation?.Invoke() ?? ResultPresentationOptions.Default;
    public string YoungsModulusUnit => Presentation.Profile[QuantityKind.YoungsModulus].Symbol;
    public string StrengthUnit => Presentation.Profile[QuantityKind.MaterialStrength].Symbol;
    public string DensityUnit => Presentation.Profile[QuantityKind.Density].Symbol;
    public Material Material { get; } = material;
    public MaterialSelectionKind Kind { get; } = kind;
    public bool IsProject => Kind == MaterialSelectionKind.Project;
    public bool IsUser => Kind == MaterialSelectionKind.User;
    public bool IsBuiltIn => Kind == MaterialSelectionKind.BuiltIn;
    public string? OriginLabel => IsProject ? Strings.MaterialCurrentEntry : IsBuiltIn ? Strings.BuiltInMaterialReadOnly : null;
    public string? ProjectSuffix => IsProject ? Strings.MaterialProjectSuffix : null;
    public string Name => Material.Name;
    public string DisplayName => MaterialDisplay.Format(Material);
    public string? MaterialNumber => Material.MaterialNumber;
    public bool HasMaterialNumber => MaterialNumber is not null;
    public bool HasCategory => true;
    public string CategoryLabel => MaterialSelectionViewModel.Categories().Single(c => c.Value == Material.Category).Label;
    public string OtherStandards => string.Join(" · ", Material.OtherStandards);
    public bool HasOtherStandards => OtherStandards.Length > 0;
    public string YoungsModulus => QuantityFormatter.FormatNumber(Material.YoungsModulus.Pascals, QuantityKind.YoungsModulus, Presentation);
    public string StrengthValue => QuantityFormatter.FormatNumber(Material.YieldStrength.Pascals, QuantityKind.MaterialStrength, Presentation);
    public TechnicalText StrengthLabelText => MaterialStrengthDisplay.LabelText(Material.StrengthType);
    public string StrengthLabel => StrengthLabelText.PlainText;
    public TechnicalText StrengthSummaryText => MaterialStrengthDisplay.SummaryText(Material, Presentation);
    public string StrengthSummary => StrengthSummaryText.PlainText;
    public TechnicalText YoungsModulusLabel => Strings.MaterialYoungsModulus + " " + TechnicalSymbols.YoungsModulus;
    public TechnicalText DensityLabel => Strings.MaterialDensity + " " + TechnicalSymbols.Density;
    public TechnicalText PoissonRatioLabel => Strings.MaterialPoissonRatio + " " + TechnicalSymbols.PoissonRatio;
    public string AccessibleName => DisplayName + " · " + (OriginLabel is { } origin ? origin + " · " : "") + StrengthSummary + " " + StrengthUnit;
    public bool HasDensity => Material.Density is not null;
    public bool HasPoissonRatio => Material.PoissonRatio is not null;
    public string? Density => Material.Density is { } rho ? QuantityFormatter.FormatNumber(rho.KilogramsPerCubicMeter, QuantityKind.Density, Presentation) : null;
    internal void Refresh()
    {
        foreach (string name in new[] { nameof(YoungsModulus), nameof(StrengthValue), nameof(StrengthSummaryText), nameof(StrengthSummary), nameof(Density), nameof(PoissonRatio), nameof(AccessibleName), nameof(YoungsModulusUnit), nameof(StrengthUnit), nameof(DensityUnit) }) Notify(name);
    }
    public string? PoissonRatio => Material.PoissonRatio is { } nu ? QuantityFormatter.FormatNumber(nu.Value, QuantityKind.PoissonRatio, Presentation) : null;
}

public sealed class MaterialSelectionViewModel : SetupStepViewModel
{
    public string StrengthHeader => string.Format(CultureInfo.CurrentUICulture, Strings.MaterialStrengthColumn, Setup.ResultPresentation.Profile[QuantityKind.MaterialStrength].Symbol);
    public TechnicalText YoungsModulusHeader => TechnicalSymbols.YoungsModulus + " [" + Setup.ResultPresentation.Profile[QuantityKind.YoungsModulus].Symbol + "]";
    private readonly IReadOnlyList<MaterialSelectionItem> _builtIns;
    private UserMaterialLibrary? _shownLibrary;
    private IReadOnlyList<MaterialSelectionItem> _users = [], _entries = [], _filtered = [];
    private MaterialSelectionItem? _currentEntry, _selected;
    private string _search = "";
    private MaterialFilter _filter;
    private string? _error;
    private MaterialEditorViewModel? _editor;
    private bool _actionBusy;

    public MaterialSelectionViewModel(ProjectSetupViewModel setup) : base(setup)
    {
        _currentEntry = setup.SelectedMaterial is { } material ? new(material, MaterialSelectionKind.Project, () => Setup.ResultPresentation) : null;
        _builtIns = (setup.Libraries?.BuiltInMaterials?.All ?? []).Select(m => new MaterialSelectionItem(m, MaterialSelectionKind.BuiltIn, () => Setup.ResultPresentation)).ToArray();
        Filters = new[] { new MaterialFilter(MaterialFilterKind.All, Strings.AllMaterials) }
            .Concat(Categories().Select(c => new MaterialFilter(MaterialFilterKind.Category, c.Label, c.Value)))
            .Append(new(MaterialFilterKind.Project, Strings.MaterialCurrentEntry))
            .Append(new(MaterialFilterKind.BuiltIn, Strings.BuiltInMaterials))
            .Append(new(MaterialFilterKind.User, Strings.MyMaterials)).ToArray();
        _filter = Filters[0];
        BackCommand = new(setup.ShowOverview, () => CanBrowse);
        NewCommand = new(() => BeginEdit(null, SetupEditorMode.UserLibraryCreate), () => CanBrowse && CanSaveLibrary);
        UseSelectedCommand = new(UseSelected, () => CanUseSelected);
        EditSelectedCommand = new(EditSelected, () => CanEditSelected);
        SaveSelectedCommand = new(SaveSelectedAsync, () => CanSaveSelected);
        DeleteSelectedCommand = new(DeleteSelectedAsync, () => CanDeleteSelected);
        ShowUserCommand = new(() => SelectedFilter = UserFilter, () => CanBrowse);
        ShowBuiltInCommand = new(() => SelectedFilter = BuiltInFilter, () => CanBrowse);
        ShowProjectCommand = new(() => SelectedFilter = ProjectFilter, () => CanBrowse);
        RebuildEntries();
        SelectedEntry = _currentEntry;
        Refresh();
    }
    public IReadOnlyList<MaterialFilter> Filters { get; }
    public IReadOnlyList<MaterialFilter> CategoryFilters => Filters.Where(f => f.Kind is MaterialFilterKind.All or MaterialFilterKind.Category).ToArray();
    public MaterialFilter UserFilter => Filters.Single(f => f.Kind == MaterialFilterKind.User);
    public MaterialFilter BuiltInFilter => Filters.Single(f => f.Kind == MaterialFilterKind.BuiltIn);
    public MaterialFilter ProjectFilter => Filters.Single(f => f.Kind == MaterialFilterKind.Project);
    public bool IsUserFilter => SelectedFilter.Kind == MaterialFilterKind.User;
    public bool IsBuiltInFilter => SelectedFilter.Kind == MaterialFilterKind.BuiltIn;
    public bool IsProjectFilter => SelectedFilter.Kind == MaterialFilterKind.Project;
    public MaterialEditorViewModel? Editor => _editor;
    public bool IsEditing => Editor is not null;
    public bool IsActionBusy => _actionBusy;
    public bool CanBrowse => !IsBusy && !IsEditing && !_actionBusy;
    private bool CanSaveLibrary => Setup.Libraries?.CanSaveMaterials == true;
    public string Title => Strings.MaterialSelection;
    public IReadOnlyList<MaterialSelectionItem> AllEntries => _entries;
    public IReadOnlyList<MaterialSelectionItem> BuiltInEntries => _builtIns;
    public IReadOnlyList<MaterialSelectionItem> UserEntries => _users;
    public MaterialSelectionItem? CurrentEntry => _currentEntry;
    public IReadOnlyList<MaterialSelectionItem> FilteredEntries => _filtered;
    public bool IsEmpty => _filtered.Count == 0;
    public string SearchText { get => _search; set { if (CanBrowse && Set(ref _search, value)) RebuildVisible(); } }
    public MaterialFilter SelectedFilter
    {
        get => _filter;
        set { if (CanBrowse && value is not null && Filters.Contains(value) && Set(ref _filter, value)) RebuildVisible(); }
    }
    public MaterialSelectionItem? SelectedEntry
    {
        get => _selected;
        set
        {
            if (value is not null && !_filtered.Contains(value) || !Set(ref _selected, value)) return;
            NotifySelection();
        }
    }
    public bool HasSelection => SelectedEntry is not null;
    public bool CanUseSelected => CanBrowse && HasSelection;
    public bool CanEditSelected => CanBrowse && SelectedEntry is { } selected && (selected.IsProject || CanSaveLibrary);
    public bool CanDeleteSelected => CanBrowse && SelectedEntry?.IsUser == true && CanSaveLibrary;
    public bool ShowDelete => SelectedEntry?.IsUser == true;
    public bool ShowSave => SelectedEntry?.IsProject == true && !_builtIns.Concat(_users).Any(e => MaterialSnapshotComparison.Equals(e.Material, SelectedEntry.Material));
    public bool CanSaveSelected => CanBrowse && ShowSave && CanSaveLibrary;
    public string EditLabel => SelectedEntry?.IsBuiltIn == true ? Strings.CopyMaterial : Strings.Customize;
    public string? BuiltInError => Setup.Libraries?.BuiltInError;
    public bool HasBuiltInError => BuiltInError is not null;
    public string? LibraryError => Setup.Libraries?.MaterialLibraryError;
    public bool HasLibraryError => LibraryError is not null;
    public string? Error => _error;
    public bool HasError => Error is not null;
    public ActionCommand BackCommand { get; }
    public ActionCommand NewCommand { get; }
    public ActionCommand UseSelectedCommand { get; }
    public ActionCommand EditSelectedCommand { get; }
    public AsyncActionCommand SaveSelectedCommand { get; }
    public AsyncActionCommand DeleteSelectedCommand { get; }
    public ActionCommand ShowUserCommand { get; }
    public ActionCommand ShowBuiltInCommand { get; }
    public ActionCommand ShowProjectCommand { get; }

    public void UseSelected()
    {
        if (!CanUseSelected || SelectedEntry is not { } selected) return;
        if (selected.Material.StrengthType is null)
        {
            BeginEdit(selected.Material, selected.IsUser ? SetupEditorMode.UserLibraryEdit : SetupEditorMode.ProjectDraft);
            return;
        }
        Setup.SelectedMaterial = selected.Material;
        Setup.ShowOverview();
    }
    private void EditSelected()
    {
        if (!CanEditSelected || SelectedEntry is not { } selected) return;
        BeginEdit(selected.Material, selected.IsProject ? SetupEditorMode.ProjectDraft
            : selected.IsUser ? SetupEditorMode.UserLibraryEdit : SetupEditorMode.UserLibraryCreate);
    }
    private void BeginEdit(Material? material, SetupEditorMode mode, bool keepProjectSelection = false)
    {
        if (!CanBrowse) return;
        var originalName = mode == SetupEditorMode.UserLibraryEdit ? material?.Name : null;
        var editor = new MaterialEditorViewModel(Setup, material, mode,
            result => mode == SetupEditorMode.ProjectDraft ? Task.FromResult<string?>(null)
                : Setup.Libraries!.SaveMaterialAsync(result, originalName),
            showNameConflict: mode == SetupEditorMode.UserLibraryCreate && material is not null);
        _editor = editor;
        editor.CloseRequested += Finish;
        NotifyMode();
        _ = ShowEditorAsync(editor);
        void Finish(bool accepted)
        {
            editor.CloseRequested -= Finish;
            _editor = null;
            if (accepted && editor.Draft is { } result)
            {
                if (mode == SetupEditorMode.ProjectDraft)
                {
                    _currentEntry = new(result, MaterialSelectionKind.Project, () => Setup.ResultPresentation);
                    SelectVisible(_currentEntry);
                    Notify(nameof(CurrentEntry));
                }
                else
                {
                    Refresh();
                    if (!keepProjectSelection)
                    {
                        if (mode == SetupEditorMode.UserLibraryCreate) SelectedFilter = UserFilter;
                        SelectReplacement(result.Name);
                    }
                }
            }
            NotifyMode();
        }
    }
    private async Task ShowEditorAsync(MaterialEditorViewModel editor)
    {
        if (Setup.Libraries is { } main) await main.ShowMaterialEditorAsync(editor);
    }
    internal void EndEdit() => Editor?.CancelCommand.Execute(null);
    private void NotifyMode()
    {
        foreach (var name in new[] { nameof(Editor), nameof(IsEditing), nameof(CanBrowse), nameof(IsActionBusy) }) Notify(name);
        Refresh();
        Setup.Libraries?.RefreshWorkspaceState();
    }
    public async Task<bool> SaveSelectedAsync()
    {
        if (!CanSaveSelected || SelectedEntry is not { } selected) return false;
        if (selected.Material.StrengthType is null)
        {
            // Resolve the local snapshot first; saving still requires a separate explicit action.
            BeginEdit(selected.Material, SetupEditorMode.ProjectDraft);
            return false;
        }
        var material = selected.Material;
        var librarySnapshot = new Material(material.Name.Trim(), material.YoungsModulus, material.YieldStrength, material.StrengthType.Value,
            material.Density, material.PoissonRatio, material.Category, material.MaterialNumber, material.OtherStandards);
        bool rename = false;
        _error = null;
        _actionBusy = true; NotifyMode();
        try
        {
            var main = Setup.Libraries!;
            var existing = main.UserMaterials.Find(selected.Name.Trim());
            if (existing is not null)
            {
                var choice = await main.ResolveMaterialNameConflictAsync(existing);
                if (choice == MaterialNameConflictDecision.Cancel) return false;
                if (choice == MaterialNameConflictDecision.Rename) rename = true;
                else
                {
                    if (!await main.ConfirmMaterialReplaceAsync(existing)) return false;
                    _error = await main.SaveMaterialAsync(librarySnapshot, existing.Name);
                }
            }
            else _error = await main.SaveMaterialAsync(librarySnapshot);
            return !rename && _error is null;
        }
        finally
        {
            _actionBusy = false; NotifyMode();
            if (rename) BeginEdit(selected.Material, SetupEditorMode.UserLibraryCreate, keepProjectSelection: true);
        }
    }
    public async Task<bool> DeleteSelectedAsync()
    {
        if (!CanDeleteSelected || SelectedEntry is not { } selected) return false;
        int index = _filtered.ToList().IndexOf(selected);
        _error = await Setup.Libraries!.DeleteMaterialAsync(selected.Name);
        Refresh();
        if (_error is null && !_filtered.Contains(selected))
            SelectedEntry = _filtered.FirstOrDefault(e => e.IsProject) ?? _filtered.ElementAtOrDefault(Math.Min(index, _filtered.Count - 1));
        Notify(nameof(Error)); Notify(nameof(HasError));
        return _error is null;
    }
    internal void SelectReplacement(string name)
    {
        Refresh();
        var replacement = _users.SingleOrDefault(e => e.Name == name);
        if (replacement is null) return;
        SelectVisible(replacement);
    }
    private void SelectVisible(MaterialSelectionItem replacement)
    {
        if (!MatchesFilter(replacement)) SelectedFilter = Filters[0];
        if (!MatchesSearch(replacement)) SearchText = "";
        RebuildVisible(replacement);
    }
    private void RebuildEntries()
    {
        var previous = SelectedEntry;
        _shownLibrary = Setup.Libraries?.UserMaterials;
        _users = (_shownLibrary?.All ?? []).Select(m => new MaterialSelectionItem(m, MaterialSelectionKind.User, () => Setup.ResultPresentation)).ToArray();
        var preferred = previous?.IsProject == true ? _currentEntry : _builtIns.Concat(_users).FirstOrDefault(e => e.Kind == previous?.Kind && e.Material == previous.Material);
        RebuildVisible(preferred);
        Notify(nameof(UserEntries));
    }
    private void RebuildVisible(MaterialSelectionItem? preferred = null)
    {
        var entries = (_currentEntry is null ? Enumerable.Empty<MaterialSelectionItem>() : [_currentEntry]).Concat(_builtIns).Concat(_users).ToArray();
        if (!_entries.SequenceEqual(entries)) { _entries = entries; Notify(nameof(AllEntries)); }
        var filtered = _entries.Where(e => MatchesFilter(e) && MatchesSearch(e)).ToArray();
        if (!_filtered.SequenceEqual(filtered)) { _filtered = filtered; Notify(nameof(FilteredEntries)); }
        var selection = preferred ?? SelectedEntry;
        SelectedEntry = selection is not null && _filtered.Contains(selection) ? selection : null;
        Notify(nameof(IsEmpty)); Notify(nameof(IsUserFilter)); Notify(nameof(IsBuiltInFilter)); Notify(nameof(IsProjectFilter));
    }
    private bool MatchesFilter(MaterialSelectionItem entry) => _filter.Kind switch
    {
        MaterialFilterKind.All => true,
        MaterialFilterKind.Category => entry.Material.Category == _filter.Category,
        MaterialFilterKind.Project => entry.IsProject,
        MaterialFilterKind.BuiltIn => entry.IsBuiltIn,
        MaterialFilterKind.User => entry.IsUser,
        _ => false
    };
    private bool MatchesSearch(MaterialSelectionItem entry) => Contains(entry.Name) || Contains(entry.MaterialNumber) || entry.Material.OtherStandards.Any(Contains);
    private bool Contains(string? text) => text?.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase) == true;
    private void NotifySelection()
    {
        foreach (var name in new[] { nameof(HasSelection), nameof(CanUseSelected), nameof(CanEditSelected), nameof(CanDeleteSelected), nameof(ShowDelete), nameof(ShowSave), nameof(CanSaveSelected), nameof(EditLabel) }) Notify(name);
        UseSelectedCommand.Refresh(); EditSelectedCommand.Refresh(); SaveSelectedCommand.Refresh(); DeleteSelectedCommand.Refresh();
    }
    public override void Refresh()
    {
        base.Refresh();
        Editor?.Refresh();
        Notify(nameof(YoungsModulusHeader)); Notify(nameof(StrengthHeader));
        foreach (var entry in _entries) entry.Refresh();
        if (!ReferenceEquals(_shownLibrary, Setup.Libraries?.UserMaterials)) RebuildEntries();
        foreach (var name in new[] { nameof(BuiltInError), nameof(HasBuiltInError), nameof(LibraryError), nameof(HasLibraryError), nameof(CanBrowse), nameof(Error), nameof(HasError) }) Notify(name);
        BackCommand.Refresh(); NewCommand.Refresh(); ShowUserCommand.Refresh(); ShowBuiltInCommand.Refresh(); ShowProjectCommand.Refresh(); NotifySelection();
    }
    internal static IReadOnlyList<SetupChoice<MaterialCategory>> Categories() =>
    [
        new(MaterialCategory.StructuralSteel, Strings.CategoryStructuralSteel),
        new(MaterialCategory.StainlessSteel, Strings.CategoryStainlessSteel),
        new(MaterialCategory.AlloySteel, Strings.CategoryAlloySteel),
        new(MaterialCategory.Aluminium, Strings.CategoryAluminium),
        new(MaterialCategory.Other, Strings.CategoryOther)
    ];
}
