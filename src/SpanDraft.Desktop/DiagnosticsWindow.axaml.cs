using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SpanDraft.Desktop.ViewModels;

namespace SpanDraft.Desktop;

public partial class DiagnosticsWindow : Window
{
    public DiagnosticsWindow() : this(new DiagnosticsViewModel(null)) { }

    public DiagnosticsWindow(DiagnosticsViewModel model)
    {
        InitializeComponent();
        DataContext = model;
        // The model owns the history listener; once the window is gone it must cost nothing.
        Closed += (_, _) => { DataContext = null; model.Dispose(); };
        Opened += (_, _) => Focus();
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            Close();
        }, RoutingStrategies.Tunnel);
    }
}
