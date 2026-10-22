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
    public static IEnumerable<object[]> CreateFields() => Enumerable.Range(0, 32).Select(i => new object[] { i });

    [Theory]
    [MemberData(nameof(CreateFields))]
    public async Task CreateRequiresEveryValueAndOnlyCreatesACompleteDocument(int fields)
    {
        var app = new App(create: false); Assert.True(await app.Main.NewAsync()); var setup = app.Main.Setup;
        if ((fields & 1) != 0) { setup.LengthInput.Begin(); setup.LengthInput.Text = "1500"; }
        if ((fields & 2) != 0) setup.SelectedSection = new RectangleSectionGeometry(M(.1), M(.2));
        if ((fields & 4) != 0) setup.SelectedMaterial = ProjectTemplates.Material;
        if ((fields & 8) != 0) setup.CalculationTitleInput.Text = "Calculation";
        if ((fields & 16) != 0) setup.AuthorInput.Text = "Author";
        Assert.Equal(fields == 31, setup.CanApply); Assert.Equal(fields == 31, setup.ApplyCommand.CanExecute(null));
        Assert.Equal(fields == 31, await setup.ConfirmAsync());
        if (fields == 31)
        {
            Assert.Equal(M(1.5), app.Main.Editor!.Document.Length);
            Assert.Equal(new ProjectInfo("Calculation", null, null, "Author"), app.Main.Session!.CurrentRevision.State.Info);
        }
        else { Assert.Null(app.Main.Editor); Assert.Null(app.Main.Session); Assert.Equal(0, app.Analyses); }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public async Task LastRequiredValueRefreshesTheCreateCommand(int last)
    {
        var app = new App(create: false); Assert.True(await app.Main.NewAsync()); var setup = app.Main.Setup;
        void SetField(int field)
        {
            if (field == 0) { setup.LengthInput.Begin(); setup.LengthInput.Text = "1500"; }
            else if (field == 1) setup.SelectedSection = ProjectTemplates.Section;
            else if (field == 2) setup.SelectedMaterial = ProjectTemplates.Material;
            else if (field == 3) setup.CalculationTitleInput.Text = "Calculation";
            else setup.AuthorInput.Text = "Author";
        }
        foreach (int field in Enumerable.Range(0, 5).Where(i => i != last)) SetField(field);
        Assert.False(setup.CanApply); bool enabled = false;
        setup.ApplyCommand.CanExecuteChanged += (_, _) => enabled = setup.ApplyCommand.CanExecute(null);
        SetField(last); Assert.True(enabled); Assert.True(setup.CanApply);
    }

    [Fact]
    public async Task CompleteCreateDraftIsBlockedOnSubpagesAndWhileLibrariesAreBusy()
    {
        var files = new Files(); var main = new MainWindowViewModel(files: files, materialStore: new(files, "materials"));
        Assert.True(await main.NewAsync()); CompleteSetup(main); var setup = main.Setup;
        Assert.True(setup.CanApply);
        setup.ShowMaterials(); Assert.False(setup.CanApply); setup.Escape(); Assert.True(setup.CanApply);
        setup.ShowSections(); Assert.False(setup.CanApply); setup.Escape(); Assert.True(setup.CanApply);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        files.BeforeRead = _ => release.Task;
        var loading = main.InitializeLibrariesAsync();
        try
        {
            Assert.True(setup.IsBusy); Assert.False(setup.CanApply); Assert.False(setup.ApplyCommand.CanExecute(null));
            Assert.False(await setup.ConfirmAsync()); Assert.Null(main.Session);
        }
        finally { release.TrySetResult(); await loading; }
        Assert.True(setup.CanApply); Assert.True(await setup.ConfirmAsync()); Assert.NotNull(main.Session);
    }

    [Fact]
    public async Task EmptySelectionsStayEmptyOnCancelAndOnlyConfirmedSectionIsTransferred()
    {
        var app = new App(create: false); Assert.True(await app.Main.NewAsync()); var setup = app.Main.Setup;
        Assert.Null(setup.SelectedLength); Assert.Empty(setup.LengthInput.Text); Assert.False(setup.HasLengthError);
        Assert.Null(setup.SelectedSection); Assert.Null(setup.SelectedMaterial); Assert.False(setup.HasSection);
        Assert.Equal(Strings.SectionSelection, setup.SectionName); Assert.Empty(setup.AxisSummary);
        Assert.Empty(setup.Area); Assert.Empty(setup.Inertia); Assert.Empty(setup.Modulus);
        Assert.Equal(Strings.SelectMaterialRequired, setup.MaterialName); Assert.Empty(setup.MaterialSummary);
        setup.ShowSections(); Assert.True(setup.Definition!.IsChoosingForm); Assert.False(setup.CanApply);
        setup.Definition.SelectForm(SectionEditorType.Rectangle);
        foreach (var input in setup.Definition.Parameters) { input.PendingText = "100"; Assert.True(input.Commit()); }
        Assert.Null(setup.SelectedSection); setup.Escape(); Assert.Null(setup.SelectedSection);
        setup.ShowMaterials(); setup.Escape(); Assert.Null(setup.SelectedMaterial);
        setup.ShowSections(); Assert.True(setup.Definition!.IsChoosingForm);
        setup.Definition.SelectForm(SectionEditorType.Rectangle);
        foreach (var input in setup.Definition.Parameters) { input.PendingText = "100"; Assert.True(input.Commit()); }
        Assert.True(await setup.Definition.ConfirmAsync()); Assert.True(setup.HasSection);
        Assert.IsType<RectangleSectionGeometry>(setup.SelectedSection); Assert.NotEmpty(setup.AxisSummary);
        Assert.False(setup.CanApply); Assert.Null(app.Main.Session);
    }

    [Theory]
    [InlineData("de-DE")] [InlineData("en-US")]
    public async Task EmptyLengthRemainsAbsentAcrossRefreshUnitChangesAndCancel(string culture)
    {
        using var scope = new UiCultureScope(culture);
        var app = new App(create: false); Assert.True(await app.Main.NewAsync()); var setup = app.Main.Setup;
        foreach (var profile in new[] { UnitProfile.UnitedStates, UnitProfile.StructuralEngineering, UnitProfile.Default })
        {
            Assert.True(app.Main.SetResultPresentation(profile, PresentationMode.Standard));
            setup.LengthInput.Refresh(); Assert.Empty(setup.LengthInput.Text); Assert.Empty(setup.LengthInput.DisplayText);
            Assert.Null(setup.SelectedLength); Assert.False(setup.LengthInput.TryGetLength(out _));
            Assert.False(setup.HasLengthError); Assert.False(setup.CanApply);
        }
        setup.LengthInput.Begin(); Assert.False(setup.LengthInput.TryGetLength(out _));
        setup.LengthInput.Text = "-"; Assert.False(setup.HasLengthError);
        Assert.False(setup.LengthInput.Confirm()); Assert.True(setup.HasLengthError);
        setup.LengthInput.Cancel(); Assert.Empty(setup.LengthInput.Text); Assert.Null(setup.SelectedLength);
        Assert.False(setup.HasLengthError);
    }

    [Theory]
    [InlineData("de-DE", "1234,5")] [InlineData("en-US", "1234.5")]
    public async Task EmptyLengthAndConfirmedInputOnlyChangeTheDraft(string culture, string text)
    {
        using var scope = new UiCultureScope(culture);
        var app = new App(create: false); Assert.True(await app.Main.NewAsync()); var setup = app.Main.Setup; CompleteProjectInfo(setup);
        Assert.Null(setup.SelectedLength); Assert.Empty(setup.LengthInput.Text);
        Assert.False(setup.HasLengthError); Assert.False(setup.CanApply);
        setup.SelectedSection = ProjectTemplates.Section; setup.SelectedMaterial = ProjectTemplates.Material;
        Assert.True(setup.CanCancel);
        setup.LengthInput.Begin(); setup.LengthInput.Text = text;
        Assert.True(setup.CanApply); Assert.True(setup.LengthInput.Confirm());
        Assert.Equal(1234.5, setup.SelectedLength!.Value.Millimeters); Assert.Null(app.Main.Session); Assert.Equal(0, app.Analyses);
        Assert.True(await setup.ConfirmAsync()); Assert.Equal(setup.SelectedLength, app.Main.Editor!.Document.Length);
        Assert.Null(app.Main.Session!.FilePath); Assert.True(app.Main.Session.IsDirty);
        Assert.Equal(0, app.Dialogs.SaveQuestions);
    }

    [Theory]
    [InlineData("-")] [InlineData("0")] [InlineData("-1")]
    [InlineData("NaN")] [InlineData("Infinity")] [InlineData("1e400")]
    public async Task RejectedLengthSurvivesBlurAndPreventsCreation(string text)
    {
        var app = new App(create: false); Assert.True(await app.Main.NewAsync()); var setup = app.Main.Setup; CompleteProjectInfo(setup);
        setup.SelectedSection = ProjectTemplates.Section; setup.SelectedMaterial = ProjectTemplates.Material;
        setup.LengthInput.Begin(); setup.LengthInput.Text = text;
        Assert.False(setup.HasLengthError); Assert.False(setup.CanApply);
        setup.LengthInput.LoseFocus();
        Assert.Equal(text, setup.LengthInput.Text); Assert.True(setup.LengthInput.IsEditing);
        Assert.True(setup.HasLengthError); Assert.False(setup.CanApply);
        Assert.False(await setup.ConfirmAsync()); Assert.Null(app.Main.Session);
        setup.LengthInput.Cancel(); Assert.False(setup.HasLengthError); Assert.False(setup.CanApply);
        Assert.Null(setup.SelectedLength); Assert.Empty(setup.LengthInput.Text);
    }

    [Theory]
    [InlineData("")] [InlineData(" ")] [InlineData("\t")]
    public async Task MissingCreateLengthStaysNeutralAfterBlurAndConfirmation(string text)
    {
        var app = new App(create: false); Assert.True(await app.Main.NewAsync()); var setup = app.Main.Setup; CompleteProjectInfo(setup);
        setup.SelectedSection = ProjectTemplates.Section; setup.SelectedMaterial = ProjectTemplates.Material;
        setup.LengthInput.Begin(); setup.LengthInput.Text = text; setup.LengthInput.LoseFocus();
        Assert.False(setup.HasLengthError); Assert.False(setup.LengthInput.HasError); Assert.Null(setup.LengthError);
        Assert.Null(setup.SelectedLength); Assert.Empty(setup.LengthInput.Text); Assert.False(setup.LengthInput.IsEditing);
        Assert.False(setup.CanApply); Assert.False(await setup.ConfirmAsync()); Assert.False(setup.HasLengthError);
        Assert.Null(app.Main.Session); Assert.Equal(0, app.Analyses);
        setup.LengthInput.Begin(); Assert.False(setup.LengthInput.Confirm()); Assert.False(setup.HasLengthError);
        Assert.True(setup.LengthInput.IsEditing); setup.LengthInput.LoseFocus();
        Assert.True(app.Main.SetResultPresentation(UnitProfile.UnitedStates, PresentationMode.Standard));
        Assert.Empty(setup.LengthInput.Text); Assert.Null(setup.SelectedLength);
    }

    [Theory]
    [InlineData("")] [InlineData("0")] [InlineData("-")]
    public async Task ExistingLengthOnlyShowsErrorsAfterValidationAndKeepsTheDocument(string text)
    {
        var app = new App(); var document = app.Main.Editor!.Document;
        app.Main.EditProject(); var setup = app.Main.Setup;
        setup.LengthInput.Begin(); setup.LengthInput.Text = text;
        Assert.False(setup.HasLengthError); Assert.False(setup.CanApply);
        Assert.False(await setup.ConfirmAsync()); Assert.True(setup.HasLengthError);
        Assert.Same(document, app.Main.Editor.Document); Assert.Equal(document.Length, setup.SelectedLength);
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
        setup.LengthInput.Begin(); setup.LengthInput.Text = "799";
        Assert.False(setup.HasLengthError); Assert.False(setup.CanApply); setup.LengthInput.LoseFocus();
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
        Assert.True(await app.Main.NewAsync()); CompleteSetup(app.Main);
        Assert.False(await app.Main.Setup.ConfirmAsync()); Assert.Same(session, app.Main.Session);
        app.Dialogs.Leave = LeaveDecision.Discard; Assert.True(await app.Main.Setup.ConfirmAsync());
        Assert.NotSame(session, app.Main.Session); Assert.Null(app.Main.Session!.FilePath); Assert.True(app.Main.Session.IsDirty);
    }

    [Fact]
    public async Task SectionWorkspaceBlocksGlobalActionsButKeepsLibrarySavingFunctional()
    {
        var files = new Files(); var main = new MainWindowViewModel(files: files, sectionStore: new(files, "sections")); main.NewCommand.Execute(null);
        await main.InitializeLibrariesAsync(); CompleteSetup(main); Assert.True(await main.Setup.ConfirmAsync()); main.EditProject(); main.Setup.ShowSections();
        Assert.True(main.IsSectionWorkspace); Assert.False(main.NewCommand.CanExecute(null)); Assert.False(main.SaveCommand.CanExecute(null));
        Assert.False(main.OpenCommand.CanExecute(null)); Assert.False(main.UndoCommand.CanExecute(null));
        Assert.False(await main.SaveAsync()); Assert.False(await main.NewAsync()); Assert.False(await main.OpenAsync());
        Assert.True(await main.Setup.Definition!.SaveLibraryAsync()); Assert.Single(main.UserSections.All);
        main.Setup.Escape(); Assert.False(main.IsSectionWorkspace); Assert.False(main.SaveCommand.CanExecute(null));
        main.Setup.CancelCommand.Execute(null); Assert.True(main.SaveCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ExplicitAndLibraryNamesRemainFormLocalAndOnlyTheActiveDraftIsPersisted(bool fromLibrary)
    {
        var files = new Files();
        var ih = new ISectionGeometry(M(.2), M(.1), M(.005), M(.01), M(0));
        files.Data["sections"] = SectionLibraryCodec.Serialize(new([new("HEB 200", ih)]));
        var main = new MainWindowViewModel(files: files, sectionStore: new(files, "sections")); main.NewCommand.Execute(null);
        await main.InitializeLibrariesAsync(); main.Setup.SelectedSection = ih; main.Setup.BendingAxis = SectionAxisDesignation.Z;
        CompleteSetup(main); Assert.True(await main.Setup.ConfirmAsync());
        main.EditProject(); main.Setup.ShowSections(); var vm = main.Setup.Definition!;
        if (fromLibrary) vm.LoadPreset(main.UserSections.All.Single());
        else { vm.BeginNameEdit(); vm.PendingName = "HEB 200"; Assert.True(vm.CommitName()); }
        var ihInputs = vm.Parameters.ToArray();
        vm.SelectForm(SectionEditorType.Circle); Assert.False(vm.HasCustomName); Assert.NotEqual("HEB 200", vm.DisplayName);
        var diameter = Assert.Single(vm.Parameters); diameter.PendingText = "80"; Assert.True(diameter.Commit());
        vm.BeginNameEdit(); vm.PendingName = "Welle 80"; Assert.True(vm.CommitName());
        for (int pass = 0; pass < 3; pass++)
        {
            vm.SelectForm(SectionEditorType.ISection); Assert.Equal("HEB 200", vm.DisplayName);
            Assert.Equal(ihInputs, vm.Parameters); Assert.Equal(SectionAxisDesignation.Z, vm.ResultAxis);
            vm.SelectForm(SectionEditorType.Circle); Assert.Equal("Welle 80", vm.DisplayName); Assert.Same(diameter, Assert.Single(vm.Parameters));
        }
        vm.ResetName(); Assert.False(vm.HasCustomName);
        vm.SelectForm(SectionEditorType.ISection); Assert.Equal("HEB 200", vm.DisplayName);
        vm.SelectForm(SectionEditorType.Circle); vm.BeginNameEdit(); vm.PendingName = "Welle 80"; Assert.True(vm.CommitName());
        Assert.True(await vm.ConfirmAsync()); Assert.True(await main.Setup.ConfirmAsync());
        Assert.IsType<CircleSectionGeometry>(main.Editor!.Document.Section);
        Assert.Equal(SectionAxisDesignation.Y, main.Editor.Document.BendingAxis); Assert.Equal("Welle 80", main.Editor.Document.SectionDisplayName);
        main.EditProject(); main.Setup.ShowSections(); vm = main.Setup.Definition!;
        Assert.Equal("Welle 80", vm.DisplayName);
        vm.SelectForm(SectionEditorType.ISection); Assert.False(vm.HasCustomName); Assert.NotEqual("HEB 200", vm.DisplayName);
        vm.CancelCommand.Execute(null); Assert.Equal("Welle 80", main.Setup.SectionDisplayName);
        main.Setup.CancelCommand.Execute(null); Assert.False(await main.Setup.ConfirmAsync());
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
    [Fact]
    public async Task SetupCommitsInfoAndBeamTogetherAndInfoOnlyDoesNotAnalyze()
    {
        var app = new App(); Assert.True(await app.Main.SaveAsync());
        var session = app.Main.Session!; var before = session.CurrentRevision.State; int calls = app.Analyses;
        Assert.Equal("Beam01.spandraft", app.Main.ProjectFileName);
        Assert.Equal("Test calculation", before.Info!.CalculationTitle);
        app.Main.EditProject(); var setup = app.Main.Setup;
        Assert.Equal(before.Info!.CalculationTitle, setup.CalculationTitleInput.Text);
        setup.CalculationTitleInput.Text = " Beam 2 "; setup.AuthorInput.Text = " New author ";
        setup.ProjectTitleInput.Text = " Project A "; setup.ReferenceInput.Text = " Drawing 2 ";
        setup.LengthInput.Begin(); setup.LengthInput.Text = "2000";
        Assert.Same(before, session.CurrentRevision.State);
        Assert.True(await setup.ConfirmAsync()); Assert.Single(session.UndoHistory); Assert.Equal(calls + 1, app.Analyses);
        var after = session.CurrentRevision.State;
        Assert.Equal(new ProjectInfo("Beam 2", "Project A", "Drawing 2", "New author"), after.Info);
        Assert.Equal(M(2), after.Document.Length);
        Assert.True(app.Main.Undo()); Assert.Same(before, session.CurrentRevision.State); Assert.False(session.IsDirty);
        Assert.True(app.Main.Redo()); Assert.Same(after, session.CurrentRevision.State);
        calls = app.Analyses; app.Main.EditProject(); setup = app.Main.Setup;
        setup.ReferenceInput.Text = "Drawing 3"; Assert.True(await setup.ConfirmAsync());
        Assert.Equal(calls, app.Analyses); Assert.Equal(2, session.UndoHistory.Count);
        app.Delay.ReleaseAll(); await WaitFor(app.Recovery.DrainAsync());
        Assert.Equal(session.CurrentRevision.State.Info, (await app.Recovery.ReadAsync())!.State.Info);
        Assert.True(app.Main.Undo()); Assert.True(app.Main.Redo()); Assert.Equal(calls, app.Analyses);
    }

    [Theory]
    [InlineData("calculationTitle")] [InlineData("author")]
    public async Task ExistingInfoCannotBeClearedAndCancelPreservesTheRevision(string field)
    {
        var app = new App(); var revision = app.Main.Session!.CurrentRevision;
        app.Main.EditProject(); var setup = app.Main.Setup;
        var input = field == "author" ? setup.AuthorInput : setup.CalculationTitleInput;
        input.Begin(); input.Text = " "; Assert.False(setup.CanApply);
        Assert.False(await setup.ConfirmAsync()); Assert.True(input.HasError);
        Assert.Same(revision, app.Main.Session.CurrentRevision);
        setup.CancelCommand.Execute(null); Assert.Same(revision, app.Main.Session.CurrentRevision);
    }

    [Fact]
    public async Task LegacyCanBeOpenedEditedAndSavedWithoutInfoButPartialInfoIsRejected()
    {
        var app = new App(create: false); var path = TestPath("legacy.spandraft");
        var json = JsonNode.Parse(ProjectFileCodec.Serialize(State()))!; json["formatVersion"] = 2;
        app.Files.Data[path] = Encoding.UTF8.GetBytes(json.ToJsonString());
        Assert.True(await app.Main.OpenAsync(path)); Assert.False(app.Main.Session!.IsDirty);
        Assert.Null(app.Main.Session.CurrentRevision.State.Info); app.Main.EditProject();
        var setup = app.Main.Setup;
        Assert.Empty(setup.CalculationTitleInput.Text); Assert.Empty(setup.AuthorInput.Text);
        setup.LengthInput.Begin(); setup.LengthInput.Text = "2000";
        Assert.True(await setup.ConfirmAsync()); Assert.Null(app.Main.Session.CurrentRevision.State.Info);
        Assert.True(await app.Main.SaveAsync()); Assert.False(app.Main.Session.IsDirty);
        Assert.Equal(3, JsonNode.Parse(app.Files.Data[path])!["formatVersion"]!.GetValue<int>());
        app.Main.EditProject(); setup = app.Main.Setup;
        setup.ReferenceInput.Text = "Drawing"; Assert.False(setup.CanApply);
        Assert.False(await setup.ConfirmAsync()); Assert.True(setup.CalculationTitleInput.HasError); Assert.True(setup.AuthorInput.HasError);
        Assert.Null(app.Main.Session.CurrentRevision.State.Info);
        setup.ReferenceInput.Text = " "; Assert.True(await setup.ConfirmAsync()); Assert.False(app.Main.Session.IsDirty);
        app.Main.EditProject(); setup = app.Main.Setup;
        setup.CalculationTitleInput.Text = "Beam"; setup.AuthorInput.Text = "Author";
        Assert.True(await setup.ConfirmAsync()); Assert.NotNull(app.Main.Session.CurrentRevision.State.Info);
        Assert.True(app.Main.Undo()); Assert.Null(app.Main.Session.CurrentRevision.State.Info);
        Assert.False(app.Main.Session.IsDirty);
    }

    [Fact]
    public async Task DefaultAuthorIsOnlyCopiedIntoFreshCreateDrafts()
    {
        var main = new MainWindowViewModel(settings: UserSettings.Default with { DefaultAuthor = " First author " });
        Assert.True(await main.NewAsync()); var setup = main.Setup;
        Assert.Equal("First author", setup.AuthorInput.Text); Assert.Equal("First author", setup.Draft.Author);
        Assert.Null(await main.ApplySettingsAsync(main.Settings with { DefaultAuthor = "Second author" }));
        Assert.Equal("First author", setup.AuthorInput.Text);
        CompleteSetup(main); Assert.True(await setup.ConfirmAsync());
        var revision = main.Session!.CurrentRevision;
        Assert.Equal("First author", revision.State.Info!.Author);
        main.EditProject(); Assert.Equal("First author", main.Setup.AuthorInput.Text);
        Assert.Null(await main.ApplySettingsAsync(main.Settings with { DefaultAuthor = "Third author" }));
        Assert.Same(revision, main.Session.CurrentRevision); Assert.Equal("First author", main.Setup.AuthorInput.Text);
        main.Setup.CancelCommand.Execute(null); Assert.True(await main.NewAsync());
        Assert.Equal("Third author", main.Setup.AuthorInput.Text);
    }

}
