using System.Text.Json;
using SpanDraft.Desktop.Libraries;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Resources;

namespace SpanDraft.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private readonly MaterialLibraryStore? _materialStore;
    private readonly SectionLibraryStore? _sectionStore;
    private bool _libraryBusy;
    public BuiltInMaterialCatalog? BuiltInMaterials { get; private set; }
    public UserMaterialLibrary UserMaterials { get; private set; } = new();
    public UserSectionLibrary UserSections { get; private set; } = new();
    public string? BuiltInError { get; private set; }
    public string? MaterialLibraryError { get; private set; }
    public string? SectionLibraryError { get; private set; }
    public bool CanSaveMaterials => _materialStore is not null && MaterialLibraryError is null;
    public bool CanSaveSections => _sectionStore is not null && SectionLibraryError is null;

    public async Task InitializeLibrariesAsync()
    {
        _libraryBusy = true;
        NotifySession();
        try
        {
            if (_materialStore is not null)
            {
                try { UserMaterials = await _materialStore.LoadAsync(); }
                catch (Exception e) when (IsLibraryError(e)) { MaterialLibraryError = Strings.MaterialLibraryUnavailable; }
            }
            if (_sectionStore is not null)
            {
                try { UserSections = await _sectionStore.LoadAsync(); }
                catch (Exception e) when (IsLibraryError(e)) { SectionLibraryError = Strings.SectionLibraryUnavailable; }
            }
        }
        finally { _libraryBusy = false; NotifySession(); }
        if (BuiltInError is { } error && _dialogs is not null) await _dialogs.ShowErrorAsync(error);
    }

    public Task<string?> SaveMaterialAsync(MaterialLibraryEntry entry, string? oldName = null) => LibraryOperationAsync(async () =>
    {
        if (!CanSaveMaterials) return Strings.MaterialLibraryUnavailable;
        var next = new UserMaterialLibrary(UserMaterials.All);
        if (oldName is null) next.Add(entry); else next.Replace(oldName, entry);
        await _materialStore!.SaveAsync(next);
        UserMaterials = next;
        return null;
    });

    public Task<string?> SaveSectionAsync(SectionLibraryEntry entry, string? oldName = null) => LibraryOperationAsync(async () =>
    {
        if (!CanSaveSections) return Strings.SectionLibraryUnavailable;
        var next = new UserSectionLibrary(UserSections.All);
        if (oldName is null) next.Add(entry); else next.Replace(oldName, entry);
        await _sectionStore!.SaveAsync(next);
        UserSections = next;
        return null;
    });

    public Task<string?> DeleteMaterialAsync(string name) => LibraryOperationAsync(async () =>
    {
        if (!CanSaveMaterials) return Strings.MaterialLibraryUnavailable;
        if (_dialogs is null || !await _dialogs.ConfirmMaterialDeleteAsync(name)) return null;
        var next = new UserMaterialLibrary(UserMaterials.All);
        if (!next.Remove(name)) return Strings.LibraryEntryMissing;
        await _materialStore!.SaveAsync(next);
        UserMaterials = next;
        return null;
    });

    public Task<string?> DeleteSectionAsync(string name) => LibraryOperationAsync(async () =>
    {
        if (!CanSaveSections) return Strings.SectionLibraryUnavailable;
        if (_dialogs is null || !await _dialogs.ConfirmSectionDeleteAsync(name)) return null;
        var next = new UserSectionLibrary(UserSections.All);
        if (!next.Remove(name)) return Strings.LibraryEntryMissing;
        await _sectionStore!.SaveAsync(next);
        UserSections = next;
        return null;
    });

    private async Task<string?> LibraryOperationAsync(Func<Task<string?>> action)
    {
        if (IsBusy || _settingsSaving) return Strings.ProjectBusy;
        _libraryBusy = true;
        Session?.SetBusy(true);
        NotifySession();
        try { return await action(); }
        catch (ArgumentException) { return Strings.LibraryNameConflict; }
        catch (KeyNotFoundException) { return Strings.LibraryEntryMissing; }
        catch (Exception e) when (IsLibraryError(e)) { return Strings.LibrarySaveFailed; }
        finally
        {
            _libraryBusy = false;
            Session?.SetBusy(false);
            NotifySession();
            if (_pendingRecoveryError is { } error)
            { _pendingRecoveryError = null; await ReportRecoveryErrorAsync(error); }
        }
    }

    private static bool IsLibraryError(Exception e) => e is IOException or UnauthorizedAccessException
        or LibraryFormatException or JsonException or ArgumentException or NotSupportedException or OverflowException;
}
