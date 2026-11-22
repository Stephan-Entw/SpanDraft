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
        // Election and the bootstrap endpoint come first: no settings, culture or Avalonia before the Primary is known.
        var outcome = InstanceCoordinator.ResolveAsync(DataScope.Local(), request).GetAwaiter().GetResult();
        if (outcome.Primary is not { } primary)
        {
            if (outcome.Kind != InstanceOutcomeKind.Forwarded) StartupErrorReporter.Report(outcome.Kind);
            return outcome.Kind == InstanceOutcomeKind.Forwarded ? 0 : 1;
        }
        using (primary)
        using (var broker = BrokerServer.Start(primary.Metadata))
        {
            BuildAvaloniaApp(broker, request).StartWithClassicDesktopLifetime(args);
        }
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp(BrokerServer? broker = null, LaunchRequest? initial = null)
    {
        var systemCulture = CultureInfo.CurrentCulture;
        var systemUiCulture = CultureInfo.CurrentUICulture;
        var files = new ProjectFileStore();
        var startup = DesktopStartupContext.LoadAsync(files, LocalSettingsStore.Local(files)).GetAwaiter().GetResult();
        CultureConfigurator.Apply(CultureConfigurator.Resolve(startup.Settings, systemCulture, systemUiCulture));
        var host = new DesktopApplicationHost(startup);
        if (broker is not null) host.AttachBroker(broker);
        if (initial is { Type: not LaunchRequestType.Activate }) host.Activation.Enqueue(initial);
        return AppBuilder.Configure(() => new App(host))
            .UsePlatformDetect()
            .LogToTrace();
    }
}
