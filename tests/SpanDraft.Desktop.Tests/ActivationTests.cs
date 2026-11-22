using Avalonia.Controls.ApplicationLifetimes;
using SpanDraft.Desktop.Activation;
using Xunit;

namespace SpanDraft.Desktop.Tests;

public sealed class ActivationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "spandraft-activation-" + Guid.NewGuid().ToString("N"));
    private readonly List<IDisposable> _disposables = [];
    private static readonly InstanceCoordinatorOptions Fast = new()
    {
        InitialBackoff = TimeSpan.FromMilliseconds(5), MaxBackoff = TimeSpan.FromMilliseconds(20),
        ConnectTimeout = TimeSpan.FromSeconds(2), BootstrapTimeout = TimeSpan.FromMilliseconds(400)
    };

    private DataScope Scope() => new(_root);
    private T Track<T>(T item) where T : IDisposable { _disposables.Add(item); return item; }

    public void Dispose()
    {
        foreach (var item in _disposables.AsEnumerable().Reverse()) item.Dispose();
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    [Fact]
    public void ArgumentsBecomeOneOfTheThreeRequestTypes()
    {
        string path = Path.Combine(_root, "a.spandraft");
        Assert.Equal(LaunchRequest.Activate, LaunchRequest.FromArguments(null));
        Assert.Equal(LaunchRequest.Activate, LaunchRequest.FromArguments([]));
        Assert.Equal(LaunchRequest.Activate, LaunchRequest.FromArguments(["notes.txt"]));
        Assert.Equal(LaunchRequest.OpenProject(path), LaunchRequest.FromArguments([path]));
        Assert.Equal(LaunchRequest.OpenUri("spandraft://open/x"), LaunchRequest.FromArguments(["spandraft://open/x"]));
        Assert.True(LaunchRequest.FromArguments(["rel.spandraft"]).IsValid);
    }

    [Fact]
    public void RequestValidationRejectsMalformedAndOversizedPayloads()
    {
        Assert.True(LaunchRequest.Activate.IsValid);
        Assert.False(new LaunchRequest(LaunchRequestType.Activate, "x").IsValid);
        Assert.False(LaunchRequest.OpenProject("relative.spandraft").IsValid);
        Assert.False(LaunchRequest.OpenProject(Path.Combine(_root, new string('a', 5000))).IsValid);
        Assert.False(LaunchRequest.OpenUri("https://example.com").IsValid);
        Assert.False(new LaunchRequest((LaunchRequestType)99).IsValid);
    }

    [Fact]
    public void ProtocolRoundTripsAndChecksVersionAndScope()
    {
        var request = LaunchRequest.OpenProject(Path.Combine(_root, "p.spandraft"));
        string line = BrokerProtocol.EncodeRequest("scope1", request);
        Assert.Equal(request, BrokerProtocol.DecodeRequest(line, "scope1"));
        Assert.Null(BrokerProtocol.DecodeRequest(line, "other"));
        Assert.Null(BrokerProtocol.DecodeRequest(line.Replace("\"V\":1", "\"V\":2"), "scope1"));
        Assert.Null(BrokerProtocol.DecodeRequest("not json", "scope1"));
        Assert.Null(BrokerProtocol.DecodeRequest(BrokerProtocol.EncodeRequest("scope1", new((LaunchRequestType)7)), "scope1"));
        foreach (var status in Enum.GetValues<BrokerStatus>())
            Assert.Equal(status, BrokerProtocol.DecodeResponse(BrokerProtocol.EncodeResponse(status)));
    }

    [Fact]
    public void OnlyOneProcessWinsTheElectionAndTheScopeIdIsStable()
    {
        var first = Track(PrimaryInstance.TryAcquire(Scope())!);
        Assert.Null(PrimaryInstance.TryAcquire(Scope()));
        Assert.Equal(first.Metadata, InstanceMetadata.TryRead(Scope()));
        first.Dispose();
        var second = Track(PrimaryInstance.TryAcquire(Scope())!);
        Assert.Equal(first.Metadata.DataScopeId, second.Metadata.DataScopeId);
    }

    [Fact]
    public async Task ConcurrentFirstStartsCreateExactlyOneMetadataAndOnePrimary()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() => PrimaryInstance.TryAcquire(Scope()), TestContext.Current.CancellationToken)));
        var winners = results.Where(static r => r is not null).ToList();
        winners.ForEach(w => Track(w!));
        Assert.Single(winners);
        Assert.Equal(winners[0]!.Metadata, InstanceMetadata.TryRead(Scope()));
    }

    [Fact]
    public void SecondaryMetadataReadNeverCreatesFilesOrReadsOtherData()
    {
        Assert.Null(InstanceMetadata.TryRead(Scope()));
        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public async Task StartingEndpointAnswersRetryThenAcceptedAfterHandlerSwitch()
    {
        var scope = Scope();
        var primary = Track(PrimaryInstance.TryAcquire(scope)!);
        var broker = Track(BrokerServer.Start(primary.Metadata));
        var request = LaunchRequest.OpenProject(Path.Combine(_root, "x.spandraft"));
        Assert.Equal(BrokerStatus.Retry, await BrokerClient.SendAsync(primary.Metadata, request, TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));

        var received = new List<LaunchRequest>();
        broker.Handler = r => { received.Add(r); return BrokerStatus.Accepted; };
        var outcome = await InstanceCoordinator.ResolveAsync(scope, request, Fast, TestContext.Current.CancellationToken);
        Assert.Equal(InstanceOutcomeKind.Forwarded, outcome.Kind);
        Assert.Equal(request, Assert.Single(received));
    }

    [Fact]
    public async Task RetryKeepsTheSecondaryWaitingWithoutTimeoutAndNeverCreatesASecondPrimary()
    {
        var scope = Scope();
        var primary = Track(PrimaryInstance.TryAcquire(scope)!);
        Track(BrokerServer.Start(primary.Metadata));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1.5));
        // Retry outlives the 400 ms bootstrap timeout because a live primary answering Retry is not a failure.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            InstanceCoordinator.ResolveAsync(scope, LaunchRequest.Activate, Fast, cts.Token));
    }

    [Fact]
    public async Task WaitingSecondaryBecomesPrimaryWhenTheLockIsReleased()
    {
        var scope = Scope();
        var old = PrimaryInstance.TryAcquire(scope)!;
        var broker = BrokerServer.Start(old.Metadata);
        var waiting = InstanceCoordinator.ResolveAsync(scope, LaunchRequest.OpenProject(Path.Combine(_root, "b.spandraft")), Fast, TestContext.Current.CancellationToken);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.False(waiting.IsCompleted);
        broker.Dispose(); old.Dispose();
        var outcome = await waiting.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(InstanceOutcomeKind.Primary, outcome.Kind);
        Track(outcome.Primary!);
        Assert.Equal(old.Metadata.DataScopeId, outcome.Primary!.Metadata.DataScopeId);
    }

    [Fact]
    public async Task RejectedEndsTheAttemptWithoutBecomingPrimary()
    {
        var scope = Scope();
        var primary = Track(PrimaryInstance.TryAcquire(scope)!);
        var broker = Track(BrokerServer.Start(primary.Metadata));
        broker.Handler = _ => BrokerStatus.Rejected;
        var outcome = await InstanceCoordinator.ResolveAsync(scope, LaunchRequest.Activate, Fast, TestContext.Current.CancellationToken);
        Assert.Equal(InstanceOutcomeKind.Rejected, outcome.Kind);
    }

    [Fact]
    public async Task BootstrapRaceIsToleratedAndAHungLockHolderEndsInCommunicationFailure()
    {
        var scope = Scope();
        var primary = Track(PrimaryInstance.TryAcquire(scope)!);
        // Lock and metadata exist, endpoint not yet: the endpoint appears within the timeout, so no failure.
        var waiting = InstanceCoordinator.ResolveAsync(scope, LaunchRequest.Activate, Fast with { BootstrapTimeout = TimeSpan.FromSeconds(5) }, TestContext.Current.CancellationToken);
        await Task.Delay(150, TestContext.Current.CancellationToken);
        Assert.False(waiting.IsCompleted);
        var broker = Track(BrokerServer.Start(primary.Metadata));
        broker.Handler = _ => BrokerStatus.Accepted;
        Assert.Equal(InstanceOutcomeKind.Forwarded, (await waiting.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)).Kind);

        broker.Dispose();
        var failure = await InstanceCoordinator.ResolveAsync(scope, LaunchRequest.Activate, Fast, TestContext.Current.CancellationToken);
        Assert.Equal(InstanceOutcomeKind.CommunicationFailure, failure.Kind);
        Assert.Null(failure.Primary);
    }

    [Fact]
    public async Task ServerRejectsGarbageAndForeignScopes()
    {
        var primary = Track(PrimaryInstance.TryAcquire(Scope())!);
        Track(BrokerServer.Start(primary.Metadata));
        // Same endpoint, different scope id: handshake mismatch.
        var mismatched = new ForeignEndpoint(primary.Metadata.Endpoint);
        Assert.Equal(BrokerStatus.Rejected, await mismatched.SendAsync("ffff", LaunchRequest.Activate));
    }

    private sealed record ForeignEndpoint(string Endpoint)
    {
        public async Task<BrokerStatus?> SendAsync(string scopeId, LaunchRequest request)
        {
            await using var pipe = new System.IO.Pipes.NamedPipeClientStream(".", Endpoint, System.IO.Pipes.PipeDirection.InOut,
                System.IO.Pipes.PipeOptions.Asynchronous | System.IO.Pipes.PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(2000, TestContext.Current.CancellationToken);
            await BrokerProtocol.WriteLineAsync(pipe, BrokerProtocol.EncodeRequest(scopeId, request), TestContext.Current.CancellationToken);
            return BrokerProtocol.DecodeResponse((await BrokerProtocol.ReadLineAsync(pipe, TestContext.Current.CancellationToken))!);
        }
    }

    [Fact]
    public async Task DispatcherKeepsRequestsQueuedUntilTheConsumerCanTakeThem()
    {
        var dispatcher = new ActivationDispatcher();
        var a = LaunchRequest.OpenProject(Path.Combine(_root, "a.spandraft"));
        var b = LaunchRequest.OpenProject(Path.Combine(_root, "b.spandraft"));
        dispatcher.Enqueue(a); dispatcher.Enqueue(b);
        await dispatcher.PumpAsync();
        Assert.Equal(2, dispatcher.Count);

        bool available = false;
        var seen = new List<LaunchRequest>();
        dispatcher.SetConsumer(r => { if (!available) return Task.FromResult(false); seen.Add(r); return Task.FromResult(true); });
        await dispatcher.PumpAsync();
        Assert.Equal(2, dispatcher.Count);
        available = true;
        await dispatcher.PumpAsync();
        Assert.Equal([a, b], seen);
        Assert.Equal(0, dispatcher.Count);
    }

    [Fact]
    public async Task DispatcherRejectsInvalidRequestsAndDropsAFailedRequestOnlyOnce()
    {
        var dispatcher = new ActivationDispatcher();
        Assert.Throws<ArgumentException>(() => dispatcher.Enqueue(LaunchRequest.OpenProject("rel")));
        dispatcher.Enqueue(LaunchRequest.Activate); dispatcher.Enqueue(LaunchRequest.Activate);
        int calls = 0;
        dispatcher.SetConsumer(_ => ++calls == 1 ? throw new InvalidOperationException() : Task.FromResult(true));
        await Assert.ThrowsAsync<InvalidOperationException>(dispatcher.PumpAsync);
        Assert.Equal(1, dispatcher.Count);
        await dispatcher.PumpAsync();
        Assert.Equal(0, dispatcher.Count);
    }

    [Fact]
    public async Task PumpTriggeredDuringProcessingIsNotLost()
    {
        var dispatcher = new ActivationDispatcher();
        var gate = new TaskCompletionSource<bool>();
        var seen = new List<LaunchRequest>();
        dispatcher.SetConsumer(async r => { seen.Add(r); await gate.Task; return true; });
        dispatcher.Enqueue(LaunchRequest.Activate);
        var pump = dispatcher.PumpAsync();
        dispatcher.Enqueue(LaunchRequest.Activate);
        await dispatcher.PumpAsync();
        gate.SetResult(true);
        await pump;
        Assert.Equal(2, seen.Count);
        Assert.Equal(0, dispatcher.Count);
    }

    [Fact]
    public void HostOnlyAcceptsExternalRequestsWhenReady()
    {
        var host = new DesktopApplicationHost();
        int retryServed = 0;
        host.RetryServed += () => retryServed++;
        var request = LaunchRequest.Activate;
        Assert.Equal(BrokerStatus.Retry, host.HandleBrokerRequest(request));
        Assert.Equal(0, host.Activation.Count);
        Assert.Equal(1, retryServed);

        host.MarkReady();
        Assert.Equal(BrokerStatus.Accepted, host.HandleBrokerRequest(request));
        Assert.Equal(1, host.Activation.Count);

        host.BeginQuit();
        Assert.Equal(BrokerStatus.Retry, host.HandleBrokerRequest(request));
        Assert.Equal(1, host.Activation.Count);
    }

    [Fact]
    public async Task AttachedBrokerRoutesAcceptedRequestsIntoTheHostQueue()
    {
        var primary = Track(PrimaryInstance.TryAcquire(Scope())!);
        var broker = Track(BrokerServer.Start(primary.Metadata));
        var host = new DesktopApplicationHost();
        host.AttachBroker(broker);
        var request = LaunchRequest.OpenUri("spandraft://open/demo");
        Assert.Equal(BrokerStatus.Retry, await BrokerClient.SendAsync(primary.Metadata, request, TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
        Assert.Equal(0, host.Activation.Count);
        host.MarkReady();
        Assert.Equal(BrokerStatus.Accepted, await BrokerClient.SendAsync(primary.Metadata, request, TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
        Assert.Equal(1, host.Activation.Count);
    }

    [Fact]
    public void MacActivationMapsToTheSharedRequestModel()
    {
        Assert.Equal([LaunchRequest.Activate], MacActivationSource.ToRequests(new ActivatedEventArgs(ActivationKind.Reopen)));
        Assert.Empty(MacActivationSource.ToRequests(new ActivatedEventArgs(ActivationKind.Background)));
        Assert.Equal([LaunchRequest.OpenUri("spandraft://open/x")],
            MacActivationSource.ToRequests(new ProtocolActivatedEventArgs(new Uri("spandraft://open/x"))));
        Assert.Empty(MacActivationSource.ToRequests(new ProtocolActivatedEventArgs(new Uri("https://example.com"))));
    }
}
