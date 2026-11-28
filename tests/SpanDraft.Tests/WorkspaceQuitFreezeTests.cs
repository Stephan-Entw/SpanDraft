using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using SpanDraft.Desktop.Workspaces;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

/// <summary>
/// Phase 8 on view model level: the application quit's freeze of a workspace (its global reservation), the handover to and
/// from the workspace's own close reservation, and recovery errors deferred meanwhile.
/// </summary>
public sealed class WorkspaceQuitFreezeTests
{
    private sealed class Workspace
    {
        public Files Files { get; } = new();
        public Dialogs Dialogs { get; } = new();
        public Delay Delay { get; } = new();
        public TestRecoveryStorage Storage { get; } = new();
        public WorkspaceRecoveryContext Recovery { get; }
        public MainWindowViewModel Main { get; }

        public Workspace()
        {
            Recovery = new(Storage, Delay.Wait, () => Now);
            Main = new(WindowId.Create(), new(), Identities, files: Files, dialogs: Dialogs, recovery: Recovery);
        }

        public async Task<ProjectSession> Open(string name)
        {
            string path = TestPath(name + ".spandraft");
            Files.Data[path] = ProjectFileCodec.Serialize(State());
            Assert.True(await Main.OpenAsync(path));
            return Main.Session!;
        }
    }

    [Fact]
    public async Task AFrozenWorkspaceTakesNoMutationNoCommandAndNoOpen()
    {
        var w = new Workspace(); var session = await w.Open("frozen"); Assert.True(session.Commit(State(2)));
        string other = TestPath("other.spandraft"); w.Files.Data[other] = ProjectFileCodec.Serialize(State());
        w.Main.FreezeForQuit();
        Assert.True(w.Main.IsBusy); Assert.True(w.Main.IsWorkspaceBusy); Assert.True(session.IsBusy); Assert.False(w.Main.IsWorkspaceEditable);
        var revision = session.CurrentRevision;
        Assert.False(session.Commit(State(3))); Assert.False(w.Main.Undo()); Assert.Same(revision, session.CurrentRevision);
        Assert.False(w.Main.SetResultPresentation(w.Main.Settings.Profile, 5));
        Assert.False(w.Main.CanNewProject); Assert.False(await w.Main.NewAsync());
        Assert.False(w.Main.CanSaveProject); Assert.False(await w.Main.SaveAsync());
        Assert.Equal(ProjectOpenStatus.Unavailable, (await w.Main.OpenPathResultAsync(other, fromRecent: false)).Status);
        Assert.False(w.Main.CanShowRecovery); Assert.Null(WorkspaceCloseTransaction.TryBegin(w.Main, handle: null));
        w.Main.ReleaseQuitFreeze();
        Assert.False(w.Main.IsWorkspaceBusy); Assert.False(session.IsBusy); Assert.True(session.Commit(State(3)));
    }

    [Fact]
    public async Task AnOperationEndingWhileFrozenNeverReleasesTheSession()
    {
        var w = new Workspace(); var session = await w.Open("ending");
        string other = TestPath("ending-other.spandraft"); w.Files.Data[other] = ProjectFileCodec.Serialize(State(4));
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        w.Files.BeforeRead = _ => gate.Task;
        w.Dialogs.Leave = LeaveDecision.Cancel;
        var open = w.Main.OpenAsync(other);   // an operation of the workspace is running (held in its read)
        w.Main.FreezeForQuit();
        gate.SetResult();
        await WaitFor(open);
        Assert.True(w.Main.IsQuitFrozen); Assert.True(w.Main.IsBusy); Assert.True(w.Main.Session!.IsBusy);
        Assert.False(w.Main.Session.Commit(State(5)));
    }

    [Fact]
    public async Task TheHandoverBetweenQuitFreezeAndCloseReservationNeverShowsTheWorkspaceIdle()
    {
        var w = new Workspace(); var session = await w.Open("handover"); Assert.True(session.Commit(State(2)));
        var observed = new List<(bool Busy, bool SessionBusy)>();
        w.Main.FreezeForQuit();
        w.Main.PropertyChanged += (_, _) => observed.Add((w.Main.IsBusy, session.IsBusy));
        // An ordinary close cannot begin on a frozen workspace; only the quit's handover can.
        Assert.Null(WorkspaceCloseTransaction.TryBegin(w.Main, handle: null));
        var close = WorkspaceCloseTransaction.TryBegin(w.Main, handle: null, fromQuitFreeze: true)!;
        Assert.False(w.Main.IsQuitFrozen); Assert.True(w.Main.IsBusy);
        w.Dialogs.Leave = LeaveDecision.Cancel;
        Assert.False(await close.PrepareAsync());
        await close.AbortAsync();   // back to the freeze, not to an idle workspace
        Assert.True(w.Main.IsQuitFrozen);
        Assert.NotEmpty(observed); Assert.All(observed, o => Assert.True(o.Busy && o.SessionBusy));
        w.Main.ReleaseQuitFreeze();
        Assert.False(w.Main.IsBusy); Assert.False(session.IsBusy);
    }

    [Fact]
    public async Task ARecoveryErrorWhileFrozenIsDeferredAndReportedOnceAfterTheRelease()
    {
        var w = new Workspace(); var session = await w.Open("deferred"); Assert.True(session.Commit(State(2)));
        w.Main.FreezeForQuit();
        w.Storage.FailWrite = _ => true;
        w.Delay.ReleaseAll(); await WaitFor(w.Recovery.DrainAsync());
        Assert.Empty(w.Dialogs.Errors); Assert.True(session.IsBusy);
        w.Main.ReleaseQuitFreeze();
        Assert.Single(w.Dialogs.Errors, e => e.StartsWith(Strings.RecoveryError, StringComparison.Ordinal));
        Assert.False(w.Main.IsRecoveryFaulted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TheQuitCheckpointSupersedesARecoveryErrorDeferredBeforeIt(bool checkpointFails)
    {
        var w = new Workspace(); var session = await w.Open("superseded"); Assert.True(session.Commit(State(2)));
        w.Main.FreezeForQuit();
        w.Storage.FailWrite = _ => true;
        w.Delay.ReleaseAll(); await WaitFor(w.Recovery.DrainAsync());   // deferred, not shown
        w.Storage.FailWrite = checkpointFails ? _ => true : null;
        var failure = await WaitFor(w.Main.CheckpointRecoveryAsync());
        Assert.Equal(checkpointFails, failure is not null);
        if (!checkpointFails) Assert.True(session.CurrentRevision.State.ContentEquals((await w.Recovery.ReadAsync())!.State));
        w.Main.ReleaseQuitFreeze();
        Assert.Empty(w.Dialogs.Errors);   // the coordinator shows the checkpoint's failure; nothing older follows
        Assert.False(w.Main.IsRecoveryFaulted);
    }
}
