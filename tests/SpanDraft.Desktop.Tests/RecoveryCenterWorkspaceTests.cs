using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SpanDraft.Desktop;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.ViewModels;
using SpanDraft.Desktop.Views;
using SpanDraft.Desktop.Workspaces;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

/// <summary>The Recovery Center as a workspace mode: entry points, start-slot counting, restore targets and structural layout.</summary>
[Collection("Avalonia")]
public sealed class RecoveryCenterWorkspaceTests
{
    private static DesktopApplicationHost Host(Files files, TestRecoveryStorage storage) =>
        new(new DesktopStartupContext(files, new LocalSettingsStore(files, "settings"), UserSettings.Default)
        { StateStores = DesktopStateStores.Local(files) with { Recovery = storage }, ProjectIdentities = Identities });

    private static RecoverySlot Seed(TestRecoveryStorage storage, string? originalPath = null, int variant = 1, DateTimeOffset? at = null)
    {
        var slot = RecoverySlot.Project(RecoveryId.New());
        storage.Put(slot, ProjectRecovery.Encode(new(State(variant), originalPath, at ?? Now)));
        return slot;
    }

    private static WorkspaceWindowHandle Start(DesktopApplicationHost host)
    {
        var handle = host.Workspaces.CreateInitial();
        DesktopControlEnvironment.ShowInitialized(handle.Window);
        return handle;
    }

    private static T Run<T>(Func<Task<T>> action)
    {
        T result = default!;
        DesktopControlEnvironment.RunOnDispatcher(async () => result = await action());
        Dispatcher.UIThread.RunJobs();
        return result;
    }

    private static void Flush() => Dispatcher.UIThread.RunJobs();

    private static RecoveryCenterViewModel Center(WorkspaceWindowHandle handle) =>
        Assert.IsType<RecoveryCenterViewModel>(handle.Model!.CurrentViewModel);

    private static void ShowCenter(WorkspaceWindowHandle handle)
    {
        Assert.True(handle.Model!.ShowRecoveryCommand.CanExecute(null));
        handle.Model.ShowRecoveryCommand.Execute(null);
        Flush();
    }

    [Fact]
    public void StartPageShowsAPermanentHintWhileUndecidedRecoveryExistsAndHidesItAfterwards()
    {
        using var environment = new DesktopControlEnvironment();
        var storage = new TestRecoveryStorage();
        Seed(storage);
        var host = Host(new Files(), storage);
        var handle = Start(host);
        handle.Window.UpdateLayout(); Flush();
        var start = handle.Model!.StartPage;
        Assert.True(start.HasRecovery);
        Assert.Equal(Strings.RecoveryHintOne, start.RecoveryHint);
        var names = string.Join(",", handle.Window.GetVisualDescendants().OfType<Control>().Where(c => c.Name is not null).Select(c => c.Name));
        var notice = handle.Window.GetVisualDescendants().OfType<Border>().SingleOrDefault(b => b.Name == "RecoveryNotice");
        Assert.True(notice is not null, names);
        Assert.True(notice.IsEffectivelyVisible);
        Assert.True(handle.Model.HasRecovery);
        Assert.True(handle.Window.FindControl<MenuItem>("RecoveryMenuItem")!.IsVisible);

        var candidate = Assert.Single(host.Recovery.Candidates);
        Assert.True(Run(() => host.Recovery.DiscardAsync(candidate.CandidateId)));
        Flush(); handle.Window.UpdateLayout();
        Assert.False(start.HasRecovery);
        Assert.False(notice.IsEffectivelyVisible);
        Assert.False(handle.Window.FindControl<MenuItem>("RecoveryMenuItem")!.IsVisible);
    }

