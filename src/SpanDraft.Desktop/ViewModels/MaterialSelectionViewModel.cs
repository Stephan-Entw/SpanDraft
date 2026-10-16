using SpanDraft.Core.Materials;
using SpanDraft.Desktop.Libraries;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.State;

namespace SpanDraft.Desktop.ViewModels;

public enum MaterialSelectionKind { BuiltIn, User, Project }
public enum MaterialFilterKind { All, Category, User }
public sealed record MaterialFilter(MaterialFilterKind Kind, string Label, MaterialCategory? Category = null);

/// <summary>Source information exists only while browsing; a project row has no library metadata.</summary>
public sealed class MaterialSelectionItem(Material material, MaterialSelectionKind kind, MaterialLibraryEntry? entry = null)
{
    public Material Material { get; } = material;
    public MaterialSelectionKind Kind { get; } = kind;
    public MaterialLibraryEntry? Entry { get; } = entry;
    public bool IsProject => Kind == MaterialSelectionKind.Project;
    public bool IsUser => Kind == MaterialSelectionKind.User;
    public bool IsBuiltIn => Kind == MaterialSelectionKind.BuiltIn;
    public string Name => Material.Name;
    public string? MaterialNumber => Entry?.MaterialNumber;
    public bool HasMaterialNumber => MaterialNumber is not null;
    public bool HasCategory => Entry is not null;
    public string? CategoryLabel => Entry is null ? null : MaterialSelectionViewModel.Categories().Single(c => c.Value == Entry.Category).Label;
    public string OtherStandards => string.Join(" · ", (Entry?.OtherStandards ?? [])
        .Where(s => !StringComparer.OrdinalIgnoreCase.Equals(s, Name) && !StringComparer.OrdinalIgnoreCase.Equals(s, MaterialNumber))
        .Distinct(StringComparer.OrdinalIgnoreCase));
    public bool HasOtherStandards => OtherStandards.Length > 0;
    public string YoungsModulus => UiNumbers.Indicator(Material.YoungsModulus.Pascals / 1e9);
    public string YieldStrength => UiNumbers.Indicator(Material.YieldStrength.Megapascals);
    public bool HasDensity => Material.Density is not null;
    public bool HasPoissonRatio => Material.PoissonRatio is not null;
    public string? Density => Material.Density is { } rho ? UiNumbers.Indicator(rho.KilogramsPerCubicMeter) : null;
    public string? PoissonRatio => Material.PoissonRatio is { } nu ? UiNumbers.Indicator(nu.Value) : null;
}

internal static class MaterialSnapshotComparison
{
    internal static bool Equals(Material a, Material b) => a.Name == b.Name && a.YoungsModulus == b.YoungsModulus
        && a.YieldStrength == b.YieldStrength && a.Density == b.Density && a.PoissonRatio == b.PoissonRatio;
    internal static bool Equals(MaterialLibraryEntry a, MaterialLibraryEntry b) => Equals(a.Material, b.Material)
        && a.Category == b.Category && a.MaterialNumber == b.MaterialNumber && a.OtherStandards.SequenceEqual(b.OtherStandards);
}

public sealed class MaterialSelectionViewModel : SetupStepViewModel
{
    private readonly Material? _currentMaterial;
    private readonly IReadOnlyList<MaterialSelectionItem> _builtIns;
    private UserMaterialLibrary? _shownLibrary;
    private IReadOnlyList<MaterialSelectionItem> _users = [], _entries = [], _visible = [], _filtered = [];
    private MaterialSelectionItem? _currentEntry, _selected;
    private string _search = "";
    private MaterialFilter _filter;
    private string? _error;
    private MaterialEditorViewModel? _editor;

