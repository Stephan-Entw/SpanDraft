using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

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
            desktop.MainWindow = new MainWindow(_host, desktop.Args);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
