using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.ViewModels;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

public sealed class CloseProjectTests
{
    private sealed class Fixture
    {
        public Files Files { get; } = new();
        public Dialogs Dialogs { get; } = new();
        public Delay Delay { get; } = new();
        public RecentProjectsStore Recents { get; }
        public ProjectRecovery Recovery { get; }
        public MainWindowViewModel Main { get; }
        public Fixture()
        {
            Recents = new(Files, "recents", () => Now);
            Recovery = new(Files, "recovery", Delay.Wait, () => Now);
            Main = new(files: Files, dialogs: Dialogs, recovery: Recovery, recentStore: Recents);
        }
        public async Task Open()
        {
            Dialogs.OpenPath = TestPath("open.spandraft");
            Files.Data[Dialogs.OpenPath] = ProjectFileCodec.Serialize(State());
            Assert.True(await Main.OpenAsync());
        }
        public async Task Create()
        {
            Assert.True(await Main.NewAsync());
            ProjectTestSupport.CompleteSetup(Main); Assert.True(await Main.Setup.ConfirmAsync());
        }
        public void AssertClosed()
        {
            Assert.Equal(MainViewMode.Start, Main.Mode);
            Assert.Equal(MainWorkspaceKind.Start, Main.Workspace);
            Assert.Same(Main.StartPage, Main.CurrentViewModel);
            Assert.Null(Main.Editor); Assert.Null(Main.Session);
            Assert.False(Main.IsWorkspaceBusy); Assert.False(Main.CanCloseProject);
            Assert.False(Main.CloseProjectCommand.CanExecute(null));
        }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task CommandAndDirectCallAreDisabledOutsideTheBeamEditor(bool editExisting)
    {
        var f = new Fixture();
        await Disabled();
        int refreshes = 0;
        f.Main.CloseProjectCommand.CanExecuteChanged += (_, _) => refreshes++;
        if (editExisting) { await f.Open(); f.Main.EditProject(); }
        else Assert.True(await f.Main.NewAsync());
        await Disabled();
        f.Main.Setup.ShowMaterials(); await Disabled();
        ((MaterialSelectionViewModel)f.Main.Setup.CurrentStep!).NewCommand.Execute(null); await Disabled();
        f.Main.Setup.Escape(); f.Main.Setup.ShowOverview();
        f.Main.Setup.ShowSections(); await Disabled();
        f.Main.Setup.Escape();
        if (editExisting) f.Main.Setup.CancelCommand.Execute(null);
        else { ProjectTestSupport.CompleteSetup(f.Main); Assert.True(await f.Main.Setup.ConfirmAsync()); }
        Assert.Equal(MainWorkspaceKind.BeamEditor, f.Main.Workspace);
        Assert.NotNull(f.Main.Session); Assert.True(f.Main.CanCloseProject);
        Assert.True(f.Main.CloseProjectCommand.CanExecute(null)); Assert.True(refreshes > 0);

        async Task Disabled()
        {
            var current = f.Main.CurrentViewModel; var session = f.Main.Session;
            Assert.False(f.Main.CanCloseProject); Assert.False(f.Main.CloseProjectCommand.CanExecute(null));
            Assert.False(await f.Main.CloseProjectAsync()); f.Main.CloseProjectCommand.Execute(null);
            Assert.Same(current, f.Main.CurrentViewModel); Assert.Same(session, f.Main.Session);
            Assert.Equal(0, f.Dialogs.LeaveQuestions);
        }
    }

    [Fact]
    public async Task CleanProjectCommandReturnsToStartAndDetachesTheSessionWithoutChangingRecents()
    {
        var f = new Fixture(); await f.Open();
        var session = f.Main.Session!; var entries = f.Recents.Entries;
        var recentRows = f.Main.StartPage.RecentProjects; var writes = f.Files.Writes.ToArray();
        f.Main.CloseProjectCommand.Execute(null);
        f.AssertClosed(); Assert.False(session.IsBusy); Assert.Equal(0, f.Dialogs.LeaveQuestions);
        Assert.Same(entries, f.Recents.Entries); Assert.Same(recentRows, f.Main.StartPage.RecentProjects);
        Assert.Equal(writes, f.Files.Writes.ToArray());
        int notifications = 0; f.Main.PropertyChanged += (_, _) => notifications++;
        Assert.True(session.Commit(State(1))); Assert.Equal(0, notifications);
        f.Delay.ReleaseAll(); await WaitFor(f.Recovery.DrainAsync());
        Assert.Null(await f.Recovery.ReadAsync());
    }

