using SpanDraft.Desktop;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using SpanDraft.Desktop.Workspaces;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

public sealed partial class WorkspaceWindowManagerTests
{
    [Fact]
    public void ThePublicMultiWindowWorkflowKeepsProjectsAndRecoveryIndependent()
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            var (host, files, storage, a, b, pathA, pathB) = OpenTwoProjects();
            var modelA = a.Model!;
            var modelB = b.Model!;
            var editorA = modelA.Editor!;
            var editorB = modelB.Editor!;
            var sessionA = modelA.Session!;
            var sessionB = modelB.Session!;
            Assert.NotSame(sessionA, sessionB);
            Assert.NotSame(editorA, editorB);
            Assert.NotSame(sessionA.UndoHistory, sessionB.UndoHistory);
            Assert.NotSame(sessionA.RedoHistory, sessionB.RedoHistory);
            Assert.Empty(storage.Slots());
            var initialA = sessionA.CurrentRevision;
            var initialAnalysisA = editorA.Presentation.Result;
            var initialAnalysisB = editorB.Presentation.Result;

            EditPointForce(editorA, -2000);
            EditPointForce(editorB, -4500);
            var changedA = sessionA.CurrentRevision;
            var changedB = sessionB.CurrentRevision;
            var analysisA = editorA.Presentation.Result!;
            var analysisB = editorB.Presentation.Result!;
            Assert.NotSame(initialAnalysisA, analysisA);
            Assert.NotSame(initialAnalysisB, analysisB);
            Assert.NotEqual(analysisA.Engineering.BendingMomentMagnitude, analysisB.Engineering.BendingMomentMagnitude);
            Assert.True(sessionA.IsDirty);
            Assert.True(sessionB.IsDirty);

            Assert.True(modelA.Undo());
            Assert.Same(initialA, sessionA.CurrentRevision);
            Assert.Same(changedB, sessionB.CurrentRevision);
            Assert.Same(analysisB, editorB.Presentation.Result);
            Assert.False(modelB.CanRedo);
            Assert.True(modelA.Redo());
            Assert.Same(changedA, sessionA.CurrentRevision);
            Assert.Same(changedB, sessionB.CurrentRevision);
            Assert.Same(analysisB, editorB.Presentation.Result);

            Assert.Null(Run(() => modelA.CheckpointRecoveryAsync()));
            Assert.Null(Run(() => modelB.CheckpointRecoveryAsync()));
            Assert.Equal(2, storage.Slots(RecoverySlotKind.Project).Count);
            var slotA = ReadCheckpoint(pathA, changedA);
            var slotB = ReadCheckpoint(pathB, changedB);
            Assert.NotEqual(slotA, slotB);
            var checkpointA = storage.Data[slotA];
            var originalA = files.Data[pathA];

            Assert.True(Run(() => modelB.SaveAsync()));
            Assert.False(sessionB.IsDirty);
            Assert.True(changedB.State.ContentEquals(ProjectFileCodec.Deserialize(files.Data[pathB])));
            Assert.DoesNotContain(slotB, storage.Slots());
            AssertAUnchanged();

            var dialogs = SetPicker(b, pathA);
            dialogs.SavePath = pathA;
            var savedB = files.Data[pathB];
            var projectWrites = files.Writes.Count(path => path == pathA || path == pathB);
            Assert.False(Run(() => modelB.SaveAsync(saveAs: true)));
            Assert.Contains(Strings.ProjectFileInUse, Assert.Single(dialogs.Errors));
            Assert.Equal(projectWrites, files.Writes.Count(path => path == pathA || path == pathB));
            Assert.Equal(savedB, files.Data[pathB]);
            Assert.Equal(pathB, sessionB.FilePath);
            Assert.Same(changedB, sessionB.CurrentRevision);
            Assert.False(sessionB.IsDirty);
            Assert.Equal(Identities.Resolve(pathB), host.ProjectPaths.IdentityOwnedBy(b.WindowId));
            AssertAUnchanged();

            Assert.True(modelB.NewCommand.CanExecute(null));
            modelB.NewCommand.Execute(null); Flush();
            var c = Assert.Single(host.Workspaces.Handles, handle => handle != a && handle != b);
            var modelC = c.Model!;
            Assert.Null(c.Window.Owner);
            Assert.Equal(WorkspaceKind.CreateSetup, c.Kind);
            Assert.Null(modelC.Session);
            foreach (var model in new[] { modelA, modelB, modelC })
            {
                Assert.Same(host.Services, model.Services);
                Assert.Same(host.Settings, model.Services.Settings);
                Assert.Same(host.SettingsCoordinator, model.Services.SettingsCoordinator);
                Assert.Same(host.Materials, model.Services.Materials);
                Assert.Same(host.Sections, model.Services.Sections);
                Assert.Same(host.Recents, model.Services.Recents);
                Assert.Same(host.ProjectPaths, model.ProjectPaths);
            }
            var title = modelC.Setup.CalculationTitleInput;
            title.Begin(); title.Text = "Workspace C draft";
            Assert.True(title.Confirm());
            Assert.Null(Run(() => modelC.CheckpointRecoveryAsync()));
            var slotC = Assert.Single(storage.Slots(RecoverySlotKind.Setup));
            Assert.NotEqual(slotA.Id, slotC.Id);
            Assert.Equal("Workspace C draft", ProjectSetupRecovery.Decode(storage.Data[slotC]).Draft.CalculationTitle);
            AssertAUnchanged();

