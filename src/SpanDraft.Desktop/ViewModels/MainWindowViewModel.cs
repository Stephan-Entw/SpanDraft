using System.Text.Json;
using SpanDraft.Analysis;
using SpanDraft.Core.Beams;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Diagnostics;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.Services;
using SpanDraft.Desktop.Reporting;
using SpanDraft.Desktop.State;

namespace SpanDraft.Desktop.ViewModels;

public enum MainViewMode { ProjectSetup, Editor, Start, ReportPreview }
public enum MainWorkspaceKind { ProjectSetup, MaterialSelection, SectionDefinition, BeamEditor, Start, ReportPreview }

public sealed partial class MainWindowViewModel : ObservableObject, ISettingsParticipant, IDisposable
{
    private readonly Func<BeamModel, BeamAnalysisOutcome>? _analyze;
    private readonly InteractionPerformance? _performance;
    private readonly IProjectFileStore _files;
    private readonly IProjectIdentityResolver _projectIdentities;
    private readonly ProjectPathRegistry _projectPaths;
    private readonly ProjectOpenCoordinator _projectOpen;
    private readonly Action<WindowId>? _activateWorkspace;
    private readonly Func<WindowId, string?>? _ownerProjectPath;
    internal WindowId WindowId { get; }
    private readonly IProjectDialogs? _dialogs;
    private readonly ProjectRecovery? _recovery;
    private readonly ProjectSetupRecovery? _setupRecovery;
    private MainViewMode _mode = MainViewMode.Start;
    private MainViewMode _setupReturnMode = MainViewMode.Start;
    private readonly SharedServices _services;
    private readonly IDisposable _settingsRegistration;
    private bool _disposed;
    private ProjectSetupViewModel _setup;
    private EditorViewModel? _editor;
    private bool _busy;
    private TaskCompletionSource? _fileOperationCompletion;
    private bool _mainMenuOpen;
    private Exception? _pendingRecoveryError;
    private ResultPresentationOptions _resultPresentation = ResultPresentationOptions.Default;
    private readonly Func<string?>? _userNameProvider;
    private bool _settingsSaving;
    private bool _settingsDialogOpen;
    private ReportPreviewViewModel? _reportPreview;

    public MainWindowViewModel(Func<BeamModel, BeamAnalysisOutcome>? analyze = null,
        IProjectFileStore? files = null, IProjectDialogs? dialogs = null, ProjectRecovery? recovery = null,
        SharedServices? services = null, Func<Task>? openUserManual = null,
        ProjectSetupRecovery? setupRecovery = null, Func<string?>? userNameProvider = null,
        InteractionPerformance? performance = null, IProjectIdentityResolver? projectIdentities = null)
        : this(WindowId.Create(), new(), projectIdentities ?? new ProjectIdentityResolver(), analyze: analyze,
            files: files, dialogs: dialogs, recovery: recovery, services: services, openUserManual: openUserManual,
            setupRecovery: setupRecovery, userNameProvider: userNameProvider, performance: performance) { }

    internal MainWindowViewModel(WindowId windowId, ProjectPathRegistry projectPaths, IProjectIdentityResolver projectIdentities,
        ProjectOpenCoordinator? projectOpen = null, Action<WindowId>? activateWorkspace = null,
        Func<WindowId, string?>? ownerProjectPath = null,
        Func<BeamModel, BeamAnalysisOutcome>? analyze = null, IProjectFileStore? files = null,
        IProjectDialogs? dialogs = null, ProjectRecovery? recovery = null, SharedServices? services = null,
        Func<Task>? openUserManual = null, ProjectSetupRecovery? setupRecovery = null, Func<string?>? userNameProvider = null,
        InteractionPerformance? performance = null)
    {
        _analyze = analyze;
        _performance = performance;
        _files = files ?? new ProjectFileStore();
        WindowId = windowId;
        _projectIdentities = projectIdentities;
        _projectPaths = projectPaths;
        _projectOpen = projectOpen ?? new(_files, projectIdentities, projectPaths);
        _activateWorkspace = activateWorkspace;
        _ownerProjectPath = ownerProjectPath;
        _dialogs = dialogs;
        _recovery = recovery;
        _setupRecovery = setupRecovery;
        _userNameProvider = userNameProvider;
        // The services own all global state; this view model mirrors it for display and applies it to its workspace.
        _services = services ?? SharedServices.Transient();
        _resultPresentation = _services.Settings.CurrentSettings.Presentation;
        if (_recovery is not null) _recovery.Failed += RecoveryFailed;
        if (_setupRecovery is not null) _setupRecovery.Failed += RecoveryFailed;
        _setup = NewSetup();
        NewCommand = new(() => NewAsync(), () => CanNewProject);
        OpenCommand = new(() => OpenAsync(), () => CanOpenProject);
        SaveCommand = new(() => SaveAsync(), () => CanSaveProject);
        SaveAsCommand = new(() => SaveAsync(saveAs: true), () => CanSaveProject);
        ReportCommand = new(ReportAsync, () => CanReport);
        CloseProjectCommand = new(() => CloseProjectAsync(), () => CanCloseProject);
        UndoCommand = new(() => Undo(), () => CanUndo);
        RedoCommand = new(() => Redo(), () => CanRedo);
        UserManualCommand = new(async () => { if (openUserManual is not null) await openUserManual(); return true; }, () => true);
        StartPage = new(NewCommand, OpenCommand, UserManualCommand, OpenRecentAsync, () => CanOpenProject);
        StartPage.SetRecentProjects(_services.Recents.Entries);
        _settingsRegistration = _services.SettingsCoordinator.Register(this);
        _services.Settings.Changed += SettingsChanged;
        _services.Materials.Changed += LibraryChanged;
        _services.Sections.Changed += LibraryChanged;
        _services.Recents.Changed += RecentsChanged;
    }

