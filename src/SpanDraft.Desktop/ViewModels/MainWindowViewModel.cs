using System.Text.Json;
using SpanDraft.Analysis;
using SpanDraft.Core.Beams;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.State;

namespace SpanDraft.Desktop.ViewModels;

public enum MainViewMode { ProjectSetup, Editor, Start }
public enum MainWorkspaceKind { ProjectSetup, MaterialSelection, MaterialEditor, SectionDefinition, BeamEditor, Start }

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly Func<BeamModel, BeamAnalysisOutcome>? _analyze;
    private readonly IProjectFileStore _files;
    private readonly IProjectDialogs? _dialogs;
    private readonly ProjectRecovery? _recovery;
    private MainViewMode _mode = MainViewMode.Start;
    private MainViewMode _setupReturnMode = MainViewMode.Start;
    private readonly RecentProjectsStore? _recentStore;
    private ProjectSetupViewModel _setup;
    private EditorViewModel? _editor;
    private bool _busy;
    private bool _mainMenuOpen;
    private Exception? _pendingRecoveryError;
    private ResultPresentationOptions _resultPresentation = ResultPresentationOptions.Default;
    private readonly LocalSettingsStore? _settingsStore;
    private bool _settingsSaving;
    private bool _settingsDialogOpen;
    private UserSettings _settings = UserSettings.Default;

    public MainWindowViewModel(Func<BeamModel, BeamAnalysisOutcome>? analyze = null,
        IProjectFileStore? files = null, IProjectDialogs? dialogs = null, ProjectRecovery? recovery = null,
        LocalSettingsStore? settingsStore = null, UserSettings? settings = null,
        MaterialLibraryStore? materialStore = null, SectionLibraryStore? sectionStore = null,
        Func<SpanDraft.Desktop.Libraries.BuiltInMaterialCatalog>? catalogLoader = null,
        RecentProjectsStore? recentStore = null, Func<Task>? openUserManual = null)
    {
        _analyze = analyze;
        _files = files ?? new ProjectFileStore();
        _dialogs = dialogs;
        _recovery = recovery;
        _recentStore = recentStore;
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
        NewCommand = new(() => NewAsync(), () => CanNewProject);
        OpenCommand = new(() => OpenAsync(), () => CanOpenProject);
        SaveCommand = new(() => SaveAsync(), () => CanSaveProject);
        SaveAsCommand = new(() => SaveAsync(saveAs: true), () => CanSaveProject);
        CloseProjectCommand = new(() => CloseProjectAsync(), () => CanCloseProject);
        UndoCommand = new(() => Undo(), () => CanUndo);
        RedoCommand = new(() => Redo(), () => CanRedo);
        UserManualCommand = new(async () => { if (openUserManual is not null) await openUserManual(); return true; }, () => true);
        StartPage = new(NewCommand, OpenCommand, UserManualCommand, OpenRecentAsync, () => CanOpenProject);
    }

    public MainViewMode Mode => _mode;
    public ProjectSetupViewModel Setup => _setup;
    public StartPageViewModel StartPage { get; }
    public EditorViewModel? Editor => _editor;
    public ResultPresentationOptions ResultPresentation => _resultPresentation;
    public UserSettings Settings => _settings;

    public async Task InitializeRecentProjectsAsync()
    {
        if (_recentStore is null) return;
        await _recentStore.LoadAsync();
        StartPage.SetRecentProjects(_recentStore.Entries);
    }

    private async Task RecordRecentAsync(string path, string? replacedPath = null)
    {
        if (_recentStore is null) return;
        await _recentStore.RecordUseAsync(path, replacedPath);
        StartPage.SetRecentProjects(_recentStore.Entries);
    }

    public void SetSettingsDialogOpen(bool open)
    {
        _settingsDialogOpen = open;
        if (Editor is { } editor) editor.IsSettingsDialogOpen = open;
    }
    internal bool PreserveSetupInputs => _mainMenuOpen || _settingsDialogOpen;

    public async Task<string?> ApplySettingsAsync(UserSettings settings)
    {
        if (_settingsSaving || IsBusy) return Strings.ProjectBusy;
        bool unitsChanged = !UserSettings.SameUnits(_settings.Profile, settings.Profile);
        if (unitsChanged && (Editor?.CanApplyInputUnits(settings.Profile) == false || !Setup.CanApplyInputUnits(settings.Profile)))
            return Strings.SettingsDraftBlocked;
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
        bool unitsChanged = !UserSettings.SameUnits(_settings.Profile, profile);
        if (_settingsSaving || IsBusy || unitsChanged && (Editor?.CanApplyInputUnits(profile) == false || !Setup.CanApplyInputUnits(profile))) return false;
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
    public bool IsWorkspaceBusy => IsBusy || Mode == MainViewMode.ProjectSetup && Setup.IsBusy;
    public MainWorkspaceKind Workspace => Mode == MainViewMode.Start ? MainWorkspaceKind.Start
        : Mode == MainViewMode.Editor ? MainWorkspaceKind.BeamEditor : Setup.Page switch
    {
        ProjectSetupPage.SectionDefinition => MainWorkspaceKind.SectionDefinition,
        ProjectSetupPage.MaterialSelection => Setup.CurrentStep is MaterialSelectionViewModel { IsEditing: true }
            ? MainWorkspaceKind.MaterialEditor : MainWorkspaceKind.MaterialSelection,
        _ => MainWorkspaceKind.ProjectSetup
    };
    public bool IsSectionWorkspace => Workspace == MainWorkspaceKind.SectionDefinition;
    public bool CanNewProject => !IsWorkspaceBusy && Workspace is MainWorkspaceKind.Start or MainWorkspaceKind.BeamEditor;
    public bool CanOpenProject => !IsWorkspaceBusy && (Workspace is MainWorkspaceKind.Start or MainWorkspaceKind.BeamEditor
        || Workspace == MainWorkspaceKind.ProjectSetup && Session is null);
    public bool CanSaveProject => !IsWorkspaceBusy && Workspace == MainWorkspaceKind.BeamEditor && Session is not null;
    public bool CanCloseProject => !IsWorkspaceBusy && Workspace == MainWorkspaceKind.BeamEditor && Session is not null;
    public bool CanUndo => !IsWorkspaceBusy && (Workspace switch
    {
        MainWorkspaceKind.BeamEditor => Session?.CanUndo == true,
        MainWorkspaceKind.SectionDefinition => Setup.Definition?.CanUndo == true,
        _ => false
    });
    public bool CanRedo => !IsWorkspaceBusy && (Workspace switch
    {
        MainWorkspaceKind.BeamEditor => Session?.CanRedo == true,
        MainWorkspaceKind.SectionDefinition => Setup.Definition?.CanRedo == true,
        _ => false
    });
    public string ProjectName => Session?.FilePath is { } path ? Path.GetFileName(path) : Strings.Untitled;
    public string WindowTitle => Mode == MainViewMode.Start ? Strings.ApplicationTitle
        : Mode == MainViewMode.ProjectSetup && Setup.Mode == ProjectSetupMode.Create
        ? Strings.NewProject + " — " + Strings.ApplicationTitle : Session is null ? Strings.ApplicationTitle
        : ProjectName + (Session.IsDirty ? "*" : "") + " — " + Strings.ApplicationTitle;
    public object CurrentViewModel => Mode switch
    {
        MainViewMode.Start => StartPage,
        MainViewMode.ProjectSetup => Setup,
        MainViewMode.Editor => Editor!,
        _ => throw new InvalidOperationException("Unknown workspace.")
    };
    public AsyncActionCommand NewCommand { get; }
    public AsyncActionCommand OpenCommand { get; }
    public AsyncActionCommand SaveCommand { get; }
    public AsyncActionCommand SaveAsCommand { get; }
    public AsyncActionCommand CloseProjectCommand { get; }
    public ActionCommand UndoCommand { get; }
    public ActionCommand RedoCommand { get; }
    public AsyncActionCommand UserManualCommand { get; }

    public void SetMainMenuOpen(bool open)
    {
        _mainMenuOpen = open;
        if (Editor is { } editor) editor.IsMainMenuOpen = open;
    }

    private ProjectSetupViewModel NewSetup() => new(ProjectSetupMode.Create,
        new SpanDraft.Core.Sections.Parametric.RectangularHollowSectionGeometry(
            Length.FromMillimeters(100), Length.FromMillimeters(100), Length.FromMillimeters(5), Length.FromMillimeters(0)),
        SpanDraft.Core.Sections.SectionAxisDesignation.Y, BuiltInMaterials?.Find("S235JR")?.Material,
        ApplySetupAsync, CancelSetup, ResultPresentation, this, canCancel: true);

    public void EditProject()
    {
        if (!CanSaveProject || Editor is null) return;
        Editor.CancelEditorInteraction();
        _setupReturnMode = MainViewMode.Editor;
        _setup = new(ProjectSetupMode.Edit, Editor.Document.Section, Editor.Document.BendingAxis, Editor.Document.Material,
            ApplySetupAsync, CancelSetup, ResultPresentation, this, Editor.Document);
        Navigate(MainViewMode.ProjectSetup);
    }

    private Task<bool> ApplySetupAsync(ProjectSetupViewModel setup)
    {
        if (Mode != MainViewMode.ProjectSetup || !ReferenceEquals(setup, Setup)
            || IsWorkspaceBusy || !setup.CanApply || setup.SelectedMaterial is not { } material)
            return Task.FromResult(false);
        if (setup.Mode == ProjectSetupMode.Edit)
        {
            if (Editor?.ApplySetup(setup.SelectedLength, setup.SelectedSection, setup.BendingAxis, material, setup.SectionDisplayName) != true)
                return Task.FromResult(false);
            Navigate(MainViewMode.Editor);
            return Task.FromResult(true);
        }
        var document = new EditorDocument(setup.SelectedLength, material, setup.SelectedSection, setup.BendingAxis,
            sectionDisplayName: setup.SectionDisplayName);
        return RunOperationAsync(async () =>
        {
            if (!await CanLeaveAsync()) return false;
            await CompleteLeaveAsync();
            Activate(ProjectSession.Create(new(document, new())));
            return true;
        });
    }

    private void CancelSetup()
    {
        if (!IsWorkspaceBusy && Mode == MainViewMode.ProjectSetup) Navigate(_setupReturnMode);
    }

    private void Activate(ProjectSession session)
    {
        DetachSession();
        _editor = EditorViewModel.ForSession(session, EditProject, _analyze, ResultPresentation);
        _editor.IsMainMenuOpen = _mainMenuOpen;
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

    public bool Undo() => CanUndo && (Workspace == MainWorkspaceKind.SectionDefinition
        ? Setup.Definition!.Undo() : Editor!.Undo());

    public bool Redo() => CanRedo && (Workspace == MainWorkspaceKind.SectionDefinition
        ? Setup.Definition!.Redo() : Editor!.Redo());

    public Task<bool> NewAsync() => !CanNewProject ? Task.FromResult(false) : RunOperationAsync(() =>
    {
        _setupReturnMode = Mode;
        _setup = NewSetup();
        Navigate(MainViewMode.ProjectSetup);
        return Task.FromResult(true);
    });

    public Task<bool> OpenAsync() => !CanOpenProject ? Task.FromResult(false) : RunOperationAsync(async () =>
    {
        string? path = _dialogs is null ? null : await _dialogs.PickOpenPathAsync();
        return path is not null && await OpenPathAsync(path);
    });

    public Task<bool> OpenRecentAsync(string path) => !CanOpenProject ? Task.FromResult(false)
        : RunOperationAsync(() => OpenPathAsync(path));

    private async Task<bool> OpenPathAsync(string path)
    {
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
        await RecordRecentAsync(Session!.FilePath!);
        return true;
    }

    public Task<bool> SaveAsync(bool saveAs = false) => !CanSaveProject ? Task.FromResult(false)
        : RunOperationAsync(() => SaveCurrentAsync(saveAs));

    public Task<bool> CloseProjectAsync() => !CanCloseProject ? Task.FromResult(false) : RunOperationAsync(async () =>
    {
        if (!await CanLeaveAsync(recordRecent: false)) return false;
        await CompleteLeaveAsync();
        _editor = null;
        Navigate(MainViewMode.Start);
        return true;
    });

    private async Task<bool> SaveCurrentAsync(bool saveAs = false, bool recordRecent = true)
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
        string? previousPath = session.FilePath;
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
        if (recordRecent) await RecordRecentAsync(path, saveAs ? previousPath : null);
        if (_recovery is not null)
        {
            try { await _recovery.DeleteAsync(); }
            catch (Exception e) when (IsFileError(e)) { await ShowErrorAsync(Strings.RecoveryError, e); }
        }
        return true;
    }

    public static string EnsureProjectExtension(string path) => path.EndsWith(".spandraft", StringComparison.OrdinalIgnoreCase)
        ? path : path + ".spandraft";

    private async Task<bool> CanLeaveAsync(bool recordRecent = true)
    {
        if (Session?.IsDirty != true) return true;
        var decision = _dialogs is null ? LeaveDecision.Cancel : await _dialogs.ConfirmLeaveAsync(ProjectName);
        return decision switch
        {
            LeaveDecision.Discard => true,
            LeaveDecision.Save => await SaveCurrentAsync(recordRecent: recordRecent),
            _ => false
        };
    }

    private async Task CompleteLeaveAsync()
    {
        if (_recovery is not null) await _recovery.DeleteAsync();
        Editor?.CancelEditorInteraction();
        DetachSession();
        Session?.SetBusy(false);
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
        if (IsWorkspaceBusy) return false;
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
        // Protect hidden editor buffers before changing the visible workspace.
        if (Editor is { } editor) editor.IsProjectSetupOpen = mode == MainViewMode.ProjectSetup;
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
        Notify(nameof(IsWorkspaceBusy)); Notify(nameof(Workspace)); Notify(nameof(IsSectionWorkspace));
        Notify(nameof(CanNewProject)); Notify(nameof(CanOpenProject)); Notify(nameof(CanSaveProject)); Notify(nameof(CanCloseProject));
        Notify(nameof(CanUndo));
        Notify(nameof(CanRedo));
        Notify(nameof(WindowTitle));
        NewCommand?.Refresh(); OpenCommand?.Refresh(); SaveCommand?.Refresh(); SaveAsCommand?.Refresh();
        CloseProjectCommand?.Refresh();
        UndoCommand?.Refresh(); RedoCommand?.Refresh();
        StartPage?.Refresh();
    }
    internal void RefreshWorkspaceState() => NotifySession();
    internal void RefreshHistoryState()
    {
        Notify(nameof(CanUndo)); Notify(nameof(CanRedo));
        UndoCommand?.Refresh(); RedoCommand?.Refresh();
    }
}
