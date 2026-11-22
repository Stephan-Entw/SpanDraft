using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.State;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

public sealed class WorkspaceRecoveryContextTests
{
    private sealed class Fixture
    {
        public TestRecoveryStorage Storage { get; } = new();
        public Delay Delay { get; } = new();
        public WorkspaceRecoveryContext Context { get; }
        public List<Exception> Failures { get; } = [];
        public Fixture()
        {
            Context = new(Storage, Delay.Wait, () => Now);
            Context.Failed += Failures.Add;
        }
        public async Task Publish() { Delay.ReleaseAll(); await WaitFor(Context.DrainAsync()); }
    }

    private static ProjectSession Dirty()
    {
        var session = ProjectSession.Open(State(), TestPath("dirty.spandraft"));
        session.Commit(State() with { Document = State().Document with { Length = M(2) } });
        return session;
    }

    [Fact]
    public async Task ACleanOpenedProjectHasNoRecoveryAndTheFirstChangeCreatesOne()
    {
        var f = new Fixture(); var session = ProjectSession.Open(State(), TestPath("clean.spandraft"));
        Assert.Equal(WorkspaceRecoveryState.None, f.Context.State);
        f.Context.ProjectChanged(session); await f.Publish();
        Assert.Equal(WorkspaceRecoveryState.None, f.Context.State); Assert.Empty(f.Storage.Slots());
        session.Commit(State() with { Document = State().Document with { Length = M(2) } });
        f.Context.ProjectChanged(session); await f.Publish();
        Assert.Equal(WorkspaceRecoveryState.ProjectRecovery, f.Context.State);
        Assert.Equal(RecoverySlot.Project(f.Context.ProjectRecoveryId!.Value), Assert.Single(f.Storage.Slots()));
        Assert.Equal(TestPath("clean.spandraft"), (await f.Context.ReadAsync())!.OriginalFilePath);
    }

    [Fact]
    public async Task ReturningToTheSavepointRemovesTheSlotButKeepsTheContextId()
    {
        var f = new Fixture(); var session = Dirty();
        f.Context.ProjectChanged(session); await f.Publish(); var id = f.Context.ProjectRecoveryId;
        session.Undo(); f.Context.ProjectChanged(session); await f.Publish();
        Assert.Empty(f.Storage.Slots()); Assert.Equal(id, f.Context.ProjectRecoveryId);
        session.Redo(); f.Context.ProjectChanged(session); await f.Publish();
        Assert.Equal(RecoverySlot.Project(id!.Value), Assert.Single(f.Storage.Slots()));
    }

    [Fact]
    public async Task ADraftGetsItsOwnIdAndEveryContextGetsDistinctIds()
    {
        var a = new Fixture(); var b = new Fixture();
        a.Context.SetupDraftChanged(new()); b.Context.SetupDraftChanged(new()); await a.Publish(); await b.Publish();
        Assert.Equal(WorkspaceRecoveryState.CreateDraftRecovery, a.Context.State);
        Assert.NotNull(a.Context.SetupRecoveryId); Assert.NotEqual(a.Context.SetupRecoveryId, b.Context.SetupRecoveryId);
        Assert.Equal(RecoverySlot.Setup(a.Context.SetupRecoveryId!.Value), Assert.Single(a.Storage.Slots()));
        Assert.Equal(a.Context.SetupRecoveryId, a.Context.CurrentRecoveryId);
        await a.Context.SetupAbandonedAsync();
        Assert.Equal(WorkspaceRecoveryState.None, a.Context.State); Assert.Empty(a.Storage.Slots());
    }