    [Fact]
    public void MenuEntryUsesTheIdleStartWorkspaceAndKeepsItsStartSlot()
    {
        using var environment = new DesktopControlEnvironment();
        var storage = new TestRecoveryStorage();
        Seed(storage);
        var host = Host(new Files(), storage);
        var handle = Start(host);
        ShowCenter(handle);

        Assert.Single(host.Workspaces.Handles);
        Assert.Equal(MainViewMode.RecoveryCenter, handle.Model!.Mode);
        Assert.Equal(WorkspaceKind.RecoveryCenter, handle.Kind);
        Assert.Equal(1, host.Workspaces.StartSlotCount);
        Assert.Equal(DesktopApplicationState.Ready, host.State);
        Assert.Empty(handle.Window.OwnedWindows);

        // Back is only leaving: nothing is decided and the application does not quit.
        Assert.True(Center(handle).BackCommand.CanExecute(null));
        Center(handle).BackCommand.Execute(null); Flush();
        Assert.Equal(MainViewMode.Start, handle.Model.Mode);
        Assert.Single(host.Workspaces.Handles);
        Assert.Equal(1, host.Workspaces.StartSlotCount);
        Assert.True(host.Recovery.HasUndecided);
        Assert.Equal(DesktopApplicationState.Ready, host.State);
    }

    [Fact]
    public void MenuEntryFromTheEditorOpensASeparateWorkspaceWithoutTouchingTheEditor()
    {
        using var environment = new DesktopControlEnvironment();
        var storage = new TestRecoveryStorage();
        Seed(storage);
        var files = new Files();
        string path = TestPath("editor.spandraft");
        files.Data[path] = ProjectFileCodec.Serialize(State(2));
        var host = Host(files, storage);
        var editor = Start(host);
        Assert.True(Run(() => editor.Model!.OpenAsync(path)));
        Assert.Equal(0, host.Workspaces.StartSlotCount);
        var session = editor.Model!.Session;

        ShowCenter(editor);
        Assert.Equal(2, host.Workspaces.Handles.Count);
        var center = host.Workspaces.Handles.Single(h => h != editor);
        Assert.Equal(WorkspaceKind.RecoveryCenter, center.Kind);
        Assert.Equal(MainViewMode.RecoveryCenter, center.Model!.Mode);
        Assert.Same(session, editor.Model.Session);
        Assert.Equal(0, host.Workspaces.StartSlotCount);

        // A second request focuses the one Recovery Center instead of creating another.
        ShowCenter(editor);
        Assert.Equal(2, host.Workspaces.Handles.Count);

        // Leaving the center of a workspace made for it falls back to the start page, which then holds the one start slot.
        Center(center).BackCommand.Execute(null); Flush();
        Assert.True(host.Workspaces.StartSlotCount <= 1);
        Assert.Equal(1, host.Workspaces.StartSlotCount);
        Assert.Same(session, editor.Model.Session);
    }

    [Fact]
    public void RestoringTheLastUndecidedCandidateInThePlaceHostingTheCenterOpensItThere()
    {
        using var environment = new DesktopControlEnvironment();
        var storage = new TestRecoveryStorage();
        var slot = Seed(storage);
        var host = Host(new Files(), storage);
        var handle = Start(host);
        ShowCenter(handle);
        var item = Assert.Single(Center(handle).Items);

        Assert.True(Run(() => Center(handle).RestoreAsync(item.CandidateId)));
        Flush();
        Assert.Single(host.Workspaces.Handles);
        Assert.Equal(MainViewMode.Editor, handle.Model!.Mode);
        Assert.NotNull(handle.Model.Session);
        Assert.True(handle.Model.Session!.IsDirty);
        Assert.False(host.Recovery.HasUndecided);
        Assert.Equal(0, host.Workspaces.StartSlotCount);
        // The restored project keeps writing into its own slot.
        Assert.True(storage.Data.ContainsKey(slot));
    }

    [Fact]
    public void RestoringWhileOthersRemainUndecidedKeepsTheCenterAndOpensANewWorkspace()
    {
        using var environment = new DesktopControlEnvironment();
        var storage = new TestRecoveryStorage();
        Seed(storage, variant: 1, at: Now);
        Seed(storage, variant: 2, at: Now.AddMinutes(-5));
        var host = Host(new Files(), storage);
        var start = Start(host);
        ShowCenter(start);
        var center = Center(start);
        Assert.Equal(2, center.Items.Count);

        Assert.True(Run(() => center.RestoreAsync(center.Items[0].CandidateId)));
        Flush();
        Assert.Equal(2, host.Workspaces.Handles.Count);
        Assert.Equal(MainViewMode.RecoveryCenter, start.Model!.Mode);
        var restored = host.Workspaces.Handles.Single(h => h != start);
        Assert.Equal(WorkspaceKind.Project, restored.Kind);
        Assert.Equal(1, host.Workspaces.StartSlotCount);
        Assert.Single(Center(start).Items);

        // Decide the last one: the center has nothing left and goes back to the start page by itself.
        Assert.True(Run(() => center.DiscardAsync(center.Items[0].CandidateId)));
        Flush();
        Assert.Equal(MainViewMode.Start, start.Model.Mode);
        Assert.Single(storage.Discarded);
        Assert.Equal(1, host.Workspaces.StartSlotCount);
    }