    [Theory]
    [InlineData(LeaveDecision.Save, false)] [InlineData(LeaveDecision.Save, true)]
    [InlineData(LeaveDecision.Discard, false)] [InlineData(LeaveDecision.Discard, true)]
    public async Task DirtyProjectSavesOrDiscardsThenReleasesSessionAndRecovery(LeaveDecision decision, bool existingFile)
    {
        var f = new Fixture();
        if (existingFile) { await f.Open(); Assert.True(f.Main.Session!.Commit(State(1))); }
        else await f.Create();
        var session = f.Main.Session!; var state = session.CurrentRevision.State;
        await f.Recovery.FlushAsync(state, session.FilePath);
        Assert.NotNull(await f.Recovery.ReadAsync());
        var entries = f.Recents.Entries; var recentRows = f.Main.StartPage.RecentProjects;
        int recentWrites = f.Files.Writes.Count(p => p == "recents");
        string path = session.FilePath ?? f.Dialogs.SavePath!;
        byte[]? original = f.Files.Data.TryGetValue(path, out var bytes) ? bytes.ToArray() : null;
        f.Dialogs.Leave = decision;

        Assert.True(await f.Main.CloseProjectAsync()); f.AssertClosed();
        Assert.False(session.IsBusy); Assert.Equal(1, f.Dialogs.LeaveQuestions);
        if (decision == LeaveDecision.Save)
        {
            Assert.True(state.ContentEquals(ProjectFileCodec.Deserialize(f.Files.Data[path])));
            Assert.False(session.IsDirty);
            Assert.Equal(existingFile ? 0 : 1, f.Dialogs.SaveQuestions);
        }
        else if (original is not null) Assert.Equal(original, f.Files.Data[path]);
        else Assert.False(f.Files.Data.ContainsKey(path));
        Assert.Same(entries, f.Recents.Entries); Assert.Same(recentRows, f.Main.StartPage.RecentProjects);
        Assert.Equal(recentWrites, f.Files.Writes.Count(p => p == "recents"));
        f.Delay.ReleaseAll(); await WaitFor(f.Recovery.DrainAsync());
        Assert.Null(await f.Recovery.ReadAsync());
    }

    [Theory]
    [InlineData("cancel")] [InlineData("save-cancel")] [InlineData("save-error")]
    public async Task CancelledOrFailedLeavePreservesTheEditorSessionDraftAndRecovery(string scenario)
    {
        var f = new Fixture(); await f.Create();
        var editor = f.Main.Editor!; var session = f.Main.Session!; var revision = session.CurrentRevision;
        await f.Recovery.FlushAsync(revision.State, session.FilePath);
        byte[] recovery = f.Files.Data["recovery"].ToArray();
        editor.DimensionLength.Begin(); editor.DimensionLength.Text = "bad draft";
        f.Dialogs.Leave = scenario == "cancel" ? LeaveDecision.Cancel : LeaveDecision.Save;
        if (scenario == "save-cancel") f.Dialogs.SavePath = null;
        if (scenario == "save-error") f.Files.FailWritePath = f.Dialogs.SavePath;

        Assert.False(await f.Main.CloseProjectAsync());
        Assert.Equal(MainViewMode.Editor, f.Main.Mode); Assert.Same(editor, f.Main.CurrentViewModel);
        Assert.Same(session, f.Main.Session); Assert.Same(revision, session.CurrentRevision);
        Assert.True(session.IsDirty); Assert.False(session.IsBusy);
        Assert.True(editor.DimensionLength.IsEditing); Assert.Equal("bad draft", editor.DimensionLength.Text);
        Assert.Equal(recovery, f.Files.Data["recovery"]); Assert.Empty(f.Recents.Entries);
        Assert.Equal(scenario == "save-error" ? 1 : 0, f.Dialogs.Errors.Count);
    }

    [Fact]
    public async Task PendingLeaveDisablesCloseAndRejectsReentrantCalls()
    {
        var f = new Fixture(); await f.Create();
        var entered = new TaskCompletionSource(); var release = new TaskCompletionSource<LeaveDecision>();
        f.Dialogs.LeaveHook = () => { entered.SetResult(); return WaitFor(release.Task); };
        var close = f.Main.CloseProjectAsync();
        try
        {
            await WaitForCheckpoint(entered.Task, close);
            Assert.True(f.Main.IsWorkspaceBusy); Assert.False(f.Main.CanCloseProject);
            Assert.False(f.Main.CloseProjectCommand.CanExecute(null)); Assert.False(await f.Main.CloseProjectAsync());
        }
        finally { release.TrySetResult(LeaveDecision.Cancel); await WaitFor(close); }
        Assert.False(await close); Assert.Equal(1, f.Dialogs.LeaveQuestions);
        Assert.True(f.Main.CanCloseProject); Assert.True(f.Main.CloseProjectCommand.CanExecute(null));
    }
}
