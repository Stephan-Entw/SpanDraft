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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SaveDisposedAtPickerOrSettlingBeginsNoReservationOrRecovery(bool waitInPicker)
    {
        var w = new Workspace(); await w.CreateProject();
        var target = w.Dialogs.SavePath!;
        var gate = Gate();
        int settlements = 0;
        w.Registry.RecoverySettler = () => { settlements++; return waitInPicker ? Task.CompletedTask : gate.Task; };
        if (waitInPicker) w.Dialogs.SaveHook = async () => { await gate.Task; return target; };
        var identity = Identities.Resolve(target);
        var save = w.Main.SaveAsync();
        Assert.False(save.IsCompleted);
        Assert.Equal(waitInPicker ? 0 : 1, settlements);
        Assert.Equal(ProjectPathState.Free, w.Registry.StateOf(identity));
        int recoveryAttempts = 0;
        w.Storage.BeforeWrite = (_, _, _) => { recoveryAttempts++; return Task.CompletedTask; };

        w.Main.Dispose();
        gate.SetResult();

        Assert.False(await WaitFor(save));
        Assert.Equal(waitInPicker ? 0 : 1, settlements);
        Assert.Equal(0, recoveryAttempts);
        Assert.Equal(ProjectPathState.Free, w.Registry.StateOf(identity));
        Assert.False(w.Files.Data.ContainsKey(target));
        Assert.Empty(w.Dialogs.Errors);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InterruptedSaveAsReleasesBothPathsAndKeepsTheCorrectRecoveryOrigin(bool writingProject)
    {
        var w = new Workspace(); await w.CreateProject();
        string source = w.Dialogs.SavePath!;
        Assert.True(await w.Main.SaveAsync());
        Assert.True(w.Main.Session!.Commit(State(2)));
        string target = w.Dialogs.SavePath = TestPath("late-save-as.spandraft");
        var gate = Gate();
        if (writingProject) w.Files.BeforeWrite = (path, _, _) => path == target ? gate.Task : Task.CompletedTask;
        else w.Storage.BeforeWrite = (_, _, _) => gate.Task;
        var save = w.Main.SaveAsync(saveAs: true);
        Assert.False(save.IsCompleted);
        Assert.Equal(ProjectPathState.Open, w.Registry.StateOf(Identities.Resolve(source)));
        Assert.Equal(ProjectPathState.SaveTargetReserved, w.Registry.StateOf(Identities.Resolve(target)));

        w.Main.Dispose();
        gate.SetResult();

        Assert.False(await WaitFor(save));
        Assert.Equal(writingProject, w.Files.Data.ContainsKey(target));
        Assert.Equal(source, w.Main.Session.FilePath);
        Assert.True(w.Main.Session.IsDirty);
        Assert.Equal(writingProject ? target : source, (await w.Recovery.ReadAsync())!.OriginalFilePath);
        Assert.Equal(ProjectPathState.Free, w.Registry.StateOf(Identities.Resolve(source)));
        Assert.Equal(ProjectPathState.Free, w.Registry.StateOf(Identities.Resolve(target)));
        Assert.Empty(w.Dialogs.Errors);
    }

    [Fact]
    public async Task SaveAlreadyCommittedBeforeRecentsSettlesStartsNoRecoveryDeleteAfterDisposal()
    {
        var recents = new Files();
        var w = new Workspace(TestServices.Create(recentStore: new(recents, "recents")));
        await w.CreateProject();
        var slot = w.Storage.OnlySlot(RecoverySlotKind.Project);
        var gate = Gate();
        recents.BeforeWrite = (_, _, _) => gate.Task;
        var save = w.Main.SaveAsync();
        Assert.False(save.IsCompleted);
        Assert.False(w.Main.Session!.IsDirty);
        Assert.True(w.Files.Data.ContainsKey(w.Dialogs.SavePath!));

        w.Main.Dispose();
        gate.SetResult();

        Assert.True(await WaitFor(save));
        Assert.Contains(slot, w.Storage.Slots());
        Assert.Empty(w.Dialogs.Errors);
    }

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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NewWhoseRecoveryFlushIsHeldOnlyNavigatesWhileTheWorkspaceLives(bool dispose)
    {
        var w = new Workspace(); await w.CreateProject();
        var gate = Gate();
        w.Storage.BeforeWrite = (_, _, _) => gate.Task;
        var create = w.Main.NewAsync();
        Assert.False(create.IsCompleted);

        if (dispose) w.Main.Dispose();
        gate.SetResult();

        Assert.Equal(!dispose, await WaitFor(create));
        Assert.Equal(dispose ? MainViewMode.Editor : MainViewMode.ProjectSetup, w.Main.Mode);
        Assert.NotNull(await w.Recovery.ReadAsync());
        Assert.Empty(w.Dialogs.Errors);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task CancelSetupKeepsTheDiscardButOnlyNavigatesWhileTheWorkspaceLives(bool dispose, bool fail)
    {
        var w = new Workspace();
        var slot = RecoverySlot.Setup(RecoveryId.New());
        var draft = new SetupRecoverySnapshot(new ProjectSetupDraft(), Now);
        w.Storage.Put(slot, ProjectSetupRecovery.Encode(draft));
        w.Main.RestoreSetup(draft, slot);
        var gate = Gate();
        w.Storage.BeforeDiscard = _ => gate.Task;
        if (fail) w.Storage.FailDiscard = _ => true;
        int cancelled = 0;
        w.Main.SetupCancelled += () => cancelled++;
        var cancel = w.Main.CancelSetupAsync();
        Assert.False(cancel.IsCompleted);

        if (dispose) w.Main.Dispose();
        gate.SetResult();

        Assert.Equal(!dispose, await WaitFor(cancel));
        Assert.Equal(dispose ? MainViewMode.ProjectSetup : MainViewMode.Start, w.Main.Mode);
        Assert.Equal(dispose ? 0 : 1, cancelled);
        Assert.Equal(fail ? [] : new[] { slot.FileName }, w.Storage.Discarded);
        Assert.Equal(fail, w.Storage.Data.ContainsKey(slot));
        Assert.Empty(w.Storage.Writes);
        Assert.Empty(w.Dialogs.Errors);
    }

    [Theory]
    [InlineData(LeaveDecision.Save)]
    [InlineData(LeaveDecision.Discard)]
    public async Task ASetupLeaveAnswerAfterDisposalStartsNeitherSaveNorDiscard(LeaveDecision decision)
    {
        var w = new Workspace(); await w.CreateProject();
        var session = w.Main.Session;
        var slot = w.Storage.OnlySlot(RecoverySlotKind.Project);
        Assert.True(await w.Main.NewAsync()); CompleteSetup(w.Main);
        var gate = new TaskCompletionSource<LeaveDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
        w.Dialogs.LeaveHook = () => gate.Task;
        var create = w.Main.Setup.ConfirmAsync();
        Assert.Equal(1, w.Dialogs.LeaveQuestions);
        Assert.False(create.IsCompleted);
        int writes = w.Storage.Writes.Count;

        w.Main.Dispose();
        gate.SetResult(decision);

        Assert.False(await WaitFor(create));
        Assert.Same(session, w.Main.Session);
        Assert.Equal(MainViewMode.ProjectSetup, w.Main.Mode);
        Assert.Equal(0, w.Dialogs.SaveQuestions);
        Assert.Contains(slot, w.Storage.Slots());
        Assert.Empty(w.Storage.Discarded);
        Assert.Equal(writes, w.Storage.Writes.Count);
        Assert.Empty(w.Dialogs.Errors);
    }

    [Fact]
    public async Task SetupPublicationFinishingAfterDisposalStartsNoSettingsWriteOrCompanionDelete()
    {
        var settings = new Files();
        var w = new Workspace(TestServices.Create(settingsStore: new(settings, "settings")));
        var id = RecoveryId.New();
        var slot = RecoverySlot.Setup(id);
        var draft = new SetupRecoverySnapshot(new ProjectSetupDraft(), Now);
        w.Storage.Put(slot, ProjectSetupRecovery.Encode(draft));
        w.Main.RestoreSetup(draft, slot);
        CompleteSetup(w.Main);
        var gate = Gate();
        w.Storage.BeforeWrite = (_, _, _) => gate.Task;
        var create = w.Main.Setup.ConfirmAsync();
        Assert.False(create.IsCompleted);

        w.Main.Dispose();
        gate.SetResult();

        Assert.False(await WaitFor(create));
        Assert.Contains(RecoverySlot.Project(id), w.Storage.Slots());
        Assert.Contains(slot, w.Storage.Slots());
        Assert.Empty(settings.Writes);
        Assert.Null(w.Main.Settings.LastAuthor);
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
