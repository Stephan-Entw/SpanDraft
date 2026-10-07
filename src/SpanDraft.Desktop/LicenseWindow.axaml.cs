using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SpanDraft.Desktop.Resources;

namespace SpanDraft.Desktop;

public partial class LicenseWindow : Window
{
    public LicenseWindow()
    {
        InitializeComponent();
        LicenseContent.Text = ApplicationLicense.ReadText();
        Opened += (_, _) => LicenseContent.Focus();
        AddHandler(KeyDownEvent, CloseOnEscape, RoutingStrategies.Tunnel);
    }

    private void CloseOnEscape(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        Close();
    }
}
