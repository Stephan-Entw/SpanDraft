using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using SpanDraft.Desktop.Workspaces;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

/// <summary>
/// Phase 7 on view model level: the WorkspaceCloseTransaction without a window, RecoveryFaulted and its remediation, and
/// the owner release of the path registry. The window lifecycle on top of it is covered by the desktop WorkspaceCloseTests.
/// </summary>
public sealed class WorkspaceCloseTransactionTests
{
    private sealed class Workspace
    {
        public Files Files { get; }
        public Dialogs Dialogs { get; } = new();
        public Delay Delay { get; } = new();
        public TestRecoveryStorage Storage { get; }
        public WorkspaceRecoveryContext Recovery { get; }
        public MainWindowViewModel Main { get; }
        public List<WindowId> Activations { get; } = [];

        public Workspace(ProjectPathRegistry? registry = null, Files? files = null, TestRecoveryStorage? storage = null)
        {
            Files = files ?? new();
            Storage = storage ?? new();
            Recovery = new(Storage, Delay.Wait, () => Now);
            Main = new(WindowId.Create(), registry ?? new(), Identities, activateWorkspace: Activations.Add,
                files: Files, dialogs: Dialogs, recovery: Recovery);
        }

        public async Task CreateProject()
        {
            Assert.True(await Main.NewAsync());
            CompleteSetup(Main);
            Assert.True(await Main.Setup.ConfirmAsync());
            Assert.True(Main.Session!.IsDirty);
            Assert.NotNull(await Recovery.ReadAsync());
        }

        public string Project(string name)
        {
            string path = TestPath(name + ".spandraft");
            Files.Data[path] = ProjectFileCodec.Serialize(State());
            return path;
        }

        /// <summary>A close that discarded the recovery and is then given up while the storage cannot publish: faulted.</summary>
        public async Task Fault()
        {
            var slot = RecoverySlot.Project(Recovery.ProjectRecoveryId!.Value);
            var close = WorkspaceCloseTransaction.TryBegin(Main, handle: null)!;
            Dialogs.Leave = LeaveDecision.Discard;
            Assert.True(await close.PrepareAsync());
            Assert.DoesNotContain(slot, Storage.Slots());
            Storage.FailWrite = _ => true;
            await close.AbortAsync();
            Assert.True(Main.IsRecoveryFaulted);
        }
    }

    [Fact]
    public async Task ACloseThatIsGivenUpAfterTheDiscardPublishesTheCurrentStateAgain()
    {
        var w = new Workspace(); await w.CreateProject();
        var slot = w.Storage.OnlySlot(RecoverySlotKind.Project); var session = w.Main.Session!; var editor = w.Main.Editor;
        var close = WorkspaceCloseTransaction.TryBegin(w.Main, handle: null)!;
        Assert.True(w.Main.IsWorkspaceBusy); Assert.Null(WorkspaceCloseTransaction.TryBegin(w.Main, handle: null));
        w.Dialogs.Leave = LeaveDecision.Discard;
        Assert.True(await close.PrepareAsync());
        Assert.Equal(slot.FileName, Assert.Single(w.Storage.Discarded)); Assert.Empty(w.Storage.Slots());
        // Nothing visible was released before the commit; the rollback restores the protection under the same id.
        Assert.Same(session, w.Main.Session); Assert.Same(editor, w.Main.Editor);
        await close.AbortAsync();
        Assert.Equal(slot, w.Storage.OnlySlot(RecoverySlotKind.Project));
        Assert.True(session.CurrentRevision.State.ContentEquals((await w.Recovery.ReadAsync())!.State));
        Assert.False(w.Main.IsRecoveryFaulted); Assert.False(w.Main.IsWorkspaceBusy); Assert.False(session.IsBusy);
        Assert.Empty(w.Dialogs.Errors);
    }