    [Fact]
    public void RestoreAllRestoresEveryReadableCandidateAndDiscardUnreadableKeepsTheRest()
    {
        using var environment = new DesktopControlEnvironment();
        var storage = new TestRecoveryStorage();
        Seed(storage, variant: 1);
        Seed(storage, variant: 2);
        var damaged = RecoverySlot.Project(RecoveryId.New());
        storage.Put(damaged, System.Text.Encoding.UTF8.GetBytes("{"));
        var host = Host(new Files(), storage);
        var start = Start(host);
        ShowCenter(start);
        var center = Center(start);
        Assert.True(center.HasUnreadable);

        RunBulk(center.DiscardUnreadableCommand, () => host.Recovery.Candidates.All(candidate => candidate.Slot != damaged));
        Assert.False(storage.Data.ContainsKey(damaged));
        Assert.Equal(2, Center(start).Items.Count);

        RunBulk(center.RestoreAllCommand, () => host.Recovery.Candidates.Count(candidate => candidate.State == RecoveryCandidateState.Claimed) == 2);
        Assert.False(host.Recovery.HasUndecided);
        Assert.Equal(2, host.Workspaces.Handles.Count(h => h.Kind == WorkspaceKind.Project));
        Assert.True(host.Workspaces.StartSlotCount <= 1);

        void RunBulk(AsyncActionCommand command, Func<bool> reached)
        {
            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnChanged()
            {
                if (reached()) completed.TrySetResult();
            }

            host.Recovery.Changed += OnChanged;
            try
            {
                DesktopControlEnvironment.RunOnDispatcher(() =>
                {
                    Assert.True(command.CanExecute(null));
                    command.Execute(null);
                    return completed.Task;
                });
                Flush();
            }
            finally { host.Recovery.Changed -= OnChanged; }
        }
    }

    [Fact]
    public void ARestoredOlderVariantIsDetachedFromTheOriginalPathAndNeverSavesOverIt()
    {
        using var environment = new DesktopControlEnvironment();
        var storage = new TestRecoveryStorage();
        var files = new Files();
        string original = TestPath("variants.spandraft");
        files.Data[original] = ProjectFileCodec.Serialize(State(9));
        Seed(storage, original, variant: 1, at: Now);
        Seed(storage, original, variant: 2, at: Now.AddMinutes(-10));
        var host = Host(files, storage);
        var start = Start(host);
        ShowCenter(start);
        var center = Center(start);
        var newest = center.Items.Single(i => !i.IsOlderVariant);
        var older = center.Items.Single(i => i.IsOlderVariant);
        var identity = Identities.Resolve(original);
        Assert.Equal(ProjectPathState.RecoveryPending, host.ProjectPaths.StateOf(identity));

        Assert.True(Run(() => center.RestoreAsync(older.CandidateId)));
        Flush();
        var restored = host.Workspaces.Handles.Single(h => h.Kind == WorkspaceKind.Project);
        Assert.Null(restored.Model!.Session!.FilePath);
        Assert.Equal(original, restored.Model.Session.RecoveryOriginPath);
        Assert.Equal(ProjectPathState.RecoveryPending, host.ProjectPaths.StateOf(identity));

        Assert.True(Run(() => center.RestoreAsync(newest.CandidateId)));
        Flush();
        Assert.Equal(ProjectPathState.Open, host.ProjectPaths.StateOf(identity));
    }

