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

public enum MainViewMode { ProjectSetup, Editor, Start, ReportPreview, RecoveryCenter }
public enum MainWorkspaceKind { ProjectSetup, MaterialSelection, SectionDefinition, BeamEditor, Start, ReportPreview, RecoveryCenter }

public sealed partial class MainWindowViewModel : ObservableObject, ISettingsParticipant, IDisposable
{
    private readonly Func<BeamModel, BeamAnalysisOutcome>? _analyze;
    private readonly InteractionPerformance? _performance;
    private readonly IProjectFileStore _files;
    private readonly IProjectIdentityResolver _projectIdentities;
    private readonly ProjectPathRegistry _projectPaths;
    private readonly ProjectOpenCoordinator _projectOpen;
    private readonly Action<WindowId>? _activateWorkspace;
    internal WindowId WindowId { get; }
    private readonly IProjectDialogs? _dialogs;
    private readonly WorkspaceRecoveryContext? _recovery;
    private readonly RecoveryCatalog? _recoveryCatalog;
    private readonly Func<Guid?, Task<bool>>? _showRecoveryCenter;
    private readonly Func<Guid, Task<bool>>? _restoreRecovery;
    private RecoveryCenterViewModel? _recoveryCenter;
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
        IProjectFileStore? files = null, IProjectDialogs? dialogs = null, WorkspaceRecoveryContext? recovery = null,
        SharedServices? services = null, Func<Task>? openUserManual = null,
        Func<string?>? userNameProvider = null,
        InteractionPerformance? performance = null, IProjectIdentityResolver? projectIdentities = null)
        : this(WindowId.Create(), new(), projectIdentities ?? new ProjectIdentityResolver(), analyze: analyze,
            files: files, dialogs: dialogs, recovery: recovery, services: services, openUserManual: openUserManual,
            userNameProvider: userNameProvider, performance: performance) { }

    internal MainWindowViewModel(WindowId windowId, ProjectPathRegistry projectPaths, IProjectIdentityResolver projectIdentities,
        ProjectOpenCoordinator? projectOpen = null, Action<WindowId>? activateWorkspace = null,
        Func<BeamModel, BeamAnalysisOutcome>? analyze = null, IProjectFileStore? files = null,
        IProjectDialogs? dialogs = null, WorkspaceRecoveryContext? recovery = null, SharedServices? services = null,
        Func<Task>? openUserManual = null, Func<string?>? userNameProvider = null,
        InteractionPerformance? performance = null, RecoveryCatalog? recoveryCatalog = null,
        Func<Guid?, Task<bool>>? showRecoveryCenter = null, Func<Guid, Task<bool>>? restoreRecovery = null,
        SettingsCoordinator.RegistrationReservation? settingsRegistration = null)
    {
        _analyze = analyze;
        _performance = performance;
        _files = files ?? new ProjectFileStore();
        WindowId = windowId;
        _projectIdentities = projectIdentities;
        _projectPaths = projectPaths;
        _projectOpen = projectOpen ?? new(_files, projectIdentities, projectPaths);
        _activateWorkspace = activateWorkspace;
        _dialogs = dialogs;
        _recovery = recovery;
        _recoveryCatalog = recoveryCatalog;
        _showRecoveryCenter = showRecoveryCenter;
        _restoreRecovery = restoreRecovery;
        _userNameProvider = userNameProvider;
        // The services own all global state; this view model mirrors it for display and applies it to its workspace.
        _services = services ?? SharedServices.Transient();
        _resultPresentation = _services.Settings.CurrentSettings.Presentation;
        if (_recovery is not null)
        {
            _recovery.Failed += RecoveryFailed;
            _recovery.FaultChanged += RecoveryFaultChanged;
        }
        _setup = NewSetup();
        NewCommand = new(() => NewProjectHandler?.Invoke() ?? NewAsync(), () => CanNewProject && CanRequestProject);
        OpenCommand = new(() => OpenFromPickerAsync(RequestOpenPathAsync), () => CanOpenProject && CanRequestProject);
        SaveCommand = new(() => SaveAsync(), () => CanSaveProject);
        SaveAsCommand = new(() => SaveAsync(saveAs: true), () => CanSaveProject);
        ReportCommand = new(ReportAsync, () => CanReport);
        CloseProjectCommand = new(() => CloseProjectAsync(), () => CanCloseProject);
        UndoCommand = new(() => Undo(), () => CanUndo);
        RedoCommand = new(() => Redo(), () => CanRedo);
        UserManualCommand = new(async () => { if (openUserManual is not null) await openUserManual(); return true; }, () => true);
        ShowRecoveryCommand = new(() => _showRecoveryCenter?.Invoke(null) ?? Task.FromResult(false), () => CanShowRecovery);
        RestoreRecoveryProtectionCommand = new(RestoreRecoveryProtectionAsync, () => CanRestoreRecoveryProtection);
        StartPage = new(NewCommand, OpenCommand, UserManualCommand, path => RequestOpenPathAsync(path, fromRecent: true),
            () => CanOpenProject && CanRequestProject, ShowRecoveryCommand);
        StartPage.SetRecentProjects(_services.Recents.Entries);
        _settingsRegistration = settingsRegistration?.Register(this) ?? _services.SettingsCoordinator.Register(this);
        _services.Settings.Changed += SettingsChanged;
        _services.Materials.Changed += LibraryChanged;
        _services.Sections.Changed += LibraryChanged;
        _services.Recents.Changed += RecentsChanged;
        if (_recoveryCatalog is not null) _recoveryCatalog.Changed += RecoveryCatalogChanged;
        StartPage.SetRecoveryCount(RecoveryCount);
    }

    /// <summary>The workspace was closed and cleaned up; late continuations of its operations decide nothing any more.</summary>
    internal bool IsDisposed => _disposed;

    /// <summary>Ends the participation in the shared services; a closed workspace must not stay registered or subscribed.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _projectPaths.ReleaseOwner(WindowId);
        DetachSession();
        if (_recovery is not null)
        {
            _recovery.Failed -= RecoveryFailed;
            _recovery.FaultChanged -= RecoveryFaultChanged;
        }
        _settingsRegistration.Dispose();
        _services.Settings.Changed -= SettingsChanged;
        _services.Materials.Changed -= LibraryChanged;
        _services.Sections.Changed -= LibraryChanged;
        _services.Recents.Changed -= RecentsChanged;
        if (_recoveryCatalog is not null) _recoveryCatalog.Changed -= RecoveryCatalogChanged;
        if (_recoveryCenter is { } center)
        {
            center.PropertyChanged -= RecoveryCenterChanged;
            center.Dispose();
        }
    }

    private void SettingsChanged() => Notify(nameof(Settings));
    private void LibraryChanged(LibraryChangedEventArgs _) => RefreshWorkspaceState();
    private void RecentsChanged() => StartPage.SetRecentProjects(_services.Recents.Entries);

    private void RecoveryCatalogChanged()
    {
        if (_disposed) return;
        StartPage.SetRecoveryCount(RecoveryCount);
        Notify(nameof(HasRecovery)); Notify(nameof(CanShowRecovery));
        ShowRecoveryCommand.Refresh();
    }

    // ---- Recovery Center ---------------------------------------------------------------------------------------

    /// <summary>Undecided recovery candidates; the permanent entry points are visible as long as there are any.</summary>
    public int RecoveryCount => _recoveryCatalog?.Undecided.Count ?? 0;
    public bool HasRecovery => RecoveryCount > 0;
    /// <summary>Reachable from every normal main window, also with an open editor or a setup in progress.</summary>
    public bool CanShowRecovery => !IsWorkspaceBusy && HasRecovery && Mode != MainViewMode.RecoveryCenter;
    public AsyncActionCommand ShowRecoveryCommand { get; }
    public RecoveryCenterViewModel? RecoveryCenter => _recoveryCenter;
    /// <summary>Raised after this workspace stopped showing the Recovery Center (its owner decides about the window).</summary>
    internal event Action? RecoveryCenterLeft;

    /// <summary>
    /// Shows the Recovery Center in this projectless workspace; false if it is busy, already shows it or holds a project.
    /// </summary>
    internal bool EnterRecoveryCenter(Guid? focus)
    {
        if (_disposed || _recoveryCatalog is null || IsWorkspaceBusy) return false;
        if (focus is { } candidate && !_recoveryCatalog.Undecided.Any(c => c.CandidateId == candidate)) return false;
        if (Mode == MainViewMode.RecoveryCenter) { if (focus is { } id) _recoveryCenter?.Focus(id); return true; }
        if (Mode != MainViewMode.Start || Session is not null) return false;
        _recoveryCenter = new(_recoveryCatalog, _restoreRecovery ?? (_ => Task.FromResult(false)), LeaveRecoveryCenter,
            e => ShowErrorAsync(Strings.RecoveryActionError, e), focus);
        _recoveryCenter.PropertyChanged += RecoveryCenterChanged;
        Navigate(MainViewMode.RecoveryCenter);
        return true;
    }

    /// <summary>Back to the start page. Leaving the Recovery Center never decides anything and never quits.</summary>
    internal void LeaveRecoveryCenter()
    {
        if (Mode == MainViewMode.RecoveryCenter) Navigate(MainViewMode.Start);
    }

    /// <summary>Any navigation away from the Recovery Center ends it, whoever causes it (Open, restore, back).</summary>
    private void EndRecoveryCenter()
    {
        var center = _recoveryCenter;
        _recoveryCenter = null;
        if (center is not null)
        {
            center.PropertyChanged -= RecoveryCenterChanged;
            center.Dispose();
        }
        Notify(nameof(RecoveryCenter));
        RecoveryCenterLeft?.Invoke();
    }

    private void RecoveryCenterChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RecoveryCenterViewModel.IsBusy)) Notify(nameof(IsWorkspaceBusy));
    }

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
    internal bool IsSettingsFrozen => _settingsSaving;

    bool ISettingsParticipant.TryFreezeForSettings()
    {
        if (_settingsSaving || HasRunningOperation) return false;
        _settingsSaving = true;
        ApplyFreeze();
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
        // The coordinator captured its participants before persisting; a workspace disposed meanwhile takes nothing.
        if (_disposed) return;
        SetPresentation(committed.Presentation);
        Notify(nameof(Settings));
    }

    void ISettingsParticipant.ReleaseSettingsFreeze()
    {
        if (!_settingsSaving) return;
        _settingsSaving = false;
        // Disposed meanwhile: only its own flag is reset; no session, no dialog.
        if (_disposed || _services.Settings.IsApplicationShuttingDown) return;
        ApplyFreeze();
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
    internal Func<Task<bool>>? NewProjectHandler { get; set; }
    internal Func<string, bool, Task<ProjectOpenResult>>? OpenProjectHandler { get; set; }
    internal Func<bool>? CanRequestProjectHandler { get; set; }
    private bool CanRequestProject => CanRequestProjectHandler?.Invoke() ?? true;
    /// <summary>
    /// Close Project through the workspace owner (§14: start page or close, decided by its <c>WorkspaceCloseTransaction</c>).
    /// Without an owner the same transaction runs here and always returns to the start page.
    /// </summary>
    internal Func<Task<bool>>? CloseWorkspaceHandler { get; set; }
    /// <summary>The owner's lifecycle view: the workspace is registered, routable and the application is not shutting down.</summary>
    internal Func<bool>? CanRouteHandler { get; set; }
    public bool IsBusy => IsOperationBusy || _leaveReserved || _quitFrozen;
    // The workspace's own operations, without a close reservation.
    private bool IsOperationBusy => _busy || _libraryBusy || _settingsSaving || ReportPreview?.IsBusy == true;
    /// <summary>An operation is running anywhere in this workspace (an open editor alone is not one).</summary>
    private bool HasRunningOperation => _quitFrozen || HasOwnOperation;
    // An operation of this workspace or its close reservation, without the application quit's freeze.
    private bool HasOwnOperation => IsOperationBusy || _leaveReserved || _recoveryCenter?.IsBusy == true
        || Mode == MainViewMode.ProjectSetup && (Setup.IsBusy || Setup.CurrentStep is MaterialSelectionViewModel { IsActionBusy: true });
    public bool IsWorkspaceBusy => _quitFrozen || IsOwnWorkspaceBusy;
    private bool IsOwnWorkspaceBusy => HasOwnOperation
        || Mode == MainViewMode.ProjectSetup && Setup.CurrentStep is MaterialSelectionViewModel { IsEditing: true };
    public MainWorkspaceKind Workspace => Mode == MainViewMode.ReportPreview ? MainWorkspaceKind.ReportPreview
        : Mode == MainViewMode.RecoveryCenter ? MainWorkspaceKind.RecoveryCenter
        : Mode == MainViewMode.Start ? MainWorkspaceKind.Start
        : Mode == MainViewMode.Editor ? MainWorkspaceKind.BeamEditor : Setup.Page switch
    {
        ProjectSetupPage.SectionDefinition => MainWorkspaceKind.SectionDefinition,
        ProjectSetupPage.MaterialSelection => MainWorkspaceKind.MaterialSelection,
        _ => MainWorkspaceKind.ProjectSetup
    };
    public bool IsSectionWorkspace => Workspace == MainWorkspaceKind.SectionDefinition;
    public bool CanNewProject => !IsWorkspaceBusy && !IsRecoveryFaulted && Workspace is MainWorkspaceKind.Start or MainWorkspaceKind.BeamEditor;
    public bool CanOpenProject => !IsWorkspaceBusy && !IsRecoveryFaulted && (Workspace is MainWorkspaceKind.Start or MainWorkspaceKind.BeamEditor or MainWorkspaceKind.RecoveryCenter
        || Workspace == MainWorkspaceKind.ProjectSetup && Session is null);
    /// <summary>Also from EditSetup or the report preview while the project's recovery is faulted: saving is its remediation.</summary>
    public bool CanSaveProject => !IsWorkspaceBusy && Session is not null
        && (Workspace == MainWorkspaceKind.BeamEditor || _recovery?.Fault == RecoveryFault.Project);
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
    public bool CanUndo => !IsWorkspaceBusy && !IsRecoveryFaulted && (Workspace switch
    {
        MainWorkspaceKind.BeamEditor => Session?.CanUndo == true,
        MainWorkspaceKind.SectionDefinition => Setup.Definition?.CanUndo == true,
        _ => false
    });
    public bool CanRedo => !IsWorkspaceBusy && !IsRecoveryFaulted && (Workspace switch
    {
        MainWorkspaceKind.BeamEditor => Session?.CanRedo == true,
        MainWorkspaceKind.SectionDefinition => Setup.Definition?.CanRedo == true,
        _ => false
    });
    public string ProjectFileName => Session?.FilePath is { } path ? Path.GetFileName(path) : Strings.Untitled;
    public string WindowTitle => Mode == MainViewMode.Start ? Strings.ApplicationTitle
        : Mode == MainViewMode.RecoveryCenter ? Strings.RecoveryCenter + " — " + Strings.ApplicationTitle
        : Mode == MainViewMode.ProjectSetup && Setup.Mode == ProjectSetupMode.Create
        ? Strings.NewProject + " — " + Strings.ApplicationTitle : Session is null ? Strings.ApplicationTitle
        : ProjectFileName + (Session.IsDirty ? "*" : "") + " — " + Strings.ApplicationTitle;
    public object CurrentViewModel => Mode switch
    {
        MainViewMode.Start => StartPage,
        MainViewMode.ProjectSetup => Setup,
        MainViewMode.Editor => Editor!,
        MainViewMode.ReportPreview => ReportPreview!,
        MainViewMode.RecoveryCenter => RecoveryCenter!,
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
        if (!_disposed && Mode == MainViewMode.ProjectSetup && setup.Mode == ProjectSetupMode.Create && ReferenceEquals(setup, Setup))
            _recovery?.SetupDraftChanged(setup.Draft);
    }

    public void EditProject()
    {
        if (!CanSaveProject || IsRecoveryFaulted || Workspace != MainWorkspaceKind.BeamEditor || Editor is null) return;
        Editor.CancelEditorInteraction();
        _setupReturnMode = MainViewMode.Editor;
        var first = Editor.Document.Segments[0];
        _setup = new(ProjectSetupMode.Edit, first.Section, first.BendingAxis, first.Material,
            ApplySetupAsync, CancelSetup, ResultPresentation, this, Editor.Document, originalInfo: Session!.CurrentRevision.State.Info, performance: _performance);
        Navigate(MainViewMode.ProjectSetup);
    }

    private Task<bool> ApplySetupAsync(ProjectSetupViewModel setup)
    {
        if (_disposed || Mode != MainViewMode.ProjectSetup || !ReferenceEquals(setup, Setup)
            || IsWorkspaceBusy || IsRecoveryFaulted || !setup.CanApply)
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
            // The local New primitive can be used over a project; its draft hands its id over below.
            var choice = await DecideLeaveAsync();
            if (_disposed || choice == LeaveChoice.Cancel) return false;
            if (!await LeaveRecoveryAsync(Session is not null ? RecoveryLeaveScope.Project : null, choice)) return false;
            // Gone meanwhile (system shutdown): no project is created; the draft's slot stays its crash protection.
            if (_disposed) return false;
            Editor?.CancelEditorInteraction();
            setup.ConfirmedDraftChanged -= SetupDraftChanged;
            ActivatePathlessSession(ProjectSession.Create(new(document, new(), setup.Info)));
            // The project takes over the draft's recovery id: its slot is published completely before the draft slot goes.
            if (_recovery is not null) await _recovery.ProjectCreatedAsync(Session!, () => !_disposed);
            if (_disposed) return false;
            // The author is current at once; remembering it on disk is best effort and never replaces damaged settings.
            await _services.Settings.UpdateLastAuthorAsync(Session!.CurrentRevision.State.Info!.Author);
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
        // The draft is given up deliberately: its slot is moved to Discarded first; if that fails the setup stays as it is.
        if (create && !await LeaveRecoveryAsync(RecoveryLeaveScope.Draft, LeaveChoice.Clean)) return false;
        if (_disposed) return false;
        if (create) Setup.ConfirmedDraftChanged -= SetupDraftChanged;
        Navigate(_setupReturnMode);
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

    private void CommittedStateChanged(ProjectState previous, ProjectState next, EditorChangeKind change) => _recovery?.ProjectChanged(Session!);
    private void SessionChanged() => NotifySession();

    public bool Undo() => CanUndo && (Workspace == MainWorkspaceKind.SectionDefinition
        ? Setup.Definition!.Undo() : Editor!.Undo());

    public bool Redo() => CanRedo && (Workspace == MainWorkspaceKind.SectionDefinition
        ? Setup.Definition!.Redo() : Editor!.Redo());

    public Task<bool> NewAsync() => !CanNewProject ? Task.FromResult(false) : RunOperationAsync(async () =>
    {
        // The local primitive also supports tools and tests that start a draft over a project. In that case the project
        // keeps its recovery slot and the current state must be in it before the draft is shown.
        if (_recovery is not null && Session is { } session)
        {
            try
            {
                if (session.IsDirty) await _recovery.FlushAsync(session.CurrentRevision.State, session.FilePath ?? session.RecoveryOriginPath);
                // Back at its savepoint the project has no recovery any more: the slot must be gone before the draft owns the context.
                else await _recovery.ReleaseCleanProjectAsync(session);
            }
            catch (Exception e) when (IsFileError(e))
            {
                if (!_disposed && session.IsDirty) _recovery.ScheduleProject(session);
                await ShowErrorAsync(Strings.RecoveryError, e);
                return false;
            }
        }
        if (_disposed) return false;
        _setupReturnMode = Mode;
        _setup = NewSetup();
        Navigate(MainViewMode.ProjectSetup);
        return true;
    });

    public Task<bool> OpenAsync() => OpenFromPickerAsync(OpenPathAsync);

    private async Task<bool> OpenFromPickerAsync(Func<string, bool, Task<bool>> openPath)
    {
        if (!CanOpenProject || _dialogs is null) return false;
        string? path = null;
        await RunOperationAsync(async () => (path = await _dialogs.PickOpenPathAsync()) is not null);
        return path is not null && await openPath(path, false);
    }

    public Task<bool> OpenAsync(string path) => OpenPathAsync(path);
    public Task<bool> OpenRecentAsync(string path) => OpenPathAsync(path, fromRecent: true);

    private async Task<bool> RequestOpenPathAsync(string path, bool fromRecent)
    {
        var result = await (OpenProjectHandler?.Invoke(path, fromRecent) ?? OpenPathResultAsync(path, fromRecent));
        return result.Status is ProjectOpenStatus.Opened or ProjectOpenStatus.AlreadyOpen;
    }

    private async Task<bool> OpenPathAsync(string path, bool fromRecent = false) =>
        (await OpenPathResultAsync(path, fromRecent)).Status is ProjectOpenStatus.Opened or ProjectOpenStatus.AlreadyOpen;

    internal async Task<ProjectOpenResult> OpenPathResultAsync(string path, bool fromRecent)
    {
        if (!CanContinueOpen || !CanOpenProject && _fileOperationCompletion is null) return ProjectOpenResult.Unavailable;
        try
        {
            var result = await _projectOpen.OpenAsync(path, new SingleWindowOpen(this, path, fromRecent));
            if (result.Status == ProjectOpenStatus.AlreadyOpen && CanOpenProject && CanContinueOpen)
                await RunOperationAsync(async () => { await RecordOpenedProjectAsync(result); return true; });
            return result;
        }
        catch (Exception e) when (IsFileError(e))
        {
            var outcome = ClassifyOpenRequestError(e);
            await TryPresentOpenFailureAsync(outcome, path, fromRecent);
            return outcome;
        }
    }

    /// <summary>The workspace may still take an open: not disposed, no close reserved, routable for its owner.</summary>
    private bool CanContinueOpen => !_disposed && !_leaveReserved && !_quitFrozen && (CanRouteHandler?.Invoke() ?? true);

    private async Task<ProjectOpenStatus> ActivateOpenedProjectAsync(ProjectState state, string path,
        Func<bool> canCommit, Action<WindowId, Action> commit)
    {
        var choice = await DecideLeaveAsync();
        if (!canCommit()) return ProjectOpenStatus.Unavailable;
        if (choice == LeaveChoice.Cancel) return ProjectOpenStatus.Cancelled;
        var editor = CreateEditor(ProjectSession.Open(state, path));
        editor.IsMainMenuOpen = _mainMenuOpen;
        editor.Session.SetBusy(IsBusy);
        // What this workspace leaves: its project, or a Create draft that is given up for the opened file.
        bool draft = Session is null && Mode == MainViewMode.ProjectSetup && Setup.Mode == ProjectSetupMode.Create;
        var scope = Session is not null ? RecoveryLeaveScope.Project : draft ? RecoveryLeaveScope.Draft : (RecoveryLeaveScope?)null;
        if (!await LeaveRecoveryAsync(scope, choice)) return canCommit() ? ProjectOpenStatus.Cancelled : ProjectOpenStatus.Unavailable;
        if (!canCommit()) return ProjectOpenStatus.Unavailable;
        Editor?.CancelEditorInteraction();
        if (!canCommit()) return ProjectOpenStatus.Unavailable;
        if (draft) Setup.ConfirmedDraftChanged -= SetupDraftChanged;
        commit(WindowId, () => AttachEditor(editor));
        return ProjectOpenStatus.Opened;
    }

    internal async Task<ProjectOpenStatus> OpenIntoEmptyWorkspaceAsync(ProjectState state, string path,
        Func<bool> canCommit, Action<WindowId, Action> commit)
    {
        var status = ProjectOpenStatus.Unavailable;
        if (!CanContinueOpen || !CanOpenProject || Mode != MainViewMode.Start || HasProject) return status;
        await RunOperationAsync(async () =>
        {
            status = await ActivateOpenedProjectAsync(state, path, () => CanContinueOpen && canCommit(), commit);
            return status == ProjectOpenStatus.Opened;
        }, fileTransaction: true, handleFileErrors: false);
        return status;
    }

    internal static ProjectOpenResult ClassifyOpenRequestError(Exception error) => error is DirectoryNotFoundException or FileNotFoundException
        ? new(ProjectOpenStatus.Missing) : new(ProjectOpenStatus.Failed, Error: error);

    internal Task<bool> TryPresentOpenFailureAsync(ProjectOpenResult result, string path, bool fromRecent) =>
        CanContinueOpen ? RunOperationAsync(() => PresentOpenFailureAsync(result, path, fromRecent)) : Task.FromResult(false);

    private async Task<bool> PresentOpenFailureAsync(ProjectOpenResult result, string path, bool fromRecent)
    {
        if (!CanContinueOpen) return false;
        if (result.Status == ProjectOpenStatus.Missing)
        {
            if (!fromRecent) await ShowErrorAsync(Strings.ProjectFileError, new IOException(Strings.ProjectFileMissing));
            else if (_dialogs is not null && await _dialogs.ConfirmRemoveMissingRecentAsync(Path.GetFullPath(path)) && CanContinueOpen)
                await _services.Recents.RemoveAsync(path);
        }
        else if (result.Error is { } error) await ShowErrorAsync(Strings.ProjectFileError, error);
        return CanContinueOpen;
    }

    internal Task RecordOpenedProjectAsync(ProjectOpenResult result) => CanContinueOpen && result.CanonicalPath is { } path
        ? RecordRecentAsync(path) : Task.CompletedTask;

    private sealed class SingleWindowOpen(MainWindowViewModel model, string path, bool fromRecent) : IProjectOpenRequest
    {
        public bool CanContinue => model.CanContinueOpen;

        public async Task<ProjectOpenResult> RunOpenAsync(Func<Task<ProjectOpenResult>> transaction, bool afterSave)
        {
            if (afterSave && model._fileOperationCompletion is { } fileOperation) await fileOperation.Task;
            // Not a decision of the user: the workspace cannot take the open right now (busy, closing, gone).
            if (!CanContinue || !model.CanOpenProject) return ProjectOpenResult.Unavailable;
            var outcome = ProjectOpenResult.Unavailable;
            await model.RunOperationAsync(async () =>
            {
                outcome = await transaction();
                if (outcome.Status is ProjectOpenStatus.Missing or ProjectOpenStatus.Failed
                    && !await model.PresentOpenFailureAsync(outcome, path, fromRecent))
                    outcome = ProjectOpenResult.Unavailable;
                if (outcome.Status == ProjectOpenStatus.Opened) await model.RecordOpenedProjectAsync(outcome);
                return outcome.Status == ProjectOpenStatus.Opened;
            }, fileTransaction: true);
            return outcome;
        }

        public Task<ProjectOpenStatus> ActivateProjectAsync(ProjectState state, string canonicalPath, Action<WindowId, Action> commit) =>
            model.ActivateOpenedProjectAsync(state, canonicalPath, () => CanContinue, commit);

        public bool ActivateExisting(WindowId owner)
        {
            if (!CanContinue) return false;
            model._activateWorkspace?.Invoke(owner);
            return true;
        }

        public Task<bool> ActivateRecoveryAsync(Guid candidate) => CanContinue && model._showRecoveryCenter is { } show
            ? show(candidate) : Task.FromResult(false);
    }

    public Task<bool> SaveAsync(bool saveAs = false) => !CanSaveProject ? Task.FromResult(false)
        : RunOperationAsync(() => SaveCurrentAsync(saveAs), fileTransaction: true);

    /// <summary>
    /// Close Project (§14) runs the workspace's <c>WorkspaceCloseTransaction</c>: through its owner, which also decides between
    /// start page and window close, or here without an owner, ending on the start page.
    /// </summary>
    public async Task<bool> CloseProjectAsync()
    {
        if (!CanCloseProject) return false;
        if (CloseWorkspaceHandler is { } close) return await close();
        return await Workspaces.WorkspaceCloseTransaction.RunAsync(this, closeWindow: false);
    }

    private async Task<bool> SaveCurrentAsync(bool saveAs = false, bool recordRecent = true)
    {
        if (_disposed || Session is not { } session) return false;
        if (session.CurrentRevision.State.Document.Segments.Any(segment => segment.Material.StrengthType is null))
        {
            if (_dialogs is not null) await _dialogs.ShowErrorAsync(Strings.MaterialStrengthResolve);
            return false;
        }
        string? path = saveAs ? null : session.FilePath;
        if (path is null)
        {
            path = _dialogs is null ? null : await _dialogs.PickSavePathAsync(session.FilePath, session.CurrentRevision.State.Info?.CalculationTitle);
            if (_disposed || path is null) return false;
            path = EnsureProjectExtension(path);
        }
        string? previousPath = session.FilePath;
        await _projectPaths.SettleRecoveryAsync();
        if (_disposed) return false;
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
            bool recoveryPublished = false;
            // Saving is the remediation of a faulted project recovery: the protective flush is still tried, but its failure
            // must not prevent the atomic file write (the protection is lost already).
            bool faulted = _recovery?.Fault == RecoveryFault.Project;
            try
            {
                var bytes = ProjectFileCodec.Serialize(revision.State);
                if (_recovery is not null && (session.IsDirty || reservation is not null))
                {
                    recoveryStarted = true;
                    try
                    {
                        await _recovery.FlushAsync(revision.State, path);
                        recoveryPublished = true;
                    }
                    catch (Exception e) when (faulted && IsFileError(e)) { }
                }
                // The target has no saved file from this transaction. Undo the protective flush's target association
                // even after disposal, so it stays associated with the project that actually owns the changes.
                if (_disposed)
                {
                    if (NeedsRecoveryRollbackAfterDisposal()) await RestoreRecoveryAfterUnsavedAsync();
                    return false;
                }
                await _files.WriteAtomicAsync(path, bytes);
            }
            catch
            {
                if (_disposed ? NeedsRecoveryRollbackAfterDisposal() : recoveryStarted)
                    await RestoreRecoveryAfterUnsavedAsync();
                throw;
            }

            bool NeedsRecoveryRollbackAfterDisposal() => recoveryPublished &&
                (!session.IsDirty || !string.Equals(path, previousPath ?? session.RecoveryOriginPath, StringComparison.Ordinal));

            async Task RestoreRecoveryAfterUnsavedAsync()
            {
                try
                {
                    if (session.IsDirty) await _recovery!.FlushAsync(revision.State, previousPath ?? session.RecoveryOriginPath);
                    else await _recovery!.DeleteAsync();
                }
                catch (Exception e) when (IsFileError(e))
                {
                    // A failed rollback keeps its existing best-effort repair path.
                    _recovery!.ScheduleProject(session);
                }
            }

            // The atomic write was the user's decision and is complete. A workspace that is gone commits no ownership and
            // gives up nothing: its published recovery slot stays, the save reservation is released below.
            if (_disposed) return false;
            if (reservation is null) save.MarkSaved();
            else if (!_projectPaths.TryCommitSave(reservation, save))
                throw new InvalidOperationException("The save reservation lost ownership after the write.");
            session.NotifySaved();
        }
        finally
        {
            if (reservation is not null) _projectPaths.TryReleaseSave(reservation, releaseOwner: _disposed);
        }
        _recovery?.ProjectSaved(session);
        if (recordRecent) await RecordRecentAsync(path, saveAs && reservation is not null ? previousPath : null);
        if (!_disposed && _recovery is not null)
        {
            try { await _recovery.DeleteAsync(); }
            catch (Exception e) when (IsFileError(e)) { await ShowErrorAsync(Strings.RecoveryError, e); }
        }
        return true;
    }

    public static string EnsureProjectExtension(string path) => path.EndsWith(".spandraft", StringComparison.OrdinalIgnoreCase)
        ? path : path + ".spandraft";

    /// <summary>
    /// Restores a project recovery snapshot into this projectless workspace. A preferred restore (<paramref name="candidate"/>
    /// set) owns the original path through <see cref="ProjectPathRegistry.TryCommitRecovery"/>; an older variant is detached:
    /// <see cref="ProjectSession.FilePath"/> stays null and the original is only remembered as the recovery origin.
    /// The recovery context takes over the very same slot. A failure before the commit leaves the workspace unchanged.
    /// </summary>
    internal void RestoreProject(RecoverySnapshot snapshot, RecoverySlot slot, Guid? candidate)
    {
        if (_disposed || Session is not null || IsWorkspaceBusy || _recovery is null)
            throw new InvalidOperationException("The workspace cannot take a restored project.");
        ProjectIdentity? identity = null;
        if (candidate is not null)
            identity = snapshot.OriginalFilePath is { } original ? _projectIdentities.Resolve(original)
                : throw new ArgumentException("A preferred restore needs an original path.", nameof(candidate));
        var session = identity is not null ? ProjectSession.Restore(snapshot.State, identity.CanonicalPath)
            : ProjectSession.RestoreDetached(snapshot.State, snapshot.OriginalFilePath);
        var editor = CreateEditor(session);
        AttachEditor(editor);
        try
        {
            // The recovery slot is adopted first and the path commit is the last step that can fail, so a failure never
            // leaves registry ownership behind; if anything after the commit were to fail it is released again.
            _recovery.AdoptProject(slot);
            if (identity is not null && !_projectPaths.TryCommitRecovery(identity, candidate!.Value, WindowId))
                throw new InvalidOperationException("The recovery candidate lost ownership of the original path during restore.");
        }
        catch
        {
            _recovery.Unadopt(slot);
            DetachSession();
            _editor = null;
            Navigate(MainViewMode.Start);
            throw;
        }
        // No new write: the restored slot already holds exactly this state and keeps its original timestamp.
    }

    /// <summary>Restores a Create draft recovery into this projectless workspace (CreateSetup has no session); the draft keeps its slot and id.</summary>
    internal void RestoreSetup(SetupRecoverySnapshot snapshot, RecoverySlot slot)
    {
        if (_disposed || Session is not null || IsWorkspaceBusy || _recovery is null || Mode != MainViewMode.Start)
            throw new InvalidOperationException("The workspace cannot take a restored draft.");
        _recovery.AdoptCreateDraft(slot);
        var previous = _setup;
        try
        {
            _setupReturnMode = Mode;
            _setup = NewSetup(snapshot.Draft);
        }
        catch
        {
            _recovery.Unadopt(slot);
            _setup = previous;
            throw;
        }
        Navigate(MainViewMode.ProjectSetup);
    }

    private async Task<bool> RunOperationAsync(Func<Task<bool>> operation, bool fileTransaction = false, bool handleFileErrors = true)
    {
        if (_disposed || IsWorkspaceBusy) return false;
        var completion = fileTransaction ? new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) : null;
        _fileOperationCompletion = completion;
        _busy = true;
        Session?.SetBusy(true);
        NotifySession();
        try { return await operation(); }
        catch (Exception e) when (handleFileErrors && IsFileError(e))
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
                if (!_disposed)
                {
                    // A quit freeze or close reservation keeps the session locked.
                    Session?.SetBusy(IsBusy);
                    NotifySession();
                    await ReportPendingRecoveryAsync();
                }
            }
            finally { completion?.SetResult(); }
        }
    }

    internal static bool IsFileError(Exception e) => e is IOException or UnauthorizedAccessException
        or ProjectFormatException or JsonException or ArgumentException or NotSupportedException or System.Security.SecurityException;
    // A disposed workspace has no window left to own a dialog: late continuations show nothing.
    private Task ShowErrorAsync(string prefix, Exception e) =>
        _disposed ? Task.CompletedTask : _dialogs?.ShowErrorAsync(prefix + "\n" + e.Message) ?? Task.CompletedTask;
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
        var previous = _mode;
        _mode = mode;
        if (previous == MainViewMode.RecoveryCenter && mode != MainViewMode.RecoveryCenter) EndRecoveryCenter();
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
        Notify(nameof(IsWorkspaceEditable)); Notify(nameof(IsRecoveryFaulted));
        Notify(nameof(CanRestoreRecoveryProtection)); RestoreRecoveryProtectionCommand?.Refresh();
        Notify(nameof(CanShowRecovery)); ShowRecoveryCommand?.Refresh();
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