    [Fact]
    public async Task ACommittedSaveIsNeverUndoneByALaterAbort()
    {
        var w = new Workspace(); await w.CreateProject();
        var close = WorkspaceCloseTransaction.TryBegin(w.Main, handle: null)!;
        w.Dialogs.Leave = LeaveDecision.Save;
        Assert.True(await close.PrepareAsync());
        await close.AbortAsync();
        Assert.False(w.Main.Session!.IsDirty); Assert.True(w.Files.Data.ContainsKey(w.Dialogs.SavePath!));
        Assert.Empty(w.Storage.Discarded);
    }

    [Fact]
    public async Task AFaultedProjectLocksMutationsAndShowsWhyOnce()
    {
        var w = new Workspace(); await w.CreateProject(); await w.Fault();
        var session = w.Main.Session!; var revision = session.CurrentRevision;
        Assert.Contains(w.Dialogs.Errors, e => e.StartsWith(Strings.RecoveryFaultedError, StringComparison.Ordinal));
        Assert.False(session.Commit(State(9))); Assert.Same(revision, session.CurrentRevision);
        Assert.False(w.Main.CanNewProject); Assert.False(await w.Main.NewAsync());
        Assert.False(w.Main.CanOpenProject); Assert.False(w.Main.CanUndo);
        w.Main.EditProject(); Assert.Equal(MainViewMode.Editor, w.Main.Mode);
        Assert.False(w.Main.IsWorkspaceEditable); Assert.False(w.Main.IsWorkspaceBusy);
        Assert.True(w.Main.CanSaveProject); Assert.True(w.Main.RestoreRecoveryProtectionCommand.CanExecute(null));
    }

