using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using SpanDraft.Desktop;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Services;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.ViewModels;
using SpanDraft.Desktop.Workspaces;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

/// <summary>
/// The enabled state of the controls actually bound in managed workspace windows. Commands are never queried or refreshed
/// directly here: a control only knows what it was notified about, so a stale state is visible exactly as the user sees it.
/// </summary>
public sealed partial class WorkspaceWindowManagerTests
{
    private static readonly KeyModifiers Primary = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;

    [Fact]
    public void AfterStartupTheStartPageMenuAndShortcutsAreUsable()
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            var files = new Files();
            var host = Host(files);
            string recent = Project(files, "startup-recent.spandraft");
            Run(async () => { await host.Recents.RecordUseAsync(recent); return true; });
            var start = Start(host);
            AssertStartUsable(start.Window);
            AssertMenu(start.Window, "QuitMenuItem", true);

            // Activated through the UI: the start page button opens the setup of a new project.
            ClickButton(StartButton(start.Window, "NewProjectButton"));
            Assert.Equal(MainViewMode.ProjectSetup, start.Model!.Mode);
            start.Model.Setup.CancelCommand.Execute(null); Flush();
            Assert.Equal(MainViewMode.Start, start.Model.Mode);
            AssertStartUsable(start.Window);

            // The recent project entry opens the project in its workspace.
            ClickButton(RecentButtons(start.Window).Single());
            Assert.Equal(recent, start.Model.Session?.FilePath);
        });
    }

    [Fact]
    public void AnOpenedProjectOffersItsFileReportAndHistoryActions()
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            var (_, _, _, a, b, _, _) = OpenTwoProjects();
            foreach (var handle in new[] { a, b })
            {
                var window = handle.Window;
                Layout(window);
                AssertEnabled(window, "NewMenuItem", "OpenMenuItem", "SaveMenuItem", "SaveAsMenuItem", "ReportMenuItem",
                    "CloseProjectMenuItem", "QuitMenuItem");
                AssertMenu(window, "UndoMenuItem", false);
                AssertButton(Toolbar(window, "ReportToolbarButton"), true);
            }
            EditPointForce(a.Model!.Editor!, -2500); Flush();
            AssertMenu(a.Window, "UndoMenuItem", true);
            AssertMenu(b.Window, "UndoMenuItem", false);
            // Undo through its shortcut, Redo becomes available in the bound menu.
            PressKey(a.Window, Key.Z, Primary);
            Assert.False(a.Model.Session!.IsDirty);
            AssertMenu(a.Window, "RedoMenuItem", true);
        });
    }

    [Fact]
    public void CloseProjectReturnsToAUsableStartPage()
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            var files = new Files();
            var host = Host(files);
            var start = Start(host);
            string path = Project(files, "close-project.spandraft");
            SetPicker(start, path);
            ClickButton(StartButton(start.Window, "OpenProjectButton"));
            Assert.Equal(path, start.Model!.Session?.FilePath);

            ClickMenu(start.Window, "CloseProjectMenuItem");
            Assert.Equal(MainViewMode.Start, start.Model.Mode);
            Assert.Same(start, Assert.Single(host.Workspaces.Handles));
            AssertStartUsable(start.Window);
            ClickButton(RecentButtons(start.Window).Single());
            Assert.Equal(path, start.Model.Session?.FilePath);
        });
    }

    [Fact]
    public void ACancelledCloseRestoresTheWorkspacesControls()
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            var (host, a, _) = TwoWorkspaces(new Files());
            Layout(a.Window);
            AssertEnabled(a.Window, "NewMenuItem", "OpenMenuItem", "SaveMenuItem", "CloseProjectMenuItem", "QuitMenuItem");

            ClickMenu(a.Window, "CloseProjectMenuItem");
            Click(Assert.Single(a.Window.OwnedWindows), Strings.Cancel); Flush();
            AssertEnabled(a.Window, "NewMenuItem", "OpenMenuItem", "SaveMenuItem", "CloseProjectMenuItem", "QuitMenuItem");

            a.Window.Close(); Flush();
            Click(Assert.Single(a.Window.OwnedWindows), Strings.Cancel); Flush();
            AssertEnabled(a.Window, "NewMenuItem", "OpenMenuItem", "SaveMenuItem", "CloseProjectMenuItem", "QuitMenuItem");
            ClickMenu(a.Window, "NewMenuItem");
            Assert.Equal(3, host.Workspaces.Handles.Count);
        });
    }

    [Fact]
    public void ACancelledQuitLeavesTheRemainingWindowsUsable()
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            // Opened without a picker stub: the leave questions are the real dialogs.
            var files = new Files();
            var host = Host(files);
            string pathA = Project(files, "quit-a.spandraft");
            Run(async () => { await host.Recents.RecordUseAsync(pathA); return true; });
            var a = Start(host);
            ClickButton(RecentButtons(a.Window).Single());
            Assert.Equal(ProjectOpenStatus.Opened, Run(() => BeginOpen(host, a, Project(files, "quit-b.spandraft"))).Status);
            var b = Assert.Single(host.Workspaces.Handles, handle => handle != a);
            EditPointForce(a.Model!.Editor!, -2000);
            EditPointForce(b.Model!.Editor!, -3000);
            Assert.True(host.Workspaces.Activate(a.WindowId));

            ClickMenu(a.Window, "QuitMenuItem");
            Click(Assert.Single(a.Window.OwnedWindows, w => w.IsVisible), Strings.DontSave); Flush(); Flush();
            Click(Assert.Single(b.Window.OwnedWindows, w => w.IsVisible), Strings.Cancel); Flush();
            Assert.Same(b, Assert.Single(host.Workspaces.Handles));
            Assert.Equal(DesktopApplicationState.Ready, host.State);
            AssertEnabled(b.Window, "NewMenuItem", "OpenMenuItem", "SaveMenuItem", "ReportMenuItem", "CloseProjectMenuItem",
                "UndoMenuItem", "QuitMenuItem");
            AssertButton(Toolbar(b.Window, "ReportToolbarButton"), true);
            PressKey(b.Window, Key.S, Primary);
            Assert.False(b.Model.Session!.IsDirty);
        });
    }

    [Fact]
    public void ASettingsCommitLocksEveryWorkspaceAndReleasesItAfterwards()
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            var (host, files, start, editor) = StartAndProject("settings");
            // An open Settings dialog of one workspace leaves the others usable (§82).
            var settingsItem = editor.Window.GetLogicalDescendants().OfType<MenuItem>().Single(i => Equals(i.Header, Strings.Settings));
            settingsItem.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.MenuItem.ClickEvent)); Flush();
            var dialog = Assert.Single(editor.Window.OwnedWindows, w => w.IsVisible);
            AssertStartUsable(start.Window);
            ClickButton(StartButton(start.Window, "NewProjectButton"));
            start.Model!.Setup.CancelCommand.Execute(null); Flush(); Flush();
            AssertStartUsable(start.Window);
            dialog.Close(); Flush(); Flush();
            Assert.Empty(editor.Window.OwnedWindows.Where(w => w.IsVisible));

            var write = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            files.BeforeWrite = (path, _, _) => path == "settings" ? write.Task : Task.CompletedTask;
            var patch = SettingsPatch.From(host.Settings.CurrentSettings) with { SignificantDigits = 5 };
            var applying = host.SettingsCoordinator.ApplyAsync(patch); Flush();
            Assert.False(applying.IsCompleted);
            Layout(start.Window);
            AssertButton(StartButton(start.Window, "NewProjectButton"), false);
            AssertButton(StartButton(start.Window, "OpenProjectButton"), false);
            AssertButton(Toolbar(editor.Window, "ReportToolbarButton"), false);
            AssertMenu(editor.Window, "SaveMenuItem", false);

            write.SetResult(); Flush();
            Assert.Equal(SettingsApplyOutcome.Applied, Run(() => applying).Outcome);
            AssertStartUsable(start.Window);
            AssertButton(Toolbar(editor.Window, "ReportToolbarButton"), true);
            AssertEnabled(editor.Window, "NewMenuItem", "OpenMenuItem", "SaveMenuItem", "ReportMenuItem", "CloseProjectMenuItem");
            ClickButton(StartButton(start.Window, "NewProjectButton"));
            Assert.Equal(MainViewMode.ProjectSetup, start.Model!.Mode);
        });
    }

    [Fact]
    public void AnOperationInOneWindowLeavesTheOtherUsableByItsOwnState()
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            var (_, files, start, editor) = StartAndProject("operation");
            EditPointForce(editor.Model!.Editor!, -2500);
            var write = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            string path = editor.Model.Session!.FilePath!;
            files.BeforeWrite = (target, _, _) => target == path ? write.Task : Task.CompletedTask;
            PressKey(editor.Window, Key.S, Primary);
            Assert.True(editor.Model.IsWorkspaceBusy);
            AssertButton(Toolbar(editor.Window, "ReportToolbarButton"), false);
            AssertMenu(editor.Window, "SaveMenuItem", false);
            AssertStartUsable(start.Window);

            write.SetResult(); Flush(); Flush();
            Assert.False(editor.Model.Session.IsDirty);
            AssertButton(Toolbar(editor.Window, "ReportToolbarButton"), true);
            AssertEnabled(editor.Window, "SaveMenuItem", "CloseProjectMenuItem", "QuitMenuItem");
            AssertStartUsable(start.Window);
        });
    }

    [Fact]
    public void TheRecoveryCenterOffersItsProjectAndRecoveryActions()
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            var files = new Files();
            var storage = new TestRecoveryStorage();
            foreach (int variant in new[] { 1, 2 })
                storage.Put(RecoverySlot.Project(RecoveryId.New()), ProjectRecovery.Encode(new(State(variant), null, Now.AddMinutes(-variant))));
            var host = new DesktopApplicationHost(new DesktopStartupContext(files, new LocalSettingsStore(files, "settings"), UserSettings.Default)
                { ProjectIdentities = Identities, StateStores = DesktopStateStores.Local(files) with { Recovery = storage } });
            var start = Start(host);
            Assert.Equal(2, host.Recovery.Undecided.Count);
            AssertStartUsable(start.Window);

            ClickButton(StartButton(start.Window, "RecoveryReviewButton"));
            Assert.Equal(MainViewMode.RecoveryCenter, start.Model!.Mode);
            AssertRecoveryUsable(start.Window, candidates: 2);
            AssertEnabled(start.Window, "OpenMenuItem");

            ClickButton(RecoveryButtons(start.Window, "secondary").First());
            Assert.Equal(1, host.Recovery.Undecided.Count);
            var restored = Assert.Single(host.Workspaces.Handles, handle => handle != start);
            Assert.NotNull(restored.Model!.Session);
            AssertEnabled(restored.Window, "SaveAsMenuItem", "CloseProjectMenuItem", "QuitMenuItem");
            AssertRecoveryUsable(start.Window, candidates: 1);

            ClickButton(StartButton(start.Window, "RecoveryBackButton"));
            Assert.Equal(MainViewMode.Start, start.Model.Mode);
            AssertStartUsable(start.Window);
            // The remaining candidate stays reachable from the start page and the menu (§82).
            AssertButton(StartButton(start.Window, "RecoveryReviewButton"), true);
            AssertMenu(start.Window, "RecoveryMenuItem", true);
            ClickMenu(start.Window, "RecoveryMenuItem");
            Assert.Equal(MainViewMode.RecoveryCenter, start.Model.Mode);
            AssertRecoveryUsable(start.Window, candidates: 1);
        });

        static void AssertRecoveryUsable(MainWindow window, int candidates)
        {
            AssertButton(StartButton(window, "RestoreAllButton"), true);
            AssertButton(StartButton(window, "RecoveryBackButton"), true);
            var restore = RecoveryButtons(window, "secondary");
            Assert.Equal(candidates, restore.Count);
            Assert.All(restore, button => AssertButton(button, true));
            Assert.All(RecoveryButtons(window, "ghost"), button => AssertButton(button, true));
        }

        static IReadOnlyList<Button> RecoveryButtons(MainWindow window, string kind)
        {
            Layout(window);
            var list = window.GetVisualDescendants().OfType<ItemsControl>().Single(c => c.Name == "RecoveryList");
            return list.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains(kind)).ToList();
        }
    }

    [Fact]
    public void ACancelledSetupLeavesAUsableStartPage()
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            var (host, _, start, editor) = StartAndProject("setup");
            // From the editor, New uses the idle start workspace.
            ClickMenu(editor.Window, "NewMenuItem");
            Assert.Equal(MainViewMode.ProjectSetup, start.Model!.Mode);
            // With the start workspace in its setup, New creates a workspace of its own; cancelling its setup closes it.
            ClickMenu(editor.Window, "NewMenuItem");
            var setup = Assert.Single(host.Workspaces.Handles, handle => handle != start && handle != editor);
            Assert.Equal(MainViewMode.ProjectSetup, setup.Model!.Mode);
            setup.Model.Setup.CancelCommand.Execute(null); Flush(); Flush();
            Assert.DoesNotContain(setup, host.Workspaces.Handles);
            AssertEnabled(editor.Window, "NewMenuItem", "OpenMenuItem", "SaveMenuItem", "CloseProjectMenuItem");

            // The start workspace's setup returns to its start page.
            start.Model.Setup.CancelCommand.Execute(null); Flush(); Flush();
            AssertStartUsable(start.Window);

            // The last workspace: its project closes to the start page, its setup cancel keeps it.
            start.Window.Close(); Flush();
            Assert.DoesNotContain(start, host.Workspaces.Handles);
            ClickMenu(editor.Window, "CloseProjectMenuItem");
            Assert.Same(editor, Assert.Single(host.Workspaces.Handles));
            AssertStartUsable(editor.Window);
            ClickButton(StartButton(editor.Window, "NewProjectButton"));
            editor.Model!.Setup.CancelCommand.Execute(null); Flush(); Flush();
            AssertStartUsable(editor.Window);
        });
    }

    /// <summary>A start workspace and a second one with an opened project, both through the bound UI.</summary>
    private static (DesktopApplicationHost Host, Files Files, WorkspaceWindowHandle Start, WorkspaceWindowHandle Editor) StartAndProject(string name)
    {
        var files = new Files();
        var host = Host(files);
        string first = Project(files, name + "-a.spandraft"), second = Project(files, name + "-b.spandraft");
        Run(async () => { await host.Recents.RecordUseAsync(second); await host.Recents.RecordUseAsync(first); return true; });
        var start = Start(host);
        ClickButton(RecentButtons(start.Window).First());
        Assert.Equal(ProjectOpenStatus.Opened, Run(() => BeginOpen(host, start, second)).Status);
        var editor = Assert.Single(host.Workspaces.Handles, handle => handle != start);
        ClickMenu(start.Window, "CloseProjectMenuItem");
        Assert.Equal(MainViewMode.Start, start.Model!.Mode);
        Assert.NotNull(editor.Model!.Session);
        return (host, files, start, editor);
    }

    // ---- helpers ------------------------------------------------------------------------------------------------

    private static void AssertStartUsable(MainWindow window)
    {
        Layout(window);
        AssertButton(StartButton(window, "NewProjectButton"), true);
        AssertButton(StartButton(window, "OpenProjectButton"), true);
        Assert.All(RecentButtons(window), button => AssertButton(button, true));
        AssertEnabled(window, "NewMenuItem", "OpenMenuItem");
        AssertMenu(window, "SaveMenuItem", false);
        AssertMenu(window, "CloseProjectMenuItem", false);
    }

    /// <summary>The message tells a stale notification (command executable now) from a really refused command.</summary>
    private static void AssertButton(Button button, bool expected) => Assert.True(button.IsEffectivelyEnabled == expected,
        $"{button.Name ?? "recent project"}: enabled = {button.IsEffectivelyEnabled}, command executable now = {button.Command?.CanExecute(null)}, "
        + $"parent enabled = {(button.Parent as Control)?.IsEffectivelyEnabled}");

    private static void AssertEnabled(MainWindow window, params string[] names)
    {
        foreach (string name in names) AssertMenu(window, name, true);
    }

    private static void Layout(Window window)
    {
        DesktopControlEnvironment.ArrangeSection(window);
        Flush();
    }

    /// <summary>
    /// Whether the user finds the menu item enabled: its submenu is opened like a click on the menu header, read, and closed
    /// again. The message tells a stale notification (command executable now) from a really refused command.
    /// </summary>
    private static bool MenuEnabled(MainWindow window, string name, out string state)
    {
        var item = window.FindControl<MenuItem>(name)!;
        var header = (MenuItem)item.Parent!;
        Assert.Single(header.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.Popup>()).ShouldUseOverlayLayer = true;
        header.IsSubMenuOpen = true; Layout(window);
        bool enabled = item.IsEffectivelyEnabled;
        header.IsSubMenuOpen = false; Layout(window);
        state = $"{name}: enabled = {enabled}, command executable now = {item.Command?.CanExecute(null)}";
        return enabled;
    }

    private static void AssertMenu(MainWindow window, string name, bool expected)
    {
        Assert.True(MenuEnabled(window, name, out string state) == expected, state);
    }

    private static Button StartButton(MainWindow window, string name)
    {
        Layout(window);
        return window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name);
    }

    private static Button Toolbar(MainWindow window, string name)
    {
        Layout(window);
        return window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name);
    }

    private static IReadOnlyList<Button> RecentButtons(MainWindow window)
    {
        Layout(window);
        return window.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("recentProject")).ToList();
    }

    /// <summary>A user click: only an enabled button can be clicked; the click runs its own handlers and command.</summary>
    private static void ClickButton(Button button)
    {
        Assert.True(button.IsEffectivelyEnabled, button.Name);
        typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, null);
        Flush(); Flush();
    }

    private static void ClickMenu(MainWindow window, string name)
    {
        AssertMenu(window, name, true);
        var item = window.FindControl<MenuItem>(name)!;
        item.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.MenuItem.ClickEvent));
        Flush(); Flush();
    }

    private static void PressKey(MainWindow window, Key key, KeyModifiers modifiers)
    {
        window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers, Source = window });
        Flush(); Flush();
    }
}
