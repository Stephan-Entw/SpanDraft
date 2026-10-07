using System.Text;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.ViewModels;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

public sealed class ProjectWorkflowTests
{
    [Fact]
    public async Task EmptySetupDoesNotCreateSessionAnalysisOrRecovery()
    {
        var app = new App(create: false);
        Assert.Null(app.Main.Session); Assert.Equal(0, app.Analyses);
        Assert.False(app.Main.SaveCommand.CanExecute(null));
        Assert.True(await app.Main.InitializeRecoveryAsync());
        Assert.Empty(app.Files.Writes); Assert.Empty(app.Files.Data); Assert.Empty(app.Delay.Waiters);
        app.Main.Setup.ApplyCommand.Execute(null);
        Assert.True(app.Main.Session!.IsDirty); Assert.Equal(1, app.Analyses);
        Assert.Contains("* — SpanDraft", app.Main.WindowTitle);
    }

    [Fact]
    public async Task SaveAndSaveAsCaptureOnlyCommittedStateAndRetainDraftAndHistory()
    {
        var app = new App(); app.ChangeLength();
        var editor = app.Main.Editor!;
        editor.ToggleSupportTool(SpanDraft.Core.Supports.SupportType.Fixed);
        editor.HoverPlacement(M(0)); editor.PlaceSupport();
        editor.SupportDraft!.PositionText = "-"; editor.SupportDraft.NameText = "Still typing";
        var draft = editor.SupportDraft;
        var revision = app.Main.Session!.CurrentRevision;
        Assert.True(await app.Main.SaveAsync());
        Assert.Same(revision, app.Main.Session.CurrentRevision);
        Assert.Single(app.Main.Session.UndoHistory); Assert.False(app.Main.Session.IsDirty);
        Assert.Same(draft, editor.SupportDraft); Assert.Equal("-", draft.PositionText);
        Assert.True(revision.State.ContentEquals(ProjectFileCodec.Deserialize(app.Files.Data[app.Dialogs.SavePath!])));
        Assert.False(app.Files.Data.ContainsKey(App.Slot));
        Assert.Equal("Beam01.spandraft — SpanDraft", app.Main.WindowTitle);
        Assert.True(app.Main.Undo()); Assert.True(app.Main.Session.IsDirty);
        Assert.True(app.Main.Redo()); Assert.False(app.Main.Session.IsDirty);
        app.Dialogs.SavePath = "/test/Copy.SPANDRAFT";
        Assert.True(await app.Main.SaveAsync(saveAs: true));
        Assert.Equal("/test/Copy.SPANDRAFT", app.Main.Session.FilePath);
        Assert.Same(revision, app.Main.Session.CurrentRevision);
        Assert.Equal(4, app.Analyses);
    }

    [Theory]
    [InlineData("/test/beam", "/test/beam.spandraft")]
    [InlineData("/test/beam.spandraft", "/test/beam.spandraft")]
    [InlineData("/test/beam.SpanDraft", "/test/beam.SpanDraft")]
    public void SaveAsExtensionIsAddedOnlyWhenMissing(string chosen, string expected) =>
        Assert.Equal(expected, MainWindowViewModel.EnsureProjectExtension(chosen));

    [Fact]
    public async Task FailedSaveAsPreservesOldFileSavepointPathAndCurrentDraft()
    {
        var app = new App(); Assert.True(await app.Main.SaveAsync());
        var oldPath = app.Main.Session!.FilePath!; var oldBytes = app.Files.Data[oldPath].ToArray();
        var saved = app.Main.Session.SavedRevisionId;
        app.ChangeLength();
        app.Main.Editor!.DimensionLength.Begin(); app.Main.Editor.DimensionLength.Text = "invalid";
        app.Dialogs.SavePath = "/test/broken.spandraft"; app.Files.FailWritePath = app.Dialogs.SavePath;
        Assert.False(await app.Main.SaveAsync(saveAs: true));
        Assert.Equal(oldPath, app.Main.Session.FilePath); Assert.Equal(saved, app.Main.Session.SavedRevisionId);
        Assert.True(app.Main.Session.IsDirty); Assert.Equal(oldBytes, app.Files.Data[oldPath]);
        Assert.True(app.Main.Editor.DimensionLength.IsEditing); Assert.Equal("invalid", app.Main.Editor.DimensionLength.Text);
        Assert.Single(app.Dialogs.Errors);
        Assert.Equal(oldPath, (await app.Recovery.ReadAsync())!.OriginalFilePath);
        app.Dialogs.SavePath = null;
        Assert.False(await app.Main.SaveAsync(saveAs: true));
        Assert.Equal(oldPath, app.Main.Session.FilePath); Assert.Equal(saved, app.Main.Session.SavedRevisionId);
    }

