using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SpanDraft.Desktop.Activation;

namespace SpanDraft.Desktop;

public partial class App : Application
{
    private readonly DesktopApplicationHost _host;

    public App() : this(new DesktopApplicationHost()) { }
    public App(DesktopApplicationHost host) => _host = host;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnLastWindowClose;
            desktop.MainWindow = _host.Workspaces.CreateInitial().Window;
            if (OperatingSystem.IsMacOS()) MacActivationSource.Attach(this, _host);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
