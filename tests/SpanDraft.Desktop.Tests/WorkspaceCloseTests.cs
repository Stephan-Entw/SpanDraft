using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SpanDraft.Desktop;
using SpanDraft.Desktop.Activation;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.Services;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using SpanDraft.Desktop.Workspaces;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

/// <summary>
/// Phase 7 through the real production path: <see cref="Window.Close()"/>, the leave and pending-request dialogs, the
/// workspace manager's last-window arbitration and the final cleanup. Storage faults are injected, never timed.
/// </summary>
[Collection("Avalonia")]
public sealed class WorkspaceCloseTests
{
    private sealed class App
    {
        public Files Files { get; } = new();
        public TestRecoveryStorage Storage { get; } = new();
        public DesktopApplicationHost Host { get; }

        public App() => Host = new(new DesktopStartupContext(Files, new LocalSettingsStore(Files, "settings"), UserSettings.Default)
        { StateStores = DesktopStateStores.Local(Files) with { Recovery = Storage }, ProjectIdentities = Identities });

        public WorkspaceWindowHandle Start()
        {
            var handle = Host.Workspaces.CreateInitial();
            DesktopControlEnvironment.ShowInitialized(handle.Window);
            return handle;
        }

        public string Project(string name)
        {
            string path = TestPath(name + ".spandraft");
            Files.Data[path] = ProjectFileCodec.Serialize(State());
            return path;
        }

        /// <summary>
        /// Opens a file in <paramref name="handle"/> and makes it dirty. Its recovery slot is published at once through the
        /// production path that flushes it (New over unsaved work, then cancelled), so no debounce timing is involved.
        /// </summary>
        public (string Path, RecoverySlot Slot) OpenDirty(WorkspaceWindowHandle handle, string name)
        {
            string path = Project(name);
            Assert.True(Run(() => handle.Model!.OpenAsync(path)));
            var before = Storage.Slots(RecoverySlotKind.Project).ToHashSet();
            Assert.True(handle.Model!.Session!.Commit(State(2)));
            Assert.True(Run(() => handle.Model.NewAsync()));
            Assert.True(Run(() => handle.Model.CancelSetupAsync()));
            Assert.Equal(MainViewMode.Editor, handle.Model.Mode);
            return (path, Assert.Single(Storage.Slots(RecoverySlotKind.Project), s => !before.Contains(s)));
        }

        /// <summary>A second workspace next to the project workspace <paramref name="source"/>, showing a clean opened project.</summary>
        public WorkspaceWindowHandle Second(WorkspaceWindowHandle source, string name)
        {
            Assert.True(source.Model!.HasProject);
            var handle = Run(() => Host.Workspaces.RequestNewProjectAsync(source))!;
            Assert.NotSame(source, handle);
            Assert.True(Run(() => handle.Model!.OpenAsync(Project(name))));
            return handle;
        }

        public WorkspaceWindowHandle StartWithProject(string name)
        {
            var handle = Start();
            Assert.True(Run(() => handle.Model!.OpenAsync(Project(name))));
            return handle;
        }
    }

    private static T Run<T>(Func<Task<T>> action)
    {
        T result = default!;
        DesktopControlEnvironment.RunOnDispatcher(async () => result = await action());
        Dispatcher.UIThread.RunJobs();
        return result;
    }

    private static void Flush() { Dispatcher.UIThread.RunJobs(); Dispatcher.UIThread.RunJobs(); }

