using System.Text;
using System.Text.Json.Nodes;
using SpanDraft.Core.Materials;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Sections.Parametric;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

public sealed class ProjectSetupRecoveryTests
{
    private static readonly Material PreciseMaterial = new(" Precise steel ",
        Pressure.FromPascals(210123456789.12345), Pressure.FromPascals(355123456.78912345), MaterialStrengthType.Re,
        MassDensity.FromKilogramsPerCubicMeter(7800.123456789), PoissonRatio.FromValue(.31234567890123456));
    private static ProjectSetupDraft Draft(int fields = 7) => new(
        (fields & 1) != 0 ? M(1.0000000000000002) : null,
        (fields & 2) != 0 ? PreciseMaterial : null,
        (fields & 4) != 0 ? new RectangleSectionGeometry(M(.12345678901234567), M(.23456789012345678)) : null,
        SectionAxisDesignation.Z, (fields & 4) != 0 ? "My section" : null,
        CalculationTitle: fields == 7 ? "Calculation" : null, Author: fields == 7 ? "Author" : null);

    private sealed class Fixture(bool honorCancellation = true)
    {
        public Files Files { get; } = new();
        public TestRecoveryStorage Storage { get; } = new();
        public Dialogs Dialogs { get; } = new();
        public Delay Delay { get; } = new(honorCancellation);
        public WorkspaceRecoveryContext Context { get; private set; } = null!;
        public MainWindowViewModel Main { get; private set; } = null!;
        private ProjectSetupRecovery? _writer;
        /// <summary>A standalone draft slot writer for the writer-level tests.</summary>
        public ProjectSetupRecovery Writer => _writer ??= new(Storage, RecoverySlot.Setup(RecoveryId.New()), Delay.Wait, () => Now);
        public RecoverySlot SetupSlot => Storage.OnlySlot(RecoverySlotKind.Setup);
        public RecoverySlot ProjectSlot => Storage.OnlySlot(RecoverySlotKind.Project);
        public ProjectSetupRecovery Reader(RecoverySlot slot) => new(Storage, slot);
        public RecoverySlot PutRaw(byte[] bytes)
        {
            var slot = RecoverySlot.Setup(RecoveryId.New());
            Storage.Put(slot, bytes);
            return slot;
        }
        /// <summary>The decoded content of the only stored draft slot, or null.</summary>
        public SetupRecoverySnapshot? Snapshot() =>
            Storage.Slots(RecoverySlotKind.Setup) is { Count: > 0 } slots ? ProjectSetupRecovery.Decode(Storage.Data[slots.Single()]) : null;
        /// <summary>The decoded content of the only stored project slot, or null.</summary>
        public RecoverySnapshot? ProjectSnapshot() =>
            Storage.Slots(RecoverySlotKind.Project) is { Count: > 0 } slots ? ProjectRecovery.Decode(Storage.Data[slots.Single()]) : null;
        public void Start(UserSettings? settings = null, Func<string?>? userNameProvider = null)
        {
            Context = new(Storage, Delay.Wait, () => Now);
            Main = new(files: Files, projectIdentities: Identities, dialogs: Dialogs, recovery: Context, userNameProvider: userNameProvider, services: TestServices.Create(materialStore: new(Files, "materials"), settings: settings));
        }
        public RecoverySlot Seed(ProjectSetupDraft? draft = null)
        {
            var slot = RecoverySlot.Setup(RecoveryId.New());
            Storage.Put(slot, ProjectSetupRecovery.Encode(new(draft ?? Draft(), Now)));
            return slot;
        }
        /// <summary>Restores the one stored draft slot into the (projectless) workspace like the restore transaction does.</summary>
        public void RestoreDraft(RecoverySlot? slot = null)
        {
            slot ??= SetupSlot;
            Main.RestoreSetup(ProjectSetupRecovery.Decode(Storage.Data[slot.Value]), slot.Value);
        }
        public void RestoreProject(ProjectState? state = null)
        {
            var slot = RecoverySlot.Project(RecoveryId.New());
            var snapshot = new RecoverySnapshot(state ?? State(), null, Now);
            Storage.Put(slot, ProjectRecovery.Encode(snapshot));
            Main.RestoreProject(snapshot, slot, null);
        }
        /// <summary>New over an open, dirty project (restored): the one workspace whose recovery is the project's.</summary>
        public async Task NewOverDirtyProject(UserSettings? settings = null)
        {
            Start(settings); RestoreProject();
            if (!await Main.NewAsync()) throw new InvalidOperationException("New must be possible over the open project.");
            CompleteSetup(Main);
        }
        public async Task Publish()
        { Delay.ReleaseAll(); await WaitFor(Context.DrainAsync()); }
        public async Task PublishWriter()
        { Delay.ReleaseAll(); await WaitFor(Writer.DrainAsync()); }
        public byte[] SetupBytes => Storage.Data[SetupSlot].ToArray();
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    public async Task PartialAndCompleteDraftsRoundTripExactValues(int fields)
    {
        var f = new Fixture(); var slot = f.Seed(Draft(fields)); f.Start();
        var snapshot = await f.Reader(slot).ReadAsync();
        Assert.NotNull(snapshot); Assert.Equal(Now, snapshot.WrittenAtUtc);
        Assert.True(Draft(fields).ContentEquals(snapshot.Draft));
        Assert.Null(f.Main.Session);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    [InlineData(5)] [InlineData(6)] [InlineData(7)] [InlineData(8)] [InlineData(9)]
    public async Task AllSectionKindsAndManualAxisConfigurationsUseTheProjectConverter(int kind)
    {
        ISectionDefinition section = kind < 8 ? SectionTestShapes.Shape(kind, .12345678901234567, .23456789012345678, .005, .01)
            : new ManualSectionDefinition(Area.FromSquareMeters(.0012345678901234567),
                new(SectionAxisDesignation.U, SecondMomentOfArea.FromMetersToTheFourth(1.2345678901234567e-6),
                    SectionModulus.FromCubicMeters(2.3456789012345678e-5)),
                kind == 9 ? new(SectionAxisDesignation.V, SecondMomentOfArea.FromMetersToTheFourth(3.4567890123456789e-6),
                    SectionModulus.FromCubicMeters(4.567890123456789e-5)) : null);
        var draft = new ProjectSetupDraft(Section: section, BendingAxis: section.Axes[^1].AxisDesignation);
        var f = new Fixture(); var slot = f.Seed(draft); f.Start();
        var restored = (await f.Reader(slot).ReadAsync())!.Draft;
        Assert.True(draft.ContentEquals(restored)); Assert.Null(restored.SectionDisplayName);
        Assert.Equal(section.Area, restored.Section!.Area); Assert.Equal(section.Axes, restored.Section.Axes);
    }

    [Theory]
    [InlineData("recoveryVersion", "3")]
    [InlineData("writtenAtUtc", "\"2026-10-07T10:00:00+01:00\"")]
    [InlineData("writtenAtUtc", "\"0001-01-01T00:00:00+00:00\"")]
    [InlineData("draft", "null")]
    [InlineData("draft.length", "0")]
    [InlineData("draft.length", "-1")]
    [InlineData("draft.length", "1e400")]
    [InlineData("draft.bendingAxis", "\"u\"")]
    [InlineData("draft.bendingAxis", "\"unknown\"")]
    [InlineData("draft.material.name", "\" \"")]
    [InlineData("draft.material.youngsModulus", "0")]
    [InlineData("draft.material.density", "-1")]
    [InlineData("draft.material.poissonRatio", "1")]
    [InlineData("draft.section.type", "\"unknown\"")]
    [InlineData("draft.section.width", "-1")]
    [InlineData("draft.sectionDisplayName", "\" \"")]
    [InlineData("draft.sectionDisplayName", "\" untrimmed \"")]
    public async Task InvalidPresentValuesAreRejected(string path, string value)
    {
        var f = new Fixture(); f.Start();
        var json = JsonNode.Parse(ProjectSetupRecovery.Encode(new(Draft(), Now)))!;
        var parts = path.Split('.'); var parent = parts[..^1].Aggregate(json, (node, key) => node[key]!);
        parent[parts[^1]] = JsonNode.Parse(value);
        var slot = f.PutRaw(Encoding.UTF8.GetBytes(json.ToJsonString()));
        await Assert.ThrowsAsync<ProjectFormatException>(() => f.Reader(slot).ReadAsync());
    }

    [Theory]
    [InlineData("recoveryVersion")] [InlineData("writtenAtUtc")] [InlineData("draft")] [InlineData("bendingAxis")]
    public async Task RequiredEnvelopeAndAxisFieldsCannotBeMissing(string field)
    {
        var f = new Fixture(); f.Start();
        var json = JsonNode.Parse(ProjectSetupRecovery.Encode(new(Draft(), Now)))!;
        (field == "bendingAxis" ? json["draft"] : json)!.AsObject().Remove(field);
        var slot = f.PutRaw(Encoding.UTF8.GetBytes(json.ToJsonString()));
        await Assert.ThrowsAsync<ProjectFormatException>(() => f.Reader(slot).ReadAsync());
    }

    [Theory]
    [InlineData("{")] [InlineData("null")] [InlineData("[]")] [InlineData("{}")]
    [InlineData("{\"recoveryVersion\":1,\"recoveryVersion\":1}")]
    public async Task MalformedRootsAreRejected(string json)
    {
        var f = new Fixture(); f.Start(); var slot = f.PutRaw(Encoding.UTF8.GetBytes(json));
        await Assert.ThrowsAsync<ProjectFormatException>(() => f.Reader(slot).ReadAsync());
    }

    [Fact]
    public async Task DebouncePublishesOnlyLatestDraftEvenWhenDelayIgnoresCancellation()
    {
        var f = new Fixture(honorCancellation: false); f.Start();
        f.Writer.Schedule(Draft(1)); var old = f.Writer.DrainAsync();
        f.Writer.Schedule(Draft()); Assert.Empty(f.Storage.Writes);
        await f.PublishWriter(); await WaitFor(old);
        Assert.Single(f.Storage.Writes); Assert.True(Draft().ContentEquals((await f.Writer.ReadAsync())!.Draft));
    }

    [Fact]
    public async Task DeletionInvalidatesDelayedWritesEvenWhenDelayIgnoresCancellation()
    {
        var f = new Fixture(honorCancellation: false); f.Start(); f.Writer.Schedule(Draft());
        var pending = f.Writer.DrainAsync(); await f.Writer.DeleteAsync();
        f.Delay.ReleaseAll(); await WaitFor(pending);
        Assert.Empty(f.Storage.Writes); Assert.Null(await f.Writer.ReadAsync());
    }

    [Fact]
    public async Task DeletionWaitsForPublishingWriteEvenWhenStoreIgnoresCancellation()
    {
        var f = new Fixture(); f.Start(); f.Storage.HonorWriteCancellation = false;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Storage.BeforeWrite = async (_, _, _) => { entered.SetResult(); await WaitFor(release.Task); };
        f.Writer.Schedule(Draft()); f.Delay.ReleaseAll(); var writing = f.Writer.DrainAsync();
        Task? deleting = null;
        try
        {
            await WaitForCheckpoint(entered.Task, writing);
            deleting = f.Writer.DeleteAsync(); Assert.False(deleting.IsCompleted);
        }
        finally
        {
            release.TrySetResult(); await WaitFor(writing);
            if (deleting is not null) await WaitFor(deleting);
        }
        Assert.Null(await f.Writer.ReadAsync());
    }

    [Fact]
    public async Task MissingLocalDirectoryIsCreatedOnlyWhenPublishing()
    {
        string directory = Path.Combine(Path.GetTempPath(), "SpanDraft-setup-" + Guid.NewGuid().ToString("N"));
        var delay = new Delay();
        var recovery = new ProjectSetupRecovery(new FileRecoveryStorage(directory), RecoverySlot.Setup(RecoveryId.New()), delay.Wait, () => Now);
        try
        {
            await recovery.DeleteAsync(); Assert.Null(await recovery.ReadAsync()); Assert.False(Directory.Exists(directory));
            recovery.Schedule(Draft()); Assert.False(Directory.Exists(directory));
            delay.ReleaseAll(); await WaitFor(recovery.DrainAsync());
            Assert.True(Draft().ContentEquals((await recovery.ReadAsync())!.Draft));
        }
        finally
        {
            delay.ReleaseAll(); await WaitFor(recovery.DrainAsync());
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task FailedWriteIsReportedAndNextConfirmedChangeRetries()
    {
        var f = new Fixture(); f.Start(); await f.Main.NewAsync(); f.Storage.FailWrite = _ => true;
        f.Main.Setup.SelectedMaterial = PreciseMaterial; await f.Publish();
        Assert.Single(f.Dialogs.Errors); Assert.Null(f.Main.Session); Assert.Null(f.Snapshot());
        f.Storage.FailWrite = null; f.Main.Setup.SelectedSection = ProjectTemplates.Section; await f.Publish();
        Assert.NotNull(f.Snapshot());
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void RestoreUsesCurrentPresentationWithoutSessionOrNewWrite(int profile)
    {
        using var culture = new UiCultureScope("de-DE");
        var units = profile switch { 0 => UnitProfile.Default, 1 => UnitProfile.StructuralEngineering, _ => UnitProfile.UnitedStates };
        var f = new Fixture(); var slot = f.Seed(); f.Start(UserSettings.Default with { Profile = units });
        var bytes = f.Storage.Data[slot].ToArray();
        f.RestoreDraft();
        Assert.Null(f.Main.Session); Assert.Null(f.Main.Editor); Assert.True(f.Main.Setup.IsOverview);
        Assert.True(Draft().ContentEquals(f.Main.Setup.Draft)); Assert.True(f.Main.Setup.CanApply);
        Assert.Equal(InputQuantityFormatter.FormatExact(Draft().Length!.Value.Meters, units[QuantityKind.BeamLength]), f.Main.Setup.LengthInput.Text);
        Assert.Equal("My section", f.Main.Setup.SectionName);
        Assert.Empty(f.Delay.Waiters); Assert.Empty(f.Storage.Writes); Assert.Equal(bytes, f.Storage.Data[slot]);
        Assert.Equal(slot.Id, f.Context.SetupRecoveryId);
    }

    [Fact]
    public async Task NewOverADirtyProjectKeepsTheProjectAsTheOnlyRecoveryAndCancelReturnsToTheSameEditor()
    {
        var f = new Fixture(); await f.NewOverDirtyProject(); await f.Publish();
        var session = f.Main.Session!; var editor = f.Main.Editor!; var revision = session.CurrentRevision;
        Assert.Equal(MainViewMode.ProjectSetup, f.Main.Mode); Assert.True(f.Main.Setup.IsOverview);
        Assert.True(State().ContentEquals(revision.State)); Assert.True(session.IsDirty); Assert.Empty(session.UndoHistory);
        // One recovery per workspace: the confirmed draft values are not written next to the unsaved project.
        Assert.Equal(WorkspaceRecoveryState.ProjectRecovery, f.Context.State);
        Assert.Null(f.Context.SetupRecoveryId); Assert.Null(f.Snapshot()); Assert.Single(f.Storage.Slots());
        var projectBytes = f.Storage.Data[f.ProjectSlot].ToArray();
        Assert.True(await f.Main.CancelSetupAsync());
        Assert.Equal(MainViewMode.Editor, f.Main.Mode); Assert.Same(editor, f.Main.Editor);
        Assert.Same(session, f.Main.Session); Assert.Same(revision, session.CurrentRevision);
        Assert.Null(f.Snapshot()); Assert.Equal(projectBytes, f.Storage.Data[f.ProjectSlot]);
        Assert.Equal(WorkspaceRecoveryState.ProjectRecovery, f.Context.State);
    }

    [Fact]
    public async Task NewOverADirtyProjectFlushesItsCurrentStateWritesNoDraftSlotAndCancelKeepsTheSameProjectRecovery()
    {
        using var culture = new UiCultureScope("en-US");
        var f = new Fixture(); f.Start(); f.RestoreProject();
        var projectSlot = f.ProjectSlot; var id = f.Context.ProjectRecoveryId;
        // A committed change whose debounced write has not happened yet.
        var length = f.Main.Editor!.DimensionLength; length.Begin(); length.Text = "2000"; Assert.True(length.Confirm());
        var current = f.Main.Session!.CurrentRevision.State;
        Assert.False(current.ContentEquals(f.ProjectSnapshot()!.State));

        Assert.True(await f.Main.NewAsync());
        // The switch to CreateSetup protected the current state; the draft gets no slot of its own.
        Assert.Equal(MainViewMode.ProjectSetup, f.Main.Mode);
        Assert.Equal(projectSlot, Assert.Single(f.Storage.Slots())); Assert.Null(f.Snapshot());
        Assert.True(current.ContentEquals(f.ProjectSnapshot()!.State));
        CompleteSetup(f.Main); await f.Publish();
        Assert.Equal(projectSlot, Assert.Single(f.Storage.Slots())); Assert.Null(f.Snapshot());
        Assert.True(current.ContentEquals(f.ProjectSnapshot()!.State));

        Assert.True(await f.Main.CancelSetupAsync());
        Assert.Equal(MainViewMode.Editor, f.Main.Mode);
        Assert.Equal(WorkspaceRecoveryState.ProjectRecovery, f.Context.State);
        Assert.Equal(id, f.Context.ProjectRecoveryId); Assert.Equal(projectSlot, Assert.Single(f.Storage.Slots()));
        Assert.True(current.ContentEquals(f.ProjectSnapshot()!.State)); Assert.Null(f.Snapshot());
        // Later changes continue in the very same slot.
        length.Begin(); length.Text = "3000"; Assert.True(length.Confirm()); await f.Publish();
        Assert.Equal(projectSlot, Assert.Single(f.Storage.Slots()));
        Assert.True(f.Main.Session.CurrentRevision.State.ContentEquals(f.ProjectSnapshot()!.State));
    }

    [Fact]
    public async Task NewOverADirtyProjectDoesNotStartWhenItsRecoveryCannotBeFlushed()
    {
        using var culture = new UiCultureScope("en-US");
        var f = new Fixture(); f.Start(); f.RestoreProject();
        var length = f.Main.Editor!.DimensionLength; length.Begin(); length.Text = "2000"; Assert.True(length.Confirm());
        f.Storage.FailWrite = slot => slot.Kind == RecoverySlotKind.Project;
        Assert.False(await f.Main.NewAsync());
        Assert.Equal(MainViewMode.Editor, f.Main.Mode); Assert.Single(f.Dialogs.Errors);
        f.Storage.FailWrite = null;
        Assert.True(await f.Main.NewAsync());
        Assert.True(f.Main.Session!.CurrentRevision.State.ContentEquals(f.ProjectSnapshot()!.State)); Assert.Null(f.Snapshot());
    }

    [Fact]
    public async Task RestoringADraftIsRefusedByAWorkspaceThatHoldsAProjectOrAnOpenSetup()
    {
        // A Create draft has no session: only a projectless workspace in Start mode can take it.
        var f = new Fixture(); f.Start(); f.RestoreProject();
        var draft = f.Seed(); var snapshot = ProjectSetupRecovery.Decode(f.Storage.Data[draft]);
        Assert.Throws<InvalidOperationException>(() => f.Main.RestoreSetup(snapshot, draft));
        Assert.Equal(MainViewMode.Editor, f.Main.Mode); Assert.Null(f.Context.SetupRecoveryId);

        var g = new Fixture(); g.Start(); Assert.True(await g.Main.NewAsync());
        Assert.Throws<InvalidOperationException>(() => g.Main.RestoreSetup(snapshot, draft));
        Assert.Null(g.Context.SetupRecoveryId);
    }

    [Fact]
    public async Task NewOverACleanProjectOwnsADraftSlotAndNeverATwoSlotWorkspace()
    {
        var f = new Fixture(); f.Seed(); f.Start(); f.RestoreDraft();
        var setupId = f.Context.SetupRecoveryId;
        Assert.True(await f.Main.Setup.ConfirmAsync());
        Assert.Equal(setupId, f.Context.ProjectRecoveryId); Assert.Null(f.Snapshot());
        Assert.True(await f.Main.SaveAsync());
        Assert.Equal(WorkspaceRecoveryState.None, f.Context.State); Assert.Empty(f.Storage.Slots());
        var session = f.Main.Session; var revision = session!.CurrentRevision;
        Assert.True(await f.Main.NewAsync()); f.Main.Setup.SelectedMaterial = PreciseMaterial; await f.Publish();
        // The saved project has no recovery, so the draft is the workspace's only one.
        Assert.Equal(WorkspaceRecoveryState.CreateDraftRecovery, f.Context.State);
        Assert.Single(f.Storage.Slots()); Assert.NotNull(f.Snapshot()); Assert.Null(f.ProjectSnapshot());
        Assert.Same(session, f.Main.Session); Assert.Same(revision, session.CurrentRevision);
        Assert.Equal(0, f.Dialogs.LeaveQuestions); Assert.True(await f.Main.CancelSetupAsync());
        Assert.Empty(f.Storage.Slots()); Assert.Equal(WorkspaceRecoveryState.None, f.Context.State); Assert.Same(session, f.Main.Session);
    }

    [Fact]
    public async Task OnlyConfirmedLengthMaterialAndSectionValuesScheduleRecovery()
    {
        using var culture = new UiCultureScope("en-US");
        var f = new Fixture(); f.Start(); Assert.True(await f.Main.NewAsync()); var setup = f.Main.Setup;
        setup.LengthInput.Begin(); setup.LengthInput.Text = "1234.5";
        Assert.Empty(f.Delay.Waiters); Assert.Null(setup.SelectedLength);
        setup.LengthInput.Text = "invalid"; Assert.False(setup.LengthInput.Confirm()); Assert.Empty(f.Delay.Waiters);
        setup.LengthInput.Text = "1234.5"; Assert.True(setup.LengthInput.Confirm()); await f.Publish();
        Assert.Equal(M(1.2345), f.Snapshot()!.Draft.Length);
        setup.ShowMaterials(); var materials = Assert.IsType<MaterialSelectionViewModel>(setup.CurrentStep);
        materials.NewCommand.Execute(null); var material = Assert.IsType<MaterialEditorViewModel>(materials.Editor);
        material.Name = "One-off"; material.Inputs[0].Text = "210"; material.Inputs[1].Text = "355"; material.StrengthType = material.StrengthTypes[0];
        int waits = f.Delay.Waiters.Count; Assert.Null(setup.SelectedMaterial);
        await f.Publish(); Assert.Null(f.Snapshot()!.Draft.Material); Assert.Equal(waits, f.Delay.Waiters.Count);
        Assert.True(await material.ConfirmAsync()); Assert.Equal(waits, f.Delay.Waiters.Count);
        Assert.Null(setup.SelectedMaterial); materials.UseSelected(); Assert.Equal(waits + 1, f.Delay.Waiters.Count);
        setup.ShowSections(); var definition = setup.Definition!; definition.SelectForm(SectionEditorType.Rectangle);
        foreach (var input in definition.Parameters) { input.PendingText = "100"; Assert.True(input.Commit()); }
        definition.BeginNameEdit(); definition.PendingName = "Confirmed name"; Assert.True(definition.CommitName());
        waits = f.Delay.Waiters.Count; await f.Publish(); Assert.Null(f.Snapshot()!.Draft.Section);
        Assert.Equal(waits, f.Delay.Waiters.Count); Assert.True(await definition.ConfirmAsync()); await f.Publish();
        var draft = f.Snapshot()!.Draft;
        Assert.NotNull(draft.Section); Assert.Equal("Confirmed name", draft.SectionDisplayName); Assert.NotNull(draft.Material);
        string json = Encoding.UTF8.GetString(f.SetupBytes);
        foreach (string key in new[] { "page", "text", "isEditing", "hasError", "currentStep", "project", "presentation" })
            Assert.DoesNotContain("\"" + key + "\"", json);
        Assert.Null(f.Main.Session);
    }

    [Fact]
    public async Task UnchangedConfirmationsNavigationAndPresentationDoNotScheduleWrites()
    {
        var f = new Fixture(); f.Seed(); f.Start(); f.RestoreDraft(); var setup = f.Main.Setup;
        setup.SelectedMaterial = new Material(PreciseMaterial.Name, PreciseMaterial.YoungsModulus, PreciseMaterial.YieldStrength, MaterialStrengthType.Re,
            PreciseMaterial.Density, PreciseMaterial.PoissonRatio);
        setup.SelectedSection = new RectangleSectionGeometry(M(.12345678901234567), M(.23456789012345678));
        setup.BendingAxis = setup.BendingAxis;
        setup.LengthInput.Begin(); Assert.True(setup.LengthInput.Confirm());
        setup.ShowMaterials(); setup.ShowOverview(); setup.ShowSections(); Assert.True(await setup.Definition!.ConfirmAsync());
        Assert.True(f.Main.SetResultPresentation(UnitProfile.UnitedStates, 5));
        Assert.Empty(f.Delay.Waiters); Assert.Empty(f.Storage.Writes);
        setup.BendingAxis = SectionAxisDesignation.Y; Assert.Single(f.Delay.Waiters); await f.Publish();
        Assert.Equal(SectionAxisDesignation.Y, f.Snapshot()!.Draft.BendingAxis);
    }

    [Fact]
    public async Task ConfirmedNameOnlyChangesAndAutomaticNamesAreRecovered()
    {
        var f = new Fixture(); f.Seed(); f.Start(); f.RestoreDraft();
        f.Main.Setup.ShowSections(); var definition = f.Main.Setup.Definition!;
        definition.BeginNameEdit(); definition.PendingName = "Renamed"; Assert.True(definition.CommitName());
        Assert.Empty(f.Delay.Waiters); Assert.True(await definition.ConfirmAsync()); await f.Publish();
        Assert.Equal("Renamed", f.Snapshot()!.Draft.SectionDisplayName);
        f.Main.Setup.ShowSections(); f.Main.Setup.Definition!.ResetName();
        Assert.True(await f.Main.Setup.Definition.ConfirmAsync()); await f.Publish();
        Assert.Null(f.Snapshot()!.Draft.SectionDisplayName);
    }

    [Fact]
    public void ADraftRecoveryRestoresWithoutRestoringAProject()
    {
        var f = new Fixture(); f.Seed(Draft(1)); f.Start(); f.RestoreDraft();
        Assert.Null(f.Main.Session); Assert.Null(f.Main.Editor); Assert.Equal(MainViewMode.ProjectSetup, f.Main.Mode);
        Assert.Equal(Draft(1).Length, f.Main.Setup.SelectedLength); Assert.False(f.Main.Setup.CanApply);
        Assert.Null(f.Main.Setup.SelectedMaterial); Assert.Null(f.Main.Setup.SelectedSection);
        Assert.Equal(WorkspaceRecoveryState.CreateDraftRecovery, f.Context.State);
    }

    [Theory]
    [InlineData("cancel")] [InlineData("escape")] [InlineData("close")] [InlineData("open")]
    public async Task ExplicitAbandonmentDeletesSlotAndInvalidatesAllDraftWrites(string action)
    {
        var f = new Fixture(honorCancellation: false); f.Seed(); f.Start(); f.RestoreDraft();
        var setup = f.Main.Setup; setup.SelectedMaterial = ProjectTemplates.Material; var pending = f.Context.DrainAsync();
        switch (action)
        {
            case "cancel": Assert.True(await f.Main.CancelSetupAsync()); break;
            case "escape": setup.Escape(); break;
            case "close": Assert.True(await f.Main.RequestCloseAsync()); break;
            case "open":
                f.Dialogs.OpenPath = TestPath("other.spandraft"); f.Files.Data[f.Dialogs.OpenPath] = ProjectFileCodec.Serialize(State());
                Assert.True(await f.Main.OpenAsync()); break;
        }
        f.Delay.ReleaseAll(); await WaitFor(pending); Assert.Null(f.Snapshot());
        int waits = f.Delay.Waiters.Count; setup.SelectedMaterial = PreciseMaterial; Assert.Equal(waits, f.Delay.Waiters.Count);
        if (action is "cancel" or "escape") { Assert.Null(f.Main.Session); Assert.Equal(MainViewMode.Start, f.Main.Mode); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task CancelledOrFailedOpenKeepsSetupRecovery(bool failed)
    {
        var f = new Fixture(); f.Seed(); f.Start(); f.RestoreDraft();
        if (failed) { f.Dialogs.OpenPath = TestPath("broken.spandraft"); f.Files.Data[f.Dialogs.OpenPath] = Encoding.UTF8.GetBytes("{"); }
        Assert.False(await f.Main.OpenAsync()); Assert.NotNull(f.Snapshot());
        Assert.Equal(MainViewMode.ProjectSetup, f.Main.Mode); Assert.Null(f.Main.Session);
    }

    [Theory]
    [InlineData("leaveCancel")] [InlineData("saveCancel")] [InlineData("saveFailure")] [InlineData("leaveCleanupFailure")]
    public async Task IncompleteCreationAndCloseKeepThePreviousSessionAndItsRecovery(string failure)
    {
        var f = new Fixture(); await f.NewOverDirtyProject(UserSettings.Default with { LastAuthor = "Local author" }); var session = f.Main.Session;
        f.Dialogs.Leave = failure == "leaveCancel" ? LeaveDecision.Cancel : LeaveDecision.Save;
        if (failure == "saveCancel") f.Dialogs.SavePath = null;
        if (failure == "saveFailure") f.Files.FailWritePath = f.Dialogs.SavePath;
        if (failure == "leaveCleanupFailure") { f.Dialogs.Leave = LeaveDecision.Discard; f.Storage.FailDelete = _ => true; }
        Assert.False(await f.Main.Setup.ConfirmAsync()); Assert.Same(session, f.Main.Session);
        Assert.Equal("Local author", f.Main.Settings.LastAuthor);
        // The previous project keeps its recovery as the workspace's only one; the draft was never written.
        Assert.Equal(MainViewMode.ProjectSetup, f.Main.Mode); Assert.NotNull(f.ProjectSnapshot()); Assert.Null(f.Snapshot());
        Assert.False(await f.Main.RequestCloseAsync()); Assert.NotNull(f.ProjectSnapshot()); Assert.Null(f.Snapshot());
    }

    [Fact]
    public async Task SuccessfulCreationHandsOffToProjectRecoveryAndEditDraftsNeverWriteSetupRecovery()
    {
        using var culture = new UiCultureScope("en-US");
        var f = new Fixture(); await f.NewOverDirtyProject(); var previous = f.Main.Session; var previousId = f.Context.ProjectRecoveryId;
        f.Dialogs.Leave = LeaveDecision.Discard; Assert.True(await f.Main.Setup.ConfirmAsync());
        Assert.NotSame(previous, f.Main.Session); Assert.True(f.Main.Session!.IsDirty); Assert.Null(f.Main.Session.FilePath);
        Assert.Empty(f.Main.Session.UndoHistory); Assert.Empty(f.Main.Session.RedoHistory); Assert.Null(f.Snapshot());
        // The discarded project's slot is gone; the new project owns a slot of its own.
        Assert.NotEqual(previousId, f.Context.ProjectRecoveryId); Assert.Equal(RecoverySlot.Project(f.Context.ProjectRecoveryId!.Value), f.ProjectSlot);
        Assert.True(f.Main.Session.CurrentRevision.State.ContentEquals(f.ProjectSnapshot()!.State));
        f.Main.EditProject(); f.Main.Setup.SelectedMaterial = ProjectTemplates.Material;
        f.Main.Setup.LengthInput.Begin(); f.Main.Setup.LengthInput.Text = "2000"; Assert.True(f.Main.Setup.LengthInput.Confirm());
        Assert.True(await f.Main.Setup.ConfirmAsync()); await f.Publish(); Assert.Null(f.Snapshot());
    }

    [Fact]
    public async Task SetupCleanupFailureDoesNotUndoSuccessfulCreationOrSave()
    {
        var f = new Fixture(); f.Seed(); f.Start(); f.RestoreDraft();
        f.Storage.FailDelete = slot => slot.Kind == RecoverySlotKind.Setup;
        Assert.True(await f.Main.Setup.ConfirmAsync());
        Assert.NotNull(f.Main.Session); Assert.Equal(MainViewMode.Editor, f.Main.Mode); Assert.True(f.Main.Session.IsDirty);
        Assert.Single(f.Dialogs.Errors); Assert.True(await f.Main.SaveAsync()); Assert.False(f.Main.Session.IsDirty);
        Assert.NotNull(f.Main.Session.SavedRevisionId); Assert.NotNull(f.Main.Session.FilePath);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    [InlineData(8)] [InlineData(9)] [InlineData(10)] [InlineData(11)]
    [InlineData(12)] [InlineData(13)] [InlineData(14)] [InlineData(15)]
    public void VersionTwoRestoresEveryCombinationOfConfirmedInfoWithoutDefaults(int fields)
    {
        var draft = new ProjectSetupDraft(CalculationTitle: (fields & 1) != 0 ? "Beam 1" : null,
            Author: (fields & 2) != 0 ? "Original author" : null,
            ProjectTitle: (fields & 4) != 0 ? "Project" : null, Reference: (fields & 8) != 0 ? "Drawing" : null);
        var f = new Fixture(); var slot = f.Seed(draft); f.Start(UserSettings.Default with { LastAuthor = "Current default" });
        var bytes = f.Storage.Data[slot]; Assert.Equal(2, JsonNode.Parse(bytes)!["recoveryVersion"]!.GetValue<int>());
        f.RestoreDraft(); Assert.True(draft.ContentEquals(f.Main.Setup.Draft));
        Assert.Equal(draft.Author ?? "", f.Main.Setup.AuthorInput.Text); Assert.Empty(f.Delay.Waiters);
        Assert.Equal(bytes, f.Storage.Data[slot]); Assert.Null(f.Main.Session);
        Assert.Equal("Current default", f.Main.Settings.LastAuthor);
    }

    [Theory]
    [InlineData(null)] [InlineData("Recovered author")]
    public async Task CreateRecoveryNeverMergesTheWindowsUserName(string? author)
    {
        var draft = new ProjectSetupDraft(Author: author);
        var f = new Fixture(); var slot = f.Seed(draft); f.Start(userNameProvider: () => "windows.user");
        var bytes = f.Storage.Data[slot];
        f.RestoreDraft();
        Assert.True(draft.ContentEquals(f.Main.Setup.Draft));
        Assert.Equal(author ?? "", f.Main.Setup.AuthorInput.Text);
        Assert.Null(f.Main.Settings.LastAuthor); Assert.Equal(bytes, f.Storage.Data[slot]);
        Assert.True(await f.Main.CancelSetupAsync());
        Assert.True(await f.Main.NewAsync()); Assert.Equal("windows.user", f.Main.Setup.AuthorInput.Text);
    }

    [Fact]
    public void VersionOneRecoveryRestoresNoInfoAndNeverMergesTheCurrentLastAuthor()
    {
        var f = new Fixture(); var slot = f.Seed();
        var json = JsonNode.Parse(f.Storage.Data[slot])!; json["recoveryVersion"] = 1;
        foreach (var key in new[] { "calculationTitle", "projectTitle", "reference", "author" }) json["draft"]!.AsObject().Remove(key);
        f.Storage.Put(slot, Encoding.UTF8.GetBytes(json.ToJsonString()));
        f.Start(UserSettings.Default with { LastAuthor = "Default author" });
        f.RestoreDraft();
        var draft = f.Main.Setup.Draft;
        Assert.Null(draft.CalculationTitle); Assert.Null(draft.ProjectTitle); Assert.Null(draft.Reference); Assert.Null(draft.Author);
        Assert.False(f.Main.Setup.CanApply); Assert.Empty(f.Main.Setup.AuthorInput.Text);
        Assert.Equal(Draft().Length, draft.Length); Assert.Empty(f.Delay.Waiters);
    }

    [Fact]
    public async Task OnlyConfirmedInfoSchedulesRecoveryAndMenuAndSettingsPreserveBuffers()
    {
        var f = new Fixture(); f.Start(); Assert.True(await f.Main.NewAsync()); var setup = f.Main.Setup;
        setup.CalculationTitleInput.Begin(); setup.CalculationTitleInput.Text = " Beam ";
        Assert.Null(setup.Draft.CalculationTitle); Assert.Empty(f.Delay.Waiters);
        f.Main.SetMainMenuOpen(true); setup.CalculationTitleInput.LoseFocus();
        Assert.Null(setup.Draft.CalculationTitle); Assert.Equal(" Beam ", setup.CalculationTitleInput.Text);
        f.Main.SetMainMenuOpen(false); f.Main.SetSettingsDialogOpen(true); setup.CalculationTitleInput.LoseFocus();
        Assert.Null(setup.Draft.CalculationTitle); Assert.Empty(f.Delay.Waiters);
        f.Main.SetSettingsDialogOpen(false); setup.CalculationTitleInput.LoseFocus(); await f.Publish();
        Assert.Equal("Beam", f.Snapshot()!.Draft.CalculationTitle);
        Assert.Null(f.Snapshot()!.Draft.Author);
        int waits = f.Delay.Waiters.Count;
        setup.AuthorInput.Begin(); setup.AuthorInput.Text = " "; Assert.True(setup.AuthorInput.Confirm());
        Assert.False(setup.AuthorInput.HasError); Assert.Equal(waits, f.Delay.Waiters.Count);
        setup.AuthorInput.Text = "Author"; setup.AuthorInput.Cancel();
        Assert.Null(setup.Draft.Author); Assert.Empty(setup.AuthorInput.Text); Assert.Equal(waits, f.Delay.Waiters.Count);
        setup.AuthorInput.Text = " Author "; Assert.True(setup.AuthorInput.Confirm()); await f.Publish();
        Assert.Equal("Author", f.Snapshot()!.Draft.Author);
        setup.AuthorInput.Text = " \t "; Assert.True(setup.AuthorInput.Confirm()); await f.Publish();
        Assert.Null(f.Snapshot()!.Draft.Author);
        Assert.Equal(2, JsonNode.Parse(f.SetupBytes)!["recoveryVersion"]!.GetValue<int>());
        var bytes = f.SetupBytes; f.Start(UserSettings.Default with { LastAuthor = "Other author" }, () => "windows.user");
        f.RestoreDraft();
        Assert.Empty(f.Main.Setup.AuthorInput.Text); Assert.Equal("Beam", f.Main.Setup.CalculationTitleInput.Text);
        Assert.Equal(bytes, f.SetupBytes); Assert.Equal("Other author", f.Main.Settings.LastAuthor);
        Assert.Null(f.Main.Session);
    }

    [Fact]
    public async Task RecoveredCompleteDraftWithoutAuthorCanBeCreatedAndClearsLastAuthor()
    {
        var f = new Fixture(); f.Seed(Draft() with { Author = null });
        f.Start(UserSettings.Default with { LastAuthor = "Local author" }, () => "windows.user");
        f.RestoreDraft();
        Assert.Empty(f.Main.Setup.AuthorInput.Text); Assert.True(f.Main.Setup.CanApply);
        Assert.Equal("Local author", f.Main.Settings.LastAuthor);
        Assert.True(await f.Main.Setup.ConfirmAsync());
        Assert.Equal(new ProjectInfo("Calculation", null, null, null), f.Main.Session!.CurrentRevision.State.Info);
        Assert.Null(f.Main.Settings.LastAuthor); Assert.Null(f.Snapshot());
        Assert.True(f.Main.Session.CurrentRevision.State.ContentEquals(f.ProjectSnapshot()!.State));
    }
}
