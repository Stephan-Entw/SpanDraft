using System.Text;
using System.Text.Json.Nodes;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

public sealed class ProjectRecoveryTests
{
    [Fact]
    public async Task DebounceCoalescesRapidCommitsAndTracksUndoRedo()
    {
        var app = new App(); app.ChangeLength(1.2); app.ChangeLength(1.4); app.ChangeLength(1.6);
        Assert.Empty(app.Files.Writes);
        app.Delay.ReleaseAll(); await app.Recovery.DrainAsync();
        Assert.Single(app.Files.Writes);
        Assert.Equal(1.6, (await app.Recovery.ReadAsync())!.State.Document.Length.Meters);
        app.Main.Undo(); app.Delay.ReleaseAll(); await app.Recovery.DrainAsync();
        Assert.Equal(1.4, (await app.Recovery.ReadAsync())!.State.Document.Length.Meters);
        app.Main.Redo(); app.Delay.ReleaseAll(); await app.Recovery.DrainAsync();
        Assert.Equal(1.6, (await app.Recovery.ReadAsync())!.State.Document.Length.Meters);
    }

    [Fact]
    public async Task ReturningToSavepointDeletesRecoveryAndNewEditsCreateItAgain()
    {
        var app = new App(); Assert.True(await app.Main.SaveAsync());
        app.ChangeLength(); app.Delay.ReleaseAll(); await app.Recovery.DrainAsync(); Assert.NotNull(await app.Recovery.ReadAsync());
        app.Main.Undo(); await app.Recovery.DrainAsync();
        Assert.False(app.Main.Session!.IsDirty); Assert.Null(await app.Recovery.ReadAsync());
        app.Main.Redo(); app.Delay.ReleaseAll(); await app.Recovery.DrainAsync(); Assert.NotNull(await app.Recovery.ReadAsync());
        Assert.True(await app.Main.SaveAsync()); Assert.Null(await app.Recovery.ReadAsync());
        app.ChangeLength(2); app.Delay.ReleaseAll(); await app.Recovery.DrainAsync(); Assert.NotNull(await app.Recovery.ReadAsync());
    }

    [Fact]
    public async Task IgnoredDelayCancellationCannotRecreateRecoveryAfterSaveOrLeave()
    {
        var files = new Files(); var delay = new Delay(honorCancellation: false);
        var recovery = new ProjectRecovery(files, App.Slot, delay.Wait, () => Now);
        var session = ProjectSession.Create(State());
        recovery.Schedule(session);
        var oldTask = recovery.DrainAsync();
        await recovery.FlushAsync(session.CurrentRevision.State, "/test/file.spandraft");
        session.MarkSaved(session.CurrentRevision, "/test/file.spandraft");
        await recovery.DeleteAsync();
        delay.ReleaseAll(); await oldTask;
        Assert.Null(await recovery.ReadAsync()); Assert.Single(files.Writes);
        session.Commit(State() with { Document = State().Document with { Length = M(2) } });
        recovery.Schedule(session); oldTask = recovery.DrainAsync();
        await recovery.DeleteAsync(); delay.ReleaseAll(); await oldTask;
        Assert.Null(await recovery.ReadAsync()); Assert.Single(files.Writes);
    }

    [Fact]
    public async Task CleanDeletionDrainsAnAlreadyRunningWriteEvenIfTheStoreIgnoresCancellation()
    {
        var files = new Files { HonorWriteCancellation = false }; var delay = new Delay();
        var recovery = new ProjectRecovery(files, App.Slot, delay.Wait, () => Now);
        var session = ProjectSession.Open(State(), "/test/file.spandraft");
        session.Commit(State() with { Document = State().Document with { Length = M(2) } });
        var entered = new TaskCompletionSource(); var release = new TaskCompletionSource();
        files.BeforeWrite = async (_, _, _) => { entered.SetResult(); await release.Task; };
        recovery.Schedule(session); delay.ReleaseAll(); await entered.Task;
        session.Undo(); recovery.Schedule(session);
        var deleting = recovery.DrainAsync(); Assert.False(deleting.IsCompleted);
        release.SetResult(); await deleting;
        Assert.Null(await recovery.ReadAsync());
    }

