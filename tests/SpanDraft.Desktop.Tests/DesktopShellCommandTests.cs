using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SpanDraft.Desktop;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

[Collection("Avalonia")]
public sealed class DesktopShellCommandTests
{
    [Theory]
    [InlineData("none")] [InlineData("empty")] [InlineData("other")] [InlineData("multiple")]
    [InlineData("valid")] [InlineData("uppercase")]
    public void StartupOpensOnlyOneProjectArgumentAfterLibrariesAndRecents(string arguments)
    {
        using var environment = new DesktopControlEnvironment();
        var files = new Files();
        string path = TestPath(Path.Combine("Folder with spaces", arguments == "uppercase" ? "Shell project.SPANDRAFT" : "Shell project.spandraft"));
        files.Data[path] = ProjectFileCodec.Serialize(State(4));
        string[]? args = arguments switch
        {
            "none" => null, "empty" => [], "other" => [TestPath("other.txt")],
            "multiple" => [path, path], _ => [path]
        };
        var reads = new List<string>();
        files.BeforeRead = p => { reads.Add(p); return Task.CompletedTask; };
        var startup = new DesktopStartupContext(files, new LocalSettingsStore(files, "settings"), UserSettings.Default);
        var host = HostWithLaunchArguments(startup, args);
        var initial = host.Workspaces.CreateInitial();
        var window = initial.Window;
        DesktopControlEnvironment.ShowInitialized(window);
        var main = Assert.IsType<MainWindowViewModel>(window.DataContext);
        var expectedReads = new List<string>
        {
            // Shared services (libraries, recents) load during bootstrap, before any window. Recovery has its own storage.
            MaterialLibraryStore.Local(files).FilePath, SectionLibraryStore.Local(files).FilePath,
            RecentProjectsStore.Local(files).FilePath
        };
        if (arguments is "valid" or "uppercase")
        {
            expectedReads.Add(path);
            Assert.Equal(path, main.Session!.FilePath);
            Assert.True(State(4).ContentEquals(main.Session.CurrentRevision.State));
            Assert.Equal(Path.GetFileName(path) + " — SpanDraft", window.Title);
            Assert.Equal(path, Assert.Single(main.StartPage.RecentProjects).FilePath);
        }
        else { Assert.Null(main.Session); Assert.Equal(MainViewMode.Start, main.Mode); }
        Assert.Equal(expectedReads, reads);
        Assert.Same(initial, Assert.Single(host.Workspaces.Handles));
        Assert.Equal(main.HasProject ? 0 : 1, host.Workspaces.StartSlotCount);
        Assert.Empty(window.OwnedWindows);
        window.Close(); Dispatcher.UIThread.RunJobs();
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void PendingRecoveryNeverChangesTheStartupFileOpenAndStaysUntouched(bool withProjectRecovery)
    {
        using var environment = new DesktopControlEnvironment();
        var files = new Files();
        string target = TestPath("shell.spandraft");
        var storage = new TestRecoveryStorage();
        var setupBytes = ProjectSetupRecovery.Encode(new(new ProjectSetupDraft(Length: M(3)), Now));
        var projectBytes = ProjectRecovery.Encode(new(State(), null, Now));
        var setupSlot = RecoverySlot.Setup(RecoveryId.New());
        storage.Put(setupSlot, setupBytes);
        var projectSlot = RecoverySlot.Project(RecoveryId.New());
        if (withProjectRecovery) storage.Put(projectSlot, projectBytes);
        files.Data[target] = ProjectFileCodec.Serialize(State(4));
        var startup = new DesktopStartupContext(files, new LocalSettingsStore(files, "settings"), UserSettings.Default)
        {
            StateStores = DesktopStateStores.Local(files) with { Recovery = storage }
        };
        var host = HostWithLaunchArguments(startup, [target]);
        var window = host.Workspaces.CreateInitial().Window;
        DesktopControlEnvironment.ShowInitialized(window);
        Dispatcher.UIThread.RunJobs();

        // Recovery never asks and never restores by itself: the launch file opens as if nothing were pending.
        var main = Assert.IsType<MainWindowViewModel>(window.DataContext);
        Assert.Empty(window.OwnedWindows);
        Assert.Equal(target, main.Session!.FilePath);
        Assert.True(State(4).ContentEquals(main.Session.CurrentRevision.State));
        Assert.Equal(withProjectRecovery ? 2 : 1, host.Recovery.Candidates.Count);
        Assert.True(host.Recovery.HasUndecided);
        Assert.Equal(setupBytes, storage.Data[setupSlot]);
        if (withProjectRecovery) Assert.Equal(projectBytes, storage.Data[projectSlot]);
        Assert.Empty(storage.Writes);
        window.Close(); Dispatcher.UIThread.RunJobs();
    }

    [Theory]
    [InlineData("escape")] [InlineData("close")] [InlineData("quit")]
    public void UnreadableRecoveryAreaClosesStartupWithoutNormalCloseAndOffersTheSameFileAgain(string action)
    {
        using var environment = new DesktopControlEnvironment();
        var files = new Files();
        string recentPath = RecentProjectsStore.Local(files).FilePath;
        string target = TestPath("startup.spandraft");
        files.Data[target] = ProjectFileCodec.Serialize(State(4));
        var storage = new TestRecoveryStorage { FailEnumerate = new IOException("Injected enumeration failure.") };
        int recentReads = 0, targetReads = 0;
        files.BeforeRead = path =>
        {
            if (path == recentPath) recentReads++;
            if (path == target) targetReads++;
            return Task.CompletedTask;
        };
        var startup = new DesktopStartupContext(files, new LocalSettingsStore(files, TestPath("settings.json")), UserSettings.Default)
        {
            StateStores = DesktopStateStores.Local(files) with { Recovery = storage }
        };
        for (int attempt = 0; attempt < 2; attempt++)
        {
            var window = (HostWithLaunchArguments(startup, [target])).Workspaces.CreateInitial().Window;
            int bootstrapRecentReads = recentReads;
            bool closed = false;
            window.Closed += (_, _) => closed = true;
            window.Show(); Dispatcher.UIThread.RunJobs();
            var dialog = Assert.Single(window.OwnedWindows);
            // Nothing of the workspace is usable before the recovery situation is known.
            Assert.Null(window.DataContext);
            if (action == "escape")
                dialog.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            else if (action == "quit") Click(dialog, Strings.Quit);
            else dialog.Close();
            Dispatcher.UIThread.RunJobs();

            Assert.True(closed); Assert.False(window.IsVisible);
            Assert.Empty(files.Writes);
            // The shared recents belong to the bootstrap; quitting never reads them again nor the startup file.
            Assert.Equal(bootstrapRecentReads, recentReads); Assert.Equal(0, targetReads);
        }
    }

    private static void Click(Window dialog, string label) => dialog.GetVisualDescendants().OfType<Button>()
        .Single(b => Equals(b.Content, label)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [Fact]
    public async Task FileCommandsFollowWorkspaceAndSessionForCommandsAndDirectCalls()
    {
        var files = new Files(); var dialogs = new Dialogs { OpenPath = TestPath("initial.spandraft") };
        files.Data[dialogs.OpenPath] = SpanDraft.Desktop.Persistence.ProjectFileCodec.Serialize(State());
        var main = new MainWindowViewModel(files: files, projectIdentities: Identities, dialogs: dialogs);
        Check(true, true, false, false);
        Assert.True(await main.NewAsync()); Check(false, true, false, false);
        Assert.False(await main.NewAsync()); Assert.False(await main.SaveAsync());
        Assert.True(await main.OpenAsync()); Check(true, true, true, false);
        main.EditProject(); Check(false, false, false, false);
        Assert.False(await main.OpenAsync()); Assert.False(await main.NewAsync()); Assert.False(await main.SaveAsync());
        main.Setup.ShowMaterials(); Assert.Equal(MainWorkspaceKind.MaterialSelection, main.Workspace); Check(false, false, false, false);
        var selection = (MaterialSelectionViewModel)main.Setup.CurrentStep!;
        int workspaceNotifications = 0; main.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(main.Workspace)) workspaceNotifications++; };
        selection.EditSelectedCommand.Execute(null); Assert.Equal(MainWorkspaceKind.MaterialSelection, main.Workspace);
        Assert.True(workspaceNotifications > 0); Check(false, false, false, false);
        Assert.False(await main.OpenAsync()); Assert.False(await main.SaveAsync());
        main.Setup.Escape(); main.Setup.ShowOverview(); main.Setup.ShowSections();
        Assert.Equal(MainWorkspaceKind.SectionDefinition, main.Workspace); Check(false, false, false, false);
        main.Setup.Definition!.SelectForm(SectionEditorType.Circle); Check(false, false, false, true);
        Assert.True(main.Undo()); Assert.True(main.CanRedo);
        main.Setup.Escape(); main.Setup.CancelCommand.Execute(null); Check(true, true, true, false);
        Assert.True(await main.NewAsync()); Check(false, false, false, false);
        Assert.False(await main.OpenAsync()); Assert.False(await main.SaveAsync(saveAs: true));
        void Check(bool create, bool open, bool save, bool undo)
        {
            Assert.Equal(create, main.NewCommand.CanExecute(null)); Assert.Equal(open, main.OpenCommand.CanExecute(null));
            Assert.Equal(save, main.SaveCommand.CanExecute(null)); Assert.Equal(save, main.SaveAsCommand.CanExecute(null));
            Assert.Equal(undo, main.UndoCommand.CanExecute(null));
        }
    }
    [Fact]
    public async Task BusyDisablesFileAndHistoryCommandsAndRestoresThemAfterwards()
    {
        var files = new Files(); var dialogs = new Dialogs();
        var main = new MainWindowViewModel(files: files, projectIdentities: Identities, dialogs: dialogs); main.NewCommand.Execute(null); ProjectTestSupport.CompleteSetup(main); Assert.True(await main.Setup.ConfirmAsync());
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        files.BeforeWrite = (_, _, _) => pending.Task;
        var save = main.SaveAsync(); Assert.True(main.IsWorkspaceBusy);
        Assert.False(main.NewCommand.CanExecute(null)); Assert.False(main.OpenCommand.CanExecute(null));
        Assert.False(main.SaveCommand.CanExecute(null)); Assert.False(main.UndoCommand.CanExecute(null));
        pending.SetResult(); Assert.True(await save); Assert.True(main.SaveCommand.CanExecute(null));
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void KeyboardHistoryRoutesToTheVisibleWorkspaceAndTextBoxesKeepNativeUndo(bool section)
    {
        using var environment = new DesktopControlEnvironment();
        var main = new MainWindowViewModel(); main.NewCommand.Execute(null); ProjectTestSupport.CompleteSetup(main); main.Setup.ApplyCommand.Execute(null);
        var session = main.Session!;
        if (section)
        {
            main.EditProject(); main.Setup.ShowSections(); main.Setup.Definition!.SelectForm(SectionEditorType.Circle);
        }
        else
        {
            var input = main.Editor!.DimensionLength; input.Begin(); input.Text = "2000"; Assert.True(input.Confirm());
        }
        var shell = new MainWindow();
        typeof(MainWindow).GetField("_model", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(shell, main);
        typeof(MainWindow).GetField("_initializing", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(shell, false);
        var text = new TextBox { Text = "", Width = 200, Height = 32, IsUndoEnabled = true };
        var host = new Window { Content = text, Width = 500, Height = 200 }; host.Show();
        host.Measure(new Size(500, 200)); host.Arrange(new Rect(0, 0, 500, 200));
        var primary = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        Assert.True(text.Focus());
        text.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = "unconfirmed" });
        Assert.Equal("unconfirmed", text.Text); Assert.True(text.CanUndo);
        var revision = session.CurrentRevision;
        var key = Send(Key.Z, primary); Assert.False(key.Handled); Assert.True(main.CanUndo);
        text.RaiseEvent(key); Assert.Equal("", text.Text); Assert.Same(revision, session.CurrentRevision);
        Assert.True(main.CanUndo);
        var redo = Send(Key.Z, primary | KeyModifiers.Shift); Assert.False(redo.Handled);
        text.RaiseEvent(redo); Assert.Equal("unconfirmed", text.Text);
        if (OperatingSystem.IsWindows()) Assert.False(Send(Key.Y, KeyModifiers.Control).Handled);
        text.IsReadOnly = true;
        Assert.True(Send(Key.Z, primary).Handled); Assert.True(main.CanRedo);
        Assert.True(Send(Key.Z, primary | KeyModifiers.Shift).Handled); Assert.True(main.CanUndo);
        if (OperatingSystem.IsWindows()) { Send(Key.Z, primary); Assert.True(Send(Key.Y, KeyModifiers.Control).Handled); }
        host.Close(); shell.Close();
        KeyEventArgs Send(Key key, KeyModifiers modifiers)
        {
            var e = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers };
            typeof(MainWindow).GetMethod("ProjectKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(shell, [shell, e]);
            return e;
        }
    }
    [Fact]
    public void MainMenuProtectsSectionNumericAndNameDraftsOnFocusLoss()
    {
        using var environment = new DesktopControlEnvironment();
        var main = new MainWindowViewModel(); main.NewCommand.Execute(null); ProjectTestSupport.CompleteSetup(main); main.Setup.ShowSections(); var vm = main.Setup.Definition!;
        var view = new SpanDraft.Desktop.Views.SectionDefinitionView { DataContext = vm };
        var host = new Window { Content = view, Width = 1100, Height = 650 }; host.Show();
        host.Measure(new Size(1100, 650)); host.Arrange(new Rect(0, 0, 1100, 650));
        var field = vm.Parameters.First(); var original = field.ConfirmedValue; field.PendingText = "150";
        main.SetMainMenuOpen(true);
        var input = new TextBox { DataContext = field };
        typeof(SpanDraft.Desktop.Views.SectionDefinitionView).GetMethod("InputLostFocus", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(view, [input, new RoutedEventArgs()]);
        Assert.Equal(original, field.ConfirmedValue); Assert.True(field.IsPending);
        vm.BeginNameEdit(); vm.PendingName = "Pending name";
        typeof(SpanDraft.Desktop.Views.SectionDefinitionView).GetMethod("NameLostFocus", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(view, [view, new RoutedEventArgs()]);
        Dispatcher.UIThread.RunJobs(); Assert.True(vm.IsEditingName); Assert.NotEqual("Pending name", vm.DisplayName);
        main.SetMainMenuOpen(false); host.Close();
    }
    [Theory]
    [InlineData("de-DE", "Benutzerhandbuch", "de")]
    [InlineData("de-CH", "Benutzerhandbuch", "de")]
    [InlineData("en-US", "User Manual", "en")]
    [InlineData("en-GB", "User Manual", "en")]
    [InlineData("fr-FR", "User Manual", "en")]
    public async Task ManualLauncherUsesLocalFileAndReportsFailuresWithoutLaunchingWhenMissing(string culture, string title, string language)
    {
        using var scope = new UiCultureScope(culture); Assert.Equal(title, Strings.UserManual);
        string directory = Path.Combine(Path.GetTempPath(), "SpanDraft-manual-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(directory, "Help", language));
        // Number formatting must not select the manual language.
        var previousCulture = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo(language == "de" ? "en-US" : "de-DE");
        try
        {
            int calls = 0; Uri? opened = null;
            var launcher = new UserManualLauncher(uri => { calls++; opened = uri; return Task.FromResult(true); }, directory);
            Assert.False(await launcher.OpenAsync()); Assert.Equal(0, calls);
            string path = Path.Combine(directory, "Help", language, "index.html"); await File.WriteAllTextAsync(path, "<html></html>", TestContext.Current.CancellationToken);
            Assert.True(await launcher.OpenAsync()); Assert.True(opened!.IsFile); Assert.Equal(path, opened.LocalPath);
            Assert.False(await new UserManualLauncher(_ => Task.FromResult(false), directory).OpenAsync());
            Assert.False(await new UserManualLauncher(_ => throw new IOException("failure"), directory).OpenAsync());
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = previousCulture; Directory.Delete(directory, true); }
    }
}