            var surfaced = new List<WindowId>();
            foreach (var handle in host.Workspaces.Handles) handle.Window.Surface = _ => surfaced.Add(handle.WindowId);
            int reads = 0;
            files.BeforeRead = path => { if (path == pathA) reads++; return Task.CompletedTask; };
            Assert.True(modelB.OpenCommand.CanExecute(null));
            modelB.OpenCommand.Execute(null); Flush();
            Assert.Equal([a.WindowId], surfaced);
            Assert.Same(a, host.Workspaces.Active);
            Assert.Equal(0, reads);
            Assert.Equal(3, host.Workspaces.Handles.Count);
            AssertAUnchanged();

            Assert.True(Run(() => modelC.CancelSetupAsync()));
            Assert.True(c.IsFinalized);
            Assert.DoesNotContain(slotC, storage.Slots());
            b.Window.Close(); Flush();
            Assert.True(b.IsFinalized);
            Assert.Equal(ProjectPathState.Free, host.ProjectPaths.StateOf(Identities.Resolve(pathB)));
            Assert.Same(a, Assert.Single(host.Workspaces.Handles));
            Assert.Equal(DesktopApplicationState.Ready, host.State);
            AssertAUnchanged();

            var resultBeforeFinalEdit = editorA.Presentation.Result!;
            EditPointForce(editorA, -6000);
            Assert.NotEqual(resultBeforeFinalEdit.Engineering.BendingMomentMagnitude, editorA.Presentation.Result!.Engineering.BendingMomentMagnitude);
            Assert.True(sessionA.IsDirty);
            Assert.Null(Run(() => modelA.CheckpointRecoveryAsync()));
            Assert.Equal(slotA, ReadCheckpoint(pathA, sessionA.CurrentRevision));
            Assert.Equal(slotA, Assert.Single(storage.Slots()));

            RecoverySlot ReadCheckpoint(string originalPath, ProjectRevision revision)
            {
                var slot = Assert.Single(storage.Slots(RecoverySlotKind.Project), candidate =>
                    ProjectRecovery.Decode(storage.Data[candidate]).OriginalFilePath == originalPath);
                Assert.False(slot.IsLegacy);
                Assert.True(revision.State.ContentEquals(ProjectRecovery.Decode(storage.Data[slot]).State));
                return slot;
            }