    [Fact]
    public async Task SaveFlushDrainsAnOldWriteAndPublishesTheLatestSnapshotBeforeSaving()
    {
        var files = new Files { HonorWriteCancellation = false }; var delay = new Delay();
        var recovery = new ProjectRecovery(files, App.Slot, delay.Wait, () => Now);
        var session = ProjectSession.Create(State());
        var entered = new TaskCompletionSource(); var release = new TaskCompletionSource();
        bool first = true;
        files.BeforeWrite = async (_, _, _) => { if (first) { first = false; entered.SetResult(); await release.Task; } };
        recovery.Schedule(session); delay.ReleaseAll(); await entered.Task;
        var latest = State() with { Document = State().Document with { Length = M(2) } };
        var flush = recovery.FlushAsync(latest, "/test/latest.spandraft"); Assert.False(flush.IsCompleted);
        release.SetResult(); await flush;
        var snapshot = await recovery.ReadAsync();
        Assert.True(latest.ContentEquals(snapshot!.State)); Assert.Equal("/test/latest.spandraft", snapshot.OriginalFilePath);
        await recovery.DeleteAsync(); Assert.Null(await recovery.ReadAsync());
    }

    [Fact]
    public async Task RestoredProjectIsDirtyWithEmptyHistoryOriginalTargetAndOneAnalysis()
    {
        var app = new App(create: false); var state = State(2);
        app.Files.Data[App.Slot] = ProjectRecovery.Encode(new(state, "/test/original.spandraft", Now));
        Assert.True(await app.Main.InitializeRecoveryAsync());
        Assert.True(app.Main.Session!.IsDirty); Assert.Null(app.Main.Session.SavedRevisionId);
        Assert.Empty(app.Main.Session.UndoHistory); Assert.Empty(app.Main.Session.RedoHistory);
        Assert.Equal("/test/original.spandraft", app.Main.Session.FilePath); Assert.Equal(1, app.Analyses);
        Assert.True(state.ContentEquals(app.Main.Session.CurrentRevision.State)); Assert.Equal(1, app.Dialogs.RecoveryQuestions);
        Assert.False(app.Files.Data.ContainsKey("/test/original.spandraft"));
    }

    [Fact]
    public async Task DiscardRecoveryStartsInSetupWithoutAnalysisAndWithoutNewRecovery()
    {
        var app = new App(create: false);
        app.Files.Data[App.Slot] = ProjectRecovery.Encode(new(State(), null, Now));
        app.Dialogs.Recovery = RecoveryDecision.Discard;
        Assert.True(await app.Main.InitializeRecoveryAsync());
        Assert.Null(app.Main.Session); Assert.Equal(0, app.Analyses); Assert.Null(await app.Recovery.ReadAsync());
        Assert.Empty(app.Delay.Waiters);
    }

    [Fact]
    public async Task RedundantRecoveryUsesFullContentEqualityRatherThanBytesOrMechanicalEquality()
    {
        var app = new App(create: false); var state = State(3); const string path = "/test/existing.spandraft";
        app.Files.Data[App.Slot] = ProjectRecovery.Encode(new(state, path, Now));
        var json = JsonNode.Parse(ProjectFileCodec.Serialize(state))!; json["futureField"] = true;
        app.Files.Data[path] = Encoding.UTF8.GetBytes(json.ToJsonString()); // Different formatting and additional data.
        Assert.True(await app.Main.InitializeRecoveryAsync());
        Assert.Null(app.Main.Session); Assert.Equal(0, app.Dialogs.RecoveryQuestions); Assert.Null(await app.Recovery.ReadAsync());
        var renamed = state with { Document = state.Document.WithSupports(state.Document.Supports.Select(s => s.Id == SupportId ? s with { Name = "Different" } : s)) };
        app.Files.Data[App.Slot] = ProjectRecovery.Encode(new(renamed, path, Now));
        Assert.True(await app.Main.InitializeRecoveryAsync());
        Assert.Equal(1, app.Dialogs.RecoveryQuestions); Assert.True(app.Main.Session!.IsDirty);
    }