    [Fact]
    public async Task InvalidOpenNeverAsksLeaveAndLeavesSessionAndTransientBuffersUntouched()
    {
        var app = new App(); app.ChangeLength();
        app.Delay.ReleaseAll(); await app.Recovery.DrainAsync();
        var recovery = app.Files.Data[App.Slot].ToArray();
        var session = app.Main.Session; var revision = session!.CurrentRevision;
        app.Main.Editor!.DimensionLength.Begin(); app.Main.Editor.DimensionLength.Text = "-";
        app.Dialogs.OpenPath = "/test/invalid.spandraft";
        app.Files.Data[app.Dialogs.OpenPath] = Encoding.UTF8.GetBytes("{}");
        app.Dialogs.Leave = LeaveDecision.Discard;
        Assert.False(await app.Main.OpenAsync());
        Assert.Equal(0, app.Dialogs.LeaveQuestions); Assert.Same(session, app.Main.Session); Assert.Same(revision, session.CurrentRevision);
        Assert.Equal(recovery, app.Files.Data[App.Slot]); Assert.Equal("-", app.Main.Editor.DimensionLength.Text);
        Assert.True(app.Main.Editor.DimensionLength.IsEditing); Assert.Equal(2, app.Analyses);
    }

    [Fact]
    public async Task ValidOpenIsActivatedOnlyAfterLeaveAndAnalyzesExactlyOnce()
    {
        var app = new App(); app.ChangeLength();
        app.Dialogs.OpenPath = "/test/open.spandraft"; app.Files.Data[app.Dialogs.OpenPath] = ProjectFileCodec.Serialize(State(4));
        var session = app.Main.Session;
        Assert.False(await app.Main.OpenAsync()); Assert.Same(session, app.Main.Session); Assert.Equal(2, app.Analyses);
        app.Dialogs.Leave = LeaveDecision.Discard;
        Assert.True(await app.Main.OpenAsync());
        Assert.NotSame(session, app.Main.Session); Assert.False(app.Main.Session!.IsDirty);
        Assert.Empty(app.Main.Session.UndoHistory); Assert.Empty(app.Main.Session.RedoHistory);
        Assert.Equal(app.Main.Session.CurrentRevision.RevisionId, app.Main.Session.SavedRevisionId);
        Assert.Equal(app.Dialogs.OpenPath, app.Main.Session.FilePath);
        Assert.True(State(4).ContentEquals(app.Main.Session.CurrentRevision.State));
        Assert.False(app.Files.Data.ContainsKey(App.Slot)); Assert.Equal(3, app.Analyses);
    }

    [Theory]
    [InlineData("new")]
    [InlineData("open")]
    [InlineData("close")]
    public async Task EveryLeaveActionRetainsEverythingOnCancelAndSaveAsCancel(string action)
    {
        var app = new App();
        app.Delay.ReleaseAll(); await app.Recovery.DrainAsync();
        var session = app.Main.Session; var recovery = app.Files.Data[App.Slot].ToArray();
        app.Main.Editor!.DimensionLength.Begin(); app.Main.Editor.DimensionLength.Text = "bad draft";
        app.Dialogs.OpenPath = "/test/target.spandraft"; app.Files.Data[app.Dialogs.OpenPath] = ProjectFileCodec.Serialize(State());
        Task<bool> Leave() => action switch { "new" => app.Main.NewAsync(), "open" => app.Main.OpenAsync(), _ => app.Main.RequestCloseAsync() };
        Assert.False(await Leave());
        app.Dialogs.Leave = LeaveDecision.Save; app.Dialogs.SavePath = null;
        Assert.False(await Leave());
        Assert.Same(session, app.Main.Session); Assert.True(session!.IsDirty);
        Assert.Equal("bad draft", app.Main.Editor.DimensionLength.Text); Assert.True(app.Main.Editor.DimensionLength.IsEditing);
        Assert.Equal(recovery, app.Files.Data[App.Slot]); Assert.Equal(1, app.Analyses);
    }

