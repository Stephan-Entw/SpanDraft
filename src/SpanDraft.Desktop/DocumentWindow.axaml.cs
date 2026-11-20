using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using ColorTextBlock.Avalonia;

namespace SpanDraft.Desktop;

internal partial class DocumentWindow : Window
{
    public DocumentWindow(string title, string markdown)
    {
        InitializeComponent();
        Title = title;
        DocumentContent.Markdown = markdown;
        Opened += OnOpened;
        AddHandler(KeyDownEvent, CloseOnEscape, RoutingStrategies.Tunnel);
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        // Markdown code blocks set scroll and wrap options locally, overriding styles.
        foreach (var scroll in DocumentContent.GetVisualDescendants().OfType<ScrollViewer>()
                     .Where(viewer => viewer.Classes.Contains("CodeBlock")))
        {
            scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            if (scroll.Content is TextBlock text)
                text.TextWrapping = TextWrapping.Wrap;
        }

        foreach (var marker in DocumentContent.GetVisualDescendants().OfType<CTextBlock>()
                     .Where(block => block.Classes.Contains("ListMarker")))
        {
            if (marker.Content.FirstOrDefault() is CRun { Text: "○" or "❏" } bullet)
                marker.Content[0] = new CRun { Text = bullet.Text == "○" ? "•" : "▪" };
        }

        DocumentContent.Focus();
    }

    private void CloseOnEscape(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        Close();
    }
}
