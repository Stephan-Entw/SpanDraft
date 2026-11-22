using Avalonia;
using System.Globalization;
using SpanDraft.Desktop.Activation;
using SpanDraft.Desktop.Persistence;

namespace SpanDraft.Desktop;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var request = LaunchRequest.FromArguments(args);
        var systemCulture = CultureInfo.CurrentCulture;
        var systemUiCulture = CultureInfo.CurrentUICulture;
        return DesktopBootstrap.Run(DataScope.Local(), request,
            () =>
            {
                var files = new ProjectFileStore();
                return DesktopStartupContext.LoadAsync(files, LocalSettingsStore.Local(files)).GetAwaiter().GetResult();
            },
            (startup, broker) =>
            {
                BuildAvaloniaApp(startup, systemCulture, systemUiCulture, broker, request).StartWithClassicDesktopLifetime(args);
                return 0;
            });
    }

    public static AppBuilder BuildAvaloniaApp(DesktopStartupContext startup, CultureInfo systemCulture,
        CultureInfo systemUiCulture, BrokerServer? broker = null, LaunchRequest? initial = null)
    {
        CultureConfigurator.Apply(CultureConfigurator.Resolve(startup.Settings, systemCulture, systemUiCulture));
        var host = new DesktopApplicationHost(startup);
        if (broker is not null) host.AttachBroker(broker);
        if (initial is { Type: not LaunchRequestType.Activate }) host.Activation.Enqueue(initial);
        return AppBuilder.Configure(() => new App(host))
            .UsePlatformDetect()
            .LogToTrace();
    }
}
