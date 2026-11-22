namespace SpanDraft.Desktop.Activation;

/// <summary>
/// Startup order: election and bootstrap endpoint first, only then settings and the UI. A Secondary never loads settings.
/// </summary>
public static class DesktopBootstrap
{
    public static int Run(DataScope scope, LaunchRequest request, Func<DesktopStartupContext> loadStartup,
        Func<DesktopStartupContext, BrokerServer, int> start, InstanceCoordinatorOptions? options = null)
    {
        var outcome = InstanceCoordinator.ResolveAsync(scope, request, options).GetAwaiter().GetResult();
        if (outcome.Primary is not { } primary)
        {
            if (outcome.Kind != InstanceOutcomeKind.Forwarded) StartupErrorReporter.Report(outcome.Kind);
            return outcome.Kind == InstanceOutcomeKind.Forwarded ? 0 : 1;
        }
        using (primary)
        using (var broker = BrokerServer.Start(primary.Metadata))
            return start(loadStartup(), broker);
    }
}