    [Theory]
    [InlineData("new", false)]
    [InlineData("close", false)]
    [InlineData("new", true)]
    [InlineData("close", true)]
    public async Task SuccessfulSaveOrDiscardLeavesAndDeletesRecovery(string action, bool save)
    {
        var app = new App(); app.Delay.ReleaseAll(); await app.Recovery.DrainAsync();
        app.Dialogs.Leave = save ? LeaveDecision.Save : LeaveDecision.Discard;
        bool result = action == "new" ? await app.Main.NewAsync() : await app.Main.RequestCloseAsync();
        Assert.True(result); Assert.False(app.Files.Data.ContainsKey(App.Slot));
        Assert.Equal(save, app.Files.Data.ContainsKey(app.Dialogs.SavePath!));
        if (action == "new") { Assert.Null(app.Main.Session); Assert.Equal(MainViewMode.ProjectSetup, app.Main.Mode); }
        app.Delay.ReleaseAll(); await app.Recovery.DrainAsync(); Assert.False(app.Files.Data.ContainsKey(App.Slot));
    }

    [Fact]
    public async Task SaveFailureAbortsLeaveAndRecoveryFailurePreventsUserFileWrite()
    {
        var app = new App(); app.Dialogs.Leave = LeaveDecision.Save;
        app.Files.FailWritePath = app.Dialogs.SavePath;
        Assert.False(await app.Main.NewAsync()); Assert.NotNull(app.Main.Session); Assert.True(app.Main.Session!.IsDirty);
        app.Files.FailWritePath = App.Slot;
        Assert.False(await app.Main.SaveAsync());
        Assert.False(app.Files.Data.ContainsKey(app.Dialogs.SavePath!)); Assert.Null(app.Main.Session.FilePath);
    }

    [Fact]
    public async Task BusyGuardRejectsConcurrentCommandsCommitsAndRepeatedCloseRequests()
    {
        var app = new App(); var entered = new TaskCompletionSource(); var release = new TaskCompletionSource<LeaveDecision>();
        app.Dialogs.LeaveHook = () => { entered.SetResult(); return release.Task; };
        var closing = app.Main.RequestCloseAsync(); await entered.Task;
        var revision = app.Main.Session!.CurrentRevision;
        Assert.True(app.Main.IsBusy); Assert.False(app.Main.NewCommand.CanExecute(null));
        Assert.False(await app.Main.RequestCloseAsync()); Assert.False(await app.Main.NewAsync()); Assert.False(await app.Main.SaveAsync());
        Assert.False(app.Main.Session.Commit(State())); Assert.Same(revision, app.Main.Session.CurrentRevision);
        app.Main.Setup.ApplyCommand.Execute(null); Assert.Same(revision, app.Main.Session.CurrentRevision);
        release.SetResult(LeaveDecision.Cancel);
        Assert.False(await closing); Assert.False(app.Main.IsBusy); Assert.False(app.Main.Session.IsBusy);
        Assert.Equal(1, app.Dialogs.LeaveQuestions);
    }

