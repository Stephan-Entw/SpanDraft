using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.ViewModels;

namespace SpanDraft.Desktop;

public partial class DiagnosticsWindow : Window
{
    private readonly Func<string, Task> _copy;
    private readonly DispatcherTimer _copied = new() { Interval = TimeSpan.FromSeconds(2) };

    public DiagnosticsWindow() : this(new DiagnosticsViewModel(null)) { }

    /// <param name="copy">Puts text on the clipboard; replaceable because a test platform has none.</param>
    public DiagnosticsWindow(DiagnosticsViewModel model, Func<string, Task>? copy = null)
    {
        InitializeComponent();
        DataContext = model;
        _copy = copy ?? new Func<string, Task>(text => TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(text) ?? Task.CompletedTask);
        // The model owns the history listener; once the window is gone it must cost nothing.
        Closed += (_, _) => { _copied.Stop(); DataContext = null; model.Dispose(); };
        Opened += (_, _) => Focus();
        _copied.Tick += (_, _) => { _copied.Stop(); CopyButton.Content = Strings.DiagnosticsCopy; };
        // The rows lose the width of their scroll bar once they scroll; the header gives up the same width to stay aligned.
        var rows = this.FindControl<ScrollViewer>("HistoryScroller")!;
        var frame = this.FindControl<Border>("HeaderFrame")!;
        rows.ScrollChanged += (_, _) =>
            frame.Padding = new Thickness(12, 0, 12 + Math.Max(0, rows.Bounds.Width - rows.Viewport.Width), 0);
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            Close();
        }, RoutingStrategies.Tunnel);
    }

    private async void CopyValues(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DiagnosticsViewModel model) return;
        try { await _copy(model.ClipboardText()); }
        catch (Exception) { return; } // A busy or missing clipboard is not worth interrupting the user.
        if (DataContext is null) return; // Closed while copying.
        CopyButton.Content = Strings.DiagnosticsCopied;
        _copied.Stop(); _copied.Start();
    }
}