    /// <summary>Ends the participation in the shared services; a closed workspace must not stay registered or subscribed.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _projectPaths.ReleaseOwner(WindowId);
        DetachSession();
        if (_recovery is not null) _recovery.Failed -= RecoveryFailed;
        if (_setupRecovery is not null) _setupRecovery.Failed -= RecoveryFailed;
        _settingsRegistration.Dispose();
        _services.Settings.Changed -= SettingsChanged;
        _services.Materials.Changed -= LibraryChanged;
        _services.Sections.Changed -= LibraryChanged;
        _services.Recents.Changed -= RecentsChanged;
    }

    private void SettingsChanged() => Notify(nameof(Settings));
    private void LibraryChanged(LibraryChangedEventArgs _) => RefreshWorkspaceState();
    private void RecentsChanged() => StartPage.SetRecentProjects(_services.Recents.Entries);

    public MainViewMode Mode => _mode;
    public ProjectSetupViewModel Setup => _setup;
    public StartPageViewModel StartPage { get; }
    public EditorViewModel? Editor => _editor;
    public ReportPreviewViewModel? ReportPreview => _reportPreview;
    public ResultPresentationOptions ResultPresentation => _resultPresentation;
    public UserSettings Settings => _services.Settings.CurrentSettings;
    public SharedServices Services => _services;

    private Task RecordRecentAsync(string path, string? replacedPath = null) => _services.Recents.RecordUseAsync(path, replacedPath);

    public void SetSettingsDialogOpen(bool open)
    {
        _settingsDialogOpen = open;
        if (Editor is { } editor) editor.IsSettingsDialogOpen = open;
    }
    internal bool PreserveSetupInputs => _mainMenuOpen || _settingsDialogOpen;

    /// <summary>The settings dialog's apply: the coordinator owns the transaction, this only translates its outcome.</summary>
    public async Task<string?> ApplySettingsAsync(SettingsPatch patch)
    {
        var result = await _services.SettingsCoordinator.ApplyAsync(patch);
        return result.Outcome switch
        {
            SettingsApplyOutcome.Applied => null,
            SettingsApplyOutcome.DraftBlocked => Strings.SettingsDraftBlocked,
            SettingsApplyOutcome.PersistFailed => Strings.SettingsSaveError + "\n" + result.Failure!.Message,
            _ => Strings.ProjectBusy
        };
    }

    // The freeze is a busy state like any other operation: every command, input session and library action is gated by
    // IsBusy / IsWorkspaceBusy, so nothing new can start between the freeze and its release.
    bool ISettingsParticipant.TryFreezeForSettings()
    {
        if (_settingsSaving || HasRunningOperation) return false;
        _settingsSaving = true;
        Session?.SetFrozen(true);
        NotifySession();
        return true;
    }

    // The units are checked against the drafts as they are right now, not as they were when the dialog was opened.
    SettingsCheck ISettingsParticipant.ValidateSettings(UserSettings candidate) =>
        !UserSettings.SameUnits(Settings.Profile, candidate.Profile)
            && (Editor?.CanApplyInputUnits(candidate.Profile) == false || !Setup.CanApplyInputUnits(candidate.Profile))
            ? SettingsCheck.DraftBlocked : SettingsCheck.Ok;

    void ISettingsParticipant.ApplySettings(UserSettings committed)
    {
        SetPresentation(committed.Presentation);
        Notify(nameof(Settings));
    }

    void ISettingsParticipant.ReleaseSettingsFreeze()
    {
        if (!_settingsSaving) return;
        _settingsSaving = false;
        Session?.SetFrozen(false);
        NotifySession();
        if (_pendingRecoveryError is { } error) { _pendingRecoveryError = null; RecoveryFailed(error); }
    }

    /// <summary>Changes presentation only; affected open input sessions reject unit changes.</summary>
    public bool SetResultPresentation(UnitProfile profile, int significantDigits = ResultPresentationOptions.DefaultSignificantDigits,
        EngineeringNotation notation = EngineeringNotation.Automatic)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var options = new ResultPresentationOptions(profile, significantDigits, notation);
        bool unitsChanged = !UserSettings.SameUnits(Settings.Profile, profile);
        if (_settingsSaving || IsBusy || unitsChanged && (Editor?.CanApplyInputUnits(profile) == false || !Setup.CanApplyInputUnits(profile))) return false;
        profile = UserSettings.Recognize(profile);
        options = new(profile, significantDigits, notation);
        _services.Settings.ApplyRuntimePresentation(profile, significantDigits, notation);
        SetPresentation(options);
        return true;
    }

    private void SetPresentation(ResultPresentationOptions options)
    {
        if (_resultPresentation.SignificantDigits == options.SignificantDigits && _resultPresentation.Notation == options.Notation && UserSettings.SameUnits(_resultPresentation.Profile, options.Profile)) return;
        _resultPresentation = options;
        Setup.ApplyResultPresentation(options);
        Editor?.ApplyResultPresentation(options);
        Notify(nameof(ResultPresentation));
    }
    public ProjectSession? Session => Editor?.Session;
    internal bool HasProject => Session is not null;

    // Workspace ownership hooks; the WorkspaceWindowManager wires them, the view model never knows other workspaces.
    internal event Action? SessionActivated;
    internal event Action? SetupCancelled;
    /// <summary>Raised while the project is closed, before the start page is shown; the owner decides start page or close (§14).</summary>
    internal Action? ProjectClosing { get; set; }
    public bool IsBusy => _busy || _libraryBusy || _settingsSaving || ReportPreview?.IsBusy == true;
    /// <summary>An operation is running anywhere in this workspace (an open editor alone is not one).</summary>
    private bool HasRunningOperation => IsBusy || Mode == MainViewMode.ProjectSetup
        && (Setup.IsBusy || Setup.CurrentStep is MaterialSelectionViewModel { IsActionBusy: true });
    public bool IsWorkspaceBusy => HasRunningOperation
        || Mode == MainViewMode.ProjectSetup && Setup.CurrentStep is MaterialSelectionViewModel { IsEditing: true };
    public MainWorkspaceKind Workspace => Mode == MainViewMode.ReportPreview ? MainWorkspaceKind.ReportPreview
        : Mode == MainViewMode.Start ? MainWorkspaceKind.Start
        : Mode == MainViewMode.Editor ? MainWorkspaceKind.BeamEditor : Setup.Page switch
    {
        ProjectSetupPage.SectionDefinition => MainWorkspaceKind.SectionDefinition,
        ProjectSetupPage.MaterialSelection => MainWorkspaceKind.MaterialSelection,
        _ => MainWorkspaceKind.ProjectSetup
    };
    public bool IsSectionWorkspace => Workspace == MainWorkspaceKind.SectionDefinition;
    public bool CanNewProject => !IsWorkspaceBusy && Workspace is MainWorkspaceKind.Start or MainWorkspaceKind.BeamEditor;
    public bool CanOpenProject => !IsWorkspaceBusy && (Workspace is MainWorkspaceKind.Start or MainWorkspaceKind.BeamEditor
        || Workspace == MainWorkspaceKind.ProjectSetup && Session is null);
    public bool CanSaveProject => !IsWorkspaceBusy && Workspace == MainWorkspaceKind.BeamEditor && Session is not null;
    public bool CanCloseProject => !IsWorkspaceBusy && Workspace == MainWorkspaceKind.BeamEditor && Session is not null;
    public bool CanReport => !IsWorkspaceBusy && Workspace == MainWorkspaceKind.BeamEditor && Editor?.Presentation.IsSuccess == true;
    public Task<bool> ReportAsync() => !CanReport ? Task.FromResult(false) : RunOperationAsync(async () =>
    {
        var editor = Editor!;
        if (editor.Document.Segments.Any(segment => segment.Material.StrengthType is null))
        {
            if (_dialogs is not null) await _dialogs.ShowErrorAsync(Strings.MaterialStrengthResolve);
            return false;
        }
        editor.IsReportOpen = true;
        try
        {
            var report = ProjectReport.Capture(editor);
            try { _reportPreview = new(report, Session!.FilePath, _files, () => CloseReport()); }
            catch { report.Dispose(); throw; }
            _reportPreview.PropertyChanged += ReportStateChanged;
            Notify(nameof(ReportPreview)); Navigate(MainViewMode.ReportPreview);
            return true;
        }
        catch (Exception error) when (error is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            editor.IsReportOpen = false;
            await ShowErrorAsync(Strings.ReportError, error);
            return false;
        }
    });

    public bool CloseReport()
    {
        if (Workspace != MainWorkspaceKind.ReportPreview || IsWorkspaceBusy) return false;
        ReleaseReport(); return true;
    }

    internal void ReleaseReport()
    {
        if (_reportPreview is null) return;
        _reportPreview.PropertyChanged -= ReportStateChanged;
        _reportPreview.Dispose(); _reportPreview = null;
        Notify(nameof(ReportPreview)); Navigate(MainViewMode.Editor);
    }

    private void ReportStateChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ReportPreviewViewModel.IsBusy)) return;
        Session?.SetBusy(IsBusy); NotifySession();
    }
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
    public string ProjectFileName => Session?.FilePath is { } path ? Path.GetFileName(path) : Strings.Untitled;
    public string WindowTitle => Mode == MainViewMode.Start ? Strings.ApplicationTitle
        : Mode == MainViewMode.ProjectSetup && Setup.Mode == ProjectSetupMode.Create
        ? Strings.NewProject + " — " + Strings.ApplicationTitle : Session is null ? Strings.ApplicationTitle
        : ProjectFileName + (Session.IsDirty ? "*" : "") + " — " + Strings.ApplicationTitle;
    public object CurrentViewModel => Mode switch
    {
        MainViewMode.Start => StartPage,
        MainViewMode.ProjectSetup => Setup,
        MainViewMode.Editor => Editor!,
        MainViewMode.ReportPreview => ReportPreview!,
        _ => throw new InvalidOperationException("Unknown workspace.")
    };
    public AsyncActionCommand NewCommand { get; }
    public AsyncActionCommand OpenCommand { get; }
    public AsyncActionCommand SaveCommand { get; }
    public AsyncActionCommand SaveAsCommand { get; }
    public AsyncActionCommand ReportCommand { get; }
    public AsyncActionCommand CloseProjectCommand { get; }
    public ActionCommand UndoCommand { get; }
    public ActionCommand RedoCommand { get; }
    public AsyncActionCommand UserManualCommand { get; }

    public void SetMainMenuOpen(bool open)
    {
        _mainMenuOpen = open;
        if (Editor is { } editor) editor.IsMainMenuOpen = open;
    }

    private ProjectSetupViewModel NewSetup(ProjectSetupDraft? draft = null)
    {
        var setup = new ProjectSetupViewModel(ProjectSetupMode.Create,
            draft?.Section, draft?.BendingAxis ?? SpanDraft.Core.Sections.SectionAxisDesignation.Y, draft?.Material,
            ApplySetupAsync, CancelSetup, ResultPresentation, this, canCancel: true, recoveredDraft: draft,
            suggestedAuthor: draft is null ? SuggestAuthor() : null);
        setup.ConfirmedDraftChanged += SetupDraftChanged;
        return setup;
    }

    private string? SuggestAuthor()
    {
        if (Settings.LastAuthor is { } author) return author;
        try { return _userNameProvider?.Invoke(); }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception
            or System.Security.SecurityException or UnauthorizedAccessException)
        { return null; }
    }

    private void SetupDraftChanged(ProjectSetupViewModel setup)
    {
        if (Mode == MainViewMode.ProjectSetup && setup.Mode == ProjectSetupMode.Create && ReferenceEquals(setup, Setup))
            _setupRecovery?.Schedule(setup.Draft);
    }

    private async Task DeleteSetupRecoveryAsync()
    {
        Setup.ConfirmedDraftChanged -= SetupDraftChanged;
        if (_setupRecovery is null) return;
        try { await _setupRecovery.DeleteAsync(); }
        catch (Exception e) when (IsFileError(e)) { await ShowErrorAsync(Strings.RecoveryError, e); }
    }

    public void EditProject()
    {
        if (!CanSaveProject || Editor is null) return;
        Editor.CancelEditorInteraction();
        _setupReturnMode = MainViewMode.Editor;
        var first = Editor.Document.Segments[0];
        _setup = new(ProjectSetupMode.Edit, first.Section, first.BendingAxis, first.Material,
            ApplySetupAsync, CancelSetup, ResultPresentation, this, Editor.Document, originalInfo: Session!.CurrentRevision.State.Info, performance: _performance);
        Navigate(MainViewMode.ProjectSetup);
    }

    private Task<bool> ApplySetupAsync(ProjectSetupViewModel setup)
    {
        if (Mode != MainViewMode.ProjectSetup || !ReferenceEquals(setup, Setup)
            || IsWorkspaceBusy || !setup.CanApply)
            return Task.FromResult(false);
        if (setup.Mode == ProjectSetupMode.Edit)
        {
            if (Editor?.ApplySetup(setup.BuildSegments(), setup.Info) != true)
                return Task.FromResult(false);
            Navigate(MainViewMode.Editor);
            return Task.FromResult(true);
        }
        var document = new EditorDocument(setup.BuildSegments());
        return RunOperationAsync(async () =>
        {
            if (!await CanLeaveAsync()) return false;
            await PrepareLeaveAsync();
            ActivatePathlessSession(ProjectSession.Create(new(document, new(), setup.Info)));
            // The author is current at once; remembering it on disk is best effort and never replaces damaged settings.
            await _services.Settings.UpdateLastAuthorAsync(Session!.CurrentRevision.State.Info!.Author);
            await DeleteSetupRecoveryAsync();
            return true;
        });
    }

    // An async void handler has no caller to catch for it: whatever fails is shown instead of ending the application.
    private async void CancelSetup()
    {
        try { await CancelSetupAsync(); }
        catch (Exception e) { await ShowErrorAsync(Strings.ProjectFileError, e); }
    }

    public Task<bool> CancelSetupAsync() => Mode != MainViewMode.ProjectSetup ? Task.FromResult(false) : RunOperationAsync(async () =>
    {
        bool create = Setup.Mode == ProjectSetupMode.Create;
        Navigate(_setupReturnMode);
        if (create) await DeleteSetupRecoveryAsync();
        if (create && Session is null) SetupCancelled?.Invoke();
        return true;
    });

    private EditorViewModel CreateEditor(ProjectSession session) =>
        EditorViewModel.ForSession(session, EditProject, _analyze, ResultPresentation, _performance);

    internal void ActivatePathlessSession(ProjectSession session)
    {
        if (session.FilePath is not null) throw new ArgumentException("A file-backed session needs registry ownership.", nameof(session));
        var editor = CreateEditor(session);
        _projectPaths.ReleaseOwner(WindowId);
        AttachEditor(editor);
        if (session.IsDirty) _recovery?.Schedule(session);
    }

    private void AttachEditor(EditorViewModel editor)
    {
        DetachSession();
        _editor = editor;
        var session = editor.Session;
        _editor.IsMainMenuOpen = _mainMenuOpen;
        _editor.PropertyChanged += EditorReportStateChanged;
        session.SetBusy(IsBusy);
        session.Changed += SessionChanged;
        session.StateApplied += CommittedStateChanged;
        Navigate(MainViewMode.Editor);
        SessionActivated?.Invoke();
    }

    private void DetachSession()
    {
        _performance?.CancelPending();
        if (Session is not { } session) return;
        session.Changed -= SessionChanged;
        session.StateApplied -= CommittedStateChanged;
        Editor!.PropertyChanged -= EditorReportStateChanged;
        session.SetBusy(false);
    }

    private void EditorReportStateChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(EditorViewModel.Presentation)) return;
        Notify(nameof(CanReport)); ReportCommand.Refresh();
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

    public async Task<bool> OpenAsync()
    {
        if (!CanOpenProject || _dialogs is null) return false;
        string? path = null;
        await RunOperationAsync(async () => (path = await _dialogs.PickOpenPathAsync()) is not null);
        return path is not null && await OpenPathAsync(path);
    }

    public Task<bool> OpenAsync(string path) => OpenPathAsync(path);
    public Task<bool> OpenRecentAsync(string path) => OpenPathAsync(path, fromRecent: true);

    private async Task<bool> OpenPathAsync(string path, bool fromRecent = false)
    {
        if (_disposed || !CanOpenProject && _fileOperationCompletion is null) return false;
        try
        {
            var result = await _projectOpen.OpenAsync(path, new SingleWindowOpen(this, path, fromRecent));
            if (result.Status == ProjectOpenStatus.AlreadyOpen && CanOpenProject)
            {
                // The project belongs to the owning workspace; this request records the canonical target, not its own session.
                var target = result.Owner is { } owner ? _ownerProjectPath?.Invoke(owner) : null;
                target ??= _projectIdentities.Resolve(Path.GetFullPath(path)).CanonicalPath;
                await RunOperationAsync(async () => { await RecordRecentAsync(target); return true; });
            }
            return result.Status is ProjectOpenStatus.Opened or ProjectOpenStatus.AlreadyOpen;
        }
        catch (Exception e) when (IsFileError(e))
        {
            var outcome = e is DirectoryNotFoundException or FileNotFoundException ? new(ProjectOpenStatus.Missing)
                : new ProjectOpenResult(ProjectOpenStatus.Failed, Error: e);
            return await RunOperationAsync(() => PresentOpenResultAsync(outcome, path, fromRecent));
        }
    }

    private async Task<bool> ActivateOpenedProjectAsync(ProjectState state, string path)
    {
        if (!await CanLeaveAsync()) return false;
        var editor = CreateEditor(ProjectSession.Open(state, path));
        await PrepareLeaveAsync();
        AttachEditor(editor);
        return true;
    }

    private async Task<bool> PresentOpenResultAsync(ProjectOpenResult result, string path, bool fromRecent)
    {
        if (result.Status == ProjectOpenStatus.Missing)
        {
            if (!fromRecent) throw new IOException(Strings.ProjectFileMissing);
            if (_dialogs is not null && await _dialogs.ConfirmRemoveMissingRecentAsync(Path.GetFullPath(path)))
                await _services.Recents.RemoveAsync(path);
        }
        if (result.Error is { } error) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(error);
        if (result.Status != ProjectOpenStatus.Opened) return false;
        await DeleteSetupRecoveryAsync();
        await RecordRecentAsync(Session!.FilePath!);
        return true;
    }

    private sealed class SingleWindowOpen(MainWindowViewModel model, string path, bool fromRecent) : IProjectOpenWorkspace
    {
        public WindowId WindowId => model.WindowId;

        public async Task<ProjectOpenResult> RunOpenAsync(Func<Task<ProjectOpenResult>> transaction, bool afterSave)
        {
            if (afterSave && model._fileOperationCompletion is { } fileOperation) await fileOperation.Task;
            if (!model.CanOpenProject || model._disposed) return ProjectOpenResult.Cancelled;
            var outcome = ProjectOpenResult.Cancelled;
            await model.RunOperationAsync(async () =>
            {
                outcome = await transaction();
                return await model.PresentOpenResultAsync(outcome, path, fromRecent);
            }, fileTransaction: true);
            return outcome;
        }

        public Task<bool> ActivateProjectAsync(ProjectState state, string canonicalPath) =>
            model.ActivateOpenedProjectAsync(state, canonicalPath);

        public void ActivateExisting(WindowId owner)
        {
            model._activateWorkspace?.Invoke(owner);
        }

        // Legacy recovery is decided before startup becomes Ready; Phase 6 supplies this surface.
        public void ActivateRecovery(Guid candidate) { }
    }

    public Task<bool> SaveAsync(bool saveAs = false) => !CanSaveProject ? Task.FromResult(false)
        : RunOperationAsync(() => SaveCurrentAsync(saveAs), fileTransaction: true);

    public Task<bool> CloseProjectAsync() => !CanCloseProject ? Task.FromResult(false) : RunOperationAsync(async () =>
    {
        if (!await CanLeaveAsync(recordRecent: false)) return false;
        await PrepareLeaveAsync();
        DetachSession();
        _projectPaths.ReleaseOwner(WindowId);
        _editor = null;
        // Without another start workspace this one becomes the start page; otherwise its owner closes the window (§14).
        // The model shows the start page either way, so its state stays consistent until the window is gone.
        ProjectClosing?.Invoke();
        Navigate(MainViewMode.Start);
        return true;
    });

    private async Task<bool> SaveCurrentAsync(bool saveAs = false, bool recordRecent = true)
    {
        if (Session is not { } session) return false;
        if (session.CurrentRevision.State.Document.Segments.Any(segment => segment.Material.StrengthType is null))
        {
            if (_dialogs is not null) await _dialogs.ShowErrorAsync(Strings.MaterialStrengthResolve);
            return false;
        }
        string? path = saveAs ? null : session.FilePath;
        if (path is null)
        {
            path = _dialogs is null ? null : await _dialogs.PickSavePathAsync(session.FilePath, session.CurrentRevision.State.Info?.CalculationTitle);
            if (path is null) return false;
            path = EnsureProjectExtension(path);
        }
        string? previousPath = session.FilePath;
        var identity = !saveAs && previousPath is not null && _projectPaths.IdentityOwnedBy(WindowId) is { } owned
            ? owned : _projectIdentities.Resolve(path);
        var decision = _projectPaths.ArbitrateSave(identity, WindowId, out var reservation);
        if (decision == SaveTargetDecision.Rejected) throw new IOException(Strings.ProjectFileInUse);

        path = decision == SaveTargetDecision.SaveCurrent ? previousPath! : identity.CanonicalPath;
        try
        {
            var revision = session.CurrentRevision;
            var save = session.PrepareSave(revision, path);
            bool recoveryStarted = false;
            try
            {
                var bytes = ProjectFileCodec.Serialize(revision.State);
                if (_recovery is not null && (session.IsDirty || reservation is not null))
                {
                    recoveryStarted = true;
                    await _recovery.FlushAsync(revision.State, path);
                }
                await _files.WriteAtomicAsync(path, bytes);
            }
            catch
            {
                if (recoveryStarted)
                {
                    try
                    {
                        if (session.IsDirty) await _recovery!.FlushAsync(revision.State, previousPath);
                        else await _recovery!.DeleteAsync();
                    }
                    catch (Exception e) when (IsFileError(e)) { _recovery!.Schedule(session); }
                }
                throw;
            }

            if (reservation is null) save.MarkSaved();
            else if (!_projectPaths.TryCommitSave(reservation, save))
                throw new InvalidOperationException("The save reservation lost ownership after the write.");
            session.NotifySaved();
        }
        finally
        {
            if (reservation is not null) _projectPaths.TryReleaseSave(reservation);
        }
        if (recordRecent) await RecordRecentAsync(path, saveAs && reservation is not null ? previousPath : null);
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
        var decision = _dialogs is null ? LeaveDecision.Cancel : await _dialogs.ConfirmLeaveAsync(ProjectFileName);
        return decision switch
        {
            LeaveDecision.Discard => true,
            LeaveDecision.Save => await SaveCurrentAsync(recordRecent: recordRecent),
            _ => false
        };
    }

    private async Task PrepareLeaveAsync()
    {
        if (_recovery is not null) await _recovery.DeleteAsync();
        Editor?.CancelEditorInteraction();
    }

    /// <summary>True only once the shared leave workflow has succeeded. The Window handles its one approved Close.</summary>
    public Task<bool> RequestCloseAsync() => RunOperationAsync(async () =>
    {
        if (!await CanLeaveAsync()) return false;
        ReleaseReport();
        await PrepareLeaveAsync();
        await DeleteSetupRecoveryAsync();
        return true;
    });

    public Task<bool> InitializeRecoveryAsync() => RunOperationAsync(async () =>
        await InitializeProjectRecoveryAsync() && await InitializeSetupRecoveryAsync());

    private async Task<bool> InitializeProjectRecoveryAsync()
    {
        if (_recovery is null || Session is not null) return true;
        RecoverySnapshot? snapshot;
        try { snapshot = await _recovery.ReadAsync(); }
        catch (Exception e) when (IsFileError(e))
        {
            await ShowErrorAsync(Strings.RecoveryDamaged, e);
            if (_dialogs is null || await _dialogs.ConfirmRecoveryAsync(true, null) != RecoveryDecision.Discard)
                return false;
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
        if (choice == RecoveryDecision.Restore) RestoreLegacyProject(snapshot);
        else if (choice == RecoveryDecision.Discard) await _recovery.DeleteAsync();
        else return false;
        return true;
    }

    private void RestoreLegacyProject(RecoverySnapshot snapshot)
    {
        // An original that cannot be identified must not turn into a pathless session: its scheduled recovery write
        // would replace the slot without OriginalFilePath. The file error fails the restore and leaves the slot intact.
        var identity = snapshot.OriginalFilePath is { } path ? _projectIdentities.Resolve(path) : null;
        if (identity is null)
        {
            ActivatePathlessSession(ProjectSession.Restore(snapshot.State, null));
            return;
        }

        var token = _projectPaths.ArbitrateOpen(identity).Reservation ?? throw new IOException(Strings.ProjectFileInUse);
        try
        {
            var editor = CreateEditor(ProjectSession.Restore(snapshot.State, identity.CanonicalPath));
            AttachEditor(editor);
            if (!_projectPaths.TryCommitOpen(token, WindowId))
                throw new InvalidOperationException("The recovery open reservation lost ownership during activation.");
            _recovery?.Schedule(editor.Session);
        }
        finally { _projectPaths.TryReleaseOpen(token, ProjectOpenResult.Cancelled); }
    }

    private async Task<bool> InitializeSetupRecoveryAsync()
    {
        if (_setupRecovery is null) return true;
        SetupRecoverySnapshot? snapshot;
        try { snapshot = await _setupRecovery.ReadAsync(); }
        catch (Exception e) when (IsFileError(e))
        {
            await ShowErrorAsync(Strings.RecoveryDamaged, e);
            if (_dialogs is null || await _dialogs.ConfirmSetupRecoveryAsync(true, null) != RecoveryDecision.Discard)
                return false;
            await _setupRecovery.DeleteAsync();
            return true;
        }
        if (snapshot is null) return true;
        var choice = _dialogs is null ? RecoveryDecision.Cancel
            : await _dialogs.ConfirmSetupRecoveryAsync(false, snapshot.WrittenAtUtc);
        if (choice == RecoveryDecision.Restore)
        {
            _setupReturnMode = Session is null ? MainViewMode.Start : MainViewMode.Editor;
            _setup = NewSetup(snapshot.Draft);
            Navigate(MainViewMode.ProjectSetup);
        }
        else if (choice == RecoveryDecision.Discard) await _setupRecovery.DeleteAsync();
        else return false;
        return true;
    }

    private async Task<bool> RunOperationAsync(Func<Task<bool>> operation, bool fileTransaction = false)
    {
        if (IsWorkspaceBusy) return false;
        var completion = fileTransaction ? new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) : null;
        _fileOperationCompletion = completion;
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
            try
            {
                _busy = false;
                _fileOperationCompletion = null;
                Session?.SetBusy(false);
                NotifySession();
                if (_pendingRecoveryError is { } recoveryError)
                {
                    _pendingRecoveryError = null;
                    await ReportRecoveryErrorAsync(recoveryError);
                }
            }
            finally { completion?.SetResult(); }
        }
    }

    private static bool IsFileError(Exception e) => e is IOException or UnauthorizedAccessException
        or ProjectFormatException or JsonException or ArgumentException or NotSupportedException or System.Security.SecurityException;
    private Task ShowErrorAsync(string prefix, Exception e) => _dialogs?.ShowErrorAsync(prefix + "\n" + e.Message) ?? Task.CompletedTask;
    private async void RecoveryFailed(Exception error)
    {
        if (IsBusy) { _pendingRecoveryError = error; return; }
        try { await ReportRecoveryErrorAsync(error); }
        catch (Exception e) { await ShowErrorAsync(Strings.RecoveryError, e); }
    }

    private Task<bool> ReportRecoveryErrorAsync(Exception error) => RunOperationAsync(async () =>
    {
        await ShowErrorAsync(Strings.RecoveryError, error);
        return true;
    });

    private void Navigate(MainViewMode mode)
    {
        if (_mode != mode) _performance?.CancelPending();
        // Protect hidden editor buffers before changing the visible workspace.
        if (Editor is { } editor)
        {
            if (mode != MainViewMode.Editor) editor.CancelResultProbe();
            editor.IsProjectSetupOpen = mode == MainViewMode.ProjectSetup;
            editor.IsReportOpen = mode == MainViewMode.ReportPreview;
        }
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
        Notify(nameof(CanReport)); ReportCommand?.Refresh();
        Notify(nameof(CanRedo));
        Notify(nameof(ProjectFileName));
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