    [Fact]
    public async Task DirtySaveFlushesTheExactCommittedSnapshotBeforeUserFilePublication()
    {
        var app = new App(); app.ChangeLength(); var revision = app.Main.Session!.CurrentRevision;
        var entered = new TaskCompletionSource(); var release = new TaskCompletionSource();
        app.Files.BeforeWrite = async (path, bytes, token) =>
        {
            if (path != app.Dialogs.SavePath) return;
            var snapshot = await app.Recovery.ReadAsync();
            Assert.NotNull(snapshot); Assert.True(revision.State.ContentEquals(snapshot.State)); Assert.Equal(path, snapshot.OriginalFilePath);
            entered.SetResult(); await release.Task;
        };
        var saving = app.Main.SaveAsync(); await entered.Task;
        Assert.Same(revision, app.Main.Session.CurrentRevision); Assert.True(app.Main.Session.IsDirty);
        Assert.False(await app.Main.OpenAsync()); Assert.False(await app.Main.SaveAsync());
        release.SetResult(); Assert.True(await saving);
        Assert.Equal(new[] { App.Slot, app.Dialogs.SavePath }, app.Files.Writes.ToArray());
        Assert.False(app.Files.Data.ContainsKey(App.Slot)); Assert.False(app.Main.Session.IsDirty);
    }

    [Fact]
    public void UndoDuringSetupDiscardsTemporarySetupAndReturnsToEditor()
    {
        var app = new App(); app.ChangeLength(); app.Main.EditProject();
        Assert.Equal(MainViewMode.ProjectSetup, app.Main.Mode);
        Assert.True(app.Main.Undo()); Assert.Equal(MainViewMode.Editor, app.Main.Mode);
        Assert.Equal(1, app.Main.Editor!.Document.Length.Meters); Assert.Equal(3, app.Analyses);
    }

    [Fact]
    public async Task CleanOpenLeavesWithoutPromptAndMissingTargetPreservesCurrentSession()
    {
        var app = new App(); Assert.True(await app.Main.SaveAsync());
        var current = app.Main.Session;
        app.Dialogs.OpenPath = "/test/missing.spandraft";
        Assert.False(await app.Main.OpenAsync()); Assert.Same(current, app.Main.Session);
        Assert.Equal(0, app.Dialogs.LeaveQuestions);
        app.Files.Data[app.Dialogs.OpenPath] = ProjectFileCodec.Serialize(State());
        Assert.True(await app.Main.OpenAsync()); Assert.Equal(0, app.Dialogs.LeaveQuestions);
        Assert.Equal(2, app.Analyses); Assert.False(app.Main.Session!.IsDirty);
    }

    [Fact]
    public void FileMenuAndBusyFocusLossPreserveInlineBufferWithoutImplicitCommit()
    {
        var app = new App(); var editor = app.Main.Editor!;
        var revision = editor.Session.CurrentRevision;
        editor.DimensionLength.Begin(); editor.DimensionLength.Text = "2500";
        app.Main.SetFileMenuOpen(true); editor.DimensionLength.LoseFocus();
        Assert.Same(revision, editor.Session.CurrentRevision); Assert.Equal("2500", editor.DimensionLength.Text);
        Assert.True(editor.DimensionLength.IsEditing);
        app.Main.SetFileMenuOpen(false); editor.Session.SetBusy(true); editor.DimensionLength.LoseFocus();
        Assert.Same(revision, editor.Session.CurrentRevision); Assert.True(editor.DimensionLength.IsEditing);
        editor.Session.SetBusy(false); editor.DimensionLength.Cancel(); Assert.Equal(1, app.Analyses);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SavingDuringLeaveDoesNotActivateAStaleReadOfTheSameOpenTarget(bool saveAs)
    {
        var app = new App();
        if (!saveAs) Assert.True(await app.Main.SaveAsync());
        else app.Files.Data[app.Dialogs.SavePath!] = ProjectFileCodec.Serialize(State());
        app.ChangeLength(2);
        app.Dialogs.Leave = LeaveDecision.Save; app.Dialogs.OpenPath = app.Dialogs.SavePath;
        var stateBeingSaved = app.Main.Session!.CurrentRevision.State;
        Assert.True(await app.Main.OpenAsync());
        Assert.True(stateBeingSaved.ContentEquals(app.Main.Session!.CurrentRevision.State));
        Assert.True(stateBeingSaved.ContentEquals(ProjectFileCodec.Deserialize(app.Files.Data[app.Dialogs.OpenPath!])));
        Assert.False(app.Main.Session.IsDirty); Assert.Empty(app.Main.Session.UndoHistory); Assert.Equal(3, app.Analyses);
    }
}
