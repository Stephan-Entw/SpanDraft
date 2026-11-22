using SpanDraft.Analysis;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

public sealed class ProjectOwnershipWorkflowTests
{
    private sealed class Fixture
    {
        internal readonly Files Files;
        internal readonly Dialogs Dialogs = new();
        internal readonly Delay Delay = new();
        internal readonly ProjectPathRegistry Registry = new();
        internal readonly IProjectIdentityResolver Resolver;
        internal readonly ProjectRecovery Recovery;
        internal readonly MainWindowViewModel Main;
        internal readonly string Slot = TestPath("ownership-recovery.json");
        internal int Analyses, Activations;

        internal Fixture(IProjectIdentityResolver? resolver = null, Files? files = null)
        {
            Files = files ?? new();
            Resolver = resolver ?? Identities;
            Recovery = new(Files, Slot, Delay.Wait, () => Now);
            Main = new(WindowId.Create(), Registry, Resolver, activateWorkspace: _ => Activations++,
                analyze: beam => { Analyses++; return BeamAnalysis.Analyze(beam); }, files: Files, dialogs: Dialogs, recovery: Recovery);
        }

        internal string Project(string name, int section = 1)
        {
            string path = TestPath(name + ".spandraft");
            Files.Data[path] = ProjectFileCodec.Serialize(State(section));
            return path;
        }

