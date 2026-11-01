using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SpanDraft.Desktop.Resources;

namespace SpanDraft.Desktop;

public partial class PrivacyWindow : Window
{
    public PrivacyWindow()
    {
        InitializeComponent();
        PrivacyContent.Markdown = ApplicationPrivacy.ReadText();
        Opened += (_, _) => PrivacyContent.Focus();
        AddHandler(KeyDownEvent, CloseOnEscape, RoutingStrategies.Tunnel);
    }

    private void CloseOnEscape(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        Close();
    }
}
