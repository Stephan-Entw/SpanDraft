using SpanDraft.Desktop.Activation;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SpanDraft.Desktop.Diagnostics;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.ViewModels;
using SpanDraft.Desktop.Workspaces;

namespace SpanDraft.Desktop;

public partial class MainWindow : Window
{
    private MainWindowViewModel _model = null!;
    private InteractionPerformance? _performance;
    private AvaloniaInteractionFrameProbe? _frameProbe;
    private readonly KeyModifiers _primary = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
    private ProjectDialogs _dialogs = null!;
    private readonly DesktopApplicationHost _host;
    private readonly WorkspaceWindowHandle _handle;
    internal WindowId WindowId => _handle.WindowId;
    // The workspace's close is running or committed; the close itself belongs to its WorkspaceCloseTransaction.
    private bool IsWorkspaceClosing => !_handle.CanRoute;
    private bool IsOpenTarget => _handle.IsOpenTargetReserved || _handle.Reason == WorkspaceCreationReason.OpenProject;
    private bool _closeApproved;
    private DiagnosticsWindow? _diagnosticsWindow;
    private bool _initializing = true;
    private readonly TaskCompletionSource _initialization = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes after the initial libraries, recovery, recents and shell request have been processed.</summary>
    public Task Initialization => _initialization.Task;

    internal event Action<InteractionPerformanceEntry?> MeasurementFinished
    {
        add => _performance!.MeasurementFinished += value;
        remove => _performance!.MeasurementFinished -= value;
    }

    internal async Task<bool> WaitForRenderedFrameAsync(CancellationToken cancellationToken)
    {
        if (_frameProbe is null) return false;
        return await _frameProbe.ObserveNextFrameAsync(TimeProvider.System, cancellationToken) is not null;
    }

    /// <summary>Designer/XAML-loader entry only; workspaces are created through the <see cref="WorkspaceWindowManager"/>.</summary>
    public MainWindow() : this(new DesktopApplicationHost(), new WorkspaceWindowHandle(WindowId.Create(), WorkspaceCreationReason.None, isInitial: false)) { }

