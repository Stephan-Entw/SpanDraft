using SpanDraft.Core.Sections;
using SpanDraft.Desktop.Libraries;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Resources;

namespace SpanDraft.Desktop.ViewModels;

public sealed class SectionSelectionItem
{
    public SectionSelectionItem(SectionLibraryEntry entry, ProjectSetupViewModel setup, Func<string, Task> delete)
    {
        Entry = entry;
        Description = SectionDisplay.Name(entry.Section, setup.ResultPresentation.Profile);
        Area = QuantityFormatter.Format(entry.Section.Area.SquareMeters, QuantityKind.Area,
            setup.ResultPresentation.Profile, setup.ResultPresentation.Mode);
        Axes = SectionAxisValue.From(entry.Section, setup.ResultPresentation);
        UseCommand = new(() => { setup.SelectedSection = entry.Section; setup.ShowOverview(); }, () => !setup.IsBusy);
        EditCommand = new(() => setup.EditSection(entry.Section, entry.Name, SetupEditorMode.UserLibraryEdit),
            () => !setup.IsBusy && setup.Libraries?.CanSaveSections == true);
        DeleteCommand = new(async () => { await delete(entry.Name); return true; },
            () => !setup.IsBusy && setup.Libraries?.CanSaveSections == true);
    }
    public SectionLibraryEntry Entry { get; }
    public string Description { get; }
    public string Area { get; }
    public IReadOnlyList<SectionAxisValue> Axes { get; }
    public ActionCommand UseCommand { get; }
    public ActionCommand EditCommand { get; }
    public AsyncActionCommand DeleteCommand { get; }
}

public sealed class SectionSelectionViewModel : SetupStepViewModel
{
    private string? _error;
    public SectionSelectionViewModel(ProjectSetupViewModel setup) : base(setup)
    {
        BackCommand = new(setup.ShowOverview, () => !IsBusy);
        NewCommand = new(() => setup.EditSection(null, null, SetupEditorMode.ProjectDraft), () => !IsBusy);
        UseCurrentCommand = new(() => { setup.SelectedSection = setup.OriginalSection!; setup.ShowOverview(); },
            () => !IsBusy && HasCurrent);
        EditCurrentCommand = new(() => setup.EditSection(setup.OriginalSection, null, SetupEditorMode.ProjectDraft),
            () => !IsBusy && HasCurrent);
    }
    public bool HasCurrent => Setup.OriginalSection is not null;
    public string CurrentDescription => HasCurrent ? SectionDisplay.Name(Setup.OriginalSection!, Setup.ResultPresentation.Profile) : "";
    public IReadOnlyList<SectionSelectionItem> UserEntries => (Setup.Libraries?.UserSections.All ?? [])
        .Select(e => new SectionSelectionItem(e, Setup, DeleteAsync)).ToArray();
    public bool IsUserEmpty => !HasLibraryError && UserEntries.Count == 0;
    public string? LibraryError => Setup.Libraries?.SectionLibraryError;
    public bool HasLibraryError => LibraryError is not null;
    public string? Error => _error;
    public bool HasError => Error is not null;
    public ActionCommand BackCommand { get; }
    public ActionCommand NewCommand { get; }
    public ActionCommand UseCurrentCommand { get; }
    public ActionCommand EditCurrentCommand { get; }
    public async Task DeleteAsync(string name)
    {
        _error = await (Setup.Libraries?.DeleteSectionAsync(name) ?? Task.FromResult<string?>(Strings.SectionLibraryUnavailable));
        Refresh();
    }
    public override void Refresh()
    {
        base.Refresh();
        foreach (var name in new[] { nameof(UserEntries), nameof(CurrentDescription), nameof(LibraryError),
            nameof(HasLibraryError), nameof(IsUserEmpty), nameof(Error), nameof(HasError) }) Notify(name);
        BackCommand.Refresh(); NewCommand.Refresh(); UseCurrentCommand.Refresh(); EditCurrentCommand.Refresh();
    }
}
