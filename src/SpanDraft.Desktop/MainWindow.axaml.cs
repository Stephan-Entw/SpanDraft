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
    private bool _closeApproved;
    private AboutWindow? _aboutWindow;
    private DocumentWindow? _releaseNotesWindow;
    private DiagnosticsWindow? _diagnosticsWindow;
    private SettingsWindow? _settingsWindow;
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
        var quitCommand = new ActionCommand(Close, () => !_initializing && !_model.IsWorkspaceBusy && !IsWorkspaceClosing);
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
                _dialogs = new ProjectDialogs(this);
                _frameProbe = new(this, WorkspaceContent);
                _performance = new(_frameProbe);
                // The shared services and the recovery catalog were initialized by the bootstrap; this window only adapts them.
                _model = new(performance: _performance, files: files, dialogs: _dialogs, recovery: host.CreateRecoveryContext(),
                    services: host.Services, openUserManual: OpenUserManualAsync,
                    userNameProvider: host.UserNameProvider, windowId: WindowId, projectIdentities: host.ProjectIdentities,
                    projectPaths: host.ProjectPaths, projectOpen: host.ProjectOpen,
                    activateWorkspace: id => host.Workspaces.Activate(id),
                    ownerProjectPath: id => host.Workspaces.Find(id)?.Model?.Session?.FilePath,
                    recoveryCatalog: host.Recovery,
                    showRecoveryCenter: focus => host.Workspaces.RequestRecoveryCenter(_handle, focus),
                    restoreRecovery: id => host.Workspaces.RestoreRecoveryAsync(id, _handle));
                host.Workspaces.BindModel(_handle, _model);
                await _model.ReportStartupErrorsAsync();
                // Not knowing about unsaved work is the one thing that holds the startup back: ask until it is known or the user leaves.
                if (_handle.IsInitial && !await EnsureRecoveryKnownAsync())
                {
                    _initialization.TrySetCanceled();
                    _closeApproved = true;
                    Dispatcher.UIThread.Post(Close);
                    return;
                }
                DataContext = _model;
                _initializing = false;
                quitCommand.Refresh();
                if (!_handle.IsInitial)
                {
                    _model.PropertyChanged += (_, _) => host.Workspaces.PostPump();
                    _initialization.TrySetResult();
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
                // Preserve normal fatal-startup behavior while making the error awaitable by hosts.
                throw;
            }
        };
        Closing += WindowClosing;
        Closed += (_, _) =>
        {
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

    private LaunchRequest? _lastBlockedRequest;

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
    /// Window part of the close preflight: initialized and no dialog open that cannot be interrupted safely (settings,
    /// about, release notes). Busy operations and native pickers are refused by the view model's reservation.
    /// </summary>
    internal bool CanBeginClose => !_initializing && _settingsWindow is null && _aboutWindow is null && _releaseNotesWindow is null;

    internal async Task<bool> HandleLaunchRequestAsync(LaunchRequest request)
    {
        if (_initializing || IsWorkspaceClosing || _host.State == DesktopApplicationState.ShuttingDown) return false;
        if (request.Type == LaunchRequestType.OpenProject)
        {
            // A setup draft in progress keeps precedence over external opens; the request waits in the queue.
            if (!_model.CanOpenProject || _model.Mode == MainViewMode.ProjectSetup)
            {
                // Not a seemingly dead double click: surface the window or the blocking dialog; the request stays queued.
                if (!ReferenceEquals(_lastBlockedRequest, request)) { _lastBlockedRequest = request; Surface(RetryTarget); }
                return false;
            }
            Activate();
            // Unprocessed (the workspace closed or became unable meanwhile) leaves the request queued; any real outcome consumes it.
            return await _model.HandleOpenRequestAsync(request.Value!);
        }
        // Activate and OpenUri only bring the window forward; URI semantics are not defined by this phase.
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        return true;
    }

    private async void ShowSettings(object? sender, RoutedEventArgs e)
    {
        if (_initializing || _model.IsWorkspaceBusy || IsWorkspaceClosing || _settingsWindow is not null) return;
        var dialog = new SettingsWindow(new(_model.Settings, _model.ApplySettingsAsync)) { Icon = Icon };
        _settingsWindow = dialog;
        _model.SetSettingsDialogOpen(true);
        try { await dialog.ShowDialog(this); }
        finally
        {
            _settingsWindow = null;
            // Leave the protection in place through activation and deferred focus events.
            Dispatcher.UIThread.Post(() => _model.SetSettingsDialogOpen(false), DispatcherPriority.Background);
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
        if (_initializing || IsWorkspaceClosing || _releaseNotesWindow is not null) return;
        try
        {
            _releaseNotesWindow = new DocumentWindow(Strings.WhatsNew, ApplicationDocuments.ReadReleaseNotes()) { Icon = Icon };
            await _releaseNotesWindow.ShowDialog(this);
        }
        catch (Exception)
        {
            await _dialogs.ShowErrorAsync(Strings.WhatsNewOpenError);
        }
        finally { _releaseNotesWindow = null; }
    }

    private async void ShowAbout(object? sender, RoutedEventArgs e)
    {
        if (_initializing || _model.IsWorkspaceBusy || IsWorkspaceClosing || _aboutWindow is not null || _settingsWindow is not null) return;
        var dialog = new AboutWindow { Icon = Icon };
        _aboutWindow = dialog;
        try { await dialog.ShowDialog(this); }
        finally { _aboutWindow = null; }
    }

    private void ProjectKeyDown(object? sender, KeyEventArgs e)
    {
        if (_initializing || _settingsWindow is not null) return;
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
        if (!await _dialogs.ConfirmDiscardPendingRequestsAsync(name, pending.Count)) return false;
        confirmed.UnionWith(pending);
        return true;
    }

    private async void WindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeApproved) return;
        e.Cancel = true;
        // The close of this workspace is its WorkspaceCloseTransaction; the reservation is taken before the first await.
        await _host.Workspaces.CloseAsync(_handle, WorkspaceCloseReason.Window);
    }
}
