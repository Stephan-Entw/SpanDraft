using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.State;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

public sealed class ProjectPathRegistryTests
{
    [Fact]
    public async Task OpenArbitratesEveryStateAndPublishesOneFlightToWaiters()
    {
        var registry = new ProjectPathRegistry();
        var identity = Identities.Resolve(TestPath("flight.spandraft"));
        var window = WindowId.Create();
        var other = WindowId.Create();
        var candidate = Guid.NewGuid();
        var older = Guid.NewGuid();

        Assert.True(registry.UpdateRecovery(identity, null, candidate));
        Assert.Equal(candidate, registry.ArbitrateOpen(identity).RecoveryCandidate);
        Assert.Equal(SaveTargetDecision.Rejected, registry.ArbitrateSave(identity, window, out _));
        Assert.True(registry.UpdateRecovery(identity, candidate, older));
        Assert.Equal(older, registry.ArbitrateOpen(identity).RecoveryCandidate);
        Assert.False(registry.UpdateRecovery(identity, candidate, null));
        Assert.False(registry.TryCommitRecovery(identity, candidate, window));
        Assert.True(registry.UpdateRecovery(identity, older, null));

        var acquired = registry.ArbitrateOpen(identity);
        Assert.Equal(ProjectPathState.Free, acquired.State);
        var token = Assert.IsType<OpenReservation>(acquired.Reservation);
        var joined = registry.ArbitrateOpen(identity);
        Assert.Equal(ProjectPathState.Opening, joined.State);
        Assert.Same(joined.Flight, registry.ArbitrateOpen(identity).Flight);
        Assert.Null(joined.Reservation);
        Assert.Equal(SaveTargetDecision.Rejected, registry.ArbitrateSave(identity, other, out _));
        Assert.False(registry.UpdateRecovery(identity, null, candidate));
        Assert.True(registry.TryCommitOpen(token, window));
        Assert.Equal(window, (await WaitFor(joined.Flight!)).Owner);
        Assert.Equal(window, registry.ArbitrateOpen(identity).Owner);
        Assert.Equal(SaveTargetDecision.Rejected, registry.ArbitrateSave(identity, other, out _));
        Assert.Equal(SaveTargetDecision.SaveCurrent, registry.ArbitrateSave(identity, window, out var normalSave));
        Assert.Null(normalSave);
        Assert.True(registry.ReleaseOwner(window));

        Assert.True(registry.UpdateRecovery(identity, null, candidate));
        Assert.True(registry.TryCommitRecovery(identity, candidate, other));
        Assert.Equal(other, registry.ArbitrateOpen(identity).Owner);
    }

    [Fact]
    public async Task StaleAndForeignOpenTokensCannotAffectTheReplacementFlight()
    {
        var registry = new ProjectPathRegistry();
        var foreign = new ProjectPathRegistry();
        var identity = Identities.Resolve(TestPath("stale-open.spandraft"));
        var owner = WindowId.Create();
        var first = registry.ArbitrateOpen(identity).Reservation!;
        var wait = registry.ArbitrateOpen(identity).Flight!;
        var failure = new ProjectOpenResult(ProjectOpenStatus.Failed, Error: new IOException());
        Assert.False(foreign.TryReleaseOpen(first, failure));
        Assert.True(registry.TryReleaseOpen(first, failure));
        Assert.Same(failure, await WaitFor(wait));

        var second = registry.ArbitrateOpen(identity).Reservation!;
        var foreignToken = foreign.ArbitrateOpen(identity).Reservation!;
        Assert.NotEqual(first.OperationId, second.OperationId);
        foreach (var invalid in new[] { first, foreignToken })
        {
            Assert.False(registry.TryCommitOpen(invalid, owner));
            Assert.False(registry.TryReleaseOpen(invalid, failure));
        }
        Assert.True(registry.TryCommitOpen(second, owner));
    }

    [Fact]
    public async Task SaveReservationPinsTheSourceAndOnlyItsTokenCanCommitOrRelease()
    {
        var registry = new ProjectPathRegistry();
        var old = Identities.Resolve(TestPath("before.spandraft"));
        var target = Identities.Resolve(TestPath("after.spandraft"));
        var window = WindowId.Create();
        var other = WindowId.Create();
        Assert.True(registry.TryCommitOpen(registry.ArbitrateOpen(old).Reservation!, window));
        var session = ProjectSession.Restore(State(), old.CanonicalPath);
        var prepared = session.PrepareSave(session.CurrentRevision, target.CanonicalPath);

        Assert.Equal(SaveTargetDecision.Reserved, registry.ArbitrateSave(target, window, out var first));
        var wait = registry.ArbitrateOpen(target);
        Assert.Equal(ProjectPathState.SaveTargetReserved, wait.State);
        Assert.False(wait.SaveCompletion!.IsCompleted);
        Assert.Equal(SaveTargetDecision.Rejected, registry.ArbitrateSave(target, other, out _));
        Assert.False(registry.ReleaseOwner(window));
        Assert.Equal(window, registry.ArbitrateOpen(old).Owner);
        Assert.True(registry.TryReleaseSave(first!));
        await WaitFor(wait.SaveCompletion);
        Assert.Equal(ProjectPathState.Free, registry.StateOf(target));
        Assert.Equal(ProjectPathState.Open, registry.StateOf(old));

        Assert.Equal(SaveTargetDecision.Reserved, registry.ArbitrateSave(target, window, out var second));
        var foreign = new ProjectPathRegistry();
        Assert.Equal(SaveTargetDecision.Reserved, foreign.ArbitrateSave(target, window, out var foreignToken));
        Assert.Equal(window, second!.WindowId);
        Assert.NotEqual(first!.OperationId, second.OperationId);
        foreach (var invalid in new[] { first, foreignToken! })
        {
            Assert.False(registry.TryCommitSave(invalid, prepared));
            Assert.False(registry.TryReleaseSave(invalid));
        }
        Assert.Equal(old.CanonicalPath, session.FilePath);
        Assert.True(session.IsDirty);
        var committed = registry.ArbitrateOpen(target).SaveCompletion!;
        Assert.True(registry.TryCommitSave(second, prepared));
        await WaitFor(committed);
        Assert.Equal(target.CanonicalPath, session.FilePath);
        Assert.False(session.IsDirty);
        Assert.Equal(ProjectPathState.Free, registry.StateOf(old));
        Assert.Equal(window, registry.ArbitrateOpen(target).Owner);
        Assert.False(registry.TryReleaseSave(second));
    }
}