    private static void WithUiContext(Action action)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new AvaloniaSynchronizationContext());
        try { action(); } finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    private static Window Dialog(WorkspaceWindowHandle handle) => Assert.Single(handle.Window.OwnedWindows, w => w.IsVisible);
    private static Window? OpenDialog(WorkspaceWindowHandle handle) => handle.Window.OwnedWindows.SingleOrDefault(w => w.IsVisible);

    private static void Click(Window dialog, string label)
    {
        dialog.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, label))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Flush();
    }

    private static string DialogText(Window dialog) =>
        string.Join("\n", dialog.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));

    // ---- Save / Discard / Cancel ----------------------------------------------------------------------------

    [Fact]
    public void ACleanWorkspaceClosesWithoutAQuestionAndReleasesItsOwnershipOnce()
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            var app = new App(); var a = app.StartWithProject("clean-a"); var b = app.Second(a, "clean-b");
            var identity = app.Host.ProjectPaths.IdentityOwnedBy(b.WindowId);
            Assert.NotNull(identity);
            b.Window.Close(); Flush();
            Assert.Null(OpenDialog(b)); Assert.DoesNotContain(b, app.Host.Workspaces.Handles);
            Assert.Null(app.Host.ProjectPaths.IdentityOwnedBy(b.WindowId));
            Assert.Equal(ProjectPathState.Free, app.Host.ProjectPaths.StateOf(identity));
            // The final cleanup ran exactly once: a second run (or an internal close) changes nothing.
            app.Host.Workspaces.FinalizeClosed(b); app.Host.Workspaces.CloseInternal(b); Flush();
            Assert.Same(a, Assert.Single(app.Host.Workspaces.Handles)); Assert.Equal(DesktopApplicationState.Ready, app.Host.State);
        });
    }

    [Theory]
    [InlineData(LeaveDecision.Save)]
    [InlineData(LeaveDecision.Discard)]
    [InlineData(LeaveDecision.Cancel)]
    public void ADirtyWorkspaceAsksAndOnlyACommittedCloseReleasesIt(LeaveDecision decision)
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            var app = new App(); var a = app.StartWithProject("dirty-a-" + decision); var b = app.Second(a, "clean-b-" + decision);
            var (path, slot) = app.OpenDirty(b, "dirty-b-" + decision);
            var session = b.Model!.Session!; byte[] original = app.Files.Data[path].ToArray();
            bool tombstonedBeforeClosed = false;
            b.Window.Closed += (_, _) => tombstonedBeforeClosed = app.Storage.Discarded.Contains(slot.FileName);
            b.Window.Close(); Flush();
            var dialog = Dialog(b);
            Assert.True(b.IsCloseRequested); Assert.False(b.CanRoute); Assert.True(b.Model.IsWorkspaceBusy);
            Click(dialog, decision switch { LeaveDecision.Save => Strings.Save, LeaveDecision.Discard => Strings.DontSave, _ => Strings.Cancel });
            if (decision == LeaveDecision.Cancel)
            {
                Assert.Contains(b, app.Host.Workspaces.Handles); Assert.True(b.CanRoute); Assert.False(b.Model.IsWorkspaceBusy);
                Assert.Same(session, b.Model.Session); Assert.True(session.IsDirty); Assert.Contains(slot, app.Storage.Slots());
                Assert.Equal(original, app.Files.Data[path]);
                return;
            }
            Assert.DoesNotContain(b, app.Host.Workspaces.Handles); Assert.Empty(app.Storage.Slots());
            Assert.Equal(decision == LeaveDecision.Discard, tombstonedBeforeClosed);
            if (decision == LeaveDecision.Save) { Assert.NotEqual(original, app.Files.Data[path]); Assert.Empty(app.Storage.Discarded); }
            else Assert.Equal(original, app.Files.Data[path]);
        });
    }

    [Fact]
    public void ACreateSetupWorkspaceDiscardsItsRestoredDraftWithoutAQuestion()
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            var app = new App();
            var slot = RecoverySlot.Setup(RecoveryId.New());
            app.Storage.Put(slot, ProjectSetupRecovery.Encode(new(new ProjectSetupDraft(), Now)));
            var a = app.Start(); app.OpenDirty(a, "beside-the-draft");
            var candidate = Assert.Single(app.Host.Recovery.Undecided).CandidateId;
            Assert.True(Run(() => app.Host.Workspaces.RestoreRecoveryAsync(candidate, a)));
            var c = Assert.Single(app.Host.Workspaces.Handles, h => h != a);
            Assert.Equal(MainViewMode.ProjectSetup, c.Model!.Mode);
            c.Window.Close(); Flush();
            Assert.Null(OpenDialog(c)); Assert.DoesNotContain(c, app.Host.Workspaces.Handles);
            Assert.DoesNotContain(slot, app.Storage.Slots()); Assert.Contains(slot.FileName, app.Storage.Discarded);
        });
    }

    [Fact]
    public void CancellingTheCloseOfAnEditSetupKeepsItsUnconfirmedInputs()
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            var app = new App(); var a = app.Start(); app.OpenDirty(a, "edit-setup");
            a.Model!.EditProject();
            var setup = a.Model.Setup; setup.CalculationTitleInput.Begin(); setup.CalculationTitleInput.Text = "Unconfirmed title";
            a.Window.Close(); Flush();
            Click(Dialog(a), Strings.Cancel);
            Assert.Equal(MainViewMode.ProjectSetup, a.Model.Mode); Assert.Same(setup, a.Model.Setup);
            Assert.True(setup.CalculationTitleInput.IsEditing); Assert.Equal("Unconfirmed title", setup.CalculationTitleInput.Text);
            Assert.Equal(DesktopApplicationState.Ready, app.Host.State);
        });
    }

    [Fact]
    public void ClosingARecoveryCenterWorkspaceLeavesEveryCandidateUndecided()
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            var app = new App();
            foreach (var variant in new[] { 1, 2 })
                app.Storage.Put(RecoverySlot.Project(RecoveryId.New()), ProjectRecovery.Encode(new(State(variant), null, Now)));
            var a = app.Start(); app.OpenDirty(a, "beside-the-center");
            var slots = app.Storage.Slots().ToHashSet(); var undecided = app.Host.Recovery.Undecided.Select(c => c.CandidateId).ToArray();
            var center = Run(() => app.Host.Workspaces.ShowRecoveryCenterAsync(a, null))!;
            Assert.NotSame(a, center); Assert.Equal(MainViewMode.RecoveryCenter, center.Model!.Mode);
            center.Window.Close(); Flush();
            Assert.Null(OpenDialog(center)); Assert.DoesNotContain(center, app.Host.Workspaces.Handles);
            Assert.Equal(undecided, app.Host.Recovery.Undecided.Select(c => c.CandidateId).ToArray());
            Assert.True(slots.SetEquals(app.Storage.Slots())); Assert.Empty(app.Storage.Discarded);
        });
    }

    // ---- busy, native dialogs, settings freeze ---------------------------------------------------------------

    [Fact]
    public void ABusyOrFrozenWorkspaceOrAnOpenSettingsDialogRefusesTheClose()
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            var app = new App(); var a = app.StartWithProject("refuse-a"); var b = app.Second(a, "refuse");
            void Refused()
            {
                b.Window.Close(); Flush();
                Assert.Contains(b, app.Host.Workspaces.Handles); Assert.False(b.IsCloseRequested); Assert.True(b.CanRoute);
            }
            // A running file operation (as while a native picker is open): the workspace is busy.
            var entered = new TaskCompletionSource(); var release = new TaskCompletionSource();
            string other = app.Project("blocked-read");
            app.Files.BeforeRead = async p => { if (p == Identities.Resolve(other).CanonicalPath) { entered.TrySetResult(); await release.Task; } };
            Task<bool>? open = null;
            Dispatcher.UIThread.Post(() => open = b.Model!.OpenAsync(other)); Flush();
            Assert.True(entered.Task.IsCompleted); Assert.True(b.Model!.IsWorkspaceBusy);
            Refused();
            release.SetResult(); Flush(); Assert.True(open!.IsCompleted);
            // Frozen for another workspace's settings commit.
            Assert.True(((ISettingsParticipant)b.Model).TryFreezeForSettings());
            Refused();
            ((ISettingsParticipant)b.Model).ReleaseSettingsFreeze(); Flush();
            // The owner-modal settings dialog.
            var settings = b.Window.GetLogicalDescendants().OfType<MenuItem>().Single(m => Equals(m.Header, Strings.Settings));
            settings.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Flush();
            var dialog = Assert.IsType<SettingsWindow>(Dialog(b));
            Refused();
            dialog.Close(); Flush();
            b.Window.Close(); Flush();
            Assert.DoesNotContain(b, app.Host.Workspaces.Handles);
        });
    }

    // ---- start slot -------------------------------------------------------------------------------------------

    [Fact]
    public void TheStartSlotStaysWithAClosingStartWorkspaceUntilItsCloseIsCommitted()
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            var app = new App(); var a = app.StartWithProject("slot-a"); app.Second(a, "slot-b");
            Assert.True(Run(() => a.Model!.CloseProjectAsync()));
            Assert.Equal(WorkspaceKind.StartOnly, a.Kind);
            Assert.True(Run(() => a.Model!.NewAsync())); CompleteSetup(a.Model!);
            // A start workspace in CreateSetup still holds the slot; while its close runs nothing else can take it.
            Assert.Equal(1, app.Host.Workspaces.StartSlotCount);
            app.Storage.BeforeDiscard = _ =>
            {
                Assert.True(a.IsCloseRequested); Assert.True(a.HoldsStartSlot); Assert.Equal(1, app.Host.Workspaces.StartSlotCount);
                return Task.CompletedTask;
            };
            app.Storage.FailDiscard = _ => true;
            Assert.Null(OpenDialog(a));
            a.Window.Close(); Flush();
            // The draft could not be discarded: the close is rolled back and the slot never left.
            Click(Dialog(a), Strings.OK);
            Assert.Contains(a, app.Host.Workspaces.Handles); Assert.True(a.HoldsStartSlot); Assert.Equal(1, app.Host.Workspaces.StartSlotCount);
            app.Storage.FailDiscard = null;
            a.Window.Close(); Flush();
            Assert.Equal(0, app.Host.Workspaces.StartSlotCount);
        });
    }

    // ---- abort after discard, RecoveryFaulted -----------------------------------------------------------------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void KeepingARequestThatArrivedDuringTheDiscardRestoresTheRecoveryOrFaults(bool republishFails)
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            var app = new App(); var a = app.Start(); var (_, slot) = app.OpenDirty(a, "last-window");
            string requested = app.Project("arrived-meanwhile");
            a.Window.Close(); Flush();
            app.Storage.BeforeDiscard = s =>
            {
                if (s == slot)
                {
                    Assert.True(app.Host.SubmitPlatformActivation(LaunchRequest.OpenProject(requested)));
                    if (republishFails) app.Storage.FailWrite = _ => true;
                }
                return Task.CompletedTask;
            };
            Click(Dialog(a), Strings.DontSave);
            // The slot is already in Discarded when the waiting request is asked about; keeping it gives the close up.
            Assert.Contains(slot.FileName, app.Storage.Discarded);
            var pending = Dialog(a); Assert.Contains("arrived-meanwhile", DialogText(pending));
            app.Storage.BeforeDiscard = null;
            Click(pending, Strings.Cancel);
            Assert.Contains(a, app.Host.Workspaces.Handles); Assert.Equal(DesktopApplicationState.Ready, app.Host.State);
            var notice = a.Window.FindControl<Border>("RecoveryFaultedNotice")!;
            if (!republishFails)
            {
                Assert.Contains(slot, app.Storage.Slots()); Assert.False(a.Model!.IsRecoveryFaulted); Assert.False(notice.IsVisible);
                return;
            }
            Assert.True(a.Model!.IsRecoveryFaulted); Assert.True(notice.IsVisible); Assert.DoesNotContain(slot, app.Storage.Slots());
            Assert.Contains(Strings.RecoveryFaultedError, DialogText(Dialog(a)));
            Click(Dialog(a), Strings.OK);
            // The faulted workspace stays locked and keeps its project. The queued request opens independently.
            Assert.Equal(0, app.Host.Activation.Count); Assert.False(a.Window.FindControl<ContentControl>("WorkspaceContent")!.IsEnabled);
            var opened = Assert.Single(app.Host.Workspaces.Handles, h => h != a);
            Assert.Equal(Identities.Resolve(requested).CanonicalPath, opened.Model!.Session!.FilePath);
            app.Storage.FailWrite = null;
            var retry = a.Window.FindControl<Button>("RecoveryFaultedRetryButton")!;
            Assert.True(retry.IsEffectivelyEnabled); retry.Command!.Execute(null); Flush();
            Assert.False(a.Model.IsRecoveryFaulted); Assert.False(notice.IsVisible); Assert.Contains(slot, app.Storage.Slots());
        });
    }

    // ---- concurrency, last window, activation ---------------------------------------------------------------

    [Fact]
    public void TwoConcurrentClosesNeitherLoseAWaitingRequestNorOwnership()
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            var app = new App(); var a = app.Start(); app.OpenDirty(a, "concurrent-a");
            var b = app.Second(a, "concurrent-b-clean"); app.OpenDirty(b, "concurrent-b");
            a.Window.Close(); Flush(); var dialogA = Dialog(a);
            b.Window.Close(); Flush(); var dialogB = Dialog(b);
            string requested = app.Project("waiting");
            Assert.True(app.Host.SubmitPlatformActivation(LaunchRequest.OpenProject(requested))); Flush();
            Assert.Equal(1, app.Host.Activation.Count);   // nobody can take it while both close
            Click(dialogB, Strings.DontSave);
            Assert.DoesNotContain(b, app.Host.Workspaces.Handles); Assert.Null(app.Host.ProjectPaths.IdentityOwnedBy(b.WindowId));
            Assert.Equal(1, app.Host.Activation.Count); Assert.Equal(DesktopApplicationState.Ready, app.Host.State);
            // a is the last one now: its commit asks about the request instead of dropping it.
            Click(dialogA, Strings.DontSave);
            var pending = Dialog(a); Assert.Contains("waiting", DialogText(pending));
            Assert.Equal(DesktopApplicationState.QuitInProgress, app.Host.State);
            Click(pending, Strings.Discard);
            Assert.Empty(app.Host.Workspaces.Handles); Assert.Equal(DesktopApplicationState.ShuttingDown, app.Host.State);
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnOpenOfAPathWhoseWindowClosesIsDecidedAgainAfterCommitOrRollback(bool commit)
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            var app = new App(); var a = app.Start(); var (path, _) = app.OpenDirty(a, "owner-closes");
            var b = app.Second(a, "opener");
            var surfaced = new List<WindowId>(); a.Window.Surface = _ => surfaced.Add(a.WindowId);
            a.Window.Close(); Flush(); var dialog = Dialog(a);
            bool? opened = null;
            Dispatcher.UIThread.Post(async () => opened = await b.Model!.OpenAsync(path)); Flush();
            Assert.Null(opened);                              // waits: no AlreadyOpen for a closing owner
            Click(dialog, commit ? Strings.DontSave : Strings.Cancel); Flush();
            Assert.True(opened);
            if (commit)
            {
                Assert.Equal(Identities.Resolve(path).CanonicalPath, b.Model!.Session!.FilePath); Assert.Empty(surfaced);
            }
            else
            {
                Assert.Equal([a.WindowId], surfaced); Assert.Same(a, app.Host.Workspaces.Active);
                Assert.Equal(Identities.Resolve(path).CanonicalPath, a.Model!.Session!.FilePath);
            }
        });
    }

    [Fact]
    public void OpensWaitingForAClosingOwnerNeitherOpenNorChangeAnythingAfterTheShutdown()
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            var app = new App(); var a = app.Start(); var (path, _) = app.OpenDirty(a, "shutdown-owner");
            var b = app.Second(a, "shutdown-opener");
            a.Window.Close(); Flush(); var dialog = Dialog(a);
            // The local primitive in b and the queue both wait for a's decision before taking any target.
            bool? opened = null;
            int reads = 0;
            app.Files.BeforeRead = file => { if (file == path) reads++; return Task.CompletedTask; };
            Dispatcher.UIThread.Post(async () => opened = await b.Model!.OpenAsync(path));
            app.Host.Activation.Enqueue(LaunchRequest.OpenProject(path)); Flush();
            Assert.Null(opened); Assert.Equal(1, app.Host.Activation.Count);
            Assert.Equal(new[] { a, b }, app.Host.Workspaces.Handles);
            Assert.Equal(0, reads);
            b.Window.Close(); Flush();
            Assert.Same(a, Assert.Single(app.Host.Workspaces.Handles));
            Assert.Equal(1, app.Host.Activation.Count);
            // The external request is still in the queue, so the last window asks about it before the shutdown.
            Click(dialog, Strings.DontSave);
            var pending = Dialog(a); Assert.Contains("shutdown-owner", DialogText(pending));
            Click(pending, Strings.Discard);
            Assert.False(opened); Assert.Empty(app.Host.Workspaces.Handles);
            Assert.Equal(DesktopApplicationState.ShuttingDown, app.Host.State);
            Assert.Equal(ProjectPathState.Free, app.Host.ProjectPaths.StateOf(Identities.Resolve(path)));
            Assert.Equal(0, reads);
        });
    }

    [Fact]
    public void AWorkspaceThatAppearsDuringTheLastWindowsQuestionCancelsOnlyThatQuit()
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            var app = new App();
            var draft = RecoverySlot.Setup(RecoveryId.New());
            app.Storage.Put(draft, ProjectSetupRecovery.Encode(new(new ProjectSetupDraft(), Now)));
            var a = app.Start(); app.OpenDirty(a, "quit-owner");
            var candidate = Assert.Single(app.Host.Recovery.Undecided).CandidateId;
            a.Window.Close(); Flush();
            string requested = app.Project("quit-request");
            Assert.True(app.Host.SubmitPlatformActivation(LaunchRequest.OpenProject(requested)));
            Click(Dialog(a), Strings.DontSave);
            var pending = Dialog(a);
            Assert.Equal(DesktopApplicationState.QuitInProgress, app.Host.State);
            // Meanwhile a restore creates another workspace; its own failed close must not touch a's quit.
            Assert.True(Run(() => app.Host.Workspaces.RestoreRecoveryAsync(candidate, a)));
            var c = Assert.Single(app.Host.Workspaces.Handles, h => h != a);
            app.Storage.FailDiscard = s => s == draft;
            c.Window.Close(); Flush(); Click(Dialog(c), Strings.OK);
            Assert.Contains(c, app.Host.Workspaces.Handles); Assert.Equal(DesktopApplicationState.QuitInProgress, app.Host.State);
            // a is not the last workspace any more: it closes alone, without a shutdown, and the request stays queued.
            Click(pending, Strings.Discard);
            Assert.DoesNotContain(a, app.Host.Workspaces.Handles); Assert.Contains(c, app.Host.Workspaces.Handles);
            Assert.Equal(DesktopApplicationState.Ready, app.Host.State); Assert.Equal(0, app.Host.Activation.Count);
            Assert.Equal(MainViewMode.ProjectSetup, c.Model!.Mode);
            var opened = Assert.Single(app.Host.Workspaces.Handles, h => h != c);
            Assert.Equal(Identities.Resolve(requested).CanonicalPath, opened.Model!.Session!.FilePath);
        });
    }

    // ---- internal closes ------------------------------------------------------------------------------------

    [Fact]
    public void ACancelledNewProjectWorkspaceIsOutOfRoutingBeforeAnAlreadyPostedRequestRuns()
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            var app = new App(); var a = app.StartWithProject("cancel-owner");
            var b = Run(() => app.Host.Workspaces.RequestNewProjectAsync(a))!;
            Assert.Equal(MainViewMode.ProjectSetup, b.Model!.Mode);
            a.Model!.EditProject();                               // a must keep its setup when the request is handled
            var session = a.Model.Session;
            string requested = app.Project("posted-before-the-close");
            DesktopControlEnvironment.RunOnDispatcher(async () =>
            {
                Assert.True(app.Host.Workspaces.Activate(b.WindowId));
                // The pump for this request is posted before the cancel decides the close.
                app.Host.Activation.Enqueue(LaunchRequest.OpenProject(requested));
                Assert.True(await b.Model.CancelSetupAsync());
                Assert.True(b.IsCloseRequested); Assert.True(b.Model.IsWorkspaceBusy); Assert.False(b.Model.CanOpenProject);
            });
            Flush();
            Assert.DoesNotContain(b, app.Host.Workspaces.Handles); Assert.Null(b.Model.Session);
            Assert.Equal(0, app.Host.Activation.Count);
            var opened = Assert.Single(app.Host.Workspaces.Handles, h => h != a);
            Assert.Equal(Identities.Resolve(requested).CanonicalPath, opened.Model!.Session!.FilePath);
            Assert.Equal(MainViewMode.ProjectSetup, a.Model.Mode);
            Assert.Same(session, a.Model.Session);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnInternalCloseThatFindsItsOperationStillRunningWaitsForItsEndAndThenRuns(bool lastWorkspace)
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            var app = new App(); var a = app.StartWithProject("waiting-owner");
            var b = Run(() => app.Host.Workspaces.RequestNewProjectAsync(a))!;
            if (lastWorkspace) { a.Window.Close(); Flush(); Assert.Same(b, Assert.Single(app.Host.Workspaces.Handles)); }
            else a.Model!.EditProject();                          // a must keep its setup when the request is handled
            string requested = app.Project("waiting-request");
            bool waited = false;
            // The dispatcher may run the posted close before the cancel's operation has ended: here it runs inside it.
            b.Model!.SetupCancelled += () =>
            {
                app.Host.Activation.Enqueue(LaunchRequest.OpenProject(requested));
                Dispatcher.UIThread.RunJobs();
                // Reserved and waiting: still registered, out of routing, no request taken, nothing closed or released.
                Assert.Contains(b, app.Host.Workspaces.Handles);
                Assert.True(b.IsCloseRequested); Assert.False(b.CanRoute); Assert.True(b.Model.IsWorkspaceBusy);
                if (lastWorkspace) Assert.Equal(1, app.Host.Activation.Count);
                Assert.Null(b.Model.Session);
                waited = true;
            };
            Assert.True(Run(() => b.Model.CancelSetupAsync())); Flush();
            Assert.True(waited);
            string canonical = Identities.Resolve(requested).CanonicalPath;
            if (lastWorkspace)
            {
                // After the operation the close runs: as the last workspace b becomes the start page, its reservation is
                // released and it takes the waiting request itself.
                Assert.Same(b, Assert.Single(app.Host.Workspaces.Handles));
                Assert.True(b.CanRoute); Assert.False(b.IsCloseRequested);
                Assert.Equal(0, app.Host.Activation.Count); Assert.Equal(canonical, b.Model.Session!.FilePath);
            }
            else
            {
                Assert.DoesNotContain(b, app.Host.Workspaces.Handles); Assert.Null(b.Model.Session);
                Assert.Equal(0, app.Host.Activation.Count);
                Assert.Equal(MainViewMode.ProjectSetup, a.Model!.Mode);
                var opened = Assert.Single(app.Host.Workspaces.Handles, h => h != a);
                Assert.Equal(canonical, opened.Model!.Session!.FilePath);
            }
        });
    }

    [Fact]
    public void ALeftRecoveryCenterWorkspaceIsOutOfRoutingBeforeAnAlreadyPostedRequestRuns()
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            var app = new App(); var s = app.StartWithProject("center-start"); var b = app.Second(s, "center-editor");
            Assert.True(Run(() => s.Model!.CloseProjectAsync())); Assert.True(Run(() => s.Model!.NewAsync())); CompleteSetup(s.Model!);
            var c = Run(() => app.Host.Workspaces.ShowRecoveryCenterAsync(b, null))!;
            Assert.Equal(WorkspaceCreationReason.RecoveryCenter, c.Reason);
            // s still holds the start slot, but its close hangs in the draft discard: it takes no requests.
            var gate = new TaskCompletionSource();
            app.Storage.BeforeDiscard = _ => gate.Task;
            s.Window.Close(); Flush();
            Assert.True(s.HoldsStartSlot); Assert.False(s.CanRoute);
            b.Model!.EditProject();
            string requested = app.Project("posted-before-the-back");
            DesktopControlEnvironment.RunOnDispatcher(() =>
            {
                Assert.True(app.Host.Workspaces.Activate(c.WindowId));
                app.Host.Activation.Enqueue(LaunchRequest.OpenProject(requested));
                c.Model!.LeaveRecoveryCenter();
                Assert.True(c.IsCloseRequested); Assert.False(c.Model.CanOpenProject);
                return Task.CompletedTask;
            });
            Flush();
            Assert.DoesNotContain(c, app.Host.Workspaces.Handles); Assert.Null(c.Model!.Session);
            Assert.Equal(0, app.Host.Activation.Count); Assert.Equal(1, app.Host.Workspaces.StartSlotCount);
            Assert.Equal(MainViewMode.ProjectSetup, b.Model!.Mode);
            var opened = Assert.Single(app.Host.Workspaces.Handles, h => h != b && h != s);
            Assert.Equal(Identities.Resolve(requested).CanonicalPath, opened.Model!.Session!.FilePath);
            gate.SetResult(); app.Storage.BeforeDiscard = null; Flush();
            Assert.DoesNotContain(s, app.Host.Workspaces.Handles);
        });
    }

    [Fact]
    public void TheLastSurvivingWorkspaceIsNeverClosedInternallyAndIsReleasedAgain()
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            var app = new App(); var a = app.StartWithProject("internal-last");
            var c = Run(() => app.Host.Workspaces.RequestNewProjectAsync(a))!;
            a.Window.Close(); Flush();
            Assert.Same(c, Assert.Single(app.Host.Workspaces.Handles)); Assert.Equal(0, app.Host.Workspaces.StartSlotCount);
            Assert.True(Run(() => c.Model!.CancelSetupAsync())); Flush();
            // It becomes the start page instead: reservation and routing are released, it holds the one start slot.
            Assert.Same(c, Assert.Single(app.Host.Workspaces.Handles));
            Assert.True(c.CanRoute); Assert.True(c.HoldsStartSlot); Assert.Equal(1, app.Host.Workspaces.StartSlotCount);
            Assert.False(c.Model!.IsWorkspaceBusy); Assert.True(c.Model.CanOpenProject);
            Assert.Equal(DesktopApplicationState.Ready, app.Host.State);
        });
    }
}