    [Fact]
    public async Task RestoringTheProtectionIsARemediationAndAFailedRetryKeepsTheFault()
    {
        var w = new Workspace(); await w.CreateProject(); await w.Fault();
        Assert.False(await w.Main.RestoreRecoveryProtectionAsync());
        Assert.True(w.Main.IsRecoveryFaulted);
        w.Storage.FailWrite = null;
        Assert.True(await w.Main.RestoreRecoveryProtectionAsync());
        Assert.False(w.Main.IsRecoveryFaulted); Assert.True(w.Main.IsWorkspaceEditable);
        Assert.NotNull(await w.Recovery.ReadAsync());
        Assert.True(w.Main.Session!.Commit(State(9)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SavingIsARemediationEvenWhileTheRecoveryStorageStillFails(bool saveAs)
    {
        var w = new Workspace(); await w.CreateProject(); await w.Fault();
        Assert.True(await w.Main.SaveAsync(saveAs));
        Assert.False(w.Main.Session!.IsDirty); Assert.True(w.Files.Data.ContainsKey(w.Dialogs.SavePath!));
        Assert.False(w.Main.IsRecoveryFaulted); Assert.True(w.Main.Session.Commit(State(9)));
    }

    [Fact]
    public async Task AFailedSaveKeepsTheFault()
    {
        var w = new Workspace(); await w.CreateProject(); await w.Fault();
        w.Files.FailWritePath = w.Dialogs.SavePath;
        Assert.False(await w.Main.SaveAsync());
        Assert.True(w.Main.IsRecoveryFaulted); Assert.True(w.Main.Session!.IsDirty);
        // A protective flush that happens to succeed inside the failed save does not clear the fault either.
        w.Storage.FailWrite = null;
        Assert.False(await w.Main.SaveAsync());
        Assert.True(w.Main.IsRecoveryFaulted);
    }

    [Fact]
    public async Task AFaultedWorkspaceCanBeClosedAgainAndOtherWorkspacesStayUnaffected()
    {
        var registry = new ProjectPathRegistry(); var storage = new TestRecoveryStorage();
        var a = new Workspace(registry, storage: storage); var b = new Workspace(registry, storage: storage);
        await a.CreateProject(); await b.CreateProject(); await a.Fault();
        storage.FailWrite = null;
        Assert.False(b.Main.IsRecoveryFaulted); Assert.True(b.Main.IsWorkspaceEditable); Assert.True(b.Main.Session!.Commit(State(9)));
        a.Dialogs.Leave = LeaveDecision.Discard;
        Assert.True(await a.Main.RequestCloseAsync());
        Assert.False(a.Main.IsRecoveryFaulted);
        Assert.Equal(b.Recovery.ProjectRecoveryId, Assert.Single(storage.Slots()).Id);
    }

    [Fact]
    public async Task AFaultedDraftLocksTheSetupAndNoSaveClearsIt()
    {
        var w = new Workspace();
        Assert.True(await w.Main.NewAsync()); CompleteSetup(w.Main);
        w.Delay.ReleaseAll(); await WaitFor(w.Recovery.DrainAsync());
        Assert.Single(w.Storage.Slots(RecoverySlotKind.Setup));
        var close = WorkspaceCloseTransaction.TryBegin(w.Main, handle: null)!;
        Assert.True(await close.PrepareAsync());   // CreateSetup closes without a question; its draft is discarded
        Assert.Empty(w.Storage.Slots()); w.Storage.FailWrite = _ => true;
        await close.AbortAsync();
        Assert.Equal(RecoveryFault.Draft, w.Recovery.Fault); Assert.True(w.Main.IsRecoveryFaulted);
        Assert.Equal(MainViewMode.ProjectSetup, w.Main.Mode); Assert.False(w.Main.IsWorkspaceEditable);
        Assert.False(w.Main.CanSaveProject); Assert.False(await w.Main.Setup.ConfirmAsync());
        w.Storage.FailWrite = null;
        Assert.True(await w.Main.RestoreRecoveryProtectionAsync());
        Assert.False(w.Main.IsRecoveryFaulted); Assert.Single(w.Storage.Slots(RecoverySlotKind.Setup));
    }

    [Theory]
    [InlineData(LeaveDecision.Discard, true)]
    [InlineData(LeaveDecision.Save, false)]
    public async Task CloseProjectTombstonesOnlyADeliberateDiscard(LeaveDecision decision, bool tombstone)
    {
        var w = new Workspace(); await w.CreateProject();
        w.Dialogs.Leave = decision;
        Assert.True(await w.Main.CloseProjectAsync());
        Assert.Equal(MainViewMode.Start, w.Main.Mode); Assert.Empty(w.Storage.Slots());
        Assert.Equal(tombstone ? 1 : 0, w.Storage.Discarded.Count);
    }

    // ---- owner release ------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AnOpenOfAPathWhoseOwnerClosesWaitsAndIsDecidedAgainAfterCommitOrRollback(bool commit)
    {
        var registry = new ProjectPathRegistry(); var files = new Files();
        var a = new Workspace(registry, files); var b = new Workspace(registry, files);
        string path = a.Project("owned"); string other = a.Project("other");
        Assert.True(await a.Main.OpenAsync(path));
        var close = WorkspaceCloseTransaction.TryBegin(a.Main, handle: null)!;
        var open = b.Main.OpenAsync(path);
        Assert.False(open.IsCompleted);                       // no AlreadyOpen for a workspace that is closing
        Assert.True(await WaitFor(b.Main.OpenAsync(other)));   // an independent path is not held back
        Assert.True(await close.PrepareAsync());
        if (commit)
        {
            await close.Commit(closeWindow: true);
            Assert.False(open.IsCompleted);                   // the ownership is released only by the final cleanup
            a.Main.Dispose(); close.FinishClosed();
            Assert.True(await WaitFor(open));
            Assert.Equal(Identities.Resolve(path).CanonicalPath, b.Main.Session!.FilePath); Assert.Empty(b.Activations);
        }
        else
        {
            await close.AbortAsync();
            Assert.True(await WaitFor(open));
            Assert.Equal([a.Main.WindowId], b.Activations);
            Assert.Equal(Identities.Resolve(other).CanonicalPath, b.Main.Session!.FilePath);
        }
    }

    [Fact]
    public async Task SaveAsCannotTakeThePathOfAClosingOwner()
    {
        var registry = new ProjectPathRegistry(); var files = new Files();
        var a = new Workspace(registry, files); var b = new Workspace(registry, files);
        string path = a.Project("closing-owner");
        Assert.True(await a.Main.OpenAsync(path)); await b.CreateProject();
        var close = WorkspaceCloseTransaction.TryBegin(a.Main, handle: null)!;
        Assert.True(await close.PrepareAsync()); await close.Commit(closeWindow: true);
        b.Dialogs.SavePath = path;
        Assert.False(await b.Main.SaveAsync(saveAs: true));
        Assert.Contains(b.Dialogs.Errors, e => e.Contains(Strings.ProjectFileInUse, StringComparison.Ordinal));
        a.Main.Dispose(); close.FinishClosed();
        Assert.True(await b.Main.SaveAsync(saveAs: true));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task APathTakenOverBySaveAsDuringTheCloseStaysProtectedUntilCommitOrRollback(bool commit)
    {
        var registry = new ProjectPathRegistry(); var files = new Files(); var storage = new TestRecoveryStorage();
        var a = new Workspace(registry, files, storage); var b = new Workspace(registry, files);
        await a.CreateProject();
        string target = a.Dialogs.SavePath!;
        var close = WorkspaceCloseTransaction.TryBegin(a.Main, handle: null)!;
        a.Dialogs.Leave = LeaveDecision.Save;
        // Synchronization point between the Save As commit and the close commit: the recovery cleanup after the save.
        Task<bool>? open = null;
        storage.BeforeDelete = slot =>
        {
            if (slot.Kind == RecoverySlotKind.Project && open is null)
            {
                Assert.Equal(ProjectPathState.Open, registry.StateOf(Identities.Resolve(target)));
                open = b.Main.OpenAsync(target);
            }
            return Task.CompletedTask;
        };
        Assert.True(await close.PrepareAsync());
        Assert.NotNull(open); Assert.False(open.IsCompleted);
        if (commit)
        {
            await close.Commit(closeWindow: true); a.Main.Dispose(); close.FinishClosed();
            Assert.True(await WaitFor(open));
            Assert.Equal(Identities.Resolve(target).CanonicalPath, b.Main.Session!.FilePath);
        }
        else
        {
            await close.AbortAsync();
            Assert.True(await WaitFor(open));
            Assert.Equal([a.Main.WindowId], b.Activations); Assert.Null(b.Main.Session);
        }
    }

    [Fact]
    public void AnOwnershipChangeDuringTheReleaseServesTheOldPathAndKeepsTheNewOneWaiting()
    {
        var registry = new ProjectPathRegistry(); var window = WindowId.Create();
        var old = Identities.Resolve(TestPath("old.spandraft")); var next = Identities.Resolve(TestPath("next.spandraft"));
        var opening = registry.ArbitrateOpen(old).Reservation!;
        Assert.True(registry.TryCommitOpen(opening, window));
        var token = registry.BeginOwnerRelease(window)!;
        Assert.Null(registry.BeginOwnerRelease(window));
        var waitOld = registry.ArbitrateOpen(old).OwnerRelease!;
        var session = ProjectSession.Open(State(), old.CanonicalPath);
        Assert.Equal(SaveTargetDecision.Reserved, registry.ArbitrateSave(next, window, out var reservation));
        Assert.True(registry.TryCommitSave(reservation!, session.PrepareSave(session.CurrentRevision, next.CanonicalPath)));
        // The Save As pulses the release: the old path is free now, the new one is protected by the same running release.
        Assert.True(waitOld.IsCompleted);
        Assert.Equal(ProjectPathState.Free, registry.StateOf(old));
        var waitNext = registry.ArbitrateOpen(next).OwnerRelease!;
        Assert.False(waitNext.IsCompleted);
        registry.EndOwnerRelease(token);
        Assert.True(waitNext.IsCompleted);
        Assert.Null(registry.ArbitrateOpen(next).OwnerRelease);
        Assert.Equal(ProjectPathState.Open, registry.StateOf(next));
    }
}