    [Fact]
    public async Task TheProjectCreatedFromADraftKeepsTheDraftsIdAndIsPublishedBeforeTheDraftGoes()
    {
        var f = new Fixture(); f.Context.SetupDraftChanged(new()); await f.Publish();
        var id = f.Context.SetupRecoveryId!.Value; var session = ProjectSession.Create(State());
        // At the moment the project slot is written, the draft slot must still exist.
        bool draftStillThere = false;
        f.Storage.BeforeWrite = (slot, _, _) =>
        {
            if (slot.Kind == RecoverySlotKind.Project) draftStillThere = f.Storage.Data.ContainsKey(RecoverySlot.Setup(id));
            return Task.CompletedTask;
        };
        await f.Context.ProjectCreatedAsync(session);
        Assert.True(draftStillThere);
        Assert.Equal(WorkspaceRecoveryState.ProjectRecovery, f.Context.State); Assert.Equal(id, f.Context.ProjectRecoveryId); Assert.Null(f.Context.SetupRecoveryId);
        Assert.Equal(RecoverySlot.Project(id), Assert.Single(f.Storage.Slots()));
        Assert.True(session.CurrentRevision.State.ContentEquals((await f.Context.ReadAsync())!.State));
        Assert.Empty(f.Failures);
    }

    [Fact]
    public async Task AProjectWithoutADraftGetsAFreshId()
    {
        var f = new Fixture(); await f.Context.ProjectCreatedAsync(ProjectSession.Create(State()));
        Assert.Equal(WorkspaceRecoveryState.ProjectRecovery, f.Context.State); Assert.Single(f.Storage.Slots(RecoverySlotKind.Project));
    }

    [Fact]
    public async Task AFailedHandOffFlushStillCreatesTheProjectKeepsTheDraftAsFallbackAndRetriesLater()
    {
        var f = new Fixture(); f.Context.SetupDraftChanged(new()); await f.Publish();
        var id = f.Context.SetupRecoveryId!.Value; var session = ProjectSession.Create(State());
        f.Storage.FailWrite = slot => slot.Kind == RecoverySlotKind.Project;
        await f.Context.ProjectCreatedAsync(session);
        Assert.Single(f.Failures); Assert.Equal(WorkspaceRecoveryState.ProjectRecovery, f.Context.State);
        // Crash state: only the draft file exists (the catalog prefers a valid project of the same id once there is one).
        Assert.Equal(RecoverySlot.Setup(id), Assert.Single(f.Storage.Slots()));
        f.Storage.FailWrite = null; await f.Publish();
        Assert.Equal(RecoverySlot.Project(id), Assert.Single(f.Storage.Slots(RecoverySlotKind.Project)));
        // Deleting the project context also removes the draft the failed hand-off left behind.
        await f.Context.DeleteAsync();
        Assert.Empty(f.Storage.Slots()); Assert.Equal(WorkspaceRecoveryState.None, f.Context.State);
    }

    [Fact]
    public async Task AFailedDraftRemovalAfterAGoodHandOffIsReportedButNeverUndoesTheProject()
    {
        var f = new Fixture(); f.Context.SetupDraftChanged(new()); await f.Publish();
        f.Storage.FailDelete = slot => slot.Kind == RecoverySlotKind.Setup;
        await f.Context.ProjectCreatedAsync(ProjectSession.Create(State()));
        Assert.Single(f.Failures); Assert.Equal(WorkspaceRecoveryState.ProjectRecovery, f.Context.State);
        Assert.Single(f.Storage.Slots(RecoverySlotKind.Project)); Assert.Single(f.Storage.Slots(RecoverySlotKind.Setup));
        f.Storage.FailDelete = null; await f.Context.DeleteAsync();
        Assert.Empty(f.Storage.Slots());
    }

    [Fact]
    public async Task AFailedDeleteKeepsTheSlotOwnedSoTheNextAttemptCanRemoveIt()
    {
        var f = new Fixture(); var session = Dirty(); f.Context.ProjectChanged(session); await f.Publish();
        f.Storage.FailDelete = _ => true;
        await Assert.ThrowsAsync<IOException>(() => f.Context.DeleteAsync());
        Assert.Equal(WorkspaceRecoveryState.ProjectRecovery, f.Context.State); Assert.Single(f.Storage.Slots());
        f.Storage.FailDelete = null; await f.Context.DeleteAsync();
        Assert.Empty(f.Storage.Slots()); Assert.Equal(WorkspaceRecoveryState.None, f.Context.State);
    }

