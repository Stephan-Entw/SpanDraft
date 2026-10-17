using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace SpanDraft.Desktop;

public partial class App : Application
{
    private readonly DesktopStartupContext? _startup;

    public App() : this(null) { }
    public App(DesktopStartupContext? startup) => _startup = startup;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow(_startup);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
