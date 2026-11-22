using SpanDraft.Core.Materials;
using SpanDraft.Desktop.Libraries;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.Services;

namespace SpanDraft.Desktop.ViewModels;

/// <summary>
/// Library access of one workspace: it shows the shared libraries, translates their technical states into the existing UI
/// texts and forwards edits to the services. It owns neither snapshots nor persistence.
/// </summary>
public sealed partial class MainWindowViewModel
{
    private static readonly UserMaterialLibrary NoMaterials = new();
    private static readonly UserSectionLibrary NoSections = new();
    private bool _libraryBusy;
    public BuiltInMaterialCatalog? BuiltInMaterials => _services.Materials.BuiltIns;
    public UserMaterialLibrary UserMaterials => _services.Materials.State.Snapshot ?? NoMaterials;
    public UserSectionLibrary UserSections => _services.Sections.State.Snapshot ?? NoSections;
    public string? BuiltInError => _services.Materials.BuiltInFailure is null ? null : Strings.BuiltInUnavailable;
    public string? MaterialLibraryError => _services.Materials.State.Failure is null ? null : Strings.MaterialLibraryUnavailable;
    public string? SectionLibraryError => _services.Sections.State.Failure is null ? null : Strings.SectionLibraryUnavailable;
    public bool CanSaveMaterials => _services.Materials.CanMutate;
    public bool CanSaveSections => _services.Sections.CanMutate;

    /// <summary>Tells the user about a built-in catalog that could not be loaded at startup.</summary>
    public async Task ReportStartupErrorsAsync()
    {
        if (BuiltInError is { } error && _dialogs is not null) await _dialogs.ShowErrorAsync(error);
    }

    internal Task ShowMaterialEditorAsync(MaterialEditorViewModel editor) => _dialogs?.ShowMaterialEditorAsync(editor) ?? Task.CompletedTask;
    internal Task<MaterialNameConflictDecision> ResolveMaterialNameConflictAsync(Material material) =>
        _dialogs?.ResolveMaterialNameConflictAsync(MaterialDisplay.Format(material)) ?? Task.FromResult(MaterialNameConflictDecision.Cancel);
    internal Task<bool> ConfirmMaterialReplaceAsync(Material material) =>
        _dialogs?.ConfirmMaterialReplaceAsync(MaterialDisplay.Format(material)) ?? Task.FromResult(false);

    /// <summary>Adds the entry, or replaces <paramref name="expectedOriginal"/>, the entry the editor started from.</summary>
    public Task<string?> SaveMaterialAsync(Material entry, Material? expectedOriginal = null) => LibraryOperationAsync(async () =>
    {
        if (!CanSaveMaterials) return Strings.MaterialLibraryUnavailable;
        if (entry.StrengthType is null) return Strings.MaterialStrengthRequired;
        var result = expectedOriginal is null ? await _services.Materials.AddAsync(entry)
            : await _services.Materials.ReplaceAsync(expectedOriginal, entry);
        return Describe(result, Strings.MaterialLibraryUnavailable);
    });

    public Task<string?> SaveSectionAsync(SectionLibraryEntry entry, SectionLibraryEntry? expectedOriginal = null) => LibraryOperationAsync(async () =>
    {
        if (!CanSaveSections) return Strings.SectionLibraryUnavailable;
        var result = expectedOriginal is null ? await _services.Sections.AddAsync(entry)
            : await _services.Sections.ReplaceAsync(expectedOriginal, entry);
        return Describe(result, Strings.SectionLibraryUnavailable);
    });

    public Task<string?> DeleteMaterialAsync(Material expectedOriginal) => LibraryOperationAsync(async () =>
    {
        if (!CanSaveMaterials) return Strings.MaterialLibraryUnavailable;
        if (UserMaterials.Find(expectedOriginal.Name) is null) return Strings.LibraryEntryMissing;
        if (_dialogs is null || !await _dialogs.ConfirmMaterialDeleteAsync(MaterialDisplay.Format(expectedOriginal))) return null;
        return Describe(await _services.Materials.DeleteAsync(expectedOriginal), Strings.MaterialLibraryUnavailable);
    });

    public Task<string?> DeleteSectionAsync(SectionLibraryEntry expectedOriginal) => LibraryOperationAsync(async () =>
    {
        if (!CanSaveSections) return Strings.SectionLibraryUnavailable;
        if (_dialogs is null || !await _dialogs.ConfirmSectionDeleteAsync(expectedOriginal.Name)) return null;
        return Describe(await _services.Sections.DeleteAsync(expectedOriginal), Strings.SectionLibraryUnavailable);
    });

    // The services report technical outcomes; the existing messages are chosen here.
    private static string? Describe(LibraryMutationResult result, string unavailable) => result.Outcome switch
    {
        LibraryMutationOutcome.Committed => null,
        LibraryMutationOutcome.Unavailable => unavailable,
        LibraryMutationOutcome.Conflict => result.Conflict is LibraryConflictKind.AlreadyExists or LibraryConflictKind.TargetNameTaken
            ? Strings.LibraryNameConflict : Strings.LibraryEntryMissing,
        LibraryMutationOutcome.Invalid => result.Failure is KeyNotFoundException ? Strings.LibraryEntryMissing : Strings.LibraryNameConflict,
        _ => Strings.LibrarySaveFailed
    };

    private async Task<string?> LibraryOperationAsync(Func<Task<string?>> action)
    {
        if (IsBusy || _settingsSaving) return Strings.ProjectBusy;
        _libraryBusy = true;
        Session?.SetBusy(true);
        NotifySession();
        try { return await action(); }
        finally
        {
            _libraryBusy = false;
            Session?.SetBusy(false);
            NotifySession();
            if (_pendingRecoveryError is { } error)
            { _pendingRecoveryError = null; await ReportRecoveryErrorAsync(error); }
        }
    }
}
