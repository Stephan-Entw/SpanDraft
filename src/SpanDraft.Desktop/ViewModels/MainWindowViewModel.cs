using System.Text.Json;
using SpanDraft.Analysis;
using SpanDraft.Core.Beams;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.State;

namespace SpanDraft.Desktop.ViewModels;

public enum MainViewMode { ProjectSetup, Editor }

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly Func<BeamModel, BeamAnalysisOutcome>? _analyze;
    private readonly IProjectFileStore _files;
    private readonly IProjectDialogs? _dialogs;
    private readonly ProjectRecovery? _recovery;
    private MainViewMode _mode;
    private ProjectSetupViewModel _setup;
    private EditorViewModel? _editor;
    private bool _busy;
    private bool _fileMenuOpen;
    private Exception? _pendingRecoveryError;
    private ResultPresentationOptions _resultPresentation = ResultPresentationOptions.Default;
    private readonly LocalSettingsStore? _settingsStore;
    private bool _settingsSaving;
    private UserSettings _settings = UserSettings.Default;

    public MainWindowViewModel(Func<BeamModel, BeamAnalysisOutcome>? analyze = null,
        IProjectFileStore? files = null, IProjectDialogs? dialogs = null, ProjectRecovery? recovery = null,
        LocalSettingsStore? settingsStore = null, UserSettings? settings = null,
        MaterialLibraryStore? materialStore = null, SectionLibraryStore? sectionStore = null,
        Func<SpanDraft.Desktop.Libraries.BuiltInMaterialCatalog>? catalogLoader = null)
    {
        _analyze = analyze;
        _files = files ?? new ProjectFileStore();
        _dialogs = dialogs;
        _recovery = recovery;
        _settingsStore = settingsStore;
        _settings = settings ?? UserSettings.Default;
        _resultPresentation = _settings.Presentation;
        _materialStore = materialStore;
        _sectionStore = sectionStore;
        try
        {
            BuiltInMaterials = (catalogLoader ?? MaterialCatalogCodec.LoadBuiltIn)();
            if (BuiltInMaterials.Find("S235JR") is null) throw new LibraryFormatException("Missing S235JR.");
        }
        catch (Exception e) when (IsLibraryError(e)) { BuiltInError = Strings.BuiltInUnavailable; BuiltInMaterials = null; }
        if (_recovery is not null) _recovery.Failed += RecoveryFailed;
        _setup = NewSetup();
        NewCommand = new(() => NewAsync(), () => !IsBusy);
        OpenCommand = new(() => OpenAsync(), () => !IsBusy);
        SaveCommand = new(() => SaveAsync(), () => !IsBusy && Session is not null);
        SaveAsCommand = new(() => SaveAsync(saveAs: true), () => !IsBusy && Session is not null);
        UndoCommand = new(() => Undo(), () => CanUndo);
        RedoCommand = new(() => Redo(), () => CanRedo);
    }

    public MainViewMode Mode => _mode;
    public ProjectSetupViewModel Setup => _setup;
    public EditorViewModel? Editor => _editor;
    public ResultPresentationOptions ResultPresentation => _resultPresentation;
    public UserSettings Settings => _settings;

    public async Task InitializeSettingsAsync()
    {
        if (_settingsStore is null) return;
        ActivateSettings(await _settingsStore.LoadAsync());
    }

    public void SetSettingsDialogOpen(bool open)
    {
        if (Editor is { } editor) editor.IsSettingsDialogOpen = open;
    }

    public async Task<string?> ApplySettingsAsync(UserSettings settings)
    {
        if (_settingsSaving || IsBusy) return Strings.ProjectBusy;
        if (Editor?.CanApplyInputUnits(settings.Profile) == false || !Setup.CanApplyInputUnits(settings.Profile)) return Strings.SettingsDraftBlocked;
        _settingsSaving = true;
        try
        {
            if (_settingsStore is not null) await _settingsStore.SaveAsync(settings);
            ActivateSettings(settings);
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        { return Strings.SettingsSaveError + "\n" + e.Message; }
        finally { _settingsSaving = false; }
    }

    private void ActivateSettings(UserSettings settings)
    {
        _settings = settings;
        SetPresentation(settings.Presentation);
        Notify(nameof(Settings));
    }

    /// <summary>Changes presentation only; affected open input sessions reject unit changes.</summary>
    public bool SetResultPresentation(UnitProfile profile, PresentationMode mode)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var options = new ResultPresentationOptions(profile, mode);
        if (_settingsSaving || IsBusy || Editor?.CanApplyInputUnits(profile) == false || !Setup.CanApplyInputUnits(profile)) return false;
        profile = UserSettings.Recognize(profile);
        options = new(profile, mode);
        _settings = _settings with { Profile = profile, Mode = mode,
            CustomProfile = profile.Kind == UnitProfileKind.Custom ? profile : _settings.CustomProfile,
            LastStandardProfile = profile.Kind == UnitProfileKind.Custom ? _settings.LastStandardProfile : profile.Kind };
        SetPresentation(options);
        return true;
    }

    private void SetPresentation(ResultPresentationOptions options)
    {
        if (_resultPresentation.Mode == options.Mode && UserSettings.SameUnits(_resultPresentation.Profile, options.Profile)) return;
        _resultPresentation = options;
        Setup.ApplyResultPresentation(options);
        Editor?.ApplyResultPresentation(options);
        Notify(nameof(ResultPresentation));
    }
    public ProjectSession? Session => Editor?.Session;
    public bool IsBusy => _busy || _libraryBusy;
    public bool CanUndo => !IsBusy && Session?.CanUndo == true;
    public bool CanRedo => !IsBusy && Session?.CanRedo == true;
    public string ProjectName => Session?.FilePath is { } path ? Path.GetFileName(path) : Strings.Untitled;
    public string WindowTitle => Session is null ? Strings.ApplicationTitle
        : ProjectName + (Session.IsDirty ? "*" : "") + " — " + Strings.ApplicationTitle;
    public object CurrentViewModel => Mode == MainViewMode.ProjectSetup ? Setup : Editor!;
    public AsyncActionCommand NewCommand { get; }
    public AsyncActionCommand OpenCommand { get; }
    public AsyncActionCommand SaveCommand { get; }
    public AsyncActionCommand SaveAsCommand { get; }
    public ActionCommand UndoCommand { get; }
    public ActionCommand RedoCommand { get; }

    public void SetFileMenuOpen(bool open)
    {
        _fileMenuOpen = open;
        if (Editor is { } editor) editor.IsFileMenuOpen = open;
    }

    private ProjectSetupViewModel NewSetup() => new(ProjectSetupMode.Create,
        new SpanDraft.Core.Sections.Parametric.RectangularHollowSectionGeometry(
            Length.FromMillimeters(100), Length.FromMillimeters(100), Length.FromMillimeters(5), Length.FromMillimeters(0)),
        SpanDraft.Core.Sections.SectionAxisDesignation.Y, BuiltInMaterials?.Find("S235JR")?.Material,
        ApplySetup, CancelSetup, ResultPresentation, this);

    public void EditProject()
    {
        if (IsBusy || Editor is null) return;
        Editor.CancelEditorInteraction();
        _setup = new(ProjectSetupMode.Edit, Editor.Document.Section, Editor.Document.BendingAxis, Editor.Document.Material, ApplySetup, CancelSetup, ResultPresentation, this);
        Navigate(MainViewMode.ProjectSetup);
    }

    private void ApplySetup(ProjectSetupViewModel setup)
    {
        if (IsBusy || !setup.CanApply || setup.SelectedMaterial is not { } material) return;
        if (setup.Mode == ProjectSetupMode.Create)
            Activate(ProjectSession.Create(new(new(Length.FromMillimeters(1000), material, setup.SelectedSection, setup.BendingAxis), new())));
        else
        {
            Editor!.ApplySetup(setup.SelectedSection, setup.BendingAxis, material);
            Navigate(MainViewMode.Editor);
        }
    }

    private void CancelSetup()
    {
        if (!IsBusy && Setup.IsEdit) Navigate(MainViewMode.Editor);
    }

    private void Activate(ProjectSession session)
    {
        DetachSession();
        _editor = EditorViewModel.ForSession(session, EditProject, _analyze, ResultPresentation);
        _editor.IsFileMenuOpen = _fileMenuOpen;
        session.SetBusy(IsBusy);
        session.Changed += SessionChanged;
        session.StateApplied += CommittedStateChanged;
        Navigate(MainViewMode.Editor);
        if (session.IsDirty) _recovery?.Schedule(session);
    }

    private void DetachSession()
    {
        if (Session is not { } session) return;
        session.Changed -= SessionChanged;
        session.StateApplied -= CommittedStateChanged;
    }

    private void CommittedStateChanged(ProjectState previous, ProjectState next, EditorChangeKind change) => _recovery?.Schedule(Session!);
    private void SessionChanged() => NotifySession();

    public bool Undo()
    {
        if (!CanUndo || Editor is null) return false;
        bool changed = Editor.Undo();
        if (Setup.IsEdit && Mode == MainViewMode.ProjectSetup) Navigate(MainViewMode.Editor);
        return changed;
    }

    public bool Redo()
    {
        if (!CanRedo || Editor is null) return false;
        bool changed = Editor.Redo();
        if (Setup.IsEdit && Mode == MainViewMode.ProjectSetup) Navigate(MainViewMode.Editor);
        return changed;
    }

    public Task<bool> NewAsync() => RunOperationAsync(async () =>
    {
        if (!await CanLeaveAsync()) return false;
        await CompleteLeaveAsync();
        _editor = null;
        _setup = NewSetup();
        Navigate(MainViewMode.ProjectSetup);
        return true;
    });

    public Task<bool> OpenAsync() => RunOperationAsync(async () =>
    {
        string? path = _dialogs is null ? null : await _dialogs.PickOpenPathAsync();
        if (path is null) return false;
        // Parse and validate before touching the old session or asking its leave question.
        var bytes = await _files.ReadAsync(path) ?? throw new IOException(Strings.ProjectFileMissing);
        var state = ProjectFileCodec.Deserialize(bytes);
        var previousSession = Session;
        bool wasDirty = previousSession?.IsDirty == true;
        if (!await CanLeaveAsync()) return false;
        // Saving the old session may itself replace the selected target (including Save As).
        // Activate the snapshot actually written in that case, rather than a stale pre-guard read.
        if (wasDirty && previousSession is { IsDirty: false, FilePath: { } savedPath }
            && string.Equals(Path.GetFullPath(path), savedPath,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            state = previousSession.CurrentRevision.State;
        await CompleteLeaveAsync();
        Activate(ProjectSession.Open(state, Path.GetFullPath(path)));
        return true;
    });

    public Task<bool> SaveAsync(bool saveAs = false) => RunOperationAsync(() => SaveCurrentAsync(saveAs));

    private async Task<bool> SaveCurrentAsync(bool saveAs = false)
    {
        if (Session is not { } session) return false;
        string? path = saveAs ? null : session.FilePath;
        if (path is null)
        {
            path = _dialogs is null ? null : await _dialogs.PickSavePathAsync(session.FilePath);
            if (path is null) return false;
            path = EnsureProjectExtension(path);
        }
        path = Path.GetFullPath(path);
        var revision = session.CurrentRevision;
        try
        {
            var bytes = ProjectFileCodec.Serialize(revision.State);
            if (session.IsDirty && _recovery is not null) await _recovery.FlushAsync(revision.State, path);
            await _files.WriteAtomicAsync(path, bytes);
            session.MarkSaved(revision, path);
        }
        catch
        {
            // A failed Save As retains the prior save target in the recovery snapshot as well.
            if (session.IsDirty && _recovery is not null)
            {
                try { await _recovery.FlushAsync(revision.State, session.FilePath); }
                catch (Exception e) when (IsFileError(e)) { _recovery.Schedule(session); }
            }
            throw;
        }
        if (_recovery is not null)
        {
            try { await _recovery.DeleteAsync(); }
            catch (Exception e) when (IsFileError(e)) { await ShowErrorAsync(Strings.RecoveryError, e); }
        }
        return true;
    }

    public static string EnsureProjectExtension(string path) => path.EndsWith(".spandraft", StringComparison.OrdinalIgnoreCase)
        ? path : path + ".spandraft";

    private async Task<bool> CanLeaveAsync()
    {
        if (Session?.IsDirty != true) return true;
        var decision = _dialogs is null ? LeaveDecision.Cancel : await _dialogs.ConfirmLeaveAsync(ProjectName);
        return decision switch
        {
            LeaveDecision.Discard => true,
            LeaveDecision.Save => await SaveCurrentAsync(),
            _ => false
        };
    }

    private async Task CompleteLeaveAsync()
    {
        if (_recovery is not null) await _recovery.DeleteAsync();
        Editor?.CancelEditorInteraction();
        DetachSession();
    }

    /// <summary>True only once the shared leave workflow has succeeded. The Window handles its one approved Close.</summary>
    public Task<bool> RequestCloseAsync() => RunOperationAsync(async () =>
    {
        if (!await CanLeaveAsync()) return false;
        await CompleteLeaveAsync();
        return true;
    });

    public Task<bool> InitializeRecoveryAsync() => RunOperationAsync(async () =>
    {
        if (_recovery is null || Session is not null) return true;
        RecoverySnapshot? snapshot;
        try { snapshot = await _recovery.ReadAsync(); }
        catch (Exception e) when (IsFileError(e))
        {
            await ShowErrorAsync(Strings.RecoveryDamaged, e);
            if (_dialogs is not null && await _dialogs.ConfirmRecoveryAsync(true, null) == RecoveryDecision.Discard)
                await _recovery.DeleteAsync();
            return true;
        }
        if (snapshot is null) return true;
        if (snapshot.OriginalFilePath is { } path)
        {
            try
            {
                var bytes = await _files.ReadAsync(path);
                if (bytes is not null && snapshot.State.ContentEquals(ProjectFileCodec.Deserialize(bytes)))
                {
                    await _recovery.DeleteAsync();
                    return true;
                }
            }
            catch (Exception e) when (IsFileError(e)) { /* A missing/invalid original does not invalidate recovery. */ }
        }
        var choice = _dialogs is null ? RecoveryDecision.Cancel
            : await _dialogs.ConfirmRecoveryAsync(false, snapshot.WrittenAtUtc);
        if (choice == RecoveryDecision.Restore) Activate(ProjectSession.Restore(snapshot.State, snapshot.OriginalFilePath));
        else if (choice == RecoveryDecision.Discard) await _recovery.DeleteAsync();
        return true;
    });

    private async Task<bool> RunOperationAsync(Func<Task<bool>> operation)
    {
        if (IsBusy) return false;
        _busy = true;
        Session?.SetBusy(true);
        NotifySession();
        try { return await operation(); }
        catch (Exception e) when (IsFileError(e))
        {
            await ShowErrorAsync(Strings.ProjectFileError, e);
            return false;
        }
        finally
        {
            _busy = false;
            Session?.SetBusy(false);
            NotifySession();
            if (_pendingRecoveryError is { } recoveryError)
            {
                _pendingRecoveryError = null;
                await ReportRecoveryErrorAsync(recoveryError);
            }
        }
    }

    private static bool IsFileError(Exception e) => e is IOException or UnauthorizedAccessException
        or ProjectFormatException or JsonException or ArgumentException or NotSupportedException;
    private Task ShowErrorAsync(string prefix, Exception e) => _dialogs?.ShowErrorAsync(prefix + "\n" + e.Message) ?? Task.CompletedTask;
    private async void RecoveryFailed(Exception error)
    {
        if (IsBusy) { _pendingRecoveryError = error; return; }
        await ReportRecoveryErrorAsync(error);
    }

    private Task<bool> ReportRecoveryErrorAsync(Exception error) => RunOperationAsync(async () =>
    {
        await ShowErrorAsync(Strings.RecoveryError, error);
        return true;
    });

    private void Navigate(MainViewMode mode)
    {
        _mode = mode;
        Notify(nameof(Mode));
        Notify(nameof(Setup));
        Notify(nameof(Editor));
        Notify(nameof(CurrentViewModel));
        NotifySession();
    }

    private void NotifySession()
    {
        _setup?.RefreshLibraryState();
        Notify(nameof(Session));
        Notify(nameof(IsBusy));
        Notify(nameof(CanUndo));
        Notify(nameof(CanRedo));
        Notify(nameof(WindowTitle));
        NewCommand?.Refresh(); OpenCommand?.Refresh(); SaveCommand?.Refresh(); SaveAsCommand?.Refresh();
        UndoCommand?.Refresh(); RedoCommand?.Refresh();
    }
}
