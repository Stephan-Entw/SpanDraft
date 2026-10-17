using Avalonia;
using System.Globalization;
using SpanDraft.Desktop.Persistence;

namespace SpanDraft.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        var systemCulture = CultureInfo.CurrentCulture;
        var systemUiCulture = CultureInfo.CurrentUICulture;
        var files = new ProjectFileStore();
        var startup = DesktopStartupContext.LoadAsync(files, LocalSettingsStore.Local(files)).GetAwaiter().GetResult();
        CultureConfigurator.Apply(CultureConfigurator.Resolve(startup.Settings, systemCulture, systemUiCulture));
        return AppBuilder.Configure(() => new App(startup))
            .UsePlatformDetect()
            .LogToTrace();
    }
}