    [Fact]
    public async Task SaveSuccessWithRecoveryDeleteFailureKeepsSavepointAndStartupRemovesRedundantSlot()
    {
        var app = new App(); app.Files.FailDelete = true;
        Assert.True(await app.Main.SaveAsync());
        Assert.False(app.Main.Session!.IsDirty); Assert.NotNull(await app.Recovery.ReadAsync()); Assert.Single(app.Dialogs.Errors);
        app.Files.FailDelete = false;
        var dialogs = new Dialogs(); var fresh = new MainWindowViewModel(files: app.Files, dialogs: dialogs, recovery: app.Recovery);
        Assert.True(await fresh.InitializeRecoveryAsync()); Assert.Null(await app.Recovery.ReadAsync());
        Assert.Equal(0, dialogs.RecoveryQuestions); Assert.Null(fresh.Session);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"recoveryVersion\":2}")]
    public async Task DamagedRecoveryDoesNotBlockStartupAndCanBeDiscarded(string json)
    {
        var app = new App(create: false); app.Files.Data[App.Slot] = Encoding.UTF8.GetBytes(json);
        Assert.True(await app.Main.InitializeRecoveryAsync());
        Assert.Null(app.Main.Session); Assert.Equal(0, app.Analyses); Assert.Single(app.Dialogs.Errors);
        Assert.Equal(1, app.Dialogs.RecoveryQuestions); Assert.Null(await app.Recovery.ReadAsync());
    }

    [Theory]
    [InlineData("recoveryVersion")]
    [InlineData("writtenAtUtc")]
    [InlineData("project")]
    public async Task RecoveryRequiresEnvelopeFields(string field)
    {
        var files = new Files(); var recovery = new ProjectRecovery(files, App.Slot);
        var json = JsonNode.Parse(ProjectRecovery.Encode(new(State(), null, Now)))!.AsObject(); json.Remove(field);
        files.Data[App.Slot] = Encoding.UTF8.GetBytes(json.ToJsonString());
        await Assert.ThrowsAsync<ProjectFormatException>(() => recovery.ReadAsync());
    }

    [Fact]
    public async Task RecoveryIsStillRestorableWhenTheOriginalProjectFileIsMissingOrMalformed()
    {
        foreach (bool malformed in new[] { false, true })
        {
            var app = new App(create: false); const string path = "/test/missing.spandraft";
            app.Files.Data[App.Slot] = ProjectRecovery.Encode(new(State(), path, Now));
            if (malformed) app.Files.Data[path] = Encoding.UTF8.GetBytes("{}");
            Assert.True(await app.Main.InitializeRecoveryAsync()); Assert.NotNull(app.Main.Session);
            Assert.True(app.Main.Session!.IsDirty); Assert.Equal(1, app.Analyses);
        }
    }

    [Fact]
    public async Task FailedRecoveryDebounceReportsErrorAndTheNextCommitCanRetry()
    {
        var app = new App(); app.Files.FailWritePath = App.Slot;
        app.Delay.ReleaseAll(); await app.Recovery.DrainAsync();
        Assert.Single(app.Dialogs.Errors); Assert.True(app.Main.Session!.IsDirty); Assert.Null(await app.Recovery.ReadAsync());
        app.Files.FailWritePath = null; app.ChangeLength(); app.Delay.ReleaseAll(); await app.Recovery.DrainAsync();
        Assert.NotNull(await app.Recovery.ReadAsync());
    }
}
