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
    private readonly MainWindowViewModel _model;
    private readonly KeyModifiers _primary = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
    private bool _closing;
    private bool _closeApproved;
    private AboutWindow? _aboutWindow;

    public MainWindow()
    {
        InitializeComponent();
        var files = new ProjectFileStore();
        _model = new(files: files, dialogs: new ProjectDialogs(this), recovery: ProjectRecovery.Local(files));
        DataContext = _model;
        NewMenuItem.InputGesture = new(Key.N, _primary);
        OpenMenuItem.InputGesture = new(Key.O, _primary);
        SaveMenuItem.InputGesture = new(Key.S, _primary);
        SaveAsMenuItem.InputGesture = new(Key.S, _primary | KeyModifiers.Shift);
        QuitMenuItem.Command = new ActionCommand(Close, () => !_model.IsBusy && !_closing);
        QuitMenuItem.InputGesture = new(Key.Q, _primary);
        UndoMenuItem.InputGesture = new(Key.Z, _primary);
        RedoMenuItem.InputGesture = new(Key.Z, _primary | KeyModifiers.Shift);
        AddHandler(KeyDownEvent, ProjectKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (e.Source is Control control && (control is Menu or MenuItem
                || control.FindAncestorOfType<Menu>() is not null)) _model.SetFileMenuOpen(true);
        }, RoutingStrategies.Tunnel);
        ProjectMenu.PropertyChanged += (_, e) =>
        {
            if (e.Property == MenuBase.IsOpenProperty)
            {
                if (ProjectMenu.IsOpen) _model.SetFileMenuOpen(true);
                else Dispatcher.UIThread.Post(() => _model.SetFileMenuOpen(ProjectMenu.IsOpen));
            }
        };
        Opened += async (_, _) => await _model.InitializeRecoveryAsync();
        Closing += WindowClosing;
    }

    private async void ShowAbout(object? sender, RoutedEventArgs e)
    {
        if (_model.IsBusy || _closing || _aboutWindow is not null) return;
        var dialog = new AboutWindow { Icon = Icon };
        _aboutWindow = dialog;
        try { await dialog.ShowDialog(this); }
        finally { _aboutWindow = null; }
    }

    private void ProjectKeyDown(object? sender, KeyEventArgs e)
    {
        ICommand? command = (e.Key, e.KeyModifiers) switch
        {
            (Key.N, var m) when m == _primary => _model.NewCommand,
            (Key.O, var m) when m == _primary => _model.OpenCommand,
            (Key.S, var m) when m == _primary => _model.SaveCommand,
            (Key.S, var m) when m == (_primary | KeyModifiers.Shift) => _model.SaveAsCommand,
            (Key.Q, var m) when m == _primary => QuitMenuItem.Command,
            (Key.Z, var m) when m == _primary => _model.UndoCommand,
            (Key.Z, var m) when m == (_primary | KeyModifiers.Shift) => _model.RedoCommand,
            (Key.Y, KeyModifiers.Control) when !OperatingSystem.IsMacOS() => _model.RedoCommand,
            _ => null
        };
        if (command is null) return;
        e.Handled = true;
        if (command.CanExecute(null)) command.Execute(null);
    }

    private async void WindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeApproved) return;
        e.Cancel = true;
        if (_closing || _model.IsBusy) return;
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
