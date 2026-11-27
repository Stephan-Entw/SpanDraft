using SpanDraft.Desktop;
using SpanDraft.Desktop.Activation;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Presentation;

// Usage: <dataScopeDirectory> <launchArgument>
// Drives the real election and broker contract from its own OS process. It never uses DesktopBootstrap, so no
// StartupErrorReporter dialog can block a test run; outcomes are printed as single lines on stdout instead.
// stdin: "go" starts the election (shared start gate), then "ready" marks the Primary Ready and "exit" releases it.
var scope = new DataScope(args[0]);
var request = LaunchRequest.FromArguments([args[1]]);
Say("waiting");
if (Console.ReadLine() != "go") return 2;

var outcome = await InstanceCoordinator.ResolveAsync(scope, request);
if (outcome.Primary is not { } primary)
{
    Say($"outcome {outcome.Kind}");
    return outcome.Kind == InstanceOutcomeKind.Forwarded ? 0 : 1;
}

using (primary)
using (var broker = BrokerServer.Start(primary.Metadata))
{
    // Domain stores live outside the DataScope and are never loaded; the host only needs them to exist.
    var files = new ProjectFileStore();
    string stores = scope.Directory + "-stores";
    var host = new DesktopApplicationHost(new DesktopStartupContext(files,
        new LocalSettingsStore(files, Path.Combine(stores, "settings.json")), UserSettings.Default)
    {
        StateStores = DesktopStateStores.InDirectory(files, stores),
        UserNameProvider = static () => null
    });
    host.SetInitialRequest(request);
    host.AttachBroker(broker);
    var handleBrokerRequest = broker.Handler;
    broker.Handler = incoming =>
    {
        var status = handleBrokerRequest(incoming);
        if (status == BrokerStatus.Retry) Say($"retry {Describe(incoming)}");
        return status;
    };
    host.Activation.SetConsumer(handled => { Say($"handled {Describe(handled)}"); return Task.FromResult(true); });
    host.Activation.Enqueued += () => _ = host.Activation.PumpAsync();
    Say($"primary {Describe(request)}");
    for (string? command; (command = Console.ReadLine()) is not null and not "exit";)
        if (command == "ready") host.MarkReady();
    host.BeginShutdown();
}
return 0;

static string Describe(LaunchRequest request) => $"{request.Type} {request.Value}";

static void Say(string line)
{
    Console.Out.WriteLine(line);
    Console.Out.Flush();
}
