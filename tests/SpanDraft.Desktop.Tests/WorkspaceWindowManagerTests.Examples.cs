using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SpanDraft.Desktop;
using SpanDraft.Desktop.Activation;
using SpanDraft.Desktop.Examples;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Services;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.ViewModels;
using SpanDraft.Desktop.Workspaces;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

public sealed class ExampleRouteTests
{
    [Theory]
    [InlineData("spandraft://beispiel/einfeldtraeger", "einfeldtraeger")]
    [InlineData("spandraft://beispiel/kragarm", "kragarm")]
    [InlineData("spandraft://beispiel/abgesetzte-welle", "abgesetzte-welle")]
    [InlineData("SpanDraft://Beispiel/kragarm", "kragarm")]
    public void KnownExampleIdsAreTheOnlyValidRoutes(string uri, string id)
    {
        var route = ExampleCatalog.Parse(uri);
        Assert.Equal(ExampleRouteStatus.Example, route.Status);
        Assert.Equal(id, route.Example!.Id);
        Assert.False(string.IsNullOrWhiteSpace(route.Example.Name));
    }

    [Theory]
    [InlineData("spandraft://beispiel/unbekannt")]
    [InlineData("spandraft://beispiel/Kragarm")]
    [InlineData("spandraft://beispiel/einfeldtraeger2")]
    public void APlainUnknownIdIsAnUnknownExample(string uri) =>
        Assert.Equal(ExampleRouteStatus.UnknownExample, ExampleCatalog.Parse(uri).Status);

    [Theory]
    [InlineData("spandraft://beispiel/other/../einfeldtraeger")]
    [InlineData("spandraft://beispiel/./einfeldtraeger")]
    [InlineData("spandraft://beispiel/%65infeldtraeger")]
    [InlineData("spandraft://beispiel/einfeldtraeger/")]
    [InlineData("spandraft://beispiel/%2e%2e/einfeldtraeger")]
    [InlineData("spandraft://beispiel/einfeldtraeger?x=1")]
    [InlineData("spandraft://beispiel/einfeldtraeger#top")]
    [InlineData("spandraft://beispiel:80/einfeldtraeger")]
    [InlineData("spandraft://user@beispiel/einfeldtraeger")]
    [InlineData("spandraft://beispiel/kragarm/einfeldtraeger")]
    [InlineData("spandraft://beispiel/")]
    [InlineData("spandraft://beispiel")]
    [InlineData("spandraft://hilfe/einfeldtraeger")]
    [InlineData("spandraft:beispiel/einfeldtraeger")]
    [InlineData("spandraft://beispiel/C:/temp/x.spandraft")]
    [InlineData("https://beispiel/einfeldtraeger")]
    public void EverythingElseIsAnInvalidLinkAlsoWhenUriNormalizationWouldTurnItIntoARoute(string uri)
    {
        Assert.Equal(ExampleRouteStatus.InvalidLink, ExampleCatalog.Parse(uri).Status);
        Assert.Null(ExampleCatalog.Parse(uri).Example);
    }
}

public sealed partial class WorkspaceWindowManagerTests
{
    private const string Beam = "spandraft://beispiel/einfeldtraeger";
    private const string Cantilever = "spandraft://beispiel/kragarm";

    /// <summary>An injectable stand-in for the shipped examples.</summary>
    private sealed class ExampleSource : IExampleProjectSource
    {
        public Dictionary<string, byte[]?> Data { get; } = new()
        {
            ["einfeldtraeger"] = ProjectFileCodec.Serialize(State()),
            ["kragarm"] = ProjectFileCodec.Serialize(State(1)),
            ["abgesetzte-welle"] = ProjectFileCodec.Serialize(State(2)),
        };
        public List<string> Reads { get; } = [];
        public Func<string, Task>? BeforeRead { get; set; }

        public async Task<byte[]?> ReadAsync(string id)
        {
            Reads.Add(id);
            if (BeforeRead is not null) await BeforeRead(id);
            return Data.GetValueOrDefault(id)?.ToArray();
        }
    }