    [Theory]
    [InlineData("menu-or-recent")] [InlineData("macos")] [InlineData("shell-or-ipc")]
    public void EveryOpenEntryPointOnARecoveryPendingPathShowsTheCenterWithoutReadingTheDisk(string entry)
    {
        using var environment = new DesktopControlEnvironment();
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new AvaloniaSynchronizationContext());
        try
        {
            var files = new Files();
            var storage = new TestRecoveryStorage();
            string path = TestPath("entry-" + entry + ".spandraft");
            files.Data[path] = ProjectFileCodec.Serialize(State(5));
            Seed(storage, path);
            var host = Host(files, storage);
            var handle = Start(host);
            int reads = 0;
            files.BeforeRead = p => { if (p == path) reads++; return Task.CompletedTask; };

            switch (entry)
            {
                case "menu-or-recent": Assert.False(Run(() => handle.Model!.OpenAsync(path))); break;
                case "macos": Assert.True(host.SubmitPlatformActivation(SpanDraft.Desktop.Activation.LaunchRequest.OpenProject(path))); break;
                default: Assert.Equal(SpanDraft.Desktop.Activation.BrokerStatus.Accepted, host.HandleBrokerRequest(SpanDraft.Desktop.Activation.LaunchRequest.OpenProject(path))); break;
            }
            Flush();
            Assert.Equal(MainViewMode.RecoveryCenter, handle.Model!.Mode);
            Assert.Null(handle.Model.Session);
            Assert.Equal(0, reads);
            Assert.Equal(1, host.Workspaces.StartSlotCount);
            Assert.True(Center(handle).Items.Single().IsFocused);
            Assert.Equal(0, host.Activation.Count);
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    [Fact]
    public void RecoveryCenterViewKeepsItsRowsContainedAlignedAndUnclipped()
    {
        using var environment = new DesktopControlEnvironment();
        var storage = new TestRecoveryStorage();
        Seed(storage, TestPath("a-very-long-name-for-a-recoverable-project-file.spandraft"), 1);
        Seed(storage, null, 2);
        storage.Put(RecoverySlot.Project(RecoveryId.New()), System.Text.Encoding.UTF8.GetBytes("{"));
        var host = Host(new Files(), storage);
        var start = Start(host);
        ShowCenter(start);
        var window = start.Window;
        window.Width = 1100; window.Height = 650;
        Flush();
        var view = window.GetVisualDescendants().OfType<RecoveryCenterView>().Single();
        var list = view.GetVisualDescendants().OfType<ItemsControl>().Single(c => c.Name == "RecoveryList");
        var rows = list.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("recoveryRow")).ToArray();
        Assert.Equal(3, rows.Length);
        var listBounds = new Rect(list.TranslatePoint(default, view)!.Value, list.Bounds.Size);
        foreach (var row in rows)
        {
            var origin = row.TranslatePoint(default, view)!.Value;
            // Contained in the list and as wide as it; no clipping or overflow of buttons or text.
            Assert.True(origin.X >= listBounds.X - .5 && origin.X + row.Bounds.Width <= listBounds.Right + .5);
            Assert.Equal(list.Bounds.Width, row.Bounds.Width, 1);
            foreach (var button in row.GetVisualDescendants().OfType<Button>())
            {
                var topLeft = button.TranslatePoint(default, row)!.Value;
                Assert.True(topLeft.X >= -.5 && topLeft.X + button.Bounds.Width <= row.Bounds.Width + .5);
                Assert.True(topLeft.Y >= -.5 && topLeft.Y + button.Bounds.Height <= row.Bounds.Height + .5);
            }
            foreach (var text in row.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && !string.IsNullOrEmpty(t.Text)))
            {
                var topLeft = text.TranslatePoint(default, row)!.Value;
                Assert.True(topLeft.X + text.Bounds.Width <= row.Bounds.Width + .5);
            }
        }
        // The per-row actions line up across rows.
        var restoreButtons = rows.Select(r => r.GetVisualDescendants().OfType<Button>().First(b => Equals(b.Content is TextBlock t ? t.Text : null, Strings.Restore)))
            .Select(b => b.TranslatePoint(default, view)!.Value.X).ToArray();
        Assert.All(restoreButtons, x => Assert.Equal(restoreButtons[0], x, 1));
        var back = view.FindControl<Button>("RecoveryBackButton")!;
        Assert.True(back.TranslatePoint(default, view)!.Value.X + back.Bounds.Width <= listBounds.X);
        var buttonHeights = new[] { view.FindControl<Button>("RestoreAllButton")!, view.FindControl<Button>("DiscardUnreadableButton")! }
            .Select(b => b.Bounds.Height).Distinct().ToArray();
        Assert.Single(buttonHeights);
    }
}
