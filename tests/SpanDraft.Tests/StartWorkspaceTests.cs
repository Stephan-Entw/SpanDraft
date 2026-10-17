using System.Globalization;
using System.Resources;
using System.Text;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.ViewModels;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

public sealed class StartWorkspaceTests
{
    private sealed class Fixture
    {
        public const string Slot = "recent-projects";
        public Files Files { get; } = new();
        public Dialogs Dialogs { get; } = new();
        public DateTimeOffset Time { get; set; } = Now;
        public RecentProjectsStore Store { get; }
        public MainWindowViewModel Main { get; }
        public Fixture()
        {
            Store = new(Files, Slot, () => Time);
            Main = new(files: Files, dialogs: Dialogs, recentStore: Store);
        }
        public async Task Create()
        { Assert.True(await Main.NewAsync()); Assert.True(await Main.Setup.ConfirmAsync()); }
        public string Project(string name = "beam")
        {
            string path = TestPath(name + ".spandraft");
            Files.Data[path] = ProjectFileCodec.Serialize(State());
            return path;
        }
    }

    [Fact]
    public void NewModelShowsStartWithOnlyApplicableCommandsAndNoSession()
    {
        var main = new MainWindowViewModel();
        Assert.Equal(MainViewMode.Start, main.Mode); Assert.Equal(MainWorkspaceKind.Start, main.Workspace);
        Assert.Same(main.StartPage, main.CurrentViewModel); Assert.Equal("SpanDraft", main.WindowTitle);
        Assert.Null(main.Editor); Assert.Null(main.Session); Assert.True(main.StartPage.IsEmpty);
        Assert.True(main.NewCommand.CanExecute(null)); Assert.True(main.OpenCommand.CanExecute(null));
        Assert.True(main.UserManualCommand.CanExecute(null));
        Assert.False(main.SaveCommand.CanExecute(null)); Assert.False(main.SaveAsCommand.CanExecute(null));
        Assert.False(main.UndoCommand.CanExecute(null)); Assert.False(main.RedoCommand.CanExecute(null));
        Assert.Same(main.NewCommand, main.StartPage.NewCommand); Assert.Same(main.OpenCommand, main.StartPage.OpenCommand);
        Assert.Same(main.UserManualCommand, main.StartPage.UserManualCommand);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task CreateSetupFromStartReturnsToStartByCancelOrEscape(bool escape)
    {
        var main = new MainWindowViewModel(); var hiddenDraft = main.Setup;
        Assert.True(await main.NewAsync()); Assert.NotSame(hiddenDraft, main.Setup);
        Assert.Equal(MainViewMode.ProjectSetup, main.Mode); Assert.Equal(ProjectSetupMode.Create, main.Setup.Mode);
        Assert.True(main.Setup.CanCancel); Assert.True(main.Setup.CancelCommand.CanExecute(null));
        if (escape) main.Setup.Escape(); else main.Setup.CancelCommand.Execute(null);
        Assert.Equal(MainWorkspaceKind.Start, main.Workspace); Assert.Same(main.StartPage, main.CurrentViewModel);
        Assert.Null(main.Session); Assert.Equal("SpanDraft", main.WindowTitle);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task CreateAndEditSetupReturnToTheSameEditorAndSession(bool edit)
    {
        var f = new Fixture(); await f.Create();
        var editor = f.Main.Editor; var session = f.Main.Session; var revision = session!.CurrentRevision;
        if (edit) f.Main.EditProject(); else Assert.True(await f.Main.NewAsync());
        Assert.True(f.Main.Setup.CanCancel); Assert.Equal(0, f.Dialogs.LeaveQuestions);
        f.Main.Setup.Escape();
        Assert.Equal(MainViewMode.Editor, f.Main.Mode); Assert.Same(editor, f.Main.CurrentViewModel);
        Assert.Same(session, f.Main.Session); Assert.Same(revision, session.CurrentRevision);
        Assert.Empty(f.Store.Entries);
    }

    [Fact]
    public async Task PickerAndRecentCommandShareOpeningAndUpdateOnlyAfterActivation()
    {
        var f = new Fixture(); string path = f.Project();
        f.Dialogs.OpenPath = path;
        f.Files.BeforeWrite = (target, _, _) =>
        {
            Assert.Equal(Fixture.Slot, target); Assert.Equal(MainViewMode.Editor, f.Main.Mode);
            Assert.Equal(path, f.Main.Session!.FilePath); Assert.False(f.Main.Session.IsDirty);
            return Task.CompletedTask;
        };
        Assert.True(await f.Main.OpenAsync()); Assert.Equal(path, Assert.Single(f.Store.Entries).Path);
        var fresh = new MainWindowViewModel(files: f.Files, dialogs: f.Dialogs, recentStore: f.Store);
        await fresh.InitializeRecentProjectsAsync();
        f.Files.BeforeWrite = null; f.Dialogs.OpenPath = TestPath("picker-must-not-be-used.spandraft");
        Assert.Single(fresh.StartPage.RecentProjects).OpenCommand.Execute(null);
        Assert.Equal(MainViewMode.Editor, fresh.Mode); Assert.Equal(path, fresh.Session!.FilePath);
        Assert.Single(f.Store.Entries);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task MissingOrInvalidRecentKeepsStartEntryAndReportsExistingProjectError(bool invalid)
    {
        var f = new Fixture(); string path = TestPath("unavailable.spandraft");
        await f.Store.RecordUseAsync(path); await f.Main.InitializeRecentProjectsAsync();
        if (invalid) f.Files.Data[path] = Encoding.UTF8.GetBytes("{}");
        var entries = f.Store.Entries; int writes = f.Files.Writes.Count;
        Assert.False(await f.Main.OpenRecentAsync(path));
        Assert.Equal(MainViewMode.Start, f.Main.Mode); Assert.Null(f.Main.Session);
        Assert.Same(entries, f.Store.Entries); Assert.Equal(writes, f.Files.Writes.Count);
        Assert.StartsWith(Strings.ProjectFileError, Assert.Single(f.Dialogs.Errors));
        Assert.Equal(0, f.Dialogs.LeaveQuestions); Assert.Single(f.Main.StartPage.RecentProjects);
    }

    [Fact]
    public async Task CancelledPickerDoesNotCreateRecentState()
    {
        var f = new Fixture(); Assert.False(await f.Main.OpenAsync());
        Assert.Empty(f.Store.Entries); Assert.Empty(f.Files.Writes); Assert.Empty(f.Dialogs.Errors);
    }

    [Theory]
    [InlineData(LeaveDecision.Cancel)] [InlineData(LeaveDecision.Discard)] [InlineData(LeaveDecision.Save)]
    public async Task RecentOpenUsesTheExistingLeaveProtection(LeaveDecision decision)
    {
        var f = new Fixture(); await f.Create(); var session = f.Main.Session;
        string path = f.Project(); f.Dialogs.Leave = decision;
        Assert.Equal(decision != LeaveDecision.Cancel, await f.Main.OpenRecentAsync(path));
        Assert.Equal(1, f.Dialogs.LeaveQuestions);
        if (decision == LeaveDecision.Cancel) { Assert.Same(session, f.Main.Session); Assert.Empty(f.Store.Entries); }
        else { Assert.NotSame(session, f.Main.Session); Assert.Equal(path, f.Store.Entries[0].Path); }
    }

    [Fact]
    public async Task FirstSaveSaveAndSaveAsUpdateOnlyAfterTheSavepointAndReplaceThePriorPath()
    {
        var f = new Fixture(); await f.Create(); Assert.Empty(f.Store.Entries);
        f.Files.BeforeWrite = (target, _, _) =>
        {
            if (target == Fixture.Slot)
            {
                Assert.False(f.Main.Session!.IsDirty);
                Assert.Equal(f.Main.Session.CurrentRevision.RevisionId, f.Main.Session.SavedRevisionId);
                Assert.True(f.Files.Data.ContainsKey(f.Main.Session.FilePath!));
            }
            return Task.CompletedTask;
        };
        Assert.True(await f.Main.SaveAsync()); string original = f.Main.Session!.FilePath!;
        Assert.Equal(original, Assert.Single(f.Store.Entries).Path);
        f.Time = Now.AddMinutes(1); Assert.True(await f.Main.SaveAsync());
        Assert.Equal(f.Time, Assert.Single(f.Store.Entries).LastUsedUtc);
        f.Dialogs.SavePath = TestPath("replacement.spandraft"); f.Time = f.Time.AddMinutes(1);
        Assert.True(await f.Main.SaveAsync(saveAs: true));
        Assert.Equal(f.Dialogs.SavePath, f.Main.Session.FilePath);
        Assert.Equal(f.Dialogs.SavePath, Assert.Single(f.Store.Entries).Path);
        Assert.True(f.Files.Data.ContainsKey(original));
    }

    [Fact]
    public async Task FailedOrCancelledSaveDoesNotUpdateRecents()
    {
        var f = new Fixture(); await f.Create();
        f.Dialogs.SavePath = null; Assert.False(await f.Main.SaveAsync()); Assert.Empty(f.Store.Entries);
        f.Dialogs.SavePath = TestPath("failed.spandraft"); f.Files.FailWritePath = f.Dialogs.SavePath;
        Assert.False(await f.Main.SaveAsync()); Assert.Empty(f.Store.Entries); Assert.True(f.Main.Session!.IsDirty);
        Assert.Single(f.Dialogs.Errors);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task RecentWriteFailureCannotFailSuccessfulOpenOrSave(bool save)
    {
        var f = new Fixture(); f.Files.FailWritePath = Fixture.Slot;
        if (save) { await f.Create(); Assert.True(await f.Main.SaveAsync()); }
        else Assert.True(await f.Main.OpenRecentAsync(f.Project()));
        Assert.False(f.Main.Session!.IsDirty); Assert.Empty(f.Dialogs.Errors);
        Assert.Equal(f.Main.Session.FilePath, Assert.Single(f.Store.Entries).Path);
        Assert.Equal(f.Main.Session.FilePath, Assert.Single(f.Main.StartPage.RecentProjects).FilePath);
    }

    [Fact]
    public async Task RecentLoadDoesNotMakeTheVisibleWorkspaceBusy()
    {
        var f = new Fixture(); var entered = new TaskCompletionSource(); var release = new TaskCompletionSource();
        f.Files.BeforeRead = async _ => { entered.SetResult(); await WaitFor(release.Task); };
        var load = f.Main.InitializeRecentProjectsAsync(); await WaitForCheckpoint(entered.Task, load);
        Assert.Equal(MainViewMode.Start, f.Main.Mode); Assert.False(f.Main.IsWorkspaceBusy);
        Assert.True(f.Main.NewCommand.CanExecute(null)); Assert.True(await f.Main.NewAsync());
        release.SetResult(); await WaitFor(load); Assert.Equal(MainViewMode.ProjectSetup, f.Main.Mode);
    }

    [Fact]
    public void ManualCommandInvokesTheInjectedWindowAction()
    {
        int calls = 0;
        var main = new MainWindowViewModel(openUserManual: () => { calls++; return Task.CompletedTask; });
        main.StartPage.UserManualCommand.Execute(null); main.UserManualCommand.Execute(null);
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData("en-US")] [InlineData("de-DE")]
    public void NewResourcesExistInBothLanguagesAndTimeLabelsUseLocalCalendarDays(string cultureName)
    {
        using var scope = new UiCultureScope(cultureName);
        var resources = new ResourceManager("SpanDraft.Desktop.Resources.Strings", typeof(Strings).Assembly);
        var set = resources.GetResourceSet(cultureName == "en-US" ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo("de"), true, false)!;
        foreach (string key in new[] { "Start", "Project", "OpenProject", "RecentProjects", "LastOpened", "NoRecentProjects", "Today", "Yesterday" })
        {
            Assert.False(string.IsNullOrWhiteSpace(set.GetString(key)));
            Assert.Equal(set.GetString(key), typeof(Strings).GetProperty(key)!.GetValue(null));
        }
        var local = new DateTimeOffset(new DateTime(2026, 10, 16, 15, 42, 0, DateTimeKind.Local));
        Assert.Equal(Strings.Today + ", " + local.ToString("t"), RecentProjectItemViewModel.FormatLastUsed(local.ToUniversalTime(), local));
        Assert.Equal(Strings.Yesterday, RecentProjectItemViewModel.FormatLastUsed(local.AddDays(-1).ToUniversalTime(), local));
        Assert.Equal(local.AddDays(-2).ToString("d"), RecentProjectItemViewModel.FormatLastUsed(local.AddDays(-2).ToUniversalTime(), local));
        var item = new RecentProjectItemViewModel(new(TestPath("Inspection beam.spandraft"), Now), _ => Task.FromResult(true), () => true);
        Assert.Equal("Inspection beam", item.Name); Assert.Contains(item.FilePath, item.AutomationName);
    }
}
