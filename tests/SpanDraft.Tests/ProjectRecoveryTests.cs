using System.Text;
using System.Text.Json.Nodes;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

public sealed class ProjectRecoveryTests
{
    private static ProjectRecovery Writer(TestRecoveryStorage storage, Delay? delay = null) =>
        new(storage, RecoverySlot.Project(RecoveryId.New()), delay is null ? null : delay.Wait, () => Now);

    [Fact]
    public async Task FirstProjectCanBeCreatedBeforeTheLocalStateDirectoryExists()
    {
        string directory = Path.Combine(Path.GetTempPath(), "SpanDraft-tests-" + Guid.NewGuid().ToString("N"));
        var files = new ProjectFileStore(); var delay = new Delay(); var dialogs = new Dialogs();
        var context = new WorkspaceRecoveryContext(new FileRecoveryStorage(directory), delay.Wait, () => Now);
        var main = new MainWindowViewModel(files: files, dialogs: dialogs, recovery: context);
        try
        {
            Assert.False(Directory.Exists(directory));
            await WaitFor(context.DeleteAsync());
            await WaitFor(context.DeleteAsync());
            Assert.False(Directory.Exists(directory));
            Assert.True(await WaitFor(main.NewAsync()));
            CompleteSetup(main);
            Assert.False(Directory.Exists(directory));
            Assert.True(await WaitFor(main.Setup.ConfirmAsync()));
            Assert.NotNull(main.Session); Assert.NotNull(main.Editor);
            Assert.Equal(MainViewMode.Editor, main.Mode);

            // The new project's complete recovery is published at once and creates the directory on the way.
            Assert.True(Directory.Exists(directory));
            delay.ReleaseAll(); await WaitFor(context.DrainAsync());
            var snapshot = await context.ReadAsync();
            Assert.NotNull(snapshot);
            Assert.True(main.Session.CurrentRevision.State.ContentEquals(snapshot.State));
            Assert.Empty(dialogs.Errors);
        }
        finally
        {
            delay.ReleaseAll(); await WaitFor(context.DrainAsync());
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task DebounceCoalescesRapidCommitsAndTracksUndoRedo()
    {
        var app = new App(); int baseline = app.Storage.Writes.Count;
        app.ChangeLength(1.2); app.ChangeLength(1.4); app.ChangeLength(1.6);
        Assert.Equal(baseline, app.Storage.Writes.Count);
        app.Delay.ReleaseAll(); await WaitFor(app.Recovery.DrainAsync());
        Assert.Equal(baseline + 1, app.Storage.Writes.Count);
        Assert.Equal(1.6, app.ProjectSnapshot()!.State.Document.Length.Meters);
        app.Main.Undo(); app.Delay.ReleaseAll(); await WaitFor(app.Recovery.DrainAsync());
        Assert.Equal(1.4, app.ProjectSnapshot()!.State.Document.Length.Meters);
        app.Main.Redo(); app.Delay.ReleaseAll(); await WaitFor(app.Recovery.DrainAsync());
        Assert.Equal(1.6, app.ProjectSnapshot()!.State.Document.Length.Meters);
    }

    [Fact]
    public async Task ReturningToSavepointDeletesRecoveryAndNewEditsCreateItAgain()
    {
        var app = new App(); Assert.True(await app.Main.SaveAsync());
        Assert.False(app.HasRecovery); Assert.Equal(WorkspaceRecoveryState.None, app.Recovery.State);
        app.ChangeLength(); app.Delay.ReleaseAll(); await WaitFor(app.Recovery.DrainAsync()); Assert.True(app.HasRecovery);
        app.Main.Undo(); await WaitFor(app.Recovery.DrainAsync());
        Assert.False(app.Main.Session!.IsDirty); Assert.False(app.HasRecovery);
        app.Main.Redo(); app.Delay.ReleaseAll(); await WaitFor(app.Recovery.DrainAsync()); Assert.True(app.HasRecovery);
        Assert.True(await app.Main.SaveAsync()); Assert.False(app.HasRecovery);
        app.ChangeLength(2); app.Delay.ReleaseAll(); await WaitFor(app.Recovery.DrainAsync()); Assert.True(app.HasRecovery);
    }

    [Fact]
    public async Task UndoAfterSaveDoesNotExposeACompanionThatCouldNotBeDeleted()
    {
        var app = new App(create: false);
        Assert.True(await app.Main.NewAsync());
        CompleteSetup(app.Main);
        app.Delay.ReleaseAll();
        await WaitFor(app.Recovery.DrainAsync());
        var draft = Assert.Single(app.Storage.Slots());
        Assert.Equal(RecoverySlotKind.Setup, draft.Kind);

        var deleted = new List<RecoverySlot>();
        bool failCompanion = true;
        app.Storage.FailDelete = slot => { deleted.Add(slot); return slot == draft && failCompanion; };
        Assert.True(await app.Main.Setup.ConfirmAsync());
        var project = app.Slot;
        Assert.Equal(draft.Id, project.Id);
        Assert.True(await app.Main.SaveAsync());
        Assert.False(app.Main.Session!.IsDirty);
        Assert.Equal(new[] { draft, draft }, deleted);

        app.ChangeLength();
        app.Delay.ReleaseAll();
        await WaitFor(app.Recovery.DrainAsync());
        Assert.True(app.Main.Undo());
        await WaitFor(app.Recovery.DrainAsync());
        Assert.False(app.Main.Session.IsDirty);

        var restarted = new RecoveryCatalog(app.Storage, app.Files, Identities, new ProjectPathRegistry());
        Assert.True(await restarted.InitializeAsync(TestContext.Current.CancellationToken));
        Assert.Equal(project, Assert.Single(restarted.Undecided).Slot);
        Assert.Equal(new[] { draft, draft, draft }, deleted);
        Assert.Equal(3, app.Dialogs.Errors.Count);

        failCompanion = false;
        await WaitFor(app.Recovery.DeleteAsync());
        Assert.Equal(new[] { draft, project }, deleted.TakeLast(2));
        restarted = new(app.Storage, app.Files, Identities, new ProjectPathRegistry());
        Assert.True(await restarted.InitializeAsync(TestContext.Current.CancellationToken));
        Assert.Empty(restarted.Candidates);
    }

    [Fact]
    public async Task IgnoredDelayCancellationCannotRecreateRecoveryAfterSaveOrLeave()
    {
        var storage = new TestRecoveryStorage(); var delay = new Delay(honorCancellation: false);
        var recovery = Writer(storage, delay);
        var session = ProjectSession.Create(State());
        recovery.Schedule(session);
        var oldTask = recovery.DrainAsync();
        try
        {
            await WaitFor(recovery.FlushAsync(session.CurrentRevision.State, TestPath("file.spandraft")));
            session.MarkSaved(session.CurrentRevision, TestPath("file.spandraft"));
            await WaitFor(recovery.DeleteAsync());
        }
        finally
        {
            delay.ReleaseAll(); await WaitFor(oldTask);
        }
        Assert.Null(await recovery.ReadAsync()); Assert.Single(storage.Writes);
        session.Commit(State() with { Document = State().Document with { Length = M(2) } });
        recovery.Schedule(session); oldTask = recovery.DrainAsync();
        try { await WaitFor(recovery.DeleteAsync()); }
        finally { delay.ReleaseAll(); await WaitFor(oldTask); }
        Assert.Null(await recovery.ReadAsync()); Assert.Single(storage.Writes);
    }

    [Fact]
    public async Task CleanDeletionDrainsAnAlreadyRunningWriteEvenIfTheStoreIgnoresCancellation()
    {
        var storage = new TestRecoveryStorage { HonorWriteCancellation = false }; var delay = new Delay();
        var recovery = Writer(storage, delay);
        var session = ProjectSession.Open(State(), TestPath("file.spandraft"));
        session.Commit(State() with { Document = State().Document with { Length = M(2) } });
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        storage.BeforeWrite = async (_, _, _) => { entered.SetResult(); await WaitFor(release.Task); };
        recovery.Schedule(session); delay.ReleaseAll();
        var writing = recovery.DrainAsync();
        try
        {
            await WaitForCheckpoint(entered.Task, writing);
            session.Undo(); recovery.Schedule(session);
            Assert.False(recovery.DrainAsync().IsCompleted);
        }
        finally
        {
            release.TrySetResult();
            await WaitFor(writing);
            await WaitFor(recovery.DrainAsync());
        }
        Assert.Null(await recovery.ReadAsync());
    }

    [Fact]
    public async Task SaveFlushDrainsAnOldWriteAndPublishesTheLatestSnapshotBeforeSaving()
    {
        var storage = new TestRecoveryStorage { HonorWriteCancellation = false }; var delay = new Delay();
        var recovery = Writer(storage, delay);
        var session = ProjectSession.Create(State());
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool first = true;
        storage.BeforeWrite = async (_, _, _) => { if (first) { first = false; entered.SetResult(); await WaitFor(release.Task); } };
        recovery.Schedule(session); delay.ReleaseAll();
        var writing = recovery.DrainAsync();
        var latest = State() with { Document = State().Document with { Length = M(2) } };
        Task? flush = null;
        try
        {
            await WaitForCheckpoint(entered.Task, writing);
            flush = recovery.FlushAsync(latest, TestPath("latest.spandraft")); Assert.False(flush.IsCompleted);
        }
        finally
        {
            release.TrySetResult();
            await WaitFor(writing);
            if (flush is not null) await WaitFor(flush);
        }
        var snapshot = await recovery.ReadAsync();
        Assert.True(latest.ContentEquals(snapshot!.State)); Assert.Equal(TestPath("latest.spandraft"), snapshot.OriginalFilePath);
        await recovery.DeleteAsync(); Assert.Null(await recovery.ReadAsync());
    }

    [Fact]
    public void RestoredProjectIsDirtyWithEmptyHistoryOriginalTargetAndOneAnalysis()
    {
        var app = new App(create: false); var state = State(2);
        app.Restore(new(state, TestPath("original.spandraft"), Now));
        Assert.Equal(MainViewMode.Editor, app.Main.Mode);
        Assert.True(app.Main.Session!.IsDirty); Assert.Null(app.Main.Session.SavedRevisionId);
        Assert.Empty(app.Main.Session.UndoHistory); Assert.Empty(app.Main.Session.RedoHistory);
        Assert.Equal(TestPath("original.spandraft"), app.Main.Session.FilePath); Assert.Equal(1, app.Analyses);
        Assert.True(state.ContentEquals(app.Main.Session.CurrentRevision.State));
        Assert.False(app.Files.Data.ContainsKey(TestPath("original.spandraft")));
        Assert.Equal(WorkspaceRecoveryState.ProjectRecovery, app.Recovery.State);
    }

    [Fact]
    public async Task DetachedRestoreRemembersTheOriginOnlyAsRecoveryOriginAndNeverTurnsItIntoTheSaveTarget()
    {
        var app = new App(create: false); var original = TestPath("original.spandraft");
        app.Restore(new(State(2), original, Now), detached: true);
        var session = app.Main.Session!;
        Assert.True(session.IsDirty); Assert.Null(session.FilePath); Assert.Equal(original, session.RecoveryOriginPath);
        Assert.Null(app.Registry.IdentityOwnedBy(app.Main.WindowId));
        // The next recovery write still names the original.
        app.ChangeLength(); app.Delay.ReleaseAll(); await WaitFor(app.Recovery.DrainAsync());
        Assert.Equal(original, app.ProjectSnapshot()!.OriginalFilePath);
        // Saving needs a new target and clears the origin.
        Assert.True(await app.Main.SaveAsync());
        Assert.Equal(app.Dialogs.SavePath, session.FilePath); Assert.Null(session.RecoveryOriginPath);
        Assert.False(app.Files.Data.ContainsKey(original)); Assert.False(app.HasRecovery);
    }

    [Theory]
    [InlineData("recoveryVersion")]
    [InlineData("writtenAtUtc")]
    [InlineData("project")]
    public async Task RecoveryRequiresEnvelopeFields(string field)
    {
        var storage = new TestRecoveryStorage(); var slot = RecoverySlot.Project(RecoveryId.New());
        var json = JsonNode.Parse(ProjectRecovery.Encode(new(State(), null, Now)))!.AsObject(); json.Remove(field);
        storage.Put(slot, Encoding.UTF8.GetBytes(json.ToJsonString()));
        await Assert.ThrowsAsync<ProjectFormatException>(() => new ProjectRecovery(storage, slot).ReadAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("relative.spandraft")]
    [InlineData("folder/relative.spandraft")]
    public async Task RecoveryRejectsPathsThatAreNotFullyQualified(string path)
    {
        var storage = new TestRecoveryStorage(); var slot = RecoverySlot.Project(RecoveryId.New());
        storage.Put(slot, ProjectRecovery.Encode(new(State(), path, Now)));
        var error = await Assert.ThrowsAsync<ProjectFormatException>(() => new ProjectRecovery(storage, slot).ReadAsync());
        Assert.Equal("Invalid recovery file path.", error.Message);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void RecoveryIsStillRestorableWhenTheOriginalProjectFileIsMissingOrMalformed(bool malformed)
    {
        var app = new App(create: false); string path = TestPath("missing.spandraft");
        if (malformed) app.Files.Data[path] = Encoding.UTF8.GetBytes("{}");
        app.Restore(new(State(), path, Now)); Assert.NotNull(app.Main.Session);
        Assert.True(app.Main.Session!.IsDirty); Assert.Equal(1, app.Analyses);
    }

    [Fact]
    public async Task SaveSuccessWithRecoveryDeleteFailureKeepsSavepointAndTheSlot()
    {
        var app = new App(); app.Storage.FailDelete = _ => true;
        Assert.True(await app.Main.SaveAsync());
        Assert.False(app.Main.Session!.IsDirty); Assert.True(app.HasRecovery); Assert.Single(app.Dialogs.Errors);
    }

    [Fact]
    public async Task FailedRecoveryDebounceReportsErrorAndTheNextCommitCanRetry()
    {
        var app = new App(); app.Storage.FailWrite = _ => true; app.ChangeLength(1.7);
        app.Delay.ReleaseAll(); await WaitFor(app.Recovery.DrainAsync());
        Assert.Single(app.Dialogs.Errors); Assert.True(app.Main.Session!.IsDirty);
        Assert.NotEqual(1.7, app.ProjectSnapshot()!.State.Document.Length.Meters);
        app.Storage.FailWrite = null; app.ChangeLength(1.8); app.Delay.ReleaseAll(); await WaitFor(app.Recovery.DrainAsync());
        Assert.Equal(1.8, app.ProjectSnapshot()!.State.Document.Length.Meters);
    }

    [Fact]
    public async Task AuthorlessProjectRecoveryWritesVersionFiveAndPreservesEveryValueWithoutLearningAnAuthor()
    {
        var state = State() with { Info = new("Beam", "Project", "Drawing", null) };
        var bytes = ProjectRecovery.Encode(new(state, TestPath("original.spandraft"), Now));
        var json = JsonNode.Parse(bytes)!;
        Assert.Equal(1, json["recoveryVersion"]!.GetValue<int>());
        Assert.Equal(5, json["project"]!["formatVersion"]!.GetValue<int>());
        Assert.False(json["project"]!["info"]!.AsObject().ContainsKey("author"));
        var snapshot = ProjectRecovery.Decode(bytes);
        Assert.True(state.ContentEquals(snapshot.State)); Assert.Equal(Now, snapshot.WrittenAtUtc);
        Assert.Equal(TestPath("original.spandraft"), snapshot.OriginalFilePath);
        var main = new MainWindowViewModel(files: new Files(), projectIdentities: Identities, recovery: new(new TestRecoveryStorage()),
            dialogs: new Dialogs(),
            userNameProvider: () => "windows.user", services: TestServices.Create(settings: UserSettings.Default with { LastAuthor = "Local author" }));
        main.RestoreProject(snapshot, RecoverySlot.Project(RecoveryId.New()), null); Assert.True(main.Session!.IsDirty);
        Assert.True(state.ContentEquals(main.Session.CurrentRevision.State));
        Assert.Equal("Local author", main.Settings.LastAuthor);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task ALaterScheduleNeitherCancelsNorSkipsARunningFlush()
    {
        var storage = new TestRecoveryStorage { HonorWriteCancellation = true }; var delay = new Delay();
        var recovery = Writer(storage, delay);
        var session = ProjectSession.Create(State());
        var flushed = State() with { Document = State().Document with { Length = M(2) } };
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        storage.BeforeWrite = async (_, _, _) => { entered.TrySetResult(); await WaitFor(release.Task); };
        var flush = recovery.FlushAsync(flushed, TestPath("flushed.spandraft"));
        try
        {
            await WaitForCheckpoint(entered.Task, flush);
            recovery.Schedule(session);
        }
        finally { release.TrySetResult(); }
        await WaitFor(flush);
        Assert.Equal(TestPath("flushed.spandraft"), (await recovery.ReadAsync())!.OriginalFilePath);
        delay.ReleaseAll(); await WaitFor(recovery.DrainAsync());
    }
}
