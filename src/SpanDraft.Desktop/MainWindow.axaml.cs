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

namespace SpanDraft.Desktop;

public partial class MainWindow : Window
{
    private MainWindowViewModel _model = null!;
    private InteractionPerformance? _performance;
    private AvaloniaInteractionFrameProbe? _frameProbe;
    private readonly KeyModifiers _primary = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
    private ProjectDialogs _dialogs = null!;
    private readonly DesktopApplicationHost _host;
    private bool _closing;
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

    public MainWindow() : this(new DesktopApplicationHost()) { }

    public MainWindow(DesktopApplicationHost host, string[]? args = null)
    {
        InitializeComponent();
        _host = host;
        var files = host.Files;
        NewMenuItem.InputGesture = new(Key.N, _primary);
        OpenMenuItem.InputGesture = new(Key.O, _primary);
        SaveMenuItem.InputGesture = new(Key.S, _primary);
        SaveAsMenuItem.InputGesture = new(Key.S, _primary | KeyModifiers.Shift);
        ReportMenuItem.InputGesture = new(Key.P, _primary);
        var quitCommand = new ActionCommand(Close, () => !_initializing && !_model.IsWorkspaceBusy && !_closing);
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
                var stores = host.StateStores;
                _model = new(performance: _performance, files: files, dialogs: _dialogs, recovery: stores.Recovery,
                    setupRecovery: stores.SetupRecovery,
                    settingsStore: host.SettingsStore, settings: host.Settings,
                    materialStore: stores.Materials, sectionStore: stores.Sections,
                    recentStore: stores.Recents, openUserManual: OpenUserManualAsync,
                    userNameProvider: host.UserNameProvider);
                await _model.InitializeLibrariesAsync();
                DataContext = _model;
                if (!await _model.InitializeRecoveryAsync())
                {
                    // An unresolved recovery must bypass the normal leave workflow, which deletes its slot.
                    _closeApproved = true;
                    _initialization.TrySetCanceled();
                    Close();
                    return;
                }
                _initializing = false;
                quitCommand.Refresh();
                await _model.InitializeRecentProjectsAsync();
                host.MarkReady();
                // A restored setup draft takes precedence over the shell's open request.
                if (_model.Mode != MainViewMode.ProjectSetup && args is [var path]
                    && string.Equals(Path.GetExtension(path), ".spandraft", StringComparison.OrdinalIgnoreCase))
                    await _model.OpenAsync(path);
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
            host.BeginShutdown();
            _performance?.CancelPending();
            _frameProbe?.Dispose();
            _model?.ReleaseReport();
        };
    }

    private async void ShowSettings(object? sender, RoutedEventArgs e)
    {
        if (_initializing || _model.IsWorkspaceBusy || _closing || _settingsWindow is not null) return;
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
        if (_initializing || _closing || _performance is null) return;
        if (_diagnosticsWindow is not null) { _diagnosticsWindow.Activate(); return; }
        var window = new DiagnosticsWindow(new(_performance)) { Icon = Icon };
        _diagnosticsWindow = window;
        window.Closed += (_, _) => _diagnosticsWindow = null;
        window.Show(this);
    }

    private async void ShowWhatsNew(object? sender, RoutedEventArgs e)
    {
        if (_initializing || _closing || _releaseNotesWindow is not null) return;
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
        if (_initializing || _model.IsWorkspaceBusy || _closing || _aboutWindow is not null || _settingsWindow is not null) return;
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
        if (_initializing || _closing) return;
        var manual = new UserManualLauncher(Launcher.LaunchUriAsync);
        if (!await manual.OpenAsync()) await _dialogs.ShowErrorAsync(SpanDraft.Desktop.Resources.Strings.UserManualOpenError);
    }

    private async void WindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeApproved) return;
        e.Cancel = true;
        if (_initializing || _closing || _model.IsWorkspaceBusy || _settingsWindow is not null) return;
        _closing = true;
        _host.BeginQuit();
        try
        {
            if (!await _model.RequestCloseAsync()) { _host.CancelQuit(); return; }
            _closeApproved = true;
            IsEnabled = false;
            // Even a synchronously completed guard must finish the current Closing event first.
            Dispatcher.UIThread.Post(Close, DispatcherPriority.Send);
        }
        catch { _host.CancelQuit(); throw; }
        finally { _closing = false; }
    }
}
