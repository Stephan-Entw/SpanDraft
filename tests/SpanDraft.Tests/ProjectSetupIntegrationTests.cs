using System.Text;
using System.Text.Json.Nodes;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Sections.Parametric;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Libraries;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.Sections;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

[Collection("Schematic text")]
public sealed class ProjectSetupIntegrationTests
{
    [Theory]
    [InlineData("de-DE", "1234,5")] [InlineData("en-US", "1234.5")]
    public async Task LengthDefaultsAndConfirmedInputOnlyChangeTheDraft(string culture, string text)
    {
        using var scope = new UiCultureScope(culture);
        var app = new App(create: false); var setup = app.Main.Setup;
        Assert.Equal(1000, setup.SelectedLength.Millimeters); Assert.Equal("1000", setup.LengthInput.Text);
        Assert.False(setup.CanCancel);
        setup.LengthInput.Begin(); setup.LengthInput.Text = text;
        Assert.True(setup.CanApply); Assert.True(setup.LengthInput.Confirm());
        Assert.Equal(1234.5, setup.SelectedLength.Millimeters); Assert.Null(app.Main.Session); Assert.Equal(0, app.Analyses);
        Assert.True(await setup.ConfirmAsync()); Assert.Equal(setup.SelectedLength, app.Main.Editor!.Document.Length);
        Assert.Null(app.Main.Session!.FilePath); Assert.True(app.Main.Session.IsDirty);
        Assert.Equal(0, app.Dialogs.SaveQuestions);
    }

