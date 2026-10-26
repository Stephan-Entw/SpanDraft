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

[Collection("Schematic text")]
public sealed class ProjectSetupRecoveryTests
{
    private static readonly string Slot = TestPath("setup-recovery.json");
    private static readonly string ProjectSlot = TestPath("project-recovery.json");
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
        public Dialogs Dialogs { get; } = new();
        public Delay Delay { get; } = new(honorCancellation);
        public Delay ProjectDelay { get; } = new();
        public ProjectSetupRecovery Recovery { get; private set; } = null!;
        public ProjectRecovery NormalRecovery { get; private set; } = null!;
        public MainWindowViewModel Main { get; private set; } = null!;
        public void Start(UserSettings? settings = null, Func<string?>? userNameProvider = null)
        {
            Recovery = new(Files, Slot, Delay.Wait, () => Now);
            NormalRecovery = new(Files, ProjectSlot, ProjectDelay.Wait, () => Now);
            Main = new(files: Files, dialogs: Dialogs, recovery: NormalRecovery, setupRecovery: Recovery, materialStore: new(Files, "materials"), settings: settings, userNameProvider: userNameProvider);
        }
        public void Seed(ProjectSetupDraft? draft = null) => Files.Data[Slot] = ProjectSetupRecovery.Encode(new(draft ?? Draft(), Now));
        public async Task Publish()
        { Delay.ReleaseAll(); await WaitFor(Recovery.DrainAsync()); }
        public async Task RestoreBoth(UserSettings? settings = null)
        {
            Files.Data[ProjectSlot] = ProjectRecovery.Encode(new(State(), null, Now));
            Seed(); Start(settings); Assert.True(await Main.InitializeRecoveryAsync());
        }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    public async Task PartialAndCompleteDraftsRoundTripExactValues(int fields)
    {
        var f = new Fixture(); f.Seed(Draft(fields)); f.Start();
        var snapshot = await f.Recovery.ReadAsync();
        Assert.NotNull(snapshot); Assert.Equal(Now, snapshot.WrittenAtUtc);
        Assert.True(Draft(fields).ContentEquals(snapshot.Draft));
        Assert.Null(f.Main.Session);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    [InlineData(5)] [InlineData(6)] [InlineData(7)] [InlineData(8)] [InlineData(9)]
    public async Task AllSectionKindsAndManualAxisConfigurationsUseTheProjectConverter(int kind)
    {
        ISectionDefinition section = kind < 8 ? ParametricSectionTestSupport.Shape(kind, .12345678901234567, .23456789012345678, .005, .01)
            : new ManualSectionDefinition(Area.FromSquareMeters(.0012345678901234567),
                new(SectionAxisDesignation.U, SecondMomentOfArea.FromMetersToTheFourth(1.2345678901234567e-6),
                    SectionModulus.FromCubicMeters(2.3456789012345678e-5)),
                kind == 9 ? new(SectionAxisDesignation.V, SecondMomentOfArea.FromMetersToTheFourth(3.4567890123456789e-6),
                    SectionModulus.FromCubicMeters(4.567890123456789e-5)) : null);
        var draft = new ProjectSetupDraft(Section: section, BendingAxis: section.Axes[^1].AxisDesignation);
        var f = new Fixture(); f.Seed(draft); f.Start();
        var restored = (await f.Recovery.ReadAsync())!.Draft;
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
        f.Files.Data[Slot] = Encoding.UTF8.GetBytes(json.ToJsonString());
        await Assert.ThrowsAsync<ProjectFormatException>(() => f.Recovery.ReadAsync());
    }

    [Theory]
    [InlineData("recoveryVersion")] [InlineData("writtenAtUtc")] [InlineData("draft")] [InlineData("bendingAxis")]
    public async Task RequiredEnvelopeAndAxisFieldsCannotBeMissing(string field)
    {
        var f = new Fixture(); f.Start();
        var json = JsonNode.Parse(ProjectSetupRecovery.Encode(new(Draft(), Now)))!;
        (field == "bendingAxis" ? json["draft"] : json)!.AsObject().Remove(field);
        f.Files.Data[Slot] = Encoding.UTF8.GetBytes(json.ToJsonString());
        await Assert.ThrowsAsync<ProjectFormatException>(() => f.Recovery.ReadAsync());
    }

    [Theory]
    [InlineData("{")] [InlineData("null")] [InlineData("[]")] [InlineData("{}")]
    [InlineData("{\"recoveryVersion\":1,\"recoveryVersion\":1}")]
    public async Task MalformedRootsAreRejected(string json)
    {
        var f = new Fixture(); f.Start(); f.Files.Data[Slot] = Encoding.UTF8.GetBytes(json);
        await Assert.ThrowsAsync<ProjectFormatException>(() => f.Recovery.ReadAsync());
    }

    [Fact]
    public async Task DebouncePublishesOnlyLatestDraftEvenWhenDelayIgnoresCancellation()
    {
        var f = new Fixture(honorCancellation: false); f.Start();
        f.Recovery.Schedule(Draft(1)); var old = f.Recovery.DrainAsync();
        f.Recovery.Schedule(Draft()); Assert.Empty(f.Files.Writes);
        await f.Publish(); await WaitFor(old);
        Assert.Single(f.Files.Writes); Assert.True(Draft().ContentEquals((await f.Recovery.ReadAsync())!.Draft));
    }

    [Fact]
    public async Task DeletionInvalidatesDelayedWritesEvenWhenDelayIgnoresCancellation()
    {
        var f = new Fixture(honorCancellation: false); f.Start(); f.Recovery.Schedule(Draft());
        var pending = f.Recovery.DrainAsync(); await f.Recovery.DeleteAsync();
        f.Delay.ReleaseAll(); await WaitFor(pending);
        Assert.Empty(f.Files.Writes); Assert.Null(await f.Recovery.ReadAsync());
    }

    [Fact]
    public async Task DeletionWaitsForPublishingWriteEvenWhenStoreIgnoresCancellation()
    {
        var f = new Fixture(); f.Start(); f.Files.HonorWriteCancellation = false;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Files.BeforeWrite = async (_, _, _) => { entered.SetResult(); await WaitFor(release.Task); };
        f.Recovery.Schedule(Draft()); f.Delay.ReleaseAll(); var writing = f.Recovery.DrainAsync();
        Task? deleting = null;
        try
        {
            await WaitForCheckpoint(entered.Task, writing);
            deleting = f.Recovery.DeleteAsync(); Assert.False(deleting.IsCompleted);
        }
        finally
        {
            release.TrySetResult(); await WaitFor(writing);
            if (deleting is not null) await WaitFor(deleting);
        }
        Assert.Null(await f.Recovery.ReadAsync());
    }

    [Fact]
    public async Task MissingLocalDirectoryIsCreatedOnlyWhenPublishing()
    {
        string directory = Path.Combine(Path.GetTempPath(), "SpanDraft-setup-" + Guid.NewGuid().ToString("N"));
        var delay = new Delay();
        var recovery = new ProjectSetupRecovery(new ProjectFileStore(), Path.Combine(directory, "setup-recovery.json"),
            delay.Wait, () => Now, () => Directory.CreateDirectory(directory));
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
        var f = new Fixture(); f.Start(); await f.Main.NewAsync(); f.Files.FailWritePath = Slot;
        f.Main.Setup.SelectedMaterial = PreciseMaterial; await f.Publish();
        Assert.Single(f.Dialogs.Errors); Assert.Null(f.Main.Session); Assert.Null(await f.Recovery.ReadAsync());
        f.Files.FailWritePath = null; f.Main.Setup.SelectedSection = ProjectTemplates.Section; await f.Publish();
        Assert.NotNull(await f.Recovery.ReadAsync());
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public async Task RestoreUsesCurrentPresentationWithoutSessionOrNewWrite(int profile)
    {
        using var culture = new UiCultureScope("de-DE");
        var units = profile switch { 0 => UnitProfile.Default, 1 => UnitProfile.StructuralEngineering, _ => UnitProfile.UnitedStates };
        var f = new Fixture(); f.Seed(); f.Start(UserSettings.Default with { Profile = units });
        var bytes = f.Files.Data[Slot].ToArray();
        Assert.True(await f.Main.InitializeRecoveryAsync());
        Assert.Null(f.Main.Session); Assert.Null(f.Main.Editor); Assert.True(f.Main.Setup.IsOverview);
        Assert.True(Draft().ContentEquals(f.Main.Setup.Draft)); Assert.True(f.Main.Setup.CanApply);
        Assert.Equal(InputQuantityFormatter.FormatExact(Draft().Length!.Value.Meters, units[QuantityKind.BeamLength]), f.Main.Setup.LengthInput.Text);
        Assert.Equal("My section", f.Main.Setup.SectionName);
        Assert.Empty(f.Delay.Waiters); Assert.Empty(f.Files.Writes); Assert.Equal(bytes, f.Files.Data[Slot]);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task CancelStopsStartupAndLeavesValidOrDamagedSlotUntouched(bool damaged)
    {
        var f = new Fixture(); f.Seed(); f.Start();
        if (damaged) f.Files.Data[Slot] = Encoding.UTF8.GetBytes("{");
        var bytes = f.Files.Data[Slot].ToArray();
        f.Dialogs.SetupRecovery = f.Dialogs.DamagedSetupRecovery = RecoveryDecision.Cancel;
        Assert.False(await f.Main.InitializeRecoveryAsync()); Assert.Null(f.Main.Session);
        Assert.Equal(bytes, f.Files.Data[Slot]); Assert.Empty(f.Files.Writes); Assert.Empty(f.Delay.Waiters);
        Assert.Equal(damaged ? 1 : 0, f.Dialogs.Errors.Count);
        var fresh = new MainWindowViewModel(files: f.Files, setupRecovery: new(f.Files, Slot));
        Assert.False(await fresh.InitializeRecoveryAsync()); Assert.Equal(bytes, f.Files.Data[Slot]);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task DiscardDeletesOnlySetupSlotAndKeepsRestoredProject(bool damaged)
    {
        var f = new Fixture(); f.Seed(); f.Start();
        f.Files.Data[ProjectSlot] = ProjectRecovery.Encode(new(State(), null, Now));
        if (damaged) f.Files.Data[Slot] = Encoding.UTF8.GetBytes("{");
        f.Dialogs.SetupRecovery = RecoveryDecision.Discard;
        Assert.True(await f.Main.InitializeRecoveryAsync());
        Assert.NotNull(f.Main.Session); Assert.Equal(MainViewMode.Editor, f.Main.Mode);
        Assert.Null(await f.Recovery.ReadAsync()); Assert.NotNull(await f.NormalRecovery.ReadAsync());
        Assert.Equal(new[] { "project", "setup" }, f.Dialogs.RecoveryOrder);
    }

    [Fact]
    public async Task BothSlotsRestoreInOrderAndCancellingDraftReturnsToSameRestoredEditor()
    {
        var f = new Fixture(); await f.RestoreBoth();
        var session = f.Main.Session!; var editor = f.Main.Editor!; var revision = session.CurrentRevision;
        Assert.Equal(MainViewMode.ProjectSetup, f.Main.Mode); Assert.True(f.Main.Setup.IsOverview);
        Assert.Equal(new[] { "project", "setup" }, f.Dialogs.RecoveryOrder);
        Assert.True(State().ContentEquals(revision.State)); Assert.True(session.IsDirty); Assert.Empty(session.UndoHistory);
        Assert.True(Draft().ContentEquals(f.Main.Setup.Draft)); Assert.Empty(f.Delay.Waiters);
        Assert.True(await f.Main.CancelSetupAsync());
        Assert.Equal(MainViewMode.Editor, f.Main.Mode); Assert.Same(editor, f.Main.Editor);
        Assert.Same(session, f.Main.Session); Assert.Same(revision, session.CurrentRevision);
        Assert.Null(await f.Recovery.ReadAsync()); Assert.NotNull(await f.NormalRecovery.ReadAsync());
    }

    [Fact]
    public async Task NewDraftOverAnOpenProjectPublishesItsOwnSlotWithoutChangingTheSessionRecovery()
    {
        var f = new Fixture(); f.Seed(); f.Start(); await f.Main.InitializeRecoveryAsync();
        Assert.True(await f.Main.Setup.ConfirmAsync());
        f.ProjectDelay.ReleaseAll(); await WaitFor(f.NormalRecovery.DrainAsync());
        var session = f.Main.Session; var revision = session!.CurrentRevision;
        var bytes = f.Files.Data[ProjectSlot].ToArray();
        Assert.True(await f.Main.NewAsync()); f.Main.Setup.SelectedMaterial = PreciseMaterial; await f.Publish();
        Assert.Equal(bytes, f.Files.Data[ProjectSlot]); Assert.Same(session, f.Main.Session); Assert.Same(revision, session.CurrentRevision);
        Assert.NotNull(await f.Recovery.ReadAsync()); Assert.NotNull(await f.NormalRecovery.ReadAsync());
        Assert.Equal(0, f.Dialogs.LeaveQuestions); Assert.True(await f.Main.CancelSetupAsync());
        Assert.Equal(bytes, f.Files.Data[ProjectSlot]); Assert.Same(session, f.Main.Session);
    }

    [Fact]
    public async Task CancellingEitherStartupDialogPreservesItsSlotAndStopsTheSequence()
    {
        foreach (bool project in new[] { true, false })
        {
            var f = new Fixture(); f.Seed(); f.Start();
            f.Files.Data[ProjectSlot] = ProjectRecovery.Encode(new(State(), null, Now));
            if (project) f.Dialogs.Recovery = RecoveryDecision.Cancel;
            else f.Dialogs.SetupRecovery = RecoveryDecision.Cancel;
            Assert.False(await f.Main.InitializeRecoveryAsync());
            Assert.Equal(project ? new[] { "project" } : new[] { "project", "setup" }, f.Dialogs.RecoveryOrder);
            Assert.NotNull(await f.Recovery.ReadAsync()); Assert.NotNull(await f.NormalRecovery.ReadAsync());
        }
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
        Assert.Equal(M(1.2345), (await f.Recovery.ReadAsync())!.Draft.Length);
        setup.ShowMaterials(); var materials = Assert.IsType<MaterialSelectionViewModel>(setup.CurrentStep);
        materials.NewCommand.Execute(null); var material = Assert.IsType<MaterialEditorViewModel>(materials.Editor);
        material.Name = "One-off"; material.Inputs[0].Text = "210"; material.Inputs[1].Text = "355"; material.StrengthType = material.StrengthTypes[0];
        int waits = f.Delay.Waiters.Count; Assert.Null(setup.SelectedMaterial);
        await f.Publish(); Assert.Null((await f.Recovery.ReadAsync())!.Draft.Material); Assert.Equal(waits, f.Delay.Waiters.Count);
        Assert.True(await material.ConfirmAsync()); Assert.Equal(waits, f.Delay.Waiters.Count);
        Assert.Null(setup.SelectedMaterial); materials.UseSelected(); Assert.Equal(waits + 1, f.Delay.Waiters.Count);
        setup.ShowSections(); var definition = setup.Definition!; definition.SelectForm(SectionEditorType.Rectangle);
        foreach (var input in definition.Parameters) { input.PendingText = "100"; Assert.True(input.Commit()); }
        definition.BeginNameEdit(); definition.PendingName = "Confirmed name"; Assert.True(definition.CommitName());
        waits = f.Delay.Waiters.Count; await f.Publish(); Assert.Null((await f.Recovery.ReadAsync())!.Draft.Section);
        Assert.Equal(waits, f.Delay.Waiters.Count); Assert.True(await definition.ConfirmAsync()); await f.Publish();
        var draft = (await f.Recovery.ReadAsync())!.Draft;
        Assert.NotNull(draft.Section); Assert.Equal("Confirmed name", draft.SectionDisplayName); Assert.NotNull(draft.Material);
        string json = Encoding.UTF8.GetString(f.Files.Data[Slot]);
        foreach (string key in new[] { "page", "text", "isEditing", "hasError", "currentStep", "project", "presentation" })
            Assert.DoesNotContain("\"" + key + "\"", json);
        Assert.Null(f.Main.Session);
    }

    [Fact]
    public async Task UnchangedConfirmationsNavigationAndPresentationDoNotScheduleWrites()
    {
        var f = new Fixture(); f.Seed(); f.Start(); await f.Main.InitializeRecoveryAsync(); var setup = f.Main.Setup;
        setup.SelectedMaterial = new Material(PreciseMaterial.Name, PreciseMaterial.YoungsModulus, PreciseMaterial.YieldStrength, MaterialStrengthType.Re,
            PreciseMaterial.Density, PreciseMaterial.PoissonRatio);
        setup.SelectedSection = new RectangleSectionGeometry(M(.12345678901234567), M(.23456789012345678));
        setup.BendingAxis = setup.BendingAxis;
        setup.LengthInput.Begin(); Assert.True(setup.LengthInput.Confirm());
        setup.ShowMaterials(); setup.ShowOverview(); setup.ShowSections(); Assert.True(await setup.Definition!.ConfirmAsync());
        Assert.True(f.Main.SetResultPresentation(UnitProfile.UnitedStates, 6));
        Assert.Empty(f.Delay.Waiters); Assert.Empty(f.Files.Writes);
        setup.BendingAxis = SectionAxisDesignation.Y; Assert.Single(f.Delay.Waiters); await f.Publish();
        Assert.Equal(SectionAxisDesignation.Y, (await f.Recovery.ReadAsync())!.Draft.BendingAxis);
    }

    [Fact]
    public async Task ConfirmedNameOnlyChangesAndAutomaticNamesAreRecovered()
    {
        var f = new Fixture(); f.Seed(); f.Start(); await f.Main.InitializeRecoveryAsync();
        f.Main.Setup.ShowSections(); var definition = f.Main.Setup.Definition!;
        definition.BeginNameEdit(); definition.PendingName = "Renamed"; Assert.True(definition.CommitName());
        Assert.Empty(f.Delay.Waiters); Assert.True(await definition.ConfirmAsync()); await f.Publish();
        Assert.Equal("Renamed", (await f.Recovery.ReadAsync())!.Draft.SectionDisplayName);
        f.Main.Setup.ShowSections(); f.Main.Setup.Definition!.ResetName();
        Assert.True(await f.Main.Setup.Definition.ConfirmAsync()); await f.Publish();
        Assert.Null((await f.Recovery.ReadAsync())!.Draft.SectionDisplayName);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task SetupCanBeRestoredOrDiscardedWithoutRestoringAProject(bool discard)
    {
        var f = new Fixture(); f.Seed(Draft(1)); f.Start();
        f.Files.Data[ProjectSlot] = ProjectRecovery.Encode(new(State(), null, Now));
        f.Dialogs.Recovery = RecoveryDecision.Discard;
        f.Dialogs.SetupRecovery = discard ? RecoveryDecision.Discard : RecoveryDecision.Restore;
        Assert.True(await f.Main.InitializeRecoveryAsync()); Assert.Null(f.Main.Session); Assert.Null(f.Main.Editor);
        Assert.Equal(discard ? MainViewMode.Start : MainViewMode.ProjectSetup, f.Main.Mode);
        Assert.Equal(discard, await f.Recovery.ReadAsync() is null); Assert.Null(await f.NormalRecovery.ReadAsync());
        if (!discard)
        {
            Assert.Equal(Draft(1).Length, f.Main.Setup.SelectedLength); Assert.False(f.Main.Setup.CanApply);
            Assert.Null(f.Main.Setup.SelectedMaterial); Assert.Null(f.Main.Setup.SelectedSection);
        }
    }

    [Theory]
    [InlineData("cancel")] [InlineData("escape")] [InlineData("close")] [InlineData("open")]
    public async Task ExplicitAbandonmentDeletesSlotAndInvalidatesAllDraftWrites(string action)
    {
        var f = new Fixture(honorCancellation: false); f.Seed(); f.Start(); await f.Main.InitializeRecoveryAsync();
        var setup = f.Main.Setup; setup.SelectedMaterial = ProjectTemplates.Material; var pending = f.Recovery.DrainAsync();
        switch (action)
        {
            case "cancel": Assert.True(await f.Main.CancelSetupAsync()); break;
            case "escape": setup.Escape(); break;
            case "close": Assert.True(await f.Main.RequestCloseAsync()); break;
            case "open":
                f.Dialogs.OpenPath = TestPath("other.spandraft"); f.Files.Data[f.Dialogs.OpenPath] = ProjectFileCodec.Serialize(State());
                Assert.True(await f.Main.OpenAsync()); break;
        }
        f.Delay.ReleaseAll(); await WaitFor(pending); Assert.Null(await f.Recovery.ReadAsync());
        int waits = f.Delay.Waiters.Count; setup.SelectedMaterial = PreciseMaterial; Assert.Equal(waits, f.Delay.Waiters.Count);
        if (action is "cancel" or "escape") { Assert.Null(f.Main.Session); Assert.Equal(MainViewMode.Start, f.Main.Mode); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task CancelledOrFailedOpenKeepsSetupRecovery(bool failed)
    {
        var f = new Fixture(); f.Seed(); f.Start(); await f.Main.InitializeRecoveryAsync();
        if (failed) { f.Dialogs.OpenPath = TestPath("broken.spandraft"); f.Files.Data[f.Dialogs.OpenPath] = Encoding.UTF8.GetBytes("{"); }
        Assert.False(await f.Main.OpenAsync()); Assert.NotNull(await f.Recovery.ReadAsync());
        Assert.Equal(MainViewMode.ProjectSetup, f.Main.Mode); Assert.Null(f.Main.Session);
    }

    [Theory]
    [InlineData("leaveCancel")] [InlineData("saveCancel")] [InlineData("saveFailure")] [InlineData("leaveCleanupFailure")]
    public async Task IncompleteCreationAndCloseKeepDraftAndPreviousSession(string failure)
    {
        var f = new Fixture(); await f.RestoreBoth(UserSettings.Default with { LastAuthor = "Local author" }); var session = f.Main.Session;
        f.Dialogs.Leave = failure == "leaveCancel" ? LeaveDecision.Cancel : LeaveDecision.Save;
        if (failure == "saveCancel") f.Dialogs.SavePath = null;
        if (failure == "saveFailure") f.Files.FailWritePath = f.Dialogs.SavePath;
        if (failure == "leaveCleanupFailure") { f.Dialogs.Leave = LeaveDecision.Discard; f.Files.FailDelete = true; }
        Assert.False(await f.Main.Setup.ConfirmAsync()); Assert.Same(session, f.Main.Session);
        Assert.Equal("Local author", f.Main.Settings.LastAuthor);
        Assert.Equal(MainViewMode.ProjectSetup, f.Main.Mode); Assert.NotNull(await f.Recovery.ReadAsync());
        Assert.False(await f.Main.RequestCloseAsync()); Assert.NotNull(await f.Recovery.ReadAsync());
    }

    [Fact]
    public async Task SuccessfulCreationHandsOffToNormalRecoveryAndEditDraftsNeverWriteSetupRecovery()
    {
        using var culture = new UiCultureScope("en-US");
        var f = new Fixture(); await f.RestoreBoth(); var previous = f.Main.Session;
        f.Dialogs.Leave = LeaveDecision.Discard; Assert.True(await f.Main.Setup.ConfirmAsync());
        Assert.NotSame(previous, f.Main.Session); Assert.True(f.Main.Session!.IsDirty); Assert.Null(f.Main.Session.FilePath);
        Assert.Empty(f.Main.Session.UndoHistory); Assert.Empty(f.Main.Session.RedoHistory); Assert.Null(await f.Recovery.ReadAsync());
        f.ProjectDelay.ReleaseAll(); await WaitFor(f.NormalRecovery.DrainAsync());
        Assert.True(f.Main.Session.CurrentRevision.State.ContentEquals((await f.NormalRecovery.ReadAsync())!.State));
        f.Main.EditProject(); f.Main.Setup.SelectedMaterial = ProjectTemplates.Material;
        f.Main.Setup.LengthInput.Begin(); f.Main.Setup.LengthInput.Text = "2000"; Assert.True(f.Main.Setup.LengthInput.Confirm());
        Assert.True(await f.Main.Setup.ConfirmAsync()); await f.Publish(); Assert.Null(await f.Recovery.ReadAsync());
    }

    [Fact]
    public async Task SetupCleanupFailureDoesNotUndoSuccessfulCreationOrSave()
    {
        var f = new Fixture(); f.Seed(); f.Start(); await f.Main.InitializeRecoveryAsync();
        f.Files.FailDeletePath = Slot;
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
    public async Task VersionTwoRestoresEveryCombinationOfConfirmedInfoWithoutDefaults(int fields)
    {
        var draft = new ProjectSetupDraft(CalculationTitle: (fields & 1) != 0 ? "Beam 1" : null,
            Author: (fields & 2) != 0 ? "Original author" : null,
            ProjectTitle: (fields & 4) != 0 ? "Project" : null, Reference: (fields & 8) != 0 ? "Drawing" : null);
        var f = new Fixture(); f.Seed(draft); f.Start(UserSettings.Default with { LastAuthor = "Current default" });
        var bytes = f.Files.Data[Slot]; Assert.Equal(2, JsonNode.Parse(bytes)!["recoveryVersion"]!.GetValue<int>());
        Assert.True(await f.Main.InitializeRecoveryAsync()); Assert.True(draft.ContentEquals(f.Main.Setup.Draft));
        Assert.Equal(draft.Author ?? "", f.Main.Setup.AuthorInput.Text); Assert.Empty(f.Delay.Waiters);
        Assert.Equal(bytes, f.Files.Data[Slot]); Assert.Null(f.Main.Session);
        Assert.Equal("Current default", f.Main.Settings.LastAuthor);
    }

    [Theory]
    [InlineData(null)] [InlineData("Recovered author")]
    public async Task CreateRecoveryNeverMergesTheWindowsUserName(string? author)
    {
        var draft = new ProjectSetupDraft(Author: author);
        var f = new Fixture(); f.Seed(draft); f.Start(userNameProvider: () => "windows.user");
        var bytes = f.Files.Data[Slot];
        Assert.True(await f.Main.InitializeRecoveryAsync());
        Assert.True(draft.ContentEquals(f.Main.Setup.Draft));
        Assert.Equal(author ?? "", f.Main.Setup.AuthorInput.Text);
        Assert.Null(f.Main.Settings.LastAuthor); Assert.Equal(bytes, f.Files.Data[Slot]);
        Assert.True(await f.Main.CancelSetupAsync());
        Assert.True(await f.Main.NewAsync()); Assert.Equal("windows.user", f.Main.Setup.AuthorInput.Text);
    }

    [Fact]
    public async Task VersionOneRecoveryRestoresNoInfoAndNeverMergesTheCurrentLastAuthor()
    {
        var f = new Fixture(); f.Seed();
        var json = JsonNode.Parse(f.Files.Data[Slot])!; json["recoveryVersion"] = 1;
        foreach (var key in new[] { "calculationTitle", "projectTitle", "reference", "author" }) json["draft"]!.AsObject().Remove(key);
        f.Files.Data[Slot] = Encoding.UTF8.GetBytes(json.ToJsonString());
        f.Start(UserSettings.Default with { LastAuthor = "Default author" });
        Assert.True(await f.Main.InitializeRecoveryAsync());
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
        Assert.Equal("Beam", (await f.Recovery.ReadAsync())!.Draft.CalculationTitle);
        Assert.Null((await f.Recovery.ReadAsync())!.Draft.Author);
        int waits = f.Delay.Waiters.Count;
        setup.AuthorInput.Begin(); setup.AuthorInput.Text = " "; Assert.False(setup.AuthorInput.Confirm());
        Assert.True(setup.AuthorInput.HasError); Assert.Equal(waits, f.Delay.Waiters.Count);
        setup.AuthorInput.Text = "Author"; setup.AuthorInput.Cancel();
        Assert.Null(setup.Draft.Author); Assert.Empty(setup.AuthorInput.Text); Assert.Equal(waits, f.Delay.Waiters.Count);
        setup.AuthorInput.Text = " Author "; Assert.True(setup.AuthorInput.Confirm()); await f.Publish();
        Assert.Equal("Author", (await f.Recovery.ReadAsync())!.Draft.Author);
        Assert.Null(f.Main.Session);
    }

}
