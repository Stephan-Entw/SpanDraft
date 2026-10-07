using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SpanDraft.Desktop.Resources;

namespace SpanDraft.Desktop;

public partial class AboutWindow : Window
{
    private LicenseWindow? _licenseWindow;

    public AboutWindow()
    {
        InitializeComponent();
        Opened += (_, _) => Focus();
        AddHandler(KeyDownEvent, CloseOnEscape, RoutingStrategies.Tunnel);
    }

    private void CloseOnEscape(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        Close();
    }

    private async void OpenLink(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Uri uri }) return;
        LinkError.Text = Strings.LinkOpenError;
        LinkError.IsVisible = false;
        try
        {
            LinkError.IsVisible = !await Launcher.LaunchUriAsync(uri);
        }
        catch (Exception)
        {
            LinkError.IsVisible = true;
        }
    }

    private async void OpenLicense(object? sender, RoutedEventArgs e)
    {
        if (_licenseWindow is not null) return;
        LinkError.Text = Strings.LicenseOpenError;
        LinkError.IsVisible = false;
        try
        {
            _licenseWindow = new LicenseWindow { Icon = Icon };
            await _licenseWindow.ShowDialog(this);
        }
        catch (Exception)
        {
            LinkError.IsVisible = true;
        }
        finally { _licenseWindow = null; }
    }
}