    [Theory]
    [InlineData("")] [InlineData("-")] [InlineData("0")] [InlineData("-1")]
    [InlineData("NaN")] [InlineData("Infinity")] [InlineData("1e400")]
    public async Task RejectedLengthSurvivesBlurAndPreventsCreation(string text)
    {
        var app = new App(create: false); var setup = app.Main.Setup;
        setup.LengthInput.Begin(); setup.LengthInput.Text = text; setup.LengthInput.LoseFocus();
        Assert.Equal(text, setup.LengthInput.Text); Assert.True(setup.LengthInput.IsEditing);
        Assert.True(setup.HasLengthError); Assert.False(setup.CanApply);
        Assert.False(await setup.ConfirmAsync()); Assert.Null(app.Main.Session);
        setup.LengthInput.Cancel(); Assert.False(setup.HasLengthError); Assert.True(setup.CanApply);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public async Task ExistingEntitiesBlockShorteningWithoutMovingOrDeletingAnything(int entity)
    {
        var app = new App(); var d = app.Main.Editor!.Document;
        d = entity switch
        {
            0 => d.WithSupports([new(Guid.NewGuid(), M(.8), SupportType.Fixed, "A")]),
            1 => d.WithLoads([new EditorPointForce(Guid.NewGuid(), M(.8), Force.FromNewtons(-10), "F1")]),
            2 => d.WithLoads([new EditorPointMoment(Guid.NewGuid(), M(.8), Moment.FromNewtonMeters(10), "M1")]),
            _ => d.WithDistributedLoads([new(Guid.NewGuid(), M(.2), M(.8), ForcePerLength.FromNewtonsPerMeter(-10), "q1")])
        };
        Assert.True(app.Main.Session!.Commit(new(d, new())));
        var revision = app.Main.Session.CurrentRevision; var analysis = app.Main.Editor.Presentation;
        app.Main.EditProject(); var setup = app.Main.Setup;
        setup.LengthInput.Begin(); setup.LengthInput.Text = "799"; setup.LengthInput.LoseFocus();
        Assert.True(setup.HasLengthError); Assert.Contains(entity == 0 ? Strings.LengthExcludesSupports : Strings.LengthExcludesEntities, setup.LengthError!);
        Assert.False(await setup.ConfirmAsync()); Assert.Same(revision, app.Main.Session.CurrentRevision); Assert.Same(analysis, app.Main.Editor.Presentation);
        setup.LengthInput.Text = "800"; Assert.True(await setup.ConfirmAsync());
        Assert.Equal(M(.8), app.Main.Editor.Document.Length);
        Assert.Equal(d.NamedEntities, app.Main.Editor.Document.NamedEntities);
        Assert.True(app.Main.Undo()); Assert.Same(revision, app.Main.Session.CurrentRevision);
    }

    [Theory]
    [InlineData("de-DE", 0)] [InlineData("en-US", 0)]
    [InlineData("de-DE", 1)] [InlineData("en-US", 1)]
    [InlineData("de-DE", 2)] [InlineData("en-US", 2)]
    public async Task UnchangedLengthKeepsExactSiAndUnitChangesProtectPendingText(string culture, int profile)
    {
        using var scope = new UiCultureScope(culture);
        var app = new App(); var options = profile switch { 0 => UnitProfile.Default, 1 => UnitProfile.StructuralEngineering, _ => UnitProfile.UnitedStates };
        Assert.True(app.Main.SetResultPresentation(options, PresentationMode.Standard));
        app.Main.Session!.Commit(app.Main.Session.CurrentRevision.State with { Document = app.Main.Editor!.Document with { Length = M(1.2345678901234567) } });
        var revision = app.Main.Session.CurrentRevision; app.Main.EditProject();
        Assert.True(await app.Main.Setup.ConfirmAsync()); Assert.Same(revision, app.Main.Session.CurrentRevision);
        app.Main.EditProject(); var input = app.Main.Setup.LengthInput; input.Begin(); input.Text = "unfinished";
        var other = options.WithUnit(QuantityKind.BeamLength, options[QuantityKind.BeamLength].Id == UnitCatalog.Inch.Id ? UnitCatalog.Millimeter : UnitCatalog.Inch);
        Assert.False(app.Main.SetResultPresentation(other, PresentationMode.Standard)); Assert.Equal("unfinished", input.Text);
        Assert.True(app.Main.SetResultPresentation(options, PresentationMode.Detailed)); Assert.Equal("unfinished", input.Text);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    public async Task EveryParametricFormLoadsItsValuesAxisAndExplicitName(int kind)
    {
        var section = ParametricSectionTestSupport.Shape(kind, r: .5); var axis = section.Axes[^1].AxisDesignation;
        var initial = new SectionDefinitionInitialState(section, axis, "HEB 200");
        var vm = new SectionDefinitionViewModel(initialState: initial);
        Assert.False(vm.IsChoosingForm); Assert.True(vm.CanConfirm); Assert.Equal(axis, vm.ResultAxis);
        Assert.Equal((SectionEditorType)kind, vm.Type); Assert.Equal("HEB 200", vm.DisplayName);
        Assert.All(vm.Parameters, p => Assert.False(p.IsPending));
        SectionDefinitionResult? result = null; vm.CloseRequested += r => result = r;
        Assert.True(await vm.ConfirmAsync()); Assert.NotNull(result); Assert.False(result.IsAutomaticName);
        var expected = State().Document.WithSection(section, axis, "HEB 200");
        var actual = expected.WithSection(result.Section, result.BendingAxis, result.DisplayName);
        Assert.True(ProjectState.DocumentContentEquals(expected, actual));
    }

    [Theory]
    [InlineData(SectionAxisDesignation.Y)] [InlineData(SectionAxisDesignation.Z)]
    [InlineData(SectionAxisDesignation.U)] [InlineData(SectionAxisDesignation.V)]
    public async Task ManualSingleAxisAndBothAxisPairsAreInitializedExactly(SectionAxisDesignation axis)
    {
        foreach (bool pair in new[] { false, true })
        {
            var first = axis is SectionAxisDesignation.Y or SectionAxisDesignation.Z ? SectionAxisDesignation.Y : SectionAxisDesignation.U;
            var second = first == SectionAxisDesignation.Y ? SectionAxisDesignation.Z : SectionAxisDesignation.V;
            ManualSectionAxis A(SectionAxisDesignation value) => new(value, SecondMomentOfArea.FromMetersToTheFourth(1.2345678901234567e-6), SectionModulus.FromCubicMeters(2.345678901234567e-5));
            var section = new ManualSectionDefinition(Area.FromSquareMeters(.0012345678901234567), A(pair ? first : axis), pair ? A(second) : null);
            var vm = new SectionDefinitionViewModel(initialState: new(section, axis));
            Assert.Equal(SectionEditorType.Manual, vm.Type); Assert.Equal(axis, vm.ResultAxis); Assert.True(vm.CanConfirm);
            SectionDefinitionResult? result = null; vm.CloseRequested += r => result = r;
            Assert.True(await vm.ConfirmAsync()); Assert.True(result!.IsAutomaticName);
            Assert.Equal(section.Area, result.Section.Area); Assert.Equal(section.Axes, result.Section.Axes);
        }
    }

    [Fact]
    public async Task SectionApplyAndDiscardAreDraftOnlyAndOneUndoRestoresAllProperties()
    {
        var app = new App(); var revision = app.Main.Session!.CurrentRevision; int analyses = app.Analyses;
        app.Main.EditProject(); var setup = app.Main.Setup; setup.ShowSections(); var vm = setup.Definition!;
        vm.BeginNameEdit(); vm.PendingName = "HEB 200"; vm.CommitName(); Assert.True(await vm.ConfirmAsync());
        Assert.Equal("HEB 200", setup.SectionDisplayName); Assert.Same(revision, app.Main.Session.CurrentRevision);
        setup.ShowSections(); vm = setup.Definition!; vm.BeginNameEdit(); vm.PendingName = "Discarded"; vm.CommitName(); setup.Escape();
        Assert.Equal("HEB 200", setup.SectionDisplayName);
        setup.LengthInput.Begin(); setup.LengthInput.Text = "1500"; setup.LengthInput.Confirm();
        setup.SelectedMaterial = ProjectTemplates.Material;
        setup.SelectedSection = new RectangleSectionGeometry(M(.1), M(.2)); setup.BendingAxis = SectionAxisDesignation.Z;
        Assert.True(await setup.ConfirmAsync()); Assert.Single(app.Main.Session.UndoHistory);
        Assert.Equal(analyses + 1, app.Analyses); Assert.Equal(M(1.5), app.Main.Editor!.Document.Length);
        Assert.Equal("HEB 200", app.Main.Editor.Document.SectionDisplayName); Assert.Equal(SectionAxisDesignation.Z, app.Main.Editor.Document.BendingAxis);
        Assert.True(app.Main.Undo()); Assert.Same(revision, app.Main.Session.CurrentRevision);
        Assert.True(app.Main.Redo()); Assert.Equal("HEB 200", app.Main.Editor.Overview.Section);
    }

    [Fact]
    public async Task NameOnlyChangesArePersistedAndUndoableWithoutAnalysis()
    {
        var app = new App(); Assert.True(await app.Main.SaveAsync()); var revision = app.Main.Session!.CurrentRevision;
        var presentation = app.Main.Editor!.Presentation; int analyses = app.Analyses;
        app.Main.EditProject(); app.Main.Setup.ShowSections(); var vm = app.Main.Setup.Definition!;
        vm.BeginNameEdit(); vm.PendingName = "HEB 200"; vm.CommitName(); Assert.True(await vm.ConfirmAsync());
        Assert.True(await app.Main.Setup.ConfirmAsync()); Assert.True(app.Main.Session.IsDirty); Assert.Equal(analyses, app.Analyses);
        Assert.Same(presentation, app.Main.Editor.Presentation); Assert.Equal("HEB 200", app.Main.Editor.Overview.Section);
        Assert.True(app.Main.Undo()); Assert.Same(revision, app.Main.Session.CurrentRevision); Assert.False(app.Main.Session.IsDirty);
        Assert.True(app.Main.Redo()); Assert.Equal(analyses, app.Analyses); Assert.True(await app.Main.SaveAsync());
        var reopened = new App(create: false); reopened.Files.Data[app.Main.Session.FilePath!] = app.Files.Data[app.Main.Session.FilePath!];
        reopened.Dialogs.OpenPath = app.Main.Session.FilePath; Assert.True(await reopened.Main.OpenAsync());
        Assert.Equal("HEB 200", reopened.Main.Editor!.Document.SectionDisplayName);
        reopened.Main.EditProject(); reopened.Main.Setup.ShowSections(); Assert.Equal("HEB 200", reopened.Main.Setup.Definition!.DisplayName);
        reopened.Main.Setup.Definition.ResetName(); Assert.True(await reopened.Main.Setup.Definition.ConfirmAsync());
        Assert.Null(reopened.Main.Setup.SectionDisplayName); Assert.True(await reopened.Main.Setup.ConfirmAsync());
        Assert.Null(reopened.Main.Editor.Document.SectionDisplayName); Assert.Equal(1, reopened.Analyses);
    }

    [Fact]
    public async Task NewDraftKeepsExistingSessionRecoveryAndEditorBuffersUntilConfirmation()
    {
        var app = new App(); app.ChangeLength(); app.Delay.ReleaseAll(); await app.Recovery.DrainAsync();
        var session = app.Main.Session!; var editor = app.Main.Editor!; var revision = session.CurrentRevision;
        var recovery = app.Files.Data[App.Slot].ToArray(); var analysis = editor.Presentation;
        editor.DimensionLength.Begin(); editor.DimensionLength.Text = "2500";
        Assert.True(await app.Main.NewAsync()); Assert.Equal(0, app.Dialogs.LeaveQuestions);
        Assert.Same(session, app.Main.Session); Assert.True(app.Main.Setup.CanCancel);
        editor.DimensionLength.LoseFocus(); Assert.Equal("2500", editor.DimensionLength.Text); Assert.Same(revision, session.CurrentRevision);
        Assert.Equal(recovery, app.Files.Data[App.Slot]); app.Main.Setup.CancelCommand.Execute(null);
        Assert.Same(editor, app.Main.Editor); Assert.Same(analysis, editor.Presentation); Assert.Same(revision, session.CurrentRevision);
        Assert.True(await app.Main.NewAsync()); Assert.False(await app.Main.Setup.ConfirmAsync()); Assert.Same(session, app.Main.Session);
        app.Dialogs.Leave = LeaveDecision.Discard; Assert.True(await app.Main.Setup.ConfirmAsync());
        Assert.NotSame(session, app.Main.Session); Assert.Null(app.Main.Session!.FilePath); Assert.True(app.Main.Session.IsDirty);
    }

    [Fact]
    public async Task SectionWorkspaceBlocksGlobalActionsButKeepsLibrarySavingFunctional()
    {
        var files = new Files(); var main = new MainWindowViewModel(files: files, sectionStore: new(files, "sections"));
        await main.InitializeLibrariesAsync(); Assert.True(await main.Setup.ConfirmAsync()); main.EditProject(); main.Setup.ShowSections();
        Assert.True(main.IsSectionWorkspace); Assert.False(main.NewCommand.CanExecute(null)); Assert.False(main.SaveCommand.CanExecute(null));
        Assert.False(main.OpenCommand.CanExecute(null)); Assert.False(main.UndoCommand.CanExecute(null));
        Assert.False(await main.SaveAsync()); Assert.False(await main.NewAsync()); Assert.False(await main.OpenAsync());
        Assert.True(await main.Setup.Definition!.SaveLibraryAsync()); Assert.Single(main.UserSections.All);
        main.Setup.Escape(); Assert.False(main.IsSectionWorkspace); Assert.True(main.SaveCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ExplicitAndLibraryNamesSurviveFormChangesUntilAnExplicitReset(bool fromLibrary)
    {
        var files = new Files();
        files.Data["sections"] = SectionLibraryCodec.Serialize(new([new("HEB 200",
            new RectangularHollowSectionGeometry(M(.1), M(.1), M(.005), M(0)))]));
        var main = new MainWindowViewModel(files: files, sectionStore: new(files, "sections"));
        await main.InitializeLibrariesAsync(); Assert.True(await main.Setup.ConfirmAsync());
        main.EditProject(); main.Setup.ShowSections(); var vm = main.Setup.Definition!;
        if (fromLibrary) vm.LoadPreset(main.UserSections.All.Single());
        else { vm.BeginNameEdit(); vm.PendingName = "HEB 200"; vm.CommitName(); }
        vm.SelectForm(SectionEditorType.Circle); var diameter = Assert.Single(vm.Parameters);
        diameter.PendingText = "150"; Assert.True(diameter.Commit()); Assert.Equal("HEB 200", vm.DisplayName);
        Assert.True(await vm.ConfirmAsync()); Assert.True(await main.Setup.ConfirmAsync());
        Assert.Equal("HEB 200", main.Editor!.Document.SectionDisplayName);
        main.EditProject(); main.Setup.ShowSections(); vm = main.Setup.Definition!;
        vm.SelectForm(SectionEditorType.RectangularHollow);
        Assert.Equal("HEB 200", vm.DisplayName); vm.ResetName(); Assert.False(vm.HasCustomName);
        vm.CancelCommand.Execute(null); Assert.Equal("HEB 200", main.Setup.SectionDisplayName);
        main.Setup.CancelCommand.Execute(null);
        Assert.False(await main.Setup.ConfirmAsync());
    }

    [Theory]
    [InlineData(null)] [InlineData("HEB 200")]
    public async Task OptionalV2NameAndRecoveryRoundTripPreserveTheCompleteDocument(string? name)
    {
        var state = State() with { Document = State().Document with { SectionDisplayName = name } };
        var bytes = ProjectFileCodec.Serialize(state); var wire = JsonNode.Parse(bytes)!;
        Assert.Equal(name is not null, wire["document"]!.AsObject().ContainsKey("sectionDisplayName"));
        Assert.True(state.ContentEquals(ProjectFileCodec.Deserialize(bytes)));
        var d = state.Document;
        foreach (var copy in new[] { d.WithSupports(d.Supports), d.WithLoads(d.Loads), d.WithDistributedLoads(d.DistributedLoads), d.WithSection(d.Section, d.BendingAxis), d.WithBendingAxis(d.BendingAxis), d with { Length = M(2) } })
            Assert.Equal(name, copy.SectionDisplayName);
        var files = new Files(); var recovery = new ProjectRecovery(files, TestPath("named-recovery.json"));
        await recovery.FlushAsync(state, null); Assert.True(state.ContentEquals((await recovery.ReadAsync())!.State));
        foreach (string invalid in new[] { "", " ", " HEB 200 " })
        {
            wire["document"]!["sectionDisplayName"] = invalid;
            Assert.Throws<ProjectFormatException>(() => ProjectFileCodec.Deserialize(Encoding.UTF8.GetBytes(wire.ToJsonString())));
        }
        wire["document"]!["sectionDisplayName"] = null;
        Assert.Null(ProjectFileCodec.Deserialize(Encoding.UTF8.GetBytes(wire.ToJsonString())).Document.SectionDisplayName);
    }
}
