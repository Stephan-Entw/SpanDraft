using SpanDraft.Core.Materials;
using SpanDraft.Desktop.Libraries;
using SpanDraft.Desktop.Resources;

namespace SpanDraft.Desktop.ViewModels;

public sealed class MaterialSelectionItem : ObservableObject
{
    public MaterialSelectionItem(MaterialLibraryEntry entry, bool builtIn, ProjectSetupViewModel setup,
        Func<string, Task> delete)
    {
        Entry = entry; IsBuiltIn = builtIn;
        UseCommand = new(() => { setup.SelectedMaterial = entry.Material; setup.ShowOverview(); }, () => !setup.IsBusy);
        EditCommand = new(() => setup.EditMaterial(entry, builtIn ? SetupEditorMode.ProjectDraft : SetupEditorMode.UserLibraryEdit),
            () => !setup.IsBusy && (builtIn || setup.Libraries?.CanSaveMaterials == true));
        DeleteCommand = new(async () => { await delete(entry.Material.Name); return true; },
            () => !builtIn && !setup.IsBusy && setup.Libraries?.CanSaveMaterials == true);
    }
    public MaterialLibraryEntry Entry { get; }
    public bool IsBuiltIn { get; }
    public bool IsUser => !IsBuiltIn;
    public string Caption => string.Join(" · ", new[] { Entry.Material.Name, Entry.MaterialNumber }
        .Concat(Entry.OtherStandards).Where(s => !string.IsNullOrEmpty(s)));
    public string SourceLabel => IsBuiltIn ? Strings.BuiltInLabel : Strings.UserLabel;
    public string EditLabel => IsBuiltIn ? Strings.Customize : Strings.EditEntry;
    public ActionCommand UseCommand { get; }
    public ActionCommand EditCommand { get; }
    public AsyncActionCommand DeleteCommand { get; }
    internal void Refresh() { UseCommand.Refresh(); EditCommand.Refresh(); DeleteCommand.Refresh(); }
}

public sealed record MaterialGroup(string Label, IReadOnlyList<MaterialSelectionItem> Entries);

public sealed class MaterialSelectionViewModel : SetupStepViewModel
{
    private string? _error;
    private UserMaterialLibrary? _shownLibrary;
    private IReadOnlyList<MaterialSelectionItem> _users = [];
    public MaterialSelectionViewModel(ProjectSetupViewModel setup) : base(setup)
    {
        BuiltInGroups = Categories().Where(c => c.Value != MaterialCategory.Other)
            .Select(c => new MaterialGroup(c.Label, (setup.Libraries?.BuiltInMaterials?.All ?? [])
                .Where(e => e.Category == c.Value).Select(e => new MaterialSelectionItem(e, true, setup, DeleteAsync)).ToArray()))
            .Where(g => g.Entries.Count > 0).ToArray();
        BackCommand = new(setup.ShowOverview, () => !IsBusy);
        NewCommand = new(() => setup.EditMaterial(null, SetupEditorMode.ProjectDraft), () => !IsBusy);
        UseCurrentCommand = new(() => { setup.SelectedMaterial = setup.OriginalMaterial; setup.ShowOverview(); },
            () => !IsBusy && HasCurrent);
        EditCurrentCommand = new(setup.EditCurrentMaterial,
            () => !IsBusy && HasCurrent);
        Refresh();
    }
    public Material? CurrentMaterial => Setup.OriginalMaterial;
    public bool HasCurrent => CurrentMaterial is not null;
    public IReadOnlyList<MaterialGroup> BuiltInGroups { get; }
    public IReadOnlyList<MaterialSelectionItem> UserEntries => _users;
    public string? BuiltInError => Setup.Libraries?.BuiltInError;
    public bool HasBuiltInError => BuiltInError is not null;
    public string? LibraryError => Setup.Libraries?.MaterialLibraryError;
    public bool HasLibraryError => LibraryError is not null;
    public bool IsUserEmpty => !HasLibraryError && UserEntries.Count == 0;
    public string? Error => _error;
    public bool HasError => Error is not null;
    public ActionCommand BackCommand { get; }
    public ActionCommand NewCommand { get; }
    public ActionCommand UseCurrentCommand { get; }
    public ActionCommand EditCurrentCommand { get; }
    public async Task DeleteAsync(string name)
    {
        _error = await (Setup.Libraries?.DeleteMaterialAsync(name) ?? Task.FromResult<string?>(Strings.MaterialLibraryUnavailable));
        Notify(nameof(Error)); Notify(nameof(HasError)); Refresh();
    }
    public override void Refresh()
    {
        base.Refresh();
        if (!ReferenceEquals(_shownLibrary, Setup.Libraries?.UserMaterials))
        {
            _shownLibrary = Setup.Libraries?.UserMaterials;
            _users = (_shownLibrary?.All ?? []).Select(e => new MaterialSelectionItem(e, false, Setup, DeleteAsync)).ToArray();
            Notify(nameof(UserEntries));
        }
        foreach (var row in BuiltInGroups.SelectMany(g => g.Entries).Concat(UserEntries)) row.Refresh();
        foreach (var name in new[] { nameof(LibraryError), nameof(HasLibraryError), nameof(IsUserEmpty) }) Notify(name);
        BackCommand.Refresh(); NewCommand.Refresh(); UseCurrentCommand.Refresh(); EditCurrentCommand.Refresh();
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