        internal ProjectPathState Status(string path) => Registry.StateOf(Resolver.Resolve(path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentOpensShareOneReadSessionAndOutcome(bool damaged)
    {
        string path = TestPath("flight.spandraft"), alias = TestPath("flight-alias.spandraft");
        var fixture = new Fixture(new TestIdentityResolver(candidate => new(candidate == alias ? path : candidate, true)));
        fixture.Project("flight", 3);
        if (damaged) fixture.Files.Data[path] = "{}"u8.ToArray();
        int reads = 0;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Files.BeforeRead = async opened =>
        {
            Assert.Equal(path, opened);
            Interlocked.Increment(ref reads);
            entered.TrySetResult();
            await WaitFor(release.Task);
        };

        var first = fixture.Main.OpenAsync(path);
        Task<bool> second;
        try
        {
            await WaitForCheckpoint(entered.Task, first);
            Assert.Equal(ProjectPathState.Opening, fixture.Status(alias));
            second = fixture.Main.OpenRecentAsync(alias);
            Assert.False(second.IsCompleted);
            Assert.Equal(1, reads);
        }
        finally { release.TrySetResult(); }

        Assert.Equal(new[] { !damaged, !damaged }, await WaitFor(Task.WhenAll(first, second)));
        Assert.Equal(1, reads);
        Assert.Equal(damaged ? 0 : 1, fixture.Analyses);
        Assert.Equal(damaged ? 0 : 1, fixture.Activations);
        Assert.Equal(damaged ? ProjectPathState.Free : ProjectPathState.Open, fixture.Status(path));
        if (damaged)
        {
            Assert.Null(fixture.Main.Session);
            Assert.Single(fixture.Dialogs.Errors);
            fixture.Project("flight", 4);
            Assert.True(await fixture.Main.OpenAsync(alias));
            Assert.Equal(2, reads);
        }
        else
        {
            var session = fixture.Main.Session;
            Assert.True(await fixture.Main.OpenAsync(alias));
            Assert.Same(session, fixture.Main.Session);
            Assert.Equal(1, reads);
        }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("invalid")]
    [InlineData("read error")]
    public async Task FailedOpenKeepsThePreviousOwnerAndFreesTheTarget(string failure)
    {
        var fixture = new Fixture();
        string previous = fixture.Project("previous");
        string target = TestPath("bad.spandraft");
        Assert.True(await fixture.Main.OpenAsync(previous));
        var session = fixture.Main.Session!;
        Assert.True(session.Commit(State(3)));
        if (failure == "invalid") fixture.Files.Data[target] = "{}"u8.ToArray();
        if (failure == "read error") fixture.Files.BeforeRead = _ => throw new IOException("Read failed");
        Assert.False(await fixture.Main.OpenAsync(target));
        Assert.Same(session, fixture.Main.Session);
        Assert.Equal(ProjectPathState.Open, fixture.Status(previous));
        Assert.Equal(ProjectPathState.Free, fixture.Status(target));
        Assert.Equal(0, fixture.Dialogs.LeaveQuestions);
    }

    [Theory]
    [InlineData("open")]
    [InlineData("opening")]
    [InlineData("reserved")]
    [InlineData("recovery")]
    [InlineData("unresolved")]
    public async Task RejectedSaveAsDoesNotWriteOrChangeAnyOwnership(string conflict)
    {
        string target = TestPath("blocked.spandraft");
        var fixture = new Fixture(new TestIdentityResolver(path => path == target && conflict == "unresolved"
            ? throw new IOException("Case semantics unknown") : new(path, true)));
        string original = fixture.Project("old");
        Assert.True(await fixture.Main.OpenAsync(original));
        var session = fixture.Main.Session!;
        Assert.True(session.Commit(State(3)));
        var targetIdentity = Identities.Resolve(target);
        var foreign = WindowId.Create();
        switch (conflict)
        {
            case "open": Assert.True(fixture.Registry.TryCommitOpen(fixture.Registry.ArbitrateOpen(targetIdentity).Reservation!, foreign)); break;
            case "opening": Assert.NotNull(fixture.Registry.ArbitrateOpen(targetIdentity).Reservation); break;
            case "reserved": Assert.Equal(SaveTargetDecision.Reserved, fixture.Registry.ArbitrateSave(targetIdentity, foreign, out _)); break;
            case "recovery": Assert.True(fixture.Registry.UpdateRecovery(targetIdentity, null, Guid.NewGuid())); break;
        }
        var before = fixture.Registry.StateOf(targetIdentity);
        var savepoint = session.SavedRevisionId;
        var bytes = fixture.Files.Data[original];
        fixture.Dialogs.SavePath = target;

        Assert.False(await fixture.Main.SaveAsync(saveAs: true));
        Assert.Empty(fixture.Files.Writes);
        Assert.Equal(bytes, fixture.Files.Data[original]);
        Assert.Equal(original, session.FilePath);
        Assert.Equal(savepoint, session.SavedRevisionId);
        Assert.True(session.IsDirty);
        Assert.Equal(ProjectPathState.Open, fixture.Status(original));
        Assert.Equal(before, fixture.Registry.StateOf(targetIdentity));
    }

    [Fact]
    public async Task OwnPathAliasUsesNormalSaveAndKeepsItsOwner()
    {
        string original = TestPath("owned.spandraft"), alias = TestPath("alias.spandraft");
        var fixture = new Fixture(new TestIdentityResolver(path => new(path == alias ? original : path, true)));
        fixture.Project("owned");
        Assert.True(await fixture.Main.OpenAsync(original));
        var session = fixture.Main.Session!;
        fixture.Dialogs.SavePath = alias;
        fixture.Files.BeforeWrite = (path, _, _) =>
        {
            Assert.Equal(original, path);
            Assert.Equal(fixture.Main.WindowId, fixture.Registry.ArbitrateOpen(Identities.Resolve(original)).Owner);
            return Task.CompletedTask;
        };

        Assert.True(await fixture.Main.SaveAsync(saveAs: true));
        Assert.Same(session, fixture.Main.Session);
        Assert.Equal(original, session.FilePath);
        Assert.False(fixture.Files.Data.ContainsKey(alias));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OpenWaitsForSaveAsAndArbitratesAgainAfterCommitOrRollback(bool failWrite)
    {
        var fixture = new Fixture();
        string original = fixture.Project("before"), target = fixture.Project("after", 5);
        Assert.True(await fixture.Main.OpenAsync(original));
        var session = fixture.Main.Session!;
        Assert.True(session.Commit(State(3)));
        fixture.Dialogs.SavePath = target;
        fixture.Dialogs.Leave = LeaveDecision.Discard;
        if (failWrite) fixture.Files.FailWritePath = target;
        int targetReads = 0, committedNotifications = 0;
        session.Changed += () =>
        {
            if (session.FilePath != target) return;
            Assert.Equal(ProjectPathState.Free, fixture.Status(original));
            Assert.Equal(ProjectPathState.Open, fixture.Status(target));
            Assert.False(session.IsDirty);
            committedNotifications++;
        };
        fixture.Files.BeforeRead = path => { if (path == target) Interlocked.Increment(ref targetReads); return Task.CompletedTask; };
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Files.BeforeWrite = async (path, _, _) =>
        {
            if (path != target) return;
            Assert.Equal(ProjectPathState.Open, fixture.Status(original));
            Assert.Equal(ProjectPathState.SaveTargetReserved, fixture.Status(target));
            Assert.Equal(original, session.FilePath);
            Assert.True(session.IsDirty);
            entered.SetResult();
            await WaitFor(release.Task);
        };

        var save = fixture.Main.SaveAsync(saveAs: true);
        Task<bool> open;
        try
        {
            await WaitForCheckpoint(entered.Task, save);
            open = fixture.Main.OpenAsync(target);
            Assert.False(open.IsCompleted);
            Assert.Equal(0, targetReads);
        }
        finally { release.TrySetResult(); }
        Assert.Equal(!failWrite, await WaitFor(save));
        Assert.True(await WaitFor(open));
        Assert.Equal(failWrite ? 1 : 0, targetReads);
        Assert.Equal(!failWrite, ReferenceEquals(session, fixture.Main.Session));
        Assert.True(State(failWrite ? 5 : 3).ContentEquals(fixture.Main.Session!.CurrentRevision.State));
        Assert.Equal(ProjectPathState.Free, fixture.Status(original));
        Assert.Equal(ProjectPathState.Open, fixture.Status(target));
        Assert.Equal(!failWrite, committedNotifications > 0);
    }

    [Fact]
    public async Task RecoveryFlushOrProjectWriteFailureRollsBackSaveAs()
    {
        var fixture = new Fixture();
        string old = fixture.Project("before"), target = TestPath("new-target.spandraft");
        Assert.True(await fixture.Main.OpenAsync(old));
        var session = fixture.Main.Session!;
        Assert.True(session.Commit(State(4)));
        var savepoint = session.SavedRevisionId;
        fixture.Dialogs.SavePath = target;
        foreach (string failure in new[] { target, fixture.Slot })
        {
            fixture.Files.FailWritePath = failure;
            Assert.False(await fixture.Main.SaveAsync(saveAs: true));
            Assert.Equal(old, session.FilePath);
            Assert.Equal(savepoint, session.SavedRevisionId);
            Assert.True(session.IsDirty);
            Assert.Equal(ProjectPathState.Open, fixture.Status(old));
            Assert.Equal(ProjectPathState.Free, fixture.Status(target));
            Assert.Equal(old, (await fixture.Recovery.ReadAsync())!.OriginalFilePath);
            Assert.False(fixture.Files.Data.ContainsKey(target));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyRestoreOwnsTheMissingOriginalOnlyWhenItsPathCanBeResolved(bool unresolved)
    {
        string path = TestPath("missing-original.spandraft");
        var fixture = new Fixture(new TestIdentityResolver(p => unresolved && p == path
            ? throw new DirectoryNotFoundException("The original parent cannot be resolved") : new(p, true)));
        var state = State(3);
        byte[] saved = ProjectRecovery.Encode(new(state, path, Now));
        fixture.Files.Data[fixture.Slot] = saved.ToArray();

        var identity = Identities.Resolve(path);
        if (unresolved)
        {
            // The failed startup keeps the legacy slot; the startup loop then ends through the normal shutdown path.
            Assert.False(await fixture.Main.InitializeRecoveryAsync());
            Assert.Null(fixture.Main.Session);
            Assert.Equal(1, fixture.Dialogs.RecoveryQuestions);
            Assert.Single(fixture.Dialogs.Errors);
            Assert.Equal(saved, fixture.Files.Data[fixture.Slot]);
            Assert.Empty(fixture.Files.Writes);
            Assert.Empty(fixture.Delay.Waiters);
            Assert.Equal(ProjectPathState.Free, fixture.Registry.StateOf(identity));
            fixture.Main.Dispose();
            Assert.Equal(saved, fixture.Files.Data[fixture.Slot]);

            // A later start with a working resolver restores the very same snapshot.
            var next = new Fixture(files: fixture.Files);
            Assert.True(await next.Main.InitializeRecoveryAsync());
            Assert.Empty(next.Dialogs.Errors);
            Assert.Equal(saved, next.Files.Data[next.Slot]);
            Assert.Empty(next.Files.Writes);
            var recovered = next.Main.Session!;
            Assert.Equal(path, recovered.FilePath);
            Assert.True(recovered.IsDirty);
            Assert.True(state.ContentEquals(recovered.CurrentRevision.State));
            Assert.Equal(next.Main.WindowId, next.Registry.ArbitrateOpen(identity).Owner);
            next.Main.Dispose();
            Assert.Equal(ProjectPathState.Free, next.Registry.StateOf(identity));
            return;
        }

        Assert.True(await fixture.Main.InitializeRecoveryAsync());
        Assert.Equal(saved, fixture.Files.Data[fixture.Slot]);
        Assert.Empty(fixture.Files.Writes);
        Assert.Empty(fixture.Dialogs.Errors);
        var restored = fixture.Main.Session!;
        Assert.True(restored.IsDirty);
        Assert.True(state.ContentEquals(restored.CurrentRevision.State));
        Assert.Equal(path, restored.FilePath);
        Assert.Equal(fixture.Main.WindowId, fixture.Registry.ArbitrateOpen(identity).Owner);
        fixture.Files.BeforeRead = _ => throw new IOException("The restored project is already open.");
        Assert.True(await fixture.Main.OpenAsync(path));
        Assert.Same(restored, fixture.Main.Session);
        fixture.Main.Dispose();
        Assert.Equal(ProjectPathState.Free, fixture.Registry.StateOf(identity));
    }

    [Fact]
    public async Task TemplateStartsCleanAndPathlessAndFirstSaveUsesThePicker()
    {
        var fixture = new Fixture();
        string source = fixture.Project("template");
        byte[] sourceBytes = fixture.Files.Data[source];
        var template = ProjectSession.FromTemplate(ProjectFileCodec.Deserialize(sourceBytes));
        fixture.Main.ActivatePathlessSession(template);
        Assert.Null(template.FilePath);
        Assert.False(template.IsDirty);
        Assert.Equal(template.CurrentRevision.RevisionId, template.SavedRevisionId);
        Assert.DoesNotContain("*", fixture.Main.WindowTitle);
        Assert.False(template.Commit(template.CurrentRevision.State));
        Assert.Empty(fixture.Delay.Waiters);
        Assert.True(await fixture.Main.CloseProjectAsync());
        Assert.Equal(0, fixture.Dialogs.LeaveQuestions);

        var editable = ProjectSession.FromTemplate(template.CurrentRevision.State);
        fixture.Main.ActivatePathlessSession(editable);
        Assert.True(editable.Commit(State(3)));
        Assert.True(editable.IsDirty);
        Assert.Contains("*", fixture.Main.WindowTitle);
        Assert.Single(fixture.Delay.Waiters);
        string target = TestPath("saved-template.spandraft");
        fixture.Dialogs.SavePath = target;
        Assert.True(await fixture.Main.SaveAsync());
        Assert.Equal(1, fixture.Dialogs.SaveQuestions);
        Assert.Equal(target, editable.FilePath);
        Assert.Equal(ProjectPathState.Free, fixture.Status(source));
        Assert.Equal(ProjectPathState.Open, fixture.Status(target));
        Assert.Equal(sourceBytes, fixture.Files.Data[source]);
        Assert.False(fixture.Files.Data.ContainsKey(fixture.Slot));
    }
}