    internal MainWindow(DesktopApplicationHost host, WorkspaceWindowHandle handle)
    {
        InitializeComponent();
        _host = host;
        _handle = handle;
        var files = host.Files;
        NewMenuItem.InputGesture = new(Key.N, _primary);
        OpenMenuItem.InputGesture = new(Key.O, _primary);
        SaveMenuItem.InputGesture = new(Key.S, _primary);
        SaveAsMenuItem.InputGesture = new(Key.S, _primary | KeyModifiers.Shift);
        ReportMenuItem.InputGesture = new(Key.P, _primary);
        // Quit is the application quit (§74), not the close of this window: one coordinator for every quit source.
        var quitCommand = new ActionCommand(() => host.Quit.RequestQuit(),
            () => !_initializing && !_model.IsWorkspaceBusy && !IsWorkspaceClosing);
        QuitMenuItem.Command = quitCommand;
        QuitMenuItem.InputGesture = new(Key.Q, _primary);
        UndoMenuItem.InputGesture = new(Key.Z, _primary);
        RedoMenuItem.InputGesture = new(Key.Z, _primary | KeyModifiers.Shift);
        AddHandler(KeyDownEvent, ProjectKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (e.Source is Control source && (source as Button ?? source.FindAncestorOfType<Button>())
                is { Name: "ReportToolbarButton", IsEffectivelyEnabled: true } reportButton
                && !_initializing && _model.Editor is { } editor && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                // Protect buffers before focus and the editor's outside-press handlers run.
                editor.IsReportOpen = true;
                AddHandler(PointerReleasedEvent, EndReportPress, RoutingStrategies.Tunnel, handledEventsToo: true);
                reportButton.AddHandler(PointerCaptureLostEvent, EndReportPress, RoutingStrategies.Direct, handledEventsToo: true);
                Deactivated += EndReportPress;
                void EndReportPress(object? sender, EventArgs args)
                {
                    RemoveHandler(PointerReleasedEvent, EndReportPress);
                    reportButton.RemoveHandler(PointerCaptureLostEvent, EndReportPress);
                    Deactivated -= EndReportPress;
                    Dispatcher.UIThread.Post(() => editor.IsReportOpen = ReferenceEquals(editor, _model.Editor)
                        && _model.Workspace == MainWorkspaceKind.ReportPreview, DispatcherPriority.Background);
                }
            }
            if (e.Source is Control control && (control is Menu or MenuItem
                || control.FindAncestorOfType<Menu>() is not null) && !_initializing) _model.SetMainMenuOpen(true);
        }, RoutingStrategies.Tunnel);
        MainMenu.PropertyChanged += (_, e) =>
        {
            if (!_initializing && e.Property == MenuBase.IsOpenProperty)
            {
                if (MainMenu.IsOpen) _model.SetMainMenuOpen(true);
                else Dispatcher.UIThread.Post(() => _model.SetMainMenuOpen(MainMenu.IsOpen));
            }
        };
        Opened += async (_, _) =>
        {
            try
            {
                if (_handle.IsFinalized || host.Workspaces.IsShuttingDown) { _initialization.TrySetCanceled(); return; }
                _dialogs = new ProjectDialogs(this, () => host.Quit.IsSystemShutdown);
                _frameProbe = new(this, WorkspaceContent);
                _performance = new(_frameProbe);
                // The shared services and the recovery catalog were initialized by the bootstrap; this window only adapts them.
                _model = new(analyze: host.AnalyzeProject, performance: _performance, files: files, dialogs: _dialogs, recovery: host.CreateRecoveryContext(),
                    services: host.Services, openUserManual: OpenUserManualAsync,
                    userNameProvider: host.UserNameProvider, windowId: WindowId, projectIdentities: host.ProjectIdentities,
                    projectPaths: host.ProjectPaths, projectOpen: host.ProjectOpen,
                    activateWorkspace: id => host.Workspaces.Activate(id),
                    recoveryCatalog: host.Recovery,
                    showRecoveryCenter: async focus => await host.Workspaces.ShowRecoveryCenterAsync(_handle, focus) is not null,
                    restoreRecovery: id => host.Workspaces.RestoreRecoveryAsync(id, _handle),
                    settingsRegistration: _handle.SettingsRegistration);
                _handle.ReleaseSettingsRegistration();
                host.Workspaces.BindModel(_handle, _model);
                await _model.ReportStartupErrorsAsync();
                if (_handle.IsFinalized || host.Workspaces.IsShuttingDown) { _initialization.TrySetCanceled(); return; }
                // Not knowing about unsaved work is the one thing that holds the startup back: ask until it is known or the user leaves.
                if (_handle.IsInitial && !await EnsureRecoveryKnownAsync())
                {
                    _initialization.TrySetCanceled();
                    _closeApproved = true;
                    Dispatcher.UIThread.Post(Close);
                    return;
                }
                if (_handle.IsFinalized || host.Workspaces.IsShuttingDown) { _initialization.TrySetCanceled(); return; }
                DataContext = _model;
                _initializing = false;
                quitCommand.Refresh();
                if (!_handle.IsInitial)
                {
                    _model.PropertyChanged += (_, _) => host.Workspaces.PostPump();
                    _initialization.TrySetResult();
                    host.Workspaces.PostPump();
                    return;
                }
                // Undecided recovery candidates never hold the host back: requests wait in the host queue from here on.
                host.MarkReady();
                host.Workspaces.EnableActivation();
                _model.PropertyChanged += (_, _) => PostPump();
                await host.Activation.PumpAsync();
                _initialization.TrySetResult();
            }
            catch (Exception error)
            {
                _initialization.TrySetException(error);
                // The requesting operation handles failures of dedicated windows; initial startup has no such caller.
                if (_handle.IsInitial) throw;
            }
            finally { _handle.ReleaseSettingsRegistration(); }
        };
        Closing += WindowClosing;
        Closed += (_, _) =>
        {
            _initialization.TrySetCanceled();
            // The one final cleanup of this workspace; appwide infrastructure ends only with the last workspace.
            host.Workspaces.FinalizeClosed(_handle);
            _performance?.CancelPending();
            _frameProbe?.Dispose();
        };
    }

    /// <summary>
    /// The recovery catalog is loaded at bootstrap; if its area could not be listed it is tried again here, with a visible
    /// question. Undecided candidates are never a reason to wait: only an unknown recovery situation is.
    /// </summary>
    private async Task<bool> EnsureRecoveryKnownAsync()
    {
        while (!await _host.Recovery.InitializeAsync())
        {
            if (!await _dialogs.ConfirmRecoveryRetryAsync(_host.Recovery.Error?.Message ?? "")) return false;
        }
        return true;
    }

    /// <summary>The TopLevel currently blocking the shell (e.g. the startup question), otherwise this window.</summary>
    internal Window RetryTarget => OwnedWindows.LastOrDefault(static w => w.IsVisible) ?? this;

    /// <summary>Test seam: the best-effort activation of the window or blocking dialog.</summary>
    internal Action<Window> Surface { get; set; } = static target =>
    {
        if (target.WindowState == WindowState.Minimized) target.WindowState = WindowState.Normal;
        target.Activate();
    };

    private void PostPump() => _host.Workspaces.PostPump();

    /// <summary>
    /// Closes the window after its close was committed (or for a provably empty workspace); only the workspace manager calls
    /// it. Posted, so that a close committed from within the Closing event finishes that event first.
    /// </summary>
    internal void CloseCommitted()
    {
        _closeApproved = true;
        IsEnabled = false;
        Dispatcher.UIThread.Post(Close, DispatcherPriority.Send);
    }

    /// <summary>
    /// Window part of an ordinary close's preflight: initialized and no application dialog open over it (Settings, About,
    /// What's new). Busy operations and native pickers are refused by the view model's reservation.
    /// </summary>
    internal bool CanBeginClose => !_initializing && !_host.AppDialogs.IsOwnedBy(this);

    /// <summary>
    /// Window part of the application quit's preflight: initialized. Its application dialogs are the quit's business: it
    /// closes those that can be closed safely and refuses to start because of the others.
    /// </summary>
    internal bool IsReadyForQuit => IsUiReady;

    // The initial window handles its own launch request before Initialization completes.
    internal bool IsUiReady => !_initializing;

    internal bool HandleActivation()
    {
        if (_initializing || IsWorkspaceClosing || _host.State == DesktopApplicationState.ShuttingDown) return false;
        // Activate and OpenUri only bring the window forward; URI semantics are not defined by this phase.
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        return true;
    }

    // One Settings dialog for the application (§77): an open one is activated, whichever workspace owns it.
    private async void ShowSettings(object? sender, RoutedEventArgs e)
    {
        if (_initializing || _model.IsWorkspaceBusy || IsWorkspaceClosing || IsOpenTarget
            || _host.AppDialogs.Activate(AppDialogKind.Settings)) return;
        _model.SetSettingsDialogOpen(true);
        try
        {
            await _host.AppDialogs.ShowAsync(AppDialogKind.Settings, this, () =>
                new SettingsWindow(new(_model.Settings, _model.ApplySettingsAsync, () => _host.Workspaces.IsShuttingDown),
                    () => _host.Quit.IsSystemShutdown) { Icon = Icon });
        }
        finally
        {
            // Leave the protection in place through activation and deferred focus events.
            if (!_host.Workspaces.IsShuttingDown)
                Dispatcher.UIThread.Post(() =>
                {
                    if (!_host.Workspaces.IsShuttingDown) _model.SetSettingsDialogOpen(false);
                }, DispatcherPriority.Background);
        }
    }

    // Modeless, so interactions made while it is open appear live. Opening it is not a measured interaction.
    private void ShowDiagnostics(object? sender, RoutedEventArgs e)
    {
        if (_initializing || IsWorkspaceClosing || _performance is null) return;
        if (_diagnosticsWindow is not null) { _diagnosticsWindow.Activate(); return; }
        var window = new DiagnosticsWindow(new(_performance)) { Icon = Icon };
        _diagnosticsWindow = window;
        _handle.AttachOwnedWindow(window);
        window.Closed += (_, _) => _diagnosticsWindow = null;
        window.Show(this);
    }

    private async void ShowWhatsNew(object? sender, RoutedEventArgs e)
    {
        if (_initializing || IsWorkspaceClosing || IsOpenTarget
            || _host.AppDialogs.Activate(AppDialogKind.WhatsNew)) return;
        try
        {
            await _host.AppDialogs.ShowAsync(AppDialogKind.WhatsNew, this,
                () => new DocumentWindow(Strings.WhatsNew, ApplicationDocuments.ReadReleaseNotes()) { Icon = Icon });
        }
        catch (Exception)
        {
            await _dialogs.ShowErrorAsync(Strings.WhatsNewOpenError);
        }
    }

    private async void ShowAbout(object? sender, RoutedEventArgs e)
    {
        if (_initializing || _model.IsWorkspaceBusy || IsWorkspaceClosing || IsOpenTarget
            || _host.AppDialogs.Activate(AppDialogKind.About)) return;
        await _host.AppDialogs.ShowAsync(AppDialogKind.About, this, () => new AboutWindow { Icon = Icon });
    }

    private void ProjectKeyDown(object? sender, KeyEventArgs e)
    {
        if (_initializing || _host.AppDialogs.IsOwnedBy(this)) return;
        bool historyKey = e.Key == Key.Z && (e.KeyModifiers == _primary || e.KeyModifiers == (_primary | KeyModifiers.Shift))
            || e.Key == Key.Y && e.KeyModifiers == KeyModifiers.Control && OperatingSystem.IsWindows();
        if (historyKey && HasEditableTextFocus()) return;
        // Keyboard menu activation must protect drafts before focus leaves their input.
        if (e.Key == Key.F10 && e.KeyModifiers == KeyModifiers.None)
        {
            _model.SetMainMenuOpen(true);
            MainMenu.Open();
            e.Handled = true;
            return;
        }
        if (e.Key is Key.LeftAlt or Key.RightAlt)
        {
            _model.SetMainMenuOpen(true);
            Dispatcher.UIThread.Post(() => _model.SetMainMenuOpen(MainMenu.IsOpen), DispatcherPriority.Background);
        }
        ICommand? command = (e.Key, e.KeyModifiers) switch
        {
            (Key.N, var m) when m == _primary => _model.NewCommand,
            (Key.O, var m) when m == _primary => _model.OpenCommand,
            (Key.S, var m) when m == _primary => _model.SaveCommand,
            (Key.S, var m) when m == (_primary | KeyModifiers.Shift) => _model.SaveAsCommand,
            (Key.P, var m) when m == _primary => _model.ReportCommand,
            (Key.Q, var m) when m == _primary => QuitMenuItem.Command,
            (Key.Z, var m) when m == _primary => _model.UndoCommand,
            (Key.Z, var m) when m == (_primary | KeyModifiers.Shift) => _model.RedoCommand,
            (Key.Y, KeyModifiers.Control) when OperatingSystem.IsWindows() => _model.RedoCommand,
            _ => null
        };
        if (command is null) return;
        e.Handled = true;
        if (command.CanExecute(null)) command.Execute(null);
    }

    private bool HasEditableTextFocus()
    {
        var focused = FocusManager?.GetFocusedElement();
        var textBox = focused as TextBox ?? (focused as Control)?.FindAncestorOfType<TextBox>();
        return textBox is { IsReadOnly: false };
    }

    private async Task OpenUserManualAsync()
    {
        if (_initializing || IsWorkspaceClosing) return;
        var manual = new UserManualLauncher(Launcher.LaunchUriAsync);
        if (!await manual.OpenAsync()) await _dialogs.ShowErrorAsync(SpanDraft.Desktop.Resources.Strings.UserManualOpenError);
    }

    /// <summary>Confirmed requests still in the queue are only dropped by an explicit user decision.</summary>
    internal async Task<bool> ConfirmDiscardPendingRequestsAsync(HashSet<LaunchRequest> confirmed)
    {
        var pending = _host.Activation.PendingOpenRequests().Where(r => !confirmed.Contains(r)).ToList();
        if (pending.Count == 0) return true;
        string? name = pending is [{ Type: LaunchRequestType.OpenProject, Value: { } path }] ? Path.GetFileName(path) : null;
        if (!await _dialogs.ConfirmDiscardPendingRequestsAsync(name, pending.Count) || _handle.IsFinalized) return false;
        confirmed.UnionWith(pending);
        return true;
    }

    private async void WindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeApproved) return;
        // A platform shutdown asks every window synchronously: a user quit is reserved and posted, and cancelling this close
        // cancels the platform's quit; a system shutdown lets the platform close the window without any question.
        switch (_host.Quit.OnPlatformClosing(e.CloseReason, e.IsProgrammatic))
        {
            case PlatformClose.SystemShutdown: return;
            case PlatformClose.UserQuit: e.Cancel = true; return;
        }
        // Once the shutdown is committed or the system ends the session, nothing is decided for a window any more.
        if (_host.Quit.IsSystemShutdown) return;
        e.Cancel = true;
        // The close of this workspace is its WorkspaceCloseTransaction; the reservation is taken before the first await.
        await _host.Workspaces.CloseAsync(_handle, WorkspaceCloseReason.Window);
    }
}
