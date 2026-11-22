using Avalonia;
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
            desktop.MainWindow = new MainWindow(_host);
            if (OperatingSystem.IsMacOS()) MacActivationSource.Attach(this, _host.Activation);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
