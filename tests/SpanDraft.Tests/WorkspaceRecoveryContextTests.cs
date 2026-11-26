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
        public Delay Delay { get; }
        public WorkspaceRecoveryContext Context { get; }
        public List<Exception> Failures { get; } = [];
        public int FaultChanges { get; private set; }
        // A delay that ignores cancellation lets a superseded debounce reach the storage: only the generation rule stops it.
        public Fixture(bool honorCancellation = true)
        {
            Delay = new(honorCancellation);
            Context = new(Storage, Delay.Wait, () => Now);
            Context.Failed += Failures.Add;
            Context.FaultChanged += () => FaultChanges++;
        }
        public async Task Publish() { Delay.ReleaseAll(); await WaitFor(Context.DrainAsync()); }
        /// <summary>The Create draft is given up deliberately (setup cancel): its slot goes to Discarded.</summary>
        public async Task DiscardDraft(ProjectSetupDraft? draft = null) =>
            (await Context.PrepareLeaveAsync(RecoveryLeaveScope.Draft, false, null, draft ?? new())).Commit(retire: false);
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
        var slot = Assert.Single(a.Storage.Slots());
        await a.DiscardDraft();
        Assert.Equal(WorkspaceRecoveryState.None, a.Context.State); Assert.Empty(a.Storage.Slots());
        Assert.Equal(slot.FileName, Assert.Single(a.Storage.Discarded));
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
        await f.DiscardDraft(); Assert.Single(f.Storage.Slots()); Assert.Empty(f.Storage.Discarded);

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

    // ---- cleanup remnants are never the active context ------------------------------------------------------------

    [Fact]
    public async Task ADraftSlotThatCannotBeDiscardedStaysTheActiveDraftAndIsNeverARemnant()
    {
        var f = new Fixture(); f.Context.SetupDraftChanged(new()); await f.Publish();
        var slot = Assert.Single(f.Storage.Slots()); var id = f.Context.SetupRecoveryId;
        f.Storage.FailDiscard = _ => true;
        await Assert.ThrowsAsync<IOException>(() => f.DiscardDraft());
        // Deliberately giving the draft up failed: it keeps its slot, its id and its protection; nothing is left behind.
        Assert.Equal(WorkspaceRecoveryState.CreateDraftRecovery, f.Context.State); Assert.Equal(id, f.Context.SetupRecoveryId);
        Assert.Empty(f.Context.Remnants); Assert.Equal(slot, Assert.Single(f.Storage.Slots())); Assert.Empty(f.Storage.Discarded);
        Assert.Equal(RecoveryFault.None, f.Context.Fault);
        f.Storage.FailDiscard = null; await f.DiscardDraft();
        Assert.Empty(f.Storage.Slots()); Assert.Equal(slot.FileName, Assert.Single(f.Storage.Discarded));
    }

    [Fact]
    public async Task ANewDraftAfterADiscardedDraftGetsItsOwnId()
    {
        var f = new Fixture(); f.Context.SetupDraftChanged(new()); await f.Publish(); var old = f.Context.SetupRecoveryId;
        await f.DiscardDraft();
        f.Context.SetupDraftChanged(new()); await f.Publish();
        Assert.Equal(WorkspaceRecoveryState.CreateDraftRecovery, f.Context.State); Assert.NotEqual(old, f.Context.SetupRecoveryId);
        Assert.Empty(f.Context.Remnants);
    }

    [Fact]
    public async Task TheProjectSlotOutlivesItsRemnantDraftSoThatTheDraftNeverReappearsAsAnOldDraft()
    {
        var f = new Fixture(); f.Context.SetupDraftChanged(new()); await f.Publish();
        f.Storage.FailDelete = slot => slot.Kind == RecoverySlotKind.Setup;
        var session = ProjectSession.Create(State()); await f.Context.ProjectCreatedAsync(session);
        var id = f.Context.ProjectRecoveryId!.Value;
        Assert.Equal([RecoverySlot.Setup(id)], f.Context.Remnants); Assert.Contains(RecoverySlot.Project(id), f.Storage.Slots());
        // Save/cleanup: the draft cannot be deleted again, so the project slot must not be deleted either.
        await Assert.ThrowsAsync<IOException>(f.Context.DeleteAsync);
        Assert.Contains(RecoverySlot.Project(id), f.Storage.Slots()); Assert.Equal(WorkspaceRecoveryState.ProjectRecovery, f.Context.State);
        // A restart now sees the valid project with its superseded draft: no old draft is offered.
        var catalog = new RecoveryCatalog(f.Storage, new Files(), Identities, new ProjectPathRegistry());
        Assert.True(await catalog.InitializeAsync(TestContext.Current.CancellationToken));
        Assert.Equal(RecoverySlotKind.Project, Assert.Single(catalog.Candidates).Kind);
        // Once the draft can be deleted the cleanup completes in the right order.
        f.Storage.FailDelete = null; await f.Context.DeleteAsync();
        Assert.Empty(f.Storage.Slots()); Assert.Empty(f.Context.Remnants); Assert.Equal(WorkspaceRecoveryState.None, f.Context.State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ANewWriteSupersedesAProjectDeleteWaitingForItsCompanion(bool explicitDelete)
    {
        var f = new Fixture();
        f.Context.SetupDraftChanged(new());
        await f.Publish();
        var draft = Assert.Single(f.Storage.Slots());
        f.Storage.FailDelete = slot => slot == draft;
        var session = ProjectSession.Create(State());
        await f.Context.ProjectCreatedAsync(session);
        f.Storage.FailDelete = null;
        var project = RecoverySlot.Project(draft.Id!.Value);
        session.MarkSaved(session.CurrentRevision, TestPath("saved.spandraft"));

        var writing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var deletingCompanion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCompanion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Storage.HonorWriteCancellation = false;
        f.Storage.BeforeWrite = (_, _, _) => { writing.SetResult(); return WaitFor(releaseWrite.Task); };
        bool projectDeleted = false;
        f.Storage.BeforeDelete = slot =>
        {
            if (slot == project) { projectDeleted = true; return Task.CompletedTask; }
            deletingCompanion.SetResult();
            return WaitFor(releaseCompanion.Task);
        };
        session.Commit(State(2));
        f.Context.ProjectChanged(session);
        f.Delay.ReleaseAll();
        var write = f.Context.DrainAsync();
        Task? delete = null;
        try
        {
            await WaitForCheckpoint(writing.Task, write);
            f.Storage.BeforeWrite = null;
            Assert.True(session.Undo());
            if (explicitDelete) delete = f.Context.DeleteAsync();
            else
            {
                f.Context.ProjectChanged(session);
                delete = f.Context.DrainAsync();
            }
            Assert.False(deletingCompanion.Task.IsCompleted);
            releaseWrite.SetResult();
            await WaitForCheckpoint(deletingCompanion.Task, delete);
            Assert.True(session.Redo());
            f.Context.ProjectChanged(session);
        }
        finally
        {
            releaseWrite.TrySetResult();
            releaseCompanion.TrySetResult();
            await WaitFor(write);
            if (delete is not null) await WaitFor(delete);
            await f.Publish();
        }
        Assert.False(projectDeleted);
        Assert.Equal(project, Assert.Single(f.Storage.Slots()));
        Assert.Empty(f.Context.Remnants);
        Assert.Equal(project.Id, f.Context.ProjectRecoveryId);
        Assert.True(session.CurrentRevision.State.ContentEquals((await f.Context.ReadAsync())!.State));
    }

    [Fact]
    public async Task AProjectThatBecameCleanReleasesItsSlotBeforeTheDraftOwnsTheContext()
    {
        var f = new Fixture(); var session = Dirty();
        f.Context.ProjectChanged(session); await f.Publish();
        session.Undo(); f.Context.ProjectChanged(session);                 // the delete is only scheduled, not yet run
        Assert.Equal(WorkspaceRecoveryState.ProjectRecovery, f.Context.State);
        await f.Context.ReleaseCleanProjectAsync(session);
        Assert.Equal(WorkspaceRecoveryState.None, f.Context.State); Assert.Empty(f.Storage.Slots());
        f.Context.SetupDraftChanged(new()); await f.Publish();
        Assert.Equal(WorkspaceRecoveryState.CreateDraftRecovery, f.Context.State); Assert.Equal(RecoverySlotKind.Setup, Assert.Single(f.Storage.Slots()).Kind);
    }

    [Fact]
    public async Task AProjectThatCannotReleaseItsSlotKeepsTheContextAndNoDraftIsWritten()
    {
        var f = new Fixture(); var session = Dirty();
        f.Context.ProjectChanged(session); await f.Publish();
        session.Undo(); f.Storage.FailDelete = _ => true; f.Context.ProjectChanged(session); await f.Publish();   // the scheduled delete fails
        Assert.Equal(RecoverySlotKind.Project, Assert.Single(f.Storage.Slots()).Kind);
        await Assert.ThrowsAsync<IOException>(() => f.Context.ReleaseCleanProjectAsync(session));
        Assert.Equal(WorkspaceRecoveryState.ProjectRecovery, f.Context.State);
        f.Context.SetupDraftChanged(new()); await f.Publish();
        Assert.Equal(RecoverySlotKind.Project, Assert.Single(f.Storage.Slots()).Kind);
    }

    // ---- Phase 7: leave (PrepareDiscard, rollback, RecoveryFaulted) ---------------------------------------------

    private static async Task<WorkspaceRecoveryContext.RecoveryLeave> Leave(Fixture f, RecoveryLeaveScope scope, bool discard,
        ProjectSession? session = null, ProjectSetupDraft? draft = null) =>
        await WaitFor(f.Context.PrepareLeaveAsync(scope, discard, session, draft));

    [Fact]
    public async Task DiscardTombstonesThePublishedSlotAndAPendingDebounceNeverRecreatesIt()
    {
        var f = new Fixture(honorCancellation: false); var session = Dirty();
        f.Context.ProjectChanged(session); await f.Publish();
        var slot = f.Storage.OnlySlot(RecoverySlotKind.Project);
        Assert.True(session.Commit(State(1))); f.Context.ProjectChanged(session);   // a newer state waits in the debounce
        var leave = await Leave(f, RecoveryLeaveScope.Workspace, discard: true, session);
        Assert.True(leave.ProtectionLifted); Assert.Empty(f.Storage.Slots()); Assert.Equal(slot.FileName, Assert.Single(f.Storage.Discarded));
        f.Delay.ReleaseAll(); await WaitFor(f.Context.DrainAsync());
        leave.Commit(retire: true);
        Assert.Empty(f.Storage.Slots()); Assert.Equal(WorkspaceRecoveryState.None, f.Context.State);
        // A retired context writes nothing any more.
        Assert.True(session.Commit(State(2))); f.Context.ProjectChanged(session); f.Context.SetupDraftChanged(new()); await f.Publish();
        Assert.Empty(f.Storage.Slots());
    }

    [Fact]
    public async Task AWriteThatIsPublishingCompletesBeforeTheSlotIsMovedAndNothingReappears()
    {
        // The write is past its point of no return (it ignores the cancellation of the invalidated debounce).
        var f = new Fixture(); var session = Dirty(); f.Storage.HonorWriteCancellation = false;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Storage.BeforeWrite = async (_, _, _) => { entered.TrySetResult(); await release.Task; };
        f.Context.ProjectChanged(session); f.Delay.ReleaseAll();
        await WaitFor(entered.Task);
        var prepare = f.Context.PrepareLeaveAsync(RecoveryLeaveScope.Workspace, true, session, null);
        Assert.False(prepare.IsCompleted);
        release.SetResult(); f.Storage.BeforeWrite = null;
        var leave = await WaitFor(prepare);
        Assert.True(leave.ProtectionLifted); Assert.Empty(f.Storage.Slots()); Assert.Single(f.Storage.Discarded);
        await WaitFor(f.Context.DrainAsync()); Assert.Empty(f.Storage.Slots());
    }

    [Fact]
    public async Task AnExplicitFlushRequestedBeforeTheDiscardIsCompletedInOrderAndThenMoved()
    {
        var f = new Fixture(); var session = Dirty();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Storage.BeforeWrite = async (_, _, _) => { entered.TrySetResult(); await release.Task; };
        var flush = f.Context.FlushAsync(session.CurrentRevision.State, session.FilePath);
        await WaitFor(entered.Task);
        var prepare = f.Context.PrepareLeaveAsync(RecoveryLeaveScope.Workspace, true, session, null);
        release.SetResult(); f.Storage.BeforeWrite = null;
        // The flush keeps its Phase-6 semantics: it publishes and reports success; only then is the slot moved.
        await WaitFor(flush); var leave = await WaitFor(prepare);
        Assert.Single(f.Storage.Writes); Assert.True(leave.ProtectionLifted);
        Assert.Empty(f.Storage.Slots()); Assert.Single(f.Storage.Discarded);
    }

    [Fact]
    public async Task AnUnpublishedDraftIsMissingAndItsPendingWriteIsInvalidated()
    {
        var f = new Fixture(honorCancellation: false); var draft = new ProjectSetupDraft();
        f.Context.SetupDraftChanged(draft);   // scheduled, not yet written
        var leave = await Leave(f, RecoveryLeaveScope.Workspace, discard: false, draft: draft);
        Assert.False(leave.ProtectionLifted); Assert.Empty(f.Storage.Discarded);
        f.Delay.ReleaseAll(); await WaitFor(f.Context.DrainAsync());
        Assert.Empty(f.Storage.Slots());
        leave.Commit(retire: true); Assert.Equal(WorkspaceRecoveryState.None, f.Context.State);
    }

    [Fact]
    public async Task AMissingSlotIsDistinguishedFromAnAccessErrorOfTheDiscard()
    {
        var f = new Fixture(); f.Context.SetupDraftChanged(new()); await f.Publish();
        var slot = f.Storage.OnlySlot(RecoverySlotKind.Setup);
        f.Storage.FailureFactory = _ => new UnauthorizedAccessException("Injected access failure.");
        f.Storage.FailDiscard = _ => true;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Context.PrepareLeaveAsync(RecoveryLeaveScope.Draft, false, null, new()));
        Assert.Equal(slot, Assert.Single(f.Storage.Slots())); Assert.Equal(WorkspaceRecoveryState.CreateDraftRecovery, f.Context.State);
        // A slot that really is gone (e.g. removed outside) is not an error: there is nothing left to give up.
        f.Storage.FailDiscard = null; f.Storage.Data.Clear();
        var leave = await Leave(f, RecoveryLeaveScope.Draft, discard: false, draft: new());
        Assert.False(leave.ProtectionLifted); Assert.Empty(f.Storage.Discarded);
    }

    [Fact]
    public async Task TheTechnicalReleaseOfACleanProjectDeletesWithoutATombstone()
    {
        var f = new Fixture(); var session = Dirty(); f.Context.ProjectChanged(session); await f.Publish();
        Assert.True(session.Undo()); // back at the savepoint; the delete is only scheduled
        var leave = await Leave(f, RecoveryLeaveScope.Workspace, discard: false, session);
        Assert.False(leave.ProtectionLifted); Assert.Empty(f.Storage.Slots()); Assert.Empty(f.Storage.Discarded);
        Assert.True(await leave.RollbackAsync()); Assert.Empty(f.Storage.Slots()); Assert.Equal(RecoveryFault.None, f.Context.Fault);
    }

    [Fact]
    public async Task TheCompanionDraftIsDiscardedBeforeItsProjectAndAFailedCompanionKeepsEverything()
    {
        var f = new Fixture(); f.Context.SetupDraftChanged(new()); await f.Publish();
        f.Storage.FailDelete = slot => slot.Kind == RecoverySlotKind.Setup;
        var session = ProjectSession.Create(State()); await f.Context.ProjectCreatedAsync(session);
        var id = f.Context.ProjectRecoveryId!.Value; f.Storage.FailDelete = null;
        Assert.Equal([RecoverySlot.Setup(id)], f.Context.Remnants);
        f.Storage.FailDiscard = slot => slot.Kind == RecoverySlotKind.Setup;
        await Assert.ThrowsAsync<IOException>(() => f.Context.PrepareLeaveAsync(RecoveryLeaveScope.Workspace, true, session, null));
        Assert.Contains(RecoverySlot.Project(id), f.Storage.Slots()); Assert.Contains(RecoverySlot.Setup(id), f.Storage.Slots());
        Assert.Equal(RecoveryFault.None, f.Context.Fault); Assert.Equal(WorkspaceRecoveryState.ProjectRecovery, f.Context.State);
        f.Storage.FailDiscard = null;
        var leave = await Leave(f, RecoveryLeaveScope.Workspace, discard: true, session);
        Assert.Equal([RecoverySlot.Setup(id).FileName, RecoverySlot.Project(id).FileName], f.Storage.Discarded.ToArray());
        Assert.Empty(f.Storage.Slots()); leave.Commit(retire: true); Assert.Empty(f.Context.Remnants);
    }

    [Fact]
    public async Task APublishedProjectWhoseMoveFailsAfterItsCompanionWasMovedKeepsItsProtection()
    {
        var f = new Fixture(); f.Context.SetupDraftChanged(new()); await f.Publish();
        f.Storage.FailDelete = slot => slot.Kind == RecoverySlotKind.Setup;
        var session = ProjectSession.Create(State()); await f.Context.ProjectCreatedAsync(session);
        var id = f.Context.ProjectRecoveryId!.Value; f.Storage.FailDelete = null;
        f.Storage.FailDiscard = slot => slot.Kind == RecoverySlotKind.Project; f.Storage.FailWrite = _ => true;
        await Assert.ThrowsAsync<IOException>(() => f.Context.PrepareLeaveAsync(RecoveryLeaveScope.Workspace, true, session, null));
        // The companion is gone, but the valid published project slot still protects the work: no fault.
        Assert.Equal([RecoverySlot.Project(id)], f.Storage.Slots()); Assert.Equal(RecoveryFault.None, f.Context.Fault);
        Assert.Empty(f.Context.Remnants);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ACompanionThatWasTheOnlyFallbackOfAnUnpublishedProjectIsRestoredOrFaults(bool republishFails)
    {
        var f = new Fixture(); f.Context.SetupDraftChanged(new()); await f.Publish();
        // The project's first publication fails: its draft stays as the only crash fallback.
        f.Storage.FailWrite = slot => slot.Kind == RecoverySlotKind.Project;
        var session = ProjectSession.Create(State()); await f.Context.ProjectCreatedAsync(session);
        var id = f.Context.ProjectRecoveryId!.Value;
        Assert.Equal([RecoverySlot.Setup(id)], f.Storage.Slots()); Assert.Single(f.Failures);
        if (!republishFails) f.Storage.FailWrite = null;
        f.Storage.FailDiscard = slot => slot.Kind == RecoverySlotKind.Project;   // an access error, not a missing slot
        await Assert.ThrowsAsync<IOException>(() => f.Context.PrepareLeaveAsync(RecoveryLeaveScope.Workspace, true, session, null));
        Assert.DoesNotContain(RecoverySlot.Setup(id), f.Storage.Slots());
        if (republishFails)
        {
            Assert.Equal(RecoveryFault.Project, f.Context.Fault); Assert.Equal(1, f.FaultChanges); Assert.NotNull(f.Context.FaultError);
            Assert.Empty(f.Storage.Slots());
        }
        else
        {
            Assert.Equal(RecoveryFault.None, f.Context.Fault);
            Assert.True(session.CurrentRevision.State.ContentEquals((await f.Context.ReadAsync())!.State));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ARollbackAfterADiscardPublishesTheCurrentStateAgainOrFaults(bool republishFails)
    {
        var f = new Fixture(); var session = Dirty(); f.Context.ProjectChanged(session); await f.Publish();
        var id = f.Context.ProjectRecoveryId; Assert.True(session.Commit(State(3)));   // newer than the slot (debounce lag)
        f.Context.ProjectChanged(session);
        var leave = await Leave(f, RecoveryLeaveScope.Workspace, discard: true, session);
        if (republishFails) f.Storage.FailWrite = _ => true;
        Assert.Equal(!republishFails, await leave.RollbackAsync());
        Assert.Equal(id, f.Context.ProjectRecoveryId); Assert.Equal(WorkspaceRecoveryState.ProjectRecovery, f.Context.State);
        if (!republishFails)
        {
            Assert.True(State(3).ContentEquals((await f.Context.ReadAsync())!.State)); Assert.Equal(RecoveryFault.None, f.Context.Fault);
            return;
        }
        Assert.Equal(RecoveryFault.Project, f.Context.Fault); Assert.Empty(f.Storage.Slots());
        // Remediation: retrying fails while the storage fails, then restores the protection and clears the fault.
        await Assert.ThrowsAsync<IOException>(() => f.Context.RestoreProtectionAsync(session, null));
        Assert.Equal(RecoveryFault.Project, f.Context.Fault);
        f.Storage.FailWrite = null; await WaitFor(f.Context.RestoreProtectionAsync(session, null));
        Assert.Equal(RecoveryFault.None, f.Context.Fault); Assert.NotNull(await f.Context.ReadAsync());
    }

    [Fact]
    public async Task ADraftFaultIsOnlyClearedByItsOwnRemediationNotByASave()
    {
        var f = new Fixture(); var draft = new ProjectSetupDraft(); f.Context.SetupDraftChanged(draft); await f.Publish();
        var leave = await Leave(f, RecoveryLeaveScope.Workspace, discard: false, draft: draft);
        f.Storage.FailWrite = _ => true;
        Assert.False(await leave.RollbackAsync()); Assert.Equal(RecoveryFault.Draft, f.Context.Fault);
        f.Context.ProjectSaved(ProjectSession.Open(State(), TestPath("clean.spandraft")));
        Assert.Equal(RecoveryFault.Draft, f.Context.Fault);
        f.Storage.FailWrite = null; await WaitFor(f.Context.RestoreProtectionAsync(null, draft));
        Assert.Equal(RecoveryFault.None, f.Context.Fault); Assert.NotNull(await f.Context.ReadSetupAsync());
    }

    [Fact]
    public async Task ANormalDebounceFailureNeverFaultsTheContext()
    {
        var f = new Fixture(); var session = Dirty(); f.Storage.FailWrite = _ => true;
        f.Context.ProjectChanged(session); await f.Publish();
        Assert.Single(f.Failures); Assert.Equal(RecoveryFault.None, f.Context.Fault); Assert.Equal(0, f.FaultChanges);
        // Nothing was lifted by a close either: a failed protective rollback without a lifted protection is no fault.
        var leave = await Leave(f, RecoveryLeaveScope.Workspace, discard: true, session);
        Assert.False(leave.ProtectionLifted); Assert.True(await leave.RollbackAsync()); Assert.Equal(RecoveryFault.None, f.Context.Fault);
    }
}