    // A null source leaves the host with the shipped, embedded examples.
    private static DesktopApplicationHost ExampleHost(Files files, ExampleSource? examples, TestRecoveryStorage? storage = null) =>
        new(new DesktopStartupContext(files, new LocalSettingsStore(files, "settings"), UserSettings.Default)
        {
            ProjectIdentities = Identities, Examples = examples,
            StateStores = DesktopStateStores.Local(files) with { Recovery = storage ?? new TestRecoveryStorage() }
        });

    private static void RequestExample(DesktopApplicationHost host, string uri)
    {
        Assert.Equal(BrokerStatus.Accepted, host.HandleBrokerRequest(LaunchRequest.OpenUri(uri)));
        Flush(); Flush();
    }

    private static string DialogText(Window dialog) =>
        string.Join("\n", dialog.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));

    private static string ExampleTitle(string name) => name + " — " + Strings.ApplicationTitle;

    private static void AssertCleanTemplate(WorkspaceWindowHandle handle, string name)
    {
        var model = handle.Model!;
        var session = model.Session!;
        Assert.Equal(MainViewMode.Editor, model.Mode);
        Assert.Null(session.FilePath);
        Assert.False(session.IsDirty);
        Assert.Equal(session.CurrentRevision.RevisionId, session.SavedRevisionId);
        Assert.Empty(session.UndoHistory);
        Assert.Equal(ExampleTitle(name), model.WindowTitle);
        Assert.Null(handle.Model!.ProjectPaths.IdentityOwnedBy(handle.WindowId));
        Assert.Equal(WorkspaceCreationReason.None, handle.Reason);
        Assert.False(handle.IsOpenTargetReserved);
    }

    [Theory]
    [InlineData("initial")]
    [InlineData("platform")]
    public void AnExampleLinkAtColdStartOpensInTheInitialStartWorkspace(string source)
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            var files = new Files();
            var examples = new ExampleSource();
            var host = ExampleHost(files, examples);
            var handle = host.Workspaces.CreateInitial();
            if (source == "initial") host.SetInitialRequest(LaunchRequest.OpenUri(Cantilever));
            else Assert.True(host.SubmitPlatformActivation(
                MacActivationSource.ToRequests(new Avalonia.Controls.ApplicationLifetimes.ProtocolActivatedEventArgs(new Uri(Cantilever))).Single()));
            DesktopControlEnvironment.ShowInitialized(handle.Window);
            Flush(); Flush();
            Assert.Equal(DesktopApplicationState.Ready, host.State);
            Assert.Equal(0, host.Activation.Count);
            Assert.Same(handle, Assert.Single(host.Workspaces.Handles));
            AssertCleanTemplate(handle, Strings.ExampleCantilever);
            Assert.True(ProjectFileCodec.Deserialize(examples.Data["kragarm"]!).ContentEquals(handle.Model!.Session!.CurrentRevision.State));
            Assert.Equal(["kragarm"], examples.Reads);
        });
    }

    [Theory]
    [InlineData("einfeldtraeger")]
    [InlineData("kragarm")]
    [InlineData("abgesetzte-welle")]
    public void AShippedExampleOpensFromItsLinkAsAnIndependentCleanTemplate(string id)
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            var files = new Files();
            var host = ExampleHost(files, examples: null);
            var start = Start(host);
            var example = ExampleCatalog.All.Single(e => e.Id == id);
            byte[] embedded = Run(() => new EmbeddedExampleProjectSource().ReadAsync(id))!;
            var shipped = ProjectFileCodec.Deserialize(embedded);

            RequestExample(host, "spandraft://beispiel/" + id);
            RequestExample(host, "spandraft://beispiel/" + id);
            Assert.Equal(0, host.Activation.Count);
            var second = Assert.Single(host.Workspaces.Handles, h => h != start);
            foreach (var handle in new[] { start, second })
            {
                AssertCleanTemplate(handle, example.Name);
                Assert.True(shipped.ContentEquals(handle.Model!.Session!.CurrentRevision.State));
            }
            Assert.NotSame(start.Model!.Session, second.Model!.Session);

            // Editing and saving one leaves the other and the shipped example untouched.
            var session = start.Model.Session!;
            Assert.True(session.Commit(session.CurrentRevision.State with { Info = null }));
            Assert.False(second.Model.Session!.IsDirty);
            string target = TestPath("shipped-" + id + ".spandraft");
            var dialogs = SetPicker(start, target);
            dialogs.SavePath = target;
            Assert.True(Run(() => start.Model.SaveAsync()));
            Assert.Equal(target, session.FilePath);
            Assert.Null(second.Model.Session!.FilePath);
            Assert.Equal(embedded, Run(() => new EmbeddedExampleProjectSource().ReadAsync(id)));
            Assert.Equal([target], files.Writes.Where(p => p.EndsWith(".spandraft", StringComparison.Ordinal)).Distinct());
        });
    }

    [Fact]
    public void AnExampleIsACleanTemplateUntilChangedAndItsFirstSaveIsSaveAs()
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            var files = new Files();
            var storage = new TestRecoveryStorage();
            var examples = new ExampleSource();
            var host = ExampleHost(files, examples, storage);
            var delay = new Delay();
            host.RecoveryDelay = delay.Wait;
            var start = Start(host);
            var source = examples.Data["einfeldtraeger"]!.ToArray();
            RequestExample(host, Beam);
            Assert.Equal(0, host.Activation.Count);
            Assert.Same(start, Assert.Single(host.Workspaces.Handles));
            AssertCleanTemplate(start, Strings.ExampleSimplySupportedBeam);
            Assert.Equal(0, host.Workspaces.StartSlotCount);
            Assert.Empty(host.Services.Recents.Entries);
            Assert.Empty(delay.Waiters);
            Assert.Empty(storage.Slots());
            Assert.DoesNotContain(files.Writes, path => path.EndsWith(".spandraft", StringComparison.Ordinal));

            var model = start.Model!;
            var session = model.Session!;
            Assert.True(session.Commit(State(3)));
            Assert.True(session.IsDirty);
            Assert.Equal(Strings.ExampleSimplySupportedBeam + "* — " + Strings.ApplicationTitle, model.WindowTitle);
            Assert.Single(delay.Waiters);
            Assert.True(model.UndoCommand.CanExecute(null));
            Assert.True(session.Undo());
            Assert.False(session.IsDirty);
            Assert.True(session.Redo());
            Assert.True(session.IsDirty);
            Assert.Null(Run(() => model.CheckpointRecoveryAsync()));
            Assert.Single(storage.Slots(RecoverySlotKind.Project));

            string target = TestPath("saved-example.spandraft");
            var dialogs = SetPicker(start, target);
            dialogs.SavePath = target;
            Assert.True(Run(() => model.SaveAsync()));
            Assert.Equal(1, dialogs.SaveQuestions);
            Assert.Equal(target, session.FilePath);
            Assert.False(session.IsDirty);
            Assert.Equal("saved-example.spandraft — " + Strings.ApplicationTitle, model.WindowTitle);
            Assert.Equal(Identities.Resolve(target), host.ProjectPaths.IdentityOwnedBy(start.WindowId));
            Assert.Contains(host.Services.Recents.Entries, entry => entry.Path == target);
            Assert.Empty(storage.Slots(RecoverySlotKind.Project));
            Assert.Equal(source, examples.Data["einfeldtraeger"]);
            // A plain save now writes the file itself; no picker any more.
            Assert.True(session.Commit(State(4)));
            Assert.True(Run(() => model.SaveAsync()));
            Assert.Equal(1, dialogs.SaveQuestions);
        });
    }

    [Fact]
    public void AnUnchangedExampleClosesWithoutAnyQuestion()
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            var host = ExampleHost(new Files(), new ExampleSource());
            var start = Start(host);
            RequestExample(host, Beam);
            var dialogs = SetPicker(start, TestPath("unused.spandraft"));
            Assert.True(Run(() => start.Model!.CloseProjectAsync()));
            Assert.Equal(0, dialogs.LeaveQuestions);
            Assert.Null(start.Model!.Session);
            Assert.Equal(MainViewMode.Start, start.Model.Mode);
            Assert.Equal(Strings.ApplicationTitle, start.Model.WindowTitle);
            Assert.Empty(host.Services.Recents.Entries);
        });
    }

    [Fact]
    public void AnExampleOpensBesideADirtyProjectAndASetupDraftAndRepeatedOpensAreIndependent()
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            var files = new Files();
            var host = ExampleHost(files, new ExampleSource());
            var a = Start(host);
            CreateProject(a);
            var dirty = a.Model!.Session!;
            Assert.True(dirty.Commit(State(5)));
            var revision = dirty.CurrentRevision;
            int undo = dirty.UndoHistory.Count;
            var draft = Run(() => host.Workspaces.RequestNewProjectAsync(a))!;
            Assert.Equal(MainViewMode.ProjectSetup, draft.Model!.Mode);

            RequestExample(host, Beam);
            RequestExample(host, Beam);
            Assert.Equal(0, host.Activation.Count);
            var opened = host.Workspaces.Handles.Where(h => h != a && h != draft).ToList();
            Assert.Equal(2, opened.Count);
            foreach (var handle in opened) AssertCleanTemplate(handle, Strings.ExampleSimplySupportedBeam);
            Assert.NotSame(opened[0].Model!.Session, opened[1].Model!.Session);
            Assert.NotSame(opened[0].Model!.Editor, opened[1].Model!.Editor);
            Assert.True(opened[0].Model!.Session!.Commit(State(6)));
            Assert.True(opened[0].Model!.Session!.IsDirty);
            Assert.False(opened[1].Model!.Session!.IsDirty);
            Assert.Empty(opened[1].Model!.Session!.UndoHistory);

            Assert.Same(dirty, a.Model.Session);
            Assert.Same(revision, dirty.CurrentRevision);
            Assert.True(dirty.IsDirty);
            Assert.Equal(undo, dirty.UndoHistory.Count);
            Assert.Equal(MainViewMode.ProjectSetup, draft.Model.Mode);
            Assert.Null(draft.Model.Session);
        });
    }

    [Theory]
    [InlineData("spandraft://beispiel/unbekannt", "unknown")]
    [InlineData("spandraft://beispiel/other/../einfeldtraeger", "invalid")]
    [InlineData(Beam, "missing")]
    [InlineData(Beam, "damaged")]
    public void FailedExampleRequestsAreShownOnceAndNeitherLeaveAWindowNorBlockTheQueue(string uri, string failure)
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            var files = new Files();
            var examples = new ExampleSource();
            if (failure == "missing") examples.Data["einfeldtraeger"] = null;
            if (failure == "damaged") examples.Data["einfeldtraeger"] = "{ not a project"u8.ToArray();
            var host = ExampleHost(files, examples);
            var a = Start(host);
            CreateProject(a);
            var session = a.Model!.Session;

            RequestExample(host, uri);
            Assert.Same(a, Assert.Single(host.Workspaces.Handles));
            Assert.Same(session, a.Model.Session);
            var dialog = Assert.Single(a.Window.OwnedWindows);
            string expected = failure switch
            {
                "unknown" => Strings.ExampleUnknown,
                "invalid" => Strings.ExampleLinkInvalid,
                _ => string.Format(System.Globalization.CultureInfo.CurrentUICulture, Strings.ExampleOpenError, Strings.ExampleSimplySupportedBeam)
            };
            Assert.Contains(expected, DialogText(dialog));
            Assert.Equal(failure is "unknown" or "invalid" ? [] : ["einfeldtraeger"], examples.Reads);
            Click(dialog, Strings.OK); Flush(); Flush();
            Assert.Equal(0, host.Activation.Count);
            Assert.Empty(a.Window.OwnedWindows);
            Assert.Same(a, Assert.Single(host.Workspaces.Handles));
            Assert.All(host.Workspaces.Handles, h => Assert.False(h.IsOpenTargetReserved));

            // The queue goes on: the next request opens normally.
            examples.Data["einfeldtraeger"] = ProjectFileCodec.Serialize(State());
            RequestExample(host, Cantilever);
            Assert.Equal(0, host.Activation.Count);
            AssertCleanTemplate(Assert.Single(host.Workspaces.Handles, h => h != a), Strings.ExampleCantilever);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NoWorkspaceIsCreatedOrReservedWhileTheExampleIsStillLoading(bool fails)
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            var files = new Files();
            var examples = new ExampleSource();
            var read = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            examples.BeforeRead = _ => read.Task;
            var host = ExampleHost(files, examples);
            var a = Start(host);
            CreateProject(a);
            var handles = host.Workspaces.Handles.ToList();

            Assert.Equal(BrokerStatus.Accepted, host.HandleBrokerRequest(LaunchRequest.OpenUri(Beam)));
            Flush(); Flush();
            Assert.Equal(["einfeldtraeger"], examples.Reads);
            Assert.Equal(1, host.Activation.Count);
            // The read is held: no target is chosen, created, reserved or registered for settings yet.
            Assert.Equal(handles, host.Workspaces.Handles);
            Assert.All(host.Workspaces.Handles, h => Assert.False(h.IsOpenTargetReserved));
            Assert.Null(a.SettingsRegistration);
            Assert.False(a.Model!.IsWorkspaceBusy);

            if (fails) examples.Data["einfeldtraeger"] = null;
            read.SetResult();
            Flush(); Flush();
            if (fails)
            {
                Assert.Same(a, Assert.Single(host.Workspaces.Handles));
                Click(Assert.Single(a.Window.OwnedWindows), Strings.OK); Flush();
                Assert.Same(a, Assert.Single(host.Workspaces.Handles));
            }
            else AssertCleanTemplate(Assert.Single(host.Workspaces.Handles, h => h != a), Strings.ExampleSimplySupportedBeam);
            Assert.Equal(0, host.Activation.Count);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AFailingEditorInitializationIsShownOnceAndCleansUpTheTarget(bool freeStart)
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            var files = new Files();
            var examples = new ExampleSource();
            var host = ExampleHost(files, examples);
            // Every workspace takes the analysis when its model is created, the reused start workspace too.
            bool armed = false;
            int initializations = 0;
            host.AnalyzeProject = model =>
            {
                if (!armed) return SpanDraft.Analysis.BeamAnalysis.Analyze(model);
                initializations++;
                throw new IOException("Injected editor initialization failure");
            };
            var a = Start(host);
            if (!freeStart) CreateProject(a);
            var session = a.Model!.Session;
            armed = true;

            RequestExample(host, Beam);
            Assert.Equal(1, initializations);
            Assert.Same(a, Assert.Single(host.Workspaces.Handles));
            Assert.Same(session, a.Model.Session);
            var dialog = Assert.Single(a.Window.OwnedWindows);
            Assert.Contains(string.Format(System.Globalization.CultureInfo.CurrentUICulture, Strings.ExampleOpenError,
                Strings.ExampleSimplySupportedBeam), DialogText(dialog));
            Click(dialog, Strings.OK); Flush(); Flush();
            Assert.Equal(0, host.Activation.Count);
            Assert.Empty(a.Window.OwnedWindows);
            Assert.Equal(1, initializations);
            Assert.Equal(["einfeldtraeger"], examples.Reads);
            Assert.Same(a, Assert.Single(host.Workspaces.Handles));
            Assert.Equal(freeStart ? MainViewMode.Start : MainViewMode.Editor, a.Model.Mode);
            Assert.Equal(freeStart ? 1 : 0, host.Workspaces.StartSlotCount);
            Assert.False(a.IsOpenTargetReserved);
        });
    }

    /// <summary>
    /// The only workspace has an application dialog, so the failed editor initialization in the new target cannot be shown
    /// yet: further pumps retry only the presentation, never the read, the target or the initialization.
    /// </summary>
    private static (DesktopApplicationHost Host, WorkspaceWindowHandle A, ExampleSource Examples, Window About, Func<int> Initializations,
        Action Disarm) FailureWaitingForPresenter()
    {
        var examples = new ExampleSource();
        var host = ExampleHost(new Files(), examples);
        bool armed = false;
        int initializations = 0;
        // Only the first initialization fails: a repeated open would succeed and end a retry loop visibly.
        host.AnalyzeProject = model =>
        {
            if (!armed || initializations++ > 0) return SpanDraft.Analysis.BeamAnalysis.Analyze(model);
            throw new IOException("Injected editor initialization failure");
        };
        var a = Start(host);
        armed = true;
        var about = new Window();
        _ = host.AppDialogs.ShowAsync(AppDialogKind.About, a.Window, () => about);
        Flush();
        RequestExample(host, Beam);
        return (host, a, examples, about, () => initializations, () => armed = false);
    }

    [Fact]
    public void AFailedExampleOpenRetriesOnlyItsPresentation()
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            var (host, a, examples, about, initializations, disarm) = FailureWaitingForPresenter();
            Assert.Equal(1, initializations());
            Assert.True(host.Workspaces.HasPendingExampleFailure);
            for (int i = 0; i < 3; i++)
            {
                Run(async () => { await host.Activation.PumpAsync(); return true; });
                host.Workspaces.PostPump(); Flush(); Flush();
            }
            Assert.Equal(1, initializations());
            Assert.Equal(["einfeldtraeger"], examples.Reads);
            Assert.Same(a, Assert.Single(host.Workspaces.Handles));
            Assert.Equal(1, host.Activation.Count);
            Assert.Same(about, Assert.Single(a.Window.OwnedWindows));

            // A later request waits behind it (FIFO).
            disarm();
            RequestExample(host, Cantilever);
            Assert.Equal(2, host.Activation.Count);
            Assert.Equal(["einfeldtraeger"], examples.Reads);

            about.Close(); Flush(); Flush();
            var failure = Assert.Single(a.Window.OwnedWindows);
            Assert.Contains(string.Format(System.Globalization.CultureInfo.CurrentUICulture, Strings.ExampleOpenError,
                Strings.ExampleSimplySupportedBeam), DialogText(failure));
            Assert.Equal(1, initializations());
            Click(failure, Strings.OK); Flush(); Flush();
            Assert.False(host.Workspaces.HasPendingExampleFailure);
            Assert.Equal(0, host.Activation.Count);
            Assert.Equal(["einfeldtraeger", "kragarm"], examples.Reads);
            Assert.Equal(1, initializations());
            Assert.Same(a, Assert.Single(host.Workspaces.Handles));
            AssertCleanTemplate(a, Strings.ExampleCantilever);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void APendingExampleFailureSurvivesACancelledQuitAndEndsWithTheShutdown(bool discard)
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            var (host, a, examples, about, initializations, _) = FailureWaitingForPresenter();
            Assert.True(host.Workspaces.HasPendingExampleFailure);
            // The quit closes the safely closable dialog, then asks about the waiting request.
            Assert.True(host.Quit.RequestQuit()); Flush(); Flush();
            Assert.Equal(1, initializations());
            var question = Assert.Single(a.Window.OwnedWindows);
            Assert.NotSame(about, question);
            Assert.Contains(string.Format(System.Globalization.CultureInfo.CurrentUICulture, Strings.PendingRequestOneQuestion,
                Strings.ExampleSimplySupportedBeam), DialogText(question));
            Click(question, discard ? Strings.Discard : Strings.Cancel); Flush(); Flush();
            if (discard)
            {
                Assert.Equal(DesktopApplicationState.ShuttingDown, host.State);
                Assert.False(host.Workspaces.HasPendingExampleFailure);
                Assert.Equal(0, host.Activation.Count);
                Assert.Equal(1, initializations());
                return;
            }
            Assert.Equal(DesktopApplicationState.Ready, host.State);
            Assert.Equal(1, initializations());
            Assert.Equal(["einfeldtraeger"], examples.Reads);
            var failure = Assert.Single(a.Window.OwnedWindows);
            Assert.Contains(string.Format(System.Globalization.CultureInfo.CurrentUICulture, Strings.ExampleOpenError,
                Strings.ExampleSimplySupportedBeam), DialogText(failure));
            Click(failure, Strings.OK); Flush(); Flush();
            Assert.False(host.Workspaces.HasPendingExampleFailure);
            Assert.Equal(0, host.Activation.Count);
            Assert.Empty(a.Window.OwnedWindows);
            Assert.Same(a, Assert.Single(host.Workspaces.Handles));
            Assert.Equal(MainViewMode.Start, a.Model!.Mode);
        });
    }

    [Fact]
    public void SaveAsOfAnExampleOntoAPathOpenElsewhereIsRejectedByTheArbitration()
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            var files = new Files();
            var host = ExampleHost(files, new ExampleSource());
            var a = Start(host);
            string pathA = Project(files, "example-occupied.spandraft");
            Assert.True(Run(() => a.Model!.OpenAsync(pathA)));
            var original = files.Data[pathA];
            RequestExample(host, Beam);
            var b = Assert.Single(host.Workspaces.Handles, h => h != a);
            var session = b.Model!.Session!;
            Assert.True(session.Commit(State(2)));
            var dialogs = SetPicker(b, pathA);
            dialogs.SavePath = pathA;
            Assert.False(Run(() => b.Model.SaveAsync()));
            Assert.Contains(Strings.ProjectFileInUse, Assert.Single(dialogs.Errors));
            Assert.Null(session.FilePath);
            Assert.True(session.IsDirty);
            Assert.Equal(Strings.ExampleSimplySupportedBeam + "* — " + Strings.ApplicationTitle, b.Model.WindowTitle);
            Assert.Equal(original, files.Data[pathA]);
            Assert.Equal(Identities.Resolve(pathA), host.ProjectPaths.IdentityOwnedBy(a.WindowId));
        });
    }

    [Fact]
    public void AnExampleWaitsForASettingsApplyAndOpensAfterwards()
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            var files = new Files();
            var examples = new ExampleSource();
            var host = ExampleHost(files, examples);
            var start = Start(host);
            var write = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            files.BeforeWrite = (path, _, _) => path == "settings" ? write.Task : Task.CompletedTask;
            var changed = host.Settings.CurrentSettings with { SignificantDigits = 5 };
            var applying = host.SettingsCoordinator.ApplyAsync(SettingsPatch.From(changed));
            Assert.NotNull(host.SettingsCoordinator.ActiveApply);

            RequestExample(host, Beam);
            Run(async () => { await host.Activation.PumpAsync(); return true; });
            Assert.Equal(1, host.Activation.Count);
            Assert.Empty(examples.Reads);
            Assert.Same(start, Assert.Single(host.Workspaces.Handles));
            Assert.Null(start.Model!.Session);

            write.SetResult();
            Assert.Equal(SettingsApplyOutcome.Applied, Run(() => applying).Outcome);
            Flush(); Flush();
            Assert.Equal(0, host.Activation.Count);
            Assert.Same(start, Assert.Single(host.Workspaces.Handles));
            AssertCleanTemplate(start, Strings.ExampleSimplySupportedBeam);
        });
    }

    [Fact]
    public void AnApplicationDialogOfAnotherWorkspaceDoesNotHoldAnExampleBack()
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            var files = new Files();
            var host = ExampleHost(files, new ExampleSource());
            var a = Start(host);
            Assert.True(Run(() => a.Model!.OpenAsync(Project(files, "dialog-a.spandraft"))));
            // A second, free start workspace.
            var b = host.Workspaces.Find(Run(() => host.Workspaces.RequestOpenProjectAsync(a, Project(files, "dialog-b.spandraft"))).Owner!.Value)!;
            Assert.True(Run(() => b.Model!.CloseProjectAsync()));
            Assert.Equal(1, host.Workspaces.StartSlotCount);
            var about = new Window();
            var shown = host.AppDialogs.ShowAsync(AppDialogKind.About, a.Window, () => about);
            Flush();
            Assert.Same(about, host.AppDialogs.Find(AppDialogKind.About));

            RequestExample(host, Beam);
            Assert.Equal(0, host.Activation.Count);
            Assert.Equal(2, host.Workspaces.Handles.Count);
            AssertCleanTemplate(b, Strings.ExampleSimplySupportedBeam);
            Assert.Same(about, Assert.Single(a.Window.OwnedWindows));
            about.Close(); Flush();
            Assert.True(shown.IsCompletedSuccessfully);
        });
    }

    [Fact]
    public void AFailureNoWorkspaceCanShowYetStaysQueuedUntilTheApplicationDialogCloses()
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            var host = ExampleHost(new Files(), new ExampleSource());
            var a = Start(host);
            var about = new Window();
            var shown = host.AppDialogs.ShowAsync(AppDialogKind.About, a.Window, () => about);
            Flush();

            RequestExample(host, "spandraft://beispiel/unbekannt");
            Assert.Equal(1, host.Activation.Count);
            Assert.Same(about, Assert.Single(a.Window.OwnedWindows));
            Assert.Same(a, Assert.Single(host.Workspaces.Handles));

            about.Close(); Flush(); Flush();
            Assert.True(shown.IsCompletedSuccessfully);
            var failure = Assert.Single(a.Window.OwnedWindows);
            Assert.Contains(Strings.ExampleUnknown, DialogText(failure));
            Click(failure, Strings.OK); Flush();
            Assert.Equal(0, host.Activation.Count);
            Assert.Empty(a.Window.OwnedWindows);
            Assert.Same(a, Assert.Single(host.Workspaces.Handles));
        });
    }

    [Fact]
    public void AnApplicationDialogOverTheOnlyStartWorkspaceLetsTheExampleOpenInANewWindow()
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            var host = ExampleHost(new Files(), new ExampleSource());
            var a = Start(host);
            var about = new Window();
            var shown = host.AppDialogs.ShowAsync(AppDialogKind.About, a.Window, () => about);
            Flush();

            RequestExample(host, Beam);
            Assert.Equal(0, host.Activation.Count);
            AssertCleanTemplate(Assert.Single(host.Workspaces.Handles, h => h != a), Strings.ExampleSimplySupportedBeam);
            Assert.Null(a.Model!.Session);
            Assert.Same(about, Assert.Single(a.Window.OwnedWindows));
            about.Close(); Flush();
            Assert.True(shown.IsCompletedSuccessfully);
        });
    }

    [Fact]
    public void AClosePendingInAnotherWorkspaceDoesNotHoldAnExampleBack()
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            var files = new Files();
            var host = ExampleHost(files, new ExampleSource());
            var a = Start(host);
            CreateProject(a);
            var session = a.Model!.Session;
            var draft = Run(() => host.Workspaces.RequestNewProjectAsync(a))!;
            a.Window.Close(); Flush();
            var leave = Assert.Single(a.Window.OwnedWindows);
            Assert.NotNull(a.CloseTransaction);

            RequestExample(host, Beam);
            Assert.Equal(0, host.Activation.Count);
            var opened = Assert.Single(host.Workspaces.Handles, h => h != a && h != draft);
            AssertCleanTemplate(opened, Strings.ExampleSimplySupportedBeam);

            Click(leave, Strings.Cancel); Flush(); Flush();
            Assert.False(a.IsClosing);
            Assert.True(a.CanRoute);
            Assert.Same(session, a.Model.Session);
            Assert.Equal(MainViewMode.ProjectSetup, draft.Model!.Mode);
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void QuittingWithAQueuedExampleNamesItAndCancellingOpensIt(bool discard)
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            var host = ExampleHost(new Files(), new ExampleSource());
            var a = Start(host);
            bool closed = false; a.Window.Closed += (_, _) => closed = true;
            Assert.Equal(BrokerStatus.Accepted, host.HandleBrokerRequest(LaunchRequest.OpenUri(Cantilever)));
            Assert.Equal(1, host.Activation.Count);
            // The last-window close starts before the posted activation pump runs.
            a.Window.Close(); Flush();
            Assert.False(closed);
            var question = Assert.Single(a.Window.OwnedWindows);
            Assert.Contains(string.Format(System.Globalization.CultureInfo.CurrentUICulture, Strings.PendingRequestOneQuestion,
                Strings.ExampleCantilever), DialogText(question));
            Click(question, discard ? Strings.Discard : Strings.Cancel); Flush(); Flush();
            Assert.Equal(0, host.Activation.Count);
            if (discard)
            {
                Assert.True(closed);
                Assert.Empty(host.Workspaces.Handles);
                return;
            }
            Assert.Equal(DesktopApplicationState.Ready, host.State);
            Assert.Same(a, Assert.Single(host.Workspaces.Handles));
            AssertCleanTemplate(a, Strings.ExampleCantilever);
        });
    }

    [Fact]
    public void AShutdownWhileTheExampleLoadsCreatesNothing()
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            var examples = new ExampleSource();
            var read = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            examples.BeforeRead = _ => read.Task;
            var host = ExampleHost(new Files(), examples);
            var a = Start(host);
            RequestExample(host, Beam);
            Assert.Equal(["einfeldtraeger"], examples.Reads);

            // The request being loaded is still pending: the last-window close asks about it.
            a.Window.Close(); Flush();
            Click(Assert.Single(a.Window.OwnedWindows), Strings.Discard); Flush(); Flush();
            Assert.Equal(DesktopApplicationState.ShuttingDown, host.State);
            Assert.Empty(host.Workspaces.Handles);
            read.SetResult();
            Flush(); Flush();
            Assert.Empty(host.Workspaces.Handles);
            Assert.Equal(0, host.Activation.Count);
        });
    }
}