    [Fact]
    public async Task FlushProtectsTheLatestStateInTheOneProjectSlot()
    {
        var f = new Fixture(); var session = Dirty(); f.Context.ProjectChanged(session); await f.Publish();
        Assert.Single(f.Storage.Slots()); Assert.Null(f.Context.SetupRecoveryId);
        var latest = State() with { Document = State().Document with { Length = M(3) } };
        await f.Context.FlushAsync(latest, TestPath("target.spandraft"));
        var snapshot = (await f.Context.ReadAsync())!;
        Assert.True(latest.ContentEquals(snapshot.State)); Assert.Equal(TestPath("target.spandraft"), snapshot.OriginalFilePath);
    }

    [Fact]
    public async Task AdoptedSlotsKeepTheirIdAndAnAdoptedProjectWritesToTheSameSlot()
    {
        var f = new Fixture(); var slot = RecoverySlot.Project(RecoveryId.New());
        f.Context.AdoptProject(slot);
        Assert.Equal(slot.Id, f.Context.ProjectRecoveryId);
        Assert.Throws<InvalidOperationException>(() => f.Context.AdoptProject(RecoverySlot.Project(RecoveryId.New())));
        f.Context.ProjectChanged(Dirty()); await f.Publish();
        Assert.Equal(slot, Assert.Single(f.Storage.Slots()));
        // One recovery per workspace: a draft cannot be adopted next to the project slot.
        Assert.Throws<InvalidOperationException>(() => f.Context.AdoptCreateDraft(RecoverySlot.Setup(RecoveryId.New())));

        var other = new Fixture(); var draft = RecoverySlot.Setup(RecoveryId.New()); other.Context.AdoptCreateDraft(draft);
        Assert.Equal(draft.Id, other.Context.SetupRecoveryId);
        Assert.Throws<InvalidOperationException>(() => other.Context.AdoptCreateDraft(RecoverySlot.Setup(RecoveryId.New())));
        Assert.Throws<InvalidOperationException>(() => other.Context.AdoptProject(RecoverySlot.Project(RecoveryId.New())));
    }

    [Fact]
    public async Task AWorkspaceNeverOwnsAProjectSlotAndADraftSlotAtTheSameTime()
    {
        // New over unsaved project work: the draft is not written, the project stays the single recovery.
        var f = new Fixture(); f.Context.ProjectChanged(Dirty()); await f.Publish();
        f.Context.SetupDraftChanged(new()); await f.Publish();
        Assert.Equal(WorkspaceRecoveryState.ProjectRecovery, f.Context.State);
        Assert.Null(f.Context.SetupRecoveryId); Assert.Single(f.Storage.Slots());
        await f.Context.SetupAbandonedAsync(); Assert.Single(f.Storage.Slots());

        // A draft over a project without recovery owns the context; project changes cannot take a second slot meanwhile.
        var other = new Fixture(); other.Context.SetupDraftChanged(new()); await other.Publish();
        other.Context.ProjectChanged(Dirty()); await other.Publish();
        Assert.Equal(WorkspaceRecoveryState.CreateDraftRecovery, other.Context.State);
        Assert.Null(other.Context.ProjectRecoveryId); Assert.Equal(RecoverySlotKind.Setup, Assert.Single(other.Storage.Slots()).Kind);
    }

    [Fact]
    public void AdoptingWithTheWrongKindOfSlotIsRejected()
    {
        var f = new Fixture();
        Assert.Throws<ArgumentException>(() => f.Context.AdoptProject(RecoverySlot.Setup(RecoveryId.New())));
        Assert.Throws<ArgumentException>(() => f.Context.AdoptCreateDraft(RecoverySlot.Project(RecoveryId.New())));
    }

    [Fact]
    public async Task ADetachedRestoreKeepsWritingTheRecoveryOriginAsTheOriginalPath()
    {
        var f = new Fixture(); f.Context.AdoptProject(RecoverySlot.Project(RecoveryId.New()));
        var session = ProjectSession.RestoreDetached(State(), TestPath("origin.spandraft"));
        f.Context.ProjectChanged(session); await f.Publish();
        var snapshot = (await f.Context.ReadAsync())!;
        Assert.Equal(TestPath("origin.spandraft"), snapshot.OriginalFilePath); Assert.Null(session.FilePath);
    }
}
