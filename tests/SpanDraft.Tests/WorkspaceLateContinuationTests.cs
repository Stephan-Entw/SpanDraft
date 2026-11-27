using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Services;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

/// <summary>
/// Phase 8 (§76): a workspace that is disposed while one of its operations waits (its window was closed by a system
/// shutdown). A held gate is released only after the disposal: a running atomic write or a decided transition completes,
/// nothing is decided, committed, owned or shown for the workspace afterwards, and every reservation is released.
/// </summary>
public sealed class WorkspaceLateContinuationTests
{
    private sealed class Workspace
    {
        public ProjectPathRegistry Registry { get; } = new();
        public Files Files { get; } = new();
        public Dialogs Dialogs { get; } = new();
        public TestRecoveryStorage Storage { get; } = new();
        public WorkspaceRecoveryContext Recovery { get; }
        public MainWindowViewModel Main { get; }

        public Workspace(SharedServices? services = null)
        {
            Recovery = new(Storage, new Delay().Wait, () => Now);
            Main = new(WindowId.Create(), Registry, Identities, files: Files, dialogs: Dialogs, recovery: Recovery, services: services);
        }

        /// <summary>A pathless, dirty project created from a confirmed setup; its recovery slot is published.</summary>
        public async Task CreateProject()
        {
            Assert.True(await Main.NewAsync());
            CompleteSetup(Main);
            Assert.True(await Main.Setup.ConfirmAsync());
            Assert.NotNull(await Recovery.ReadAsync());
        }
    }

    private static TaskCompletionSource Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Fact]
    public async Task ASaveWhoseWriteIsRunningCompletesTheFileButCommitsNoOwnershipForAWorkspaceThatIsGone()
    {
        var w = new Workspace(); await w.CreateProject();
        var slot = w.Storage.OnlySlot(RecoverySlotKind.Project); var session = w.Main.Session!;
        string target = w.Dialogs.SavePath!; var gate = Gate();
        w.Files.BeforeWrite = (path, _, _) => path == target ? gate.Task : Task.CompletedTask;
        var save = w.Main.SaveAsync();   // pathless: a new target with a save reservation, held in its atomic write
        Assert.False(save.IsCompleted);
        w.Main.Dispose();
        gate.SetResult();
        Assert.False(await WaitFor(save));
        Assert.True(w.Files.Data.ContainsKey(target));   // the running atomic write was not torn
        Assert.Null(session.FilePath); Assert.True(session.IsDirty);
        Assert.Null(w.Registry.IdentityOwnedBy(w.Main.WindowId));
        Assert.Equal(ProjectPathState.Free, w.Registry.StateOf(Identities.Resolve(target)));
        Assert.Contains(slot, w.Storage.Slots());          // the published recovery is never given up
        Assert.Empty(w.Dialogs.Errors);
    }

    [Fact]
    public async Task ASaveThatWaitsBeforeItsWriteStartsNoWriteAndPutsTheRecoveryBack()
    {
        var w = new Workspace(); await w.CreateProject();
        var slot = w.Storage.OnlySlot(RecoverySlotKind.Project); string target = w.Dialogs.SavePath!; var gate = Gate();
        w.Storage.BeforeWrite = (_, _, _) => gate.Task;   // the protective recovery flush of the save
        var save = w.Main.SaveAsync();
        Assert.False(save.IsCompleted);
        w.Main.Dispose();
        w.Storage.BeforeWrite = null; gate.SetResult();
        Assert.False(await WaitFor(save));
        Assert.False(w.Files.Data.ContainsKey(target));
        Assert.Equal(slot, w.Storage.OnlySlot(RecoverySlotKind.Project));
        Assert.Null((await w.Recovery.ReadAsync())!.OriginalFilePath);   // back to the state before the save
        Assert.Equal(ProjectPathState.Free, w.Registry.StateOf(Identities.Resolve(target)));
        Assert.Empty(w.Dialogs.Errors);
    }

    [Fact]
    public async Task AnOpenWhoseReadIsHeldTakesNoSessionAndNoOwnershipForAWorkspaceThatIsGone()
    {
        var w = new Workspace(); string path = TestPath("late-open.spandraft");
        w.Files.Data[path] = ProjectFileCodec.Serialize(State());
        var gate = Gate(); w.Files.BeforeRead = _ => gate.Task;
        var open = w.Main.OpenAsync(path);
        Assert.False(open.IsCompleted);
        w.Main.Dispose();
        gate.SetResult();
        Assert.False(await WaitFor(open));
        Assert.Null(w.Main.Session);
        Assert.Null(w.Registry.IdentityOwnedBy(w.Main.WindowId));
        Assert.Equal(ProjectPathState.Free, w.Registry.StateOf(Identities.Resolve(path)));
        Assert.Empty(w.Dialogs.Errors); Assert.Empty(w.Storage.Writes);
    }

    [Fact]
    public async Task ACreateWhoseLeaveIsHeldKeepsTheDecidedDiscardButCreatesNoProject()
    {
        var w = new Workspace(); await w.CreateProject();
        var slot = w.Storage.OnlySlot(RecoverySlotKind.Project); var session = w.Main.Session!;
        // Create over the unsaved project (transition until Phase 9): the user discards it, the discard is held.
        Assert.True(await w.Main.NewAsync()); CompleteSetup(w.Main);
        w.Dialogs.Leave = LeaveDecision.Discard; var gate = Gate(); w.Storage.BeforeDiscard = _ => gate.Task;
        var create = w.Main.Setup.ConfirmAsync();
        Assert.False(create.IsCompleted);
        w.Main.Dispose(); int writes = w.Storage.Writes.Count;
        gate.SetResult();
        Assert.False(await WaitFor(create));
        Assert.Equal(slot.FileName, Assert.Single(w.Storage.Discarded));   // the user's decision stays
        Assert.Empty(w.Storage.Slots()); Assert.Equal(writes, w.Storage.Writes.Count);   // no new project slot
        Assert.Same(session, w.Main.Session);
        Assert.Empty(w.Dialogs.Errors);
    }

    [Fact]
    public async Task ASettingsCommitThatPersistsAfterTheWorkspaceWasDisposedNeitherAppliesNorReleasesAnythingThere()
    {
        var files = new Files(); var store = new LocalSettingsStore(files, "settings");
        var services = TestServices.Create(store);
        var w = new Workspace(services); var before = w.Main.ResultPresentation;
        var gate = Gate(); files.BeforeWrite = (_, _, _) => gate.Task;
        var apply = services.SettingsCoordinator.ApplyAsync(
            SettingsPatch.From(services.Settings.CurrentSettings) with { SignificantDigits = before.SignificantDigits + 1 });
        Assert.False(apply.IsCompleted); Assert.True(w.Main.IsWorkspaceBusy);   // frozen for the commit
        w.Main.Dispose();
        gate.SetResult();
        Assert.Equal(SettingsApplyOutcome.Applied, (await WaitFor(apply)).Outcome);
        Assert.Equal(before.SignificantDigits + 1, services.Settings.CurrentSettings.SignificantDigits);   // persisted
        Assert.Same(before, w.Main.ResultPresentation);   // the disposed participant took nothing
        Assert.Empty(w.Dialogs.Errors);
    }
}
