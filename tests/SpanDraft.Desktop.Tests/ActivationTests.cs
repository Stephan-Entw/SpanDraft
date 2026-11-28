using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using Avalonia.Controls.ApplicationLifetimes;
using SpanDraft.Desktop.Activation;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

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

    private static CancellationTokenSource ResolutionDeadline()
    {
        var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        stop.CancelAfter(TimeSpan.FromSeconds(5));
        return stop;
    }

    private static async Task StopResolutionsAsync(CancellationTokenSource stop, params Task<InstanceOutcome>[] resolutions)
    {
        await stop.CancelAsync();
        await Task.WhenAll(resolutions.Select(async resolution =>
        {
            try { (await resolution.WaitAsync(TimeSpan.FromSeconds(5))).Primary?.Dispose(); }
            catch (OperationCanceledException) when (resolution.IsCanceled) { }
        }));
    }

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
            {
            Assert.Equal(status, BrokerProtocol.DecodeResponse(BrokerProtocol.EncodeResponse(status, "scope1"), "scope1"));
            Assert.Null(BrokerProtocol.DecodeResponse(BrokerProtocol.EncodeResponse(status, "scope1"), "other"));
        }
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
        using var stop = ResolutionDeadline();
        var outcome = await InstanceCoordinator.ResolveAsync(scope, request, Fast, stop.Token);
        using var unexpectedPrimary = outcome.Primary;
        Assert.Equal(InstanceOutcomeKind.Forwarded, outcome.Kind);
        Assert.Equal(request, Assert.Single(received));
    }

    [Fact]
    public async Task RetryKeepsTheSecondaryWaitingWithoutTimeoutAndNeverCreatesASecondPrimary()
    {
        var scope = Scope();
        var primary = Track(PrimaryInstance.TryAcquire(scope)!);
        var request = LaunchRequest.OpenProject(Path.Combine(_root, "retry.spandraft"));
        var broker = Track(BrokerServer.Start(primary.Metadata));
        var retries = new ConcurrentQueue<LaunchRequest>();
        var retriedPastBootstrap = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var retryTime = new Stopwatch();
        broker.Handler = received =>
        {
            retries.Enqueue(received);
            // By the second request the first IPC attempt has finished, even if it saw no reply.
            if (retries.Count == 2) retryTime.Start();
            if (retries.Count >= 4 && retryTime.Elapsed > Fast.BootstrapTimeout)
                retriedPastBootstrap.TrySetResult();
            return BrokerStatus.Retry;
        };

        using var stop = ResolutionDeadline();
        var waiting = InstanceCoordinator.ResolveAsync(scope, request, Fast, stop.Token);
        try
        {
            await WaitFor(Task.WhenAny(retriedPastBootstrap.Task, waiting));
            Assert.True(retriedPastBootstrap.Task.IsCompletedSuccessfully,
                $"Expected repeated broker requests beyond the bootstrap timeout. Saw {retries.Count}; secondary task: {waiting.Status}.");
            Assert.False(waiting.IsCompleted, "The secondary stopped while the primary was still answering Retry.");
            using var unexpectedWhileWaiting = PrimaryInstance.TryAcquire(scope);
            Assert.Null(unexpectedWhileWaiting);

            var accepted = new TaskCompletionSource<LaunchRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
            broker.Handler = received => { accepted.TrySetResult(received); return BrokerStatus.Accepted; };
            var outcome = await waiting;
            Assert.Equal(InstanceOutcomeKind.Forwarded, outcome.Kind);
            Assert.Null(outcome.Primary);
            Assert.Equal(request, await WaitFor(accepted.Task));
            Assert.All(retries, retried => Assert.Equal(request, retried));
            using var unexpectedAfterForwarding = PrimaryInstance.TryAcquire(scope);
            Assert.Null(unexpectedAfterForwarding);
        }
        finally { await StopResolutionsAsync(stop, waiting); }
    }

    [Fact]
    public async Task WaitingSecondaryBecomesPrimaryWhenTheLockIsReleased()
    {
        var scope = Scope();
        using var old = PrimaryInstance.TryAcquire(scope)!;
        using var broker = BrokerServer.Start(old.Metadata);
        var retried = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int attempts = 0;
        broker.Handler = _ =>
        {
            if (Interlocked.Increment(ref attempts) == 2) retried.TrySetResult();
            return BrokerStatus.Retry;
        };
        using var stop = ResolutionDeadline();
        var waiting = InstanceCoordinator.ResolveAsync(scope, LaunchRequest.OpenProject(Path.Combine(_root, "b.spandraft")), Fast, stop.Token);
        try
        {
            await WaitForCheckpoint(retried.Task, waiting);
            Assert.False(waiting.IsCompleted);
            broker.Dispose(); old.Dispose();
            var outcome = await waiting;
            Assert.Equal(InstanceOutcomeKind.Primary, outcome.Kind);
            Assert.Equal(old.Metadata.DataScopeId, outcome.Primary!.Metadata.DataScopeId);
        }
        finally { await StopResolutionsAsync(stop, waiting); }
    }

    [Fact]
    public async Task TwoWaitingSecondariesYieldOnePrimaryAndOneForwardedRequestAfterTheOldPrimaryEnds()
    {
        var scope = Scope();
        using var old = PrimaryInstance.TryAcquire(scope)!;
        using var oldBroker = BrokerServer.Start(old.Metadata);
        var r1 = LaunchRequest.OpenProject(Path.Combine(_root, "w1.spandraft"));
        var r2 = LaunchRequest.OpenProject(Path.Combine(_root, "w2.spandraft"));
        var firstRetried = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondRetried = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = new ConcurrentDictionary<LaunchRequest, int>();
        oldBroker.Handler = received =>
        {
            if (attempts.AddOrUpdate(received, 1, static (_, count) => count + 1) == 2)
            {
                if (received == r1) firstRetried.TrySetResult();
                if (received == r2) secondRetried.TrySetResult();
            }
            return BrokerStatus.Retry;
        };
        using var stop = ResolutionDeadline();
        var w1 = InstanceCoordinator.ResolveAsync(scope, r1, Fast, stop.Token);
        var w2 = InstanceCoordinator.ResolveAsync(scope, r2, Fast, stop.Token);
        var election = Task.WhenAny(w1, w2);
        try
        {
            await WaitForCheckpoint(Task.WhenAll(firstRetried.Task, secondRetried.Task), election);
            Assert.False(w1.IsCompleted || w2.IsCompleted);
            oldBroker.Dispose(); old.Dispose();

            var winner = await election;
            var winnerOutcome = await winner;
            Assert.Equal(InstanceOutcomeKind.Primary, winnerOutcome.Kind);
            var received = new ConcurrentQueue<LaunchRequest>();
            using var broker = BrokerServer.Start(winnerOutcome.Primary!.Metadata);
            broker.Handler = r => { received.Enqueue(r); return BrokerStatus.Accepted; };

            var loser = await (winner == w1 ? w2 : w1);
            Assert.Equal(InstanceOutcomeKind.Forwarded, loser.Kind);
            Assert.Equal(winner == w1 ? r2 : r1, Assert.Single(received));
        }
        finally { await StopResolutionsAsync(stop, w1, w2); }
    }

    [Fact]
    public async Task HandoffDuringAnExpiredBrokerAttemptRechecksElectionAndTheSuccessorBroker()
    {
        var scope = Scope();
        using var old = PrimaryInstance.TryAcquire(scope)!;
        var r1 = LaunchRequest.OpenProject(Path.Combine(_root, "handoff1.spandraft"));
        var r2 = LaunchRequest.OpenProject(Path.Combine(_root, "handoff2.spandraft"));
        using var stop = ResolutionDeadline();
        var time = new HandoffTime();
        var options = Fast with { Time = time };

        System.IO.Pipes.NamedPipeServerStream OldConnection() => new(old.Metadata.Endpoint,
            System.IO.Pipes.PipeDirection.InOut, System.IO.Pipes.NamedPipeServerStream.MaxAllowedServerInstances,
            System.IO.Pipes.PipeTransmissionMode.Byte,
            System.IO.Pipes.PipeOptions.Asynchronous | System.IO.Pipes.PipeOptions.CurrentUserOnly);

        async Task HoldSecondAttempt(System.IO.Pipes.NamedPipeServerStream pipe, LaunchRequest expected)
        {
            await pipe.WaitForConnectionAsync(stop.Token);
            Assert.Equal(expected, BrokerProtocol.DecodeRequest(
                (await BrokerProtocol.ReadLineAsync(pipe, stop.Token))!, old.Metadata.DataScopeId));
            // A lost first reply starts the unreachable period; keep the next attempt in flight.
            pipe.Disconnect();
            await pipe.WaitForConnectionAsync(stop.Token);
            Assert.Equal(expected, BrokerProtocol.DecodeRequest(
                (await BrokerProtocol.ReadLineAsync(pipe, stop.Token))!, old.Metadata.DataScopeId));
        }

        using var firstConnection = OldConnection();
        var firstInFlight = HoldSecondAttempt(firstConnection, r1);
        var w1 = InstanceCoordinator.ResolveAsync(scope, r1, options, stop.Token);
        Task<InstanceOutcome>? w2 = null;
        Task? secondInFlight = null;
        try
        {
            await WaitForCheckpoint(firstInFlight, w1);
            using var secondConnection = OldConnection();
            secondInFlight = HoldSecondAttempt(secondConnection, r2);
            w2 = InstanceCoordinator.ResolveAsync(scope, r2, options, stop.Token);
            await WaitForCheckpoint(secondInFlight, w2);
            Assert.False(w1.IsCompleted || w2.IsCompleted);
            using (var unexpectedPrimary = PrimaryInstance.TryAcquire(scope)) Assert.Null(unexpectedPrimary);

            time.Advance(Fast.BootstrapTimeout + TimeSpan.FromTicks(1));
            old.Dispose();
            firstConnection.Dispose();
            var winner = await w1;
            Assert.Equal(InstanceOutcomeKind.Primary, winner.Kind);
            Assert.Equal(old.Metadata.DataScopeId, winner.Primary!.Metadata.DataScopeId);
            Assert.False(w2.IsCompleted);

            var received = new ConcurrentQueue<LaunchRequest>();
            using var broker = BrokerServer.Start(winner.Primary.Metadata);
            broker.Handler = request => { received.Enqueue(request); return BrokerStatus.Accepted; };
            // The other stale attempt fails only after the successor holds the lock and runs its broker.
            secondConnection.Dispose();
            var forwarded = await w2;
            Assert.Equal(InstanceOutcomeKind.Forwarded, forwarded.Kind);
            Assert.Null(forwarded.Primary);
            Assert.Equal(r2, Assert.Single(received));
            using var unexpectedAfterHandoff = PrimaryInstance.TryAcquire(scope);
            Assert.Null(unexpectedAfterHandoff);
        }
        finally
        {
            await StopResolutionsAsync(stop, w2 is null ? [w1] : [w1, w2]);
            await firstInFlight;
            if (secondInFlight is not null) await secondInFlight;
        }
    }

    private sealed class HandoffTime : TimeProvider
    {
        private long _ticks;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddTicks(Interlocked.Read(ref _ticks));
        public void Advance(TimeSpan elapsed) => Interlocked.Add(ref _ticks, elapsed.Ticks);
    }

    [Fact]
    public void ElectionAndEndpointExistBeforeSettingsAreLoadedAndASecondaryLoadsNone()
    {
        var scope = Scope();
        var request = LaunchRequest.OpenProject(Path.Combine(_root, "boot.spandraft"));
        BrokerStatus? statusWhileLoading = null;
        bool lockHeldWhileLoading = false, started = false;
        var files = new SpanDraft.Tests.ProjectTestSupport.Files();
        var context = new DesktopStartupContext(files, new SpanDraft.Desktop.Persistence.LocalSettingsStore(files, "settings"), SpanDraft.Desktop.Presentation.UserSettings.Default);
        int exit = DesktopBootstrap.Run(scope, request, () =>
        {
            lockHeldWhileLoading = PrimaryInstance.TryAcquire(scope) is null;
            var metadata = InstanceMetadata.TryRead(scope)!;
            statusWhileLoading = BrokerClient.SendAsync(metadata, request, TimeSpan.FromSeconds(2), CancellationToken.None).GetAwaiter().GetResult();
            return context;
        }, (startup, broker) => { started = ReferenceEquals(startup, context); return 0; }, Fast);
        Assert.Equal(0, exit);
        Assert.True(started);
        Assert.True(lockHeldWhileLoading);
        Assert.Equal(BrokerStatus.Retry, statusWhileLoading);

        var primary = Track(PrimaryInstance.TryAcquire(scope)!);
        var live = Track(BrokerServer.Start(primary.Metadata));
        live.Handler = _ => BrokerStatus.Accepted;
        bool loaded = false;
        int secondary = DesktopBootstrap.Run(scope, request, () => { loaded = true; return context; }, (_, _) => 99, Fast);
        Assert.Equal(0, secondary);
        Assert.False(loaded);
    }

    [Fact]
    public async Task RejectedEndsTheAttemptWithoutBecomingPrimary()
    {
        var scope = Scope();
        var primary = Track(PrimaryInstance.TryAcquire(scope)!);
        var broker = Track(BrokerServer.Start(primary.Metadata));
        broker.Handler = _ => BrokerStatus.Rejected;
        using var stop = ResolutionDeadline();
        var outcome = await InstanceCoordinator.ResolveAsync(scope, LaunchRequest.Activate, Fast, stop.Token);
        using var unexpectedPrimary = outcome.Primary;
        Assert.Equal(InstanceOutcomeKind.Rejected, outcome.Kind);
    }

    [Fact]
    public async Task BootstrapRaceIsToleratedAndAHungLockHolderEndsInCommunicationFailure()
    {
        var scope = Scope();
        var primary = Track(PrimaryInstance.TryAcquire(scope)!);
        // Lock and metadata exist, endpoint not yet: the endpoint appears within the timeout, so no failure.
        var time = new BackoffObserver();
        using (var stop = ResolutionDeadline())
        {
            var waiting = InstanceCoordinator.ResolveAsync(scope, LaunchRequest.Activate,
                Fast with { BootstrapTimeout = TimeSpan.FromSeconds(5), Time = time }, stop.Token);
            try
            {
                await WaitForCheckpoint(time.BackoffStarted.Task, waiting);
                Assert.False(waiting.IsCompleted);
                using var broker = BrokerServer.Start(primary.Metadata);
                broker.Handler = _ => BrokerStatus.Accepted;
                Assert.Equal(InstanceOutcomeKind.Forwarded, (await waiting).Kind);
            }
            finally { await StopResolutionsAsync(stop, waiting); }
        }

        using var failureDeadline = ResolutionDeadline();
        var failure = await InstanceCoordinator.ResolveAsync(scope, LaunchRequest.Activate, Fast, failureDeadline.Token);
        using var unexpectedPrimary = failure.Primary;
        Assert.Equal(InstanceOutcomeKind.CommunicationFailure, failure.Kind);
        Assert.Null(failure.Primary);
    }

    private sealed class BackoffObserver : TimeProvider
    {
        public TaskCompletionSource BackoffStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = base.CreateTimer(callback, state, dueTime, period);
            BackoffStarted.TrySetResult();
            return timer;
        }
    }

    [Fact]
    public async Task ServerRejectsGarbageAndForeignScopes()
    {
        var primary = Track(PrimaryInstance.TryAcquire(Scope())!);
        Track(BrokerServer.Start(primary.Metadata));
        // Same endpoint, different scope id: handshake mismatch.
        var mismatched = new ForeignEndpoint(primary.Metadata.Endpoint, primary.Metadata.DataScopeId);
        Assert.Equal(BrokerStatus.Rejected, await mismatched.SendAsync("ffff", LaunchRequest.Activate));
    }

    private sealed record ForeignEndpoint(string Endpoint, string ScopeId)
    {
        public async Task<BrokerStatus?> SendAsync(string scopeId, LaunchRequest request)
        {
            using var stop = ResolutionDeadline();
            await using var pipe = new System.IO.Pipes.NamedPipeClientStream(".", Endpoint, System.IO.Pipes.PipeDirection.InOut,
                System.IO.Pipes.PipeOptions.Asynchronous | System.IO.Pipes.PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(2000, stop.Token);
            await BrokerProtocol.WriteLineAsync(pipe, BrokerProtocol.EncodeRequest(scopeId, request), stop.Token);
            return BrokerProtocol.DecodeResponse((await BrokerProtocol.ReadLineAsync(pipe, stop.Token))!, ScopeId);
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
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var seen = new List<LaunchRequest>();
        dispatcher.SetConsumer(async r => { seen.Add(r); await gate.Task; return true; });
        dispatcher.Enqueue(LaunchRequest.Activate);
        var pump = dispatcher.PumpAsync();
        var repump = Task.CompletedTask;
        try
        {
            dispatcher.Enqueue(LaunchRequest.Activate);
            repump = dispatcher.PumpAsync();
            await WaitFor(repump);
        }
        finally
        {
            gate.TrySetResult();
            await Task.WhenAll(pump, repump).WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        }
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

    private static async Task<BrokerStatus?> SendRawAsync(string endpoint, byte[] payload)
    {
        await using var pipe = new System.IO.Pipes.NamedPipeClientStream(".", endpoint, System.IO.Pipes.PipeDirection.InOut,
            System.IO.Pipes.PipeOptions.Asynchronous | System.IO.Pipes.PipeOptions.CurrentUserOnly);
        var cancellation = TestContext.Current.CancellationToken;
        await pipe.ConnectAsync(2000, cancellation);
        try
        {
            await WaitFor(pipe.WriteAsync(payload, cancellation).AsTask());
            await WaitFor(pipe.FlushAsync(cancellation));
            // An idle connection is closed by the broker's own request timeout.
            string? line = await WaitFor(BrokerProtocol.ReadLineAsync(pipe, cancellation));
            return line is null ? null : BrokerProtocol.DecodeResponse(line, endpoint["SpanDraft-".Length..]);
        }
        catch (IOException) { return null; }
    }

    [Fact]
    public async Task InvalidWireDataIsRejectedOrDroppedAndTheServerStaysUsable()
    {
        var primary = Track(PrimaryInstance.TryAcquire(Scope())!);
        Track(BrokerServer.Start(primary.Metadata));
        var metadata = primary.Metadata;
        var valid = LaunchRequest.OpenProject(Path.Combine(_root, "ok.spandraft"));
        byte[] Line(string text) => System.Text.Encoding.UTF8.GetBytes(text + "\n");

        var invalid = new[]
        {
            Line("this is not json"),
            Line(new string('x', BrokerProtocol.MaxMessageBytes * 2)),
            Line(BrokerProtocol.EncodeRequest(metadata.DataScopeId, valid).Replace("\"V\":1", "\"V\":2"))
        };
        foreach (var payload in invalid)
        {
            Assert.True(await SendRawAsync(metadata.Endpoint, payload) is null or BrokerStatus.Rejected);
            Assert.Equal(BrokerStatus.Retry, await BrokerClient.SendAsync(metadata, valid, TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
        }
        // A connected client that sends no line must not wedge the endpoint either.
        Assert.Null(await SendRawAsync(metadata.Endpoint, []));
        Assert.Equal(BrokerStatus.Retry, await BrokerClient.SendAsync(metadata, valid, TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task NextListenerExistsBeforeAServedConnectionIsReleased()
    {
        var primary = Track(PrimaryInstance.TryAcquire(Scope())!);
        var metadata = primary.Metadata;
        var reachable = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Track(BrokerServer.Start(metadata, async served =>
        {
            if (reachable.Task.IsCompleted) return;
            // The handed-off instance is fully served and disposed; the endpoint must still accept a connection now.
            await served;
            await using var probe = new System.IO.Pipes.NamedPipeClientStream(".", metadata.Endpoint, System.IO.Pipes.PipeDirection.InOut,
                System.IO.Pipes.PipeOptions.Asynchronous | System.IO.Pipes.PipeOptions.CurrentUserOnly);
            try { await probe.ConnectAsync(500, TestContext.Current.CancellationToken); reachable.TrySetResult(true); }
            catch (Exception error) when (error is TimeoutException or IOException) { reachable.TrySetResult(false); }
        }));
        var valid = LaunchRequest.OpenProject(Path.Combine(_root, "ok.spandraft"));

        Assert.Equal(BrokerStatus.Retry, await BrokerClient.SendAsync(metadata, valid, TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
        Assert.True(await WaitFor(reachable.Task));
        Assert.Equal(BrokerStatus.Retry, await BrokerClient.SendAsync(metadata, valid, TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ClientTreatsAnEndpointConfirmingAnotherDataScopeAsUnreachable()
    {
        var metadata = new InstanceMetadata("aaaa" + Guid.NewGuid().ToString("N")[..24]);
        using var cts = ResolutionDeadline();
        var listening = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fake = Task.Run(async () =>
        {
            await using var pipe = new System.IO.Pipes.NamedPipeServerStream(metadata.Endpoint, System.IO.Pipes.PipeDirection.InOut, 1,
                System.IO.Pipes.PipeTransmissionMode.Byte, System.IO.Pipes.PipeOptions.Asynchronous | System.IO.Pipes.PipeOptions.CurrentUserOnly);
            listening.TrySetResult();
            await pipe.WaitForConnectionAsync(cts.Token);
            Assert.Equal(LaunchRequest.Activate, BrokerProtocol.DecodeRequest((await BrokerProtocol.ReadLineAsync(pipe, cts.Token))!, metadata.DataScopeId));
            await BrokerProtocol.WriteLineAsync(pipe, BrokerProtocol.EncodeResponse(BrokerStatus.Accepted, "bbbb"), cts.Token);
        }, cts.Token);
        try
        {
            await WaitForCheckpoint(listening.Task, fake);
            Assert.Null(await BrokerClient.SendAsync(metadata, LaunchRequest.Activate, TimeSpan.FromSeconds(2), cts.Token));
            await fake;
        }
        finally
        {
            await cts.CancelAsync();
            try { await fake.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None); }
            catch (OperationCanceledException) when (fake.IsCanceled) { }
        }
    }

    [Fact]
    public async Task ResponseCarriesThePrimaryDataScopeId()
    {
        var primary = Track(PrimaryInstance.TryAcquire(Scope())!);
        var broker = Track(BrokerServer.Start(primary.Metadata));
        broker.Handler = _ => BrokerStatus.Accepted;
        Assert.Equal(BrokerStatus.Accepted, await SendRawAsync(primary.Metadata.Endpoint,
            System.Text.Encoding.UTF8.GetBytes(BrokerProtocol.EncodeRequest(primary.Metadata.DataScopeId, LaunchRequest.Activate) + "\n")));
    }

    [Fact]
    public async Task AcceptedRequestsNeverSlipPastBeginQuit()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var lifecycleLock = typeof(DesktopApplicationHost).GetField("_gate", flags)!;
        var queueLock = typeof(ActivationDispatcher).GetField("_gate", flags)!;
        for (int round = 0; round < 200; round++)
        {
            var host = new DesktopApplicationHost();
            host.MarkReady();
            int acceptedBeforeRace = round % 7;
            for (int i = 0; i < acceptedBeforeRace; i++)
                Assert.Equal(BrokerStatus.Accepted, host.HandleBrokerRequest(LaunchRequest.Activate));

            var enqueued = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var release = new ManualResetEventSlim();
            var cancellation = TestContext.Current.CancellationToken;
            host.Activation.Enqueued += () =>
            {
                enqueued.TrySetResult();
                if (!release.Wait(TimeSpan.FromSeconds(5), cancellation))
                    throw new TimeoutException("BeginQuit did not release the in-flight broker request.");
            };
            var lifecycleGate = lifecycleLock.GetValue(host)!;
            var queueGate = queueLock.GetValue(host.Activation)!;
            Task<(BrokerStatus, BrokerStatus)>? producer = null;
            int queuedAtQuit;
            try
            {
                // Block the queue write and check that it is still inside the lifecycle transaction.
                lock (queueGate)
                {
                    producer = Task.Run(() =>
                    {
                        var inFlight = host.HandleBrokerRequest(LaunchRequest.Activate);
                        var afterQuit = host.HandleBrokerRequest(LaunchRequest.Activate);
                        return (inFlight, afterQuit);
                    }, cancellation);
                    Assert.True(SpinWait.SpinUntil(() =>
                    {
                        cancellation.ThrowIfCancellationRequested();
                        if (!Monitor.TryEnter(lifecycleGate)) return true;
                        Monitor.Exit(lifecycleGate);
                        return false;
                    }, TimeSpan.FromSeconds(5)), "The broker must hold the lifecycle gate while waiting to enqueue.");
                }
                await WaitForCheckpoint(enqueued.Task, producer);
                Assert.False(producer.IsCompleted);
                host.BeginQuit();
                queuedAtQuit = host.Activation.Count;
            }
            finally
            {
                release.Set();
                if (producer is not null) await producer.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
            }
            var (inFlight, afterQuit) = await producer;
            Assert.Equal(BrokerStatus.Accepted, inFlight);
            Assert.Equal(BrokerStatus.Retry, afterQuit);
            Assert.Equal(acceptedBeforeRace + 1, host.Activation.Count);
            Assert.Equal(queuedAtQuit, host.Activation.Count);
            Assert.Equal(BrokerStatus.Retry, host.HandleBrokerRequest(LaunchRequest.Activate));
        }
    }

    [Fact]
    public async Task RequestAcceptedBeforeQuitIsProcessedAfterCancelQuitAndNothingIsAcceptedDuringShutdown()
    {
        var host = new DesktopApplicationHost();
        host.MarkReady();
        var request = LaunchRequest.OpenProject(Path.Combine(_root, "q.spandraft"));
        Assert.Equal(BrokerStatus.Accepted, host.HandleBrokerRequest(request));
        host.BeginQuit();
        Assert.Equal(BrokerStatus.Retry, host.HandleBrokerRequest(LaunchRequest.Activate));
        host.CancelQuit();
        var seen = new List<LaunchRequest>();
        host.Activation.SetConsumer(r => { seen.Add(r); return Task.FromResult(true); });
        await host.Activation.PumpAsync();
        Assert.Equal([request], seen);
        host.BeginShutdown();
        Assert.Equal(BrokerStatus.Retry, host.HandleBrokerRequest(LaunchRequest.Activate));
        Assert.Equal(0, host.Activation.Count);
    }
}