            void AssertAUnchanged()
            {
                Assert.Same(sessionA, modelA.Session);
                Assert.Same(changedA, sessionA.CurrentRevision);
                Assert.True(sessionA.IsDirty);
                Assert.Equal(Identities.Resolve(pathA), host.ProjectPaths.IdentityOwnedBy(a.WindowId));
                Assert.Equal(originalA, files.Data[pathA]);
                Assert.Equal(checkpointA, storage.Data[slotA]);
            }
        });
    }

    [Fact]
    public void TheSelectedStartSlotCannotBeTakenByOtherWorkspaceActions()
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            var (host, files, _, source, start, _, _) = OpenTwoProjects();
            Assert.True(Run(() => start.Model!.CloseProjectAsync()));
            Assert.True(start.HoldsStartSlot);
            string mainPath = Project(files, "reserved-main.spandraft"), otherPath = Project(files, "reserved-other.spandraft");
            bool notified = false, slotHeld = false, workspaceBusy = false;
            Task<WorkspaceWindowHandle?>? newDraft = null;
            Task<ProjectOpenResult>? otherOpen = null;
            start.Model!.PropertyChanged += (_, _) =>
            {
                if (notified || !start.IsOpenTargetReserved) return;
                notified = true;
                slotHeld = start.HoldsStartSlot && host.Workspaces.StartSlotCount == 1;
                workspaceBusy = start.Model.IsWorkspaceBusy;
                newDraft = host.Workspaces.RequestNewProjectAsync(source);
                otherOpen = host.Workspaces.RequestOpenProjectAsync(source, otherPath);
            };

            var main = Run(() => host.Workspaces.RequestOpenProjectAsync(source, mainPath));
            Assert.True(notified);
            Assert.True(slotHeld);
            Assert.False(workspaceBusy);
            Assert.Equal(start.WindowId, main.Owner);
            Assert.Equal(mainPath, start.Model.Session!.FilePath);
            Assert.False(start.IsOpenTargetReserved);
            var draft = Run(() => newDraft!);
            var opened = Run(() => otherOpen!);
            Assert.NotSame(start, draft);
            Assert.Equal(WorkspaceCreationReason.NewProject, draft!.Reason);
            Assert.NotEqual(start.WindowId, opened.Owner);
            Assert.Equal(otherPath, host.Workspaces.Find(opened.Owner!.Value)!.Model!.Session!.FilePath);
            Assert.Equal(0, host.Workspaces.StartSlotCount);
        });
    }

    [Fact]
    public void TheOtherWorkspaceCanEditWhileANormalSaveIsWaitingOnDisk()
    {
        using var environment = new DesktopControlEnvironment();
        WithUiContext(() =>
        {
            var (_, files, _, a, b, pathA, pathB) = OpenTwoProjects();
            var modelA = a.Model!;
            var modelB = b.Model!;
            var sessionA = modelA.Session!;
            var sessionB = modelB.Session!;
            EditPointForce(modelA.Editor!, -2000);
            var writtenA = sessionA.CurrentRevision;
            var beforeB = sessionB.CurrentRevision;
            var beforeAnalysisB = modelB.Editor!.Presentation.Result;
            var diskB = files.Data[pathB];
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var write = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            files.BeforeWrite = (path, _, _) =>
            {
                if (path != pathA) return Task.CompletedTask;
                entered.SetResult();
                return write.Task;
            };
            Task<bool> saving = null!;
            DesktopControlEnvironment.RunOnDispatcher(async () =>
            {
                saving = modelA.SaveAsync();
                await WaitForCheckpoint(entered.Task, saving);
            });
            try
            {
                Assert.True(modelA.IsWorkspaceBusy);
                Assert.False(modelB.IsWorkspaceBusy);
                EditPointForce(modelB.Editor, -4500);
                var changedB = sessionB.CurrentRevision;
                Assert.NotSame(beforeB, changedB);
                Assert.NotSame(beforeAnalysisB, modelB.Editor.Presentation.Result);
                Assert.True(sessionB.IsDirty);
                Assert.False(saving.IsCompleted);
                Assert.Equal(diskB, files.Data[pathB]);

                write.SetResult();
                Assert.True(Run(() => saving));
                Assert.False(sessionA.IsDirty);
                Assert.True(writtenA.State.ContentEquals(ProjectFileCodec.Deserialize(files.Data[pathA])));
                Assert.Same(changedB, sessionB.CurrentRevision);
                Assert.True(sessionB.IsDirty);
                Assert.Equal(diskB, files.Data[pathB]);
            }
            finally { write.TrySetResult(); }
        });
    }

    private static (DesktopApplicationHost Host, Files Files, TestRecoveryStorage Recovery,
        WorkspaceWindowHandle A, WorkspaceWindowHandle B, string PathA, string PathB) OpenTwoProjects()
    {
        var files = new Files();
        var storage = new TestRecoveryStorage();
        var host = new DesktopApplicationHost(new DesktopStartupContext(files, new LocalSettingsStore(files, "settings"), UserSettings.Default)
        { ProjectIdentities = Identities, StateStores = DesktopStateStores.Local(files) with { Recovery = storage } });
        host.RecoveryDelay = new Delay().Wait;
        string pathA = Project(files, "acceptance-a.spandraft"), pathB = Project(files, "acceptance-b.spandraft");
        var a = Start(host);
        OpenFromCommand(a, pathA);
        Assert.Equal(pathA, a.Model!.Session!.FilePath);
        Assert.Same(a, Assert.Single(host.Workspaces.Handles));
        OpenFromCommand(a, pathB);
        var b = Assert.Single(host.Workspaces.Handles, handle => handle != a);
        Assert.Null(b.Window.Owner);
        Assert.Equal(pathA, a.Model.Session!.FilePath);
        Assert.Equal(pathB, b.Model!.Session!.FilePath);
        Assert.Equal(0, host.Workspaces.StartSlotCount);
        return (host, files, storage, a, b, pathA, pathB);

        static void OpenFromCommand(WorkspaceWindowHandle source, string path)
        {
            SetPicker(source, path);
            var command = source.Model!.OpenCommand;
            Assert.True(command.CanExecute(null));
            command.Execute(null); Flush();
        }
    }

    private static void EditPointForce(EditorViewModel editor, double newtons)
    {
        var force = Assert.Single(editor.Document.Loads.OfType<EditorPointForce>());
        Assert.True(editor.EditLoad(force.Id));
        editor.LoadDraft!.ValueText = InputQuantityFormatter.FormatExact(newtons, editor.ResultPresentation.Profile[QuantityKind.TransverseForce]);
        Assert.True(editor.ConfirmLoad());
        Assert.True(editor.Presentation.IsSuccess);
    }
}