    public MaterialSelectionViewModel(ProjectSetupViewModel setup) : base(setup)
    {
        _currentMaterial = setup.SelectedMaterial;
        _builtIns = (setup.Libraries?.BuiltInMaterials?.All ?? []).Select(e => new MaterialSelectionItem(e.Material, MaterialSelectionKind.BuiltIn, e)).ToArray();
        Filters = new[] { new MaterialFilter(MaterialFilterKind.All, Strings.AllMaterials) }
            .Concat(Categories().Select(c => new MaterialFilter(MaterialFilterKind.Category, c.Label, c.Value)))
            .Append(new(MaterialFilterKind.User, Strings.MyMaterials)).ToArray();
        _filter = Filters[0];
        BackCommand = new(setup.ShowOverview, () => CanBrowse);
        NewCommand = new(() => BeginEdit(null, SetupEditorMode.ProjectDraft), () => CanBrowse);
        UseSelectedCommand = new(UseSelected, () => CanUseSelected);
        EditSelectedCommand = new(EditSelected, () => CanEditSelected);
        DeleteSelectedCommand = new(DeleteSelectedAsync, () => CanDeleteSelected);
        ShowUserCommand = new(() => SelectedFilter = UserFilter, () => CanBrowse);
        SelectProjectCommand = new(() => SelectedEntry = CurrentEntry, () => CanBrowse && CurrentEntry is not null);
        RebuildEntries();
        Refresh();
    }
    public IReadOnlyList<MaterialFilter> Filters { get; }
    public IReadOnlyList<MaterialFilter> CategoryFilters => Filters.Where(f => f.Kind != MaterialFilterKind.User).ToArray();
    public MaterialFilter UserFilter => Filters.Single(f => f.Kind == MaterialFilterKind.User);
    public bool IsUserFilter => SelectedFilter.Kind == MaterialFilterKind.User;
    public MaterialEditorViewModel? Editor => _editor;
    public bool IsEditing => Editor is not null;
    public bool CanBrowse => !IsBusy && !IsEditing;
    public string Title => Editor?.Title ?? Strings.MaterialSelection;
    public IReadOnlyList<MaterialSelectionItem> AllEntries => _entries;
    public IReadOnlyList<MaterialSelectionItem> BuiltInEntries => _builtIns;
    public IReadOnlyList<MaterialSelectionItem> UserEntries => _users;
    public MaterialSelectionItem? CurrentEntry => _currentEntry;
    public bool HasProjectEntry => CurrentEntry is not null;
    public bool IsProjectSelected => SelectedEntry?.IsProject == true;
    public IReadOnlyList<MaterialSelectionItem> VisibleEntries => _visible;
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
            if (value is not null && !_visible.Contains(value) || !Set(ref _selected, value)) return;
            NotifySelection();
        }
    }
    public bool HasSelection => SelectedEntry is not null;
    public bool CanUseSelected => CanBrowse && HasSelection;
    public bool CanEditSelected => CanBrowse && SelectedEntry is { } selected && (!selected.IsUser || Setup.Libraries?.CanSaveMaterials == true);
    public bool CanDeleteSelected => CanBrowse && SelectedEntry?.IsUser == true && Setup.Libraries?.CanSaveMaterials == true;
    public bool ShowDelete => SelectedEntry?.IsUser == true;
    public string EditLabel => SelectedEntry?.IsUser == true ? Strings.EditEntry : Strings.Customize;
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
    public AsyncActionCommand DeleteSelectedCommand { get; }
    public ActionCommand ShowUserCommand { get; }
    public ActionCommand SelectProjectCommand { get; }

    public void UseSelected()
    {
        if (!CanUseSelected || SelectedEntry is not { } selected) return;
        if (selected.Entry is { } entry) Setup.SelectMaterial(entry);
        else Setup.SelectedMaterial = selected.Material;
        Setup.ShowOverview();
    }
    private void EditSelected()
    {
        if (!CanEditSelected || SelectedEntry is not { } selected) return;
        BeginEdit(selected.Entry, selected.IsUser ? SetupEditorMode.UserLibraryEdit : SetupEditorMode.ProjectDraft,
            selected.IsProject ? selected.Material : null);
    }
    private void BeginEdit(MaterialLibraryEntry? entry, SetupEditorMode mode, Material? snapshot = null)
    {
        if (!CanBrowse) return;
        _editor = new(Setup, entry, mode, snapshot);
        NotifyMode();
    }
    internal void EndEdit(string? replacement = null)
    {
        if (IsBusy) return;
        _editor = null;
        Refresh();
        if (replacement is not null) SelectReplacement(replacement);
        NotifyMode();
    }
    private void NotifyMode()
    {
        foreach (var name in new[] { nameof(Editor), nameof(IsEditing), nameof(CanBrowse), nameof(Title) }) Notify(name);
        BackCommand.Refresh(); NewCommand.Refresh(); ShowUserCommand.Refresh(); SelectProjectCommand.Refresh(); NotifySelection();
        Setup.Libraries?.RefreshWorkspaceState();
    }
    public async Task<bool> DeleteSelectedAsync()
    {
        if (!CanDeleteSelected || SelectedEntry is not { } selected) return false;
        int index = _visible.ToList().IndexOf(selected);
        _error = await Setup.Libraries!.DeleteMaterialAsync(selected.Name);
        Refresh();
        if (_error is null && !_visible.Contains(selected))
            SelectedEntry = _currentEntry ?? _visible.ElementAtOrDefault(Math.Min(index, _visible.Count - 1));
        Notify(nameof(Error)); Notify(nameof(HasError));
        return _error is null;
    }
    internal void SelectReplacement(string name)
    {
        Refresh();
        var replacement = _users.SingleOrDefault(e => e.Name == name);
        if (replacement is null) return;
        if (_filter.Kind == MaterialFilterKind.Category && replacement.Entry?.Category != _filter.Category)
            SelectedFilter = Filters[0];
        if (!_filtered.Contains(replacement)) SearchText = "";
        SelectedEntry = replacement;
    }
    private void RebuildEntries()
    {
        var previous = SelectedEntry;
        _shownLibrary = Setup.Libraries?.UserMaterials;
        _users = (_shownLibrary?.All ?? []).Select(e => new MaterialSelectionItem(e.Material, MaterialSelectionKind.User, e)).ToArray();
        _entries = _builtIns.Concat(_users).ToArray();
        var matches = _currentMaterial is null ? [] : _entries.Where(e => MaterialSnapshotComparison.Equals(e.Material, _currentMaterial)).ToArray();
        _currentEntry = _currentMaterial is not null && matches.Length != 1
            ? _currentEntry ?? new(_currentMaterial, MaterialSelectionKind.Project) : null;
        var preferred = previous is null ? _currentEntry ?? matches.SingleOrDefault()
            : _entries.FirstOrDefault(e => e.Kind == previous.Kind && e.Entry == previous.Entry)
                ?? (previous.IsProject ? _currentEntry ?? matches.SingleOrDefault() : null);
        RebuildVisible(preferred);
        foreach (var name in new[] { nameof(UserEntries), nameof(AllEntries), nameof(CurrentEntry), nameof(HasProjectEntry) }) Notify(name);
    }
    private void RebuildVisible(MaterialSelectionItem? preferred = null)
    {
        _filtered = _entries.Where(e => (_filter.Kind == MaterialFilterKind.All || _filter.Kind == MaterialFilterKind.User && e.IsUser
            || _filter.Kind == MaterialFilterKind.Category && e.Entry?.Category == _filter.Category)
            && (Contains(e.Name) || Contains(e.MaterialNumber) || e.Entry?.OtherStandards.Any(Contains) == true)).ToArray();
        var visible = (_currentEntry is null ? _filtered : new[] { _currentEntry }.Concat(_filtered)).ToArray();
        var selection = preferred ?? SelectedEntry;
        // Preserve the native selection when a busy-state notification has not changed any rows.
        if (!_visible.SequenceEqual(visible)) { _visible = visible; Notify(nameof(VisibleEntries)); }
        SelectedEntry = selection is not null && _visible.Contains(selection) ? selection : null;
        Notify(nameof(FilteredEntries)); Notify(nameof(IsEmpty));
        Notify(nameof(IsUserFilter));
    }
    private bool Contains(string? text) => text?.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase) == true;
    private void NotifySelection()
    {
        foreach (var name in new[] { nameof(HasSelection), nameof(IsProjectSelected), nameof(CanUseSelected), nameof(CanEditSelected), nameof(CanDeleteSelected), nameof(ShowDelete), nameof(EditLabel) }) Notify(name);
        UseSelectedCommand.Refresh(); EditSelectedCommand.Refresh(); DeleteSelectedCommand.Refresh();
    }
    public override void Refresh()
    {
        base.Refresh();
        Editor?.Refresh();
        if (!ReferenceEquals(_shownLibrary, Setup.Libraries?.UserMaterials)) RebuildEntries();
        foreach (var name in new[] { nameof(BuiltInError), nameof(HasBuiltInError), nameof(LibraryError), nameof(HasLibraryError), nameof(CanBrowse) }) Notify(name);
        BackCommand.Refresh(); NewCommand.Refresh(); ShowUserCommand.Refresh(); SelectProjectCommand.Refresh(); NotifySelection();
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
