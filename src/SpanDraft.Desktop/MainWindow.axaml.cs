using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.ViewModels;

namespace SpanDraft.Desktop;

public partial class MainWindow : Window
{
    private MainWindowViewModel _model = null!;
    private readonly KeyModifiers _primary = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
    private ProjectDialogs _dialogs = null!;
    private bool _closing;
    private bool _closeApproved;
    private AboutWindow? _aboutWindow;
    private SettingsWindow? _settingsWindow;
    private bool _initializing = true;

    public MainWindow()
    {
        InitializeComponent();
        var files = new ProjectFileStore();
        var settingsStore = LocalSettingsStore.Local(files);
        NewMenuItem.InputGesture = new(Key.N, _primary);
        OpenMenuItem.InputGesture = new(Key.O, _primary);
        SaveMenuItem.InputGesture = new(Key.S, _primary);
        SaveAsMenuItem.InputGesture = new(Key.S, _primary | KeyModifiers.Shift);
        var quitCommand = new ActionCommand(Close, () => !_initializing && !_model.IsWorkspaceBusy && !_closing);
        QuitMenuItem.Command = quitCommand;
        QuitMenuItem.InputGesture = new(Key.Q, _primary);
        UndoMenuItem.InputGesture = new(Key.Z, _primary);
        RedoMenuItem.InputGesture = new(Key.Z, _primary | KeyModifiers.Shift);
        AddHandler(KeyDownEvent, ProjectKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerPressedEvent, (_, e) =>
        {
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
            var settings = await settingsStore.LoadAsync();
            _dialogs = new ProjectDialogs(this);
            _model = new(files: files, dialogs: _dialogs, recovery: ProjectRecovery.Local(files),
                settingsStore: settingsStore, settings: settings,
                materialStore: MaterialLibraryStore.Local(files), sectionStore: SectionLibraryStore.Local(files));
            await _model.InitializeLibrariesAsync();
            DataContext = _model;
            _initializing = false;
            quitCommand.Refresh();
            await _model.InitializeRecoveryAsync();
        };
        Closing += WindowClosing;
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
        if (e.Key is Key.F10 or Key.LeftAlt or Key.RightAlt)
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

    private async void ShowUserManual(object? sender, RoutedEventArgs e)
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
        try
        {
            if (!await _model.RequestCloseAsync()) return;
            _closeApproved = true;
            IsEnabled = false;
            // Even a synchronously completed guard must finish the current Closing event first.
            Dispatcher.UIThread.Post(Close, DispatcherPriority.Send);
        }
        finally { _closing = false; }
    }
}
