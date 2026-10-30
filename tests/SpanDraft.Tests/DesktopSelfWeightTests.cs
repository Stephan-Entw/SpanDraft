using SpanDraft.Analysis;
using SpanDraft.Core.Loads;
using SpanDraft.Core.Materials;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

public sealed class DesktopSelfWeightTests
{
    internal static Material Steel(double? density = 7850) => new("Steel", Pressure.FromPascals(210e9),
        Pressure.FromPascals(235e6), MaterialStrengthType.Re,
        density is { } rho ? MassDensity.FromKilogramsPerCubicMeter(rho) : null, null);
    internal static EditorDocument Document(bool enabled = false) => new(M(3), Steel(),
        new SpanDraft.Core.Sections.Parametric.RectangleSectionGeometry(M(.1), M(.2)), SectionAxisDesignation.Y, [new(Guid.NewGuid(), M(0), SupportType.Fixed, "A")],
        selfWeight: enabled ? new(Guid.NewGuid(), true) : null);

    private sealed class Observed
    {
        public int Calls;
        public EditorViewModel Editor { get; }
        public Observed(EditorDocument document) => Editor = new(document, () => { },
            beam => { Calls++; return BeamAnalysis.Analyze(beam); });
    }

    [Fact]
    public void FormulaMapsToExistingUdlAndMatchesCantileverReference()
    {
        var document = Document(true);
        var load = Assert.IsType<UniformDistributedLoad>(Assert.Single(document.ToBeamModel().Loads));
        Assert.Equal(-1539.64405, load.Intensity.NewtonsPerMeter, 8);
        Assert.Equal(M(0), load.StartPosition); Assert.Equal(M(3), load.EndPosition);
        Assert.Empty(document.DistributedLoads);
        Assert.DoesNotContain(document.NamedEntities, e => e.Id == document.SelfWeight!.Id);
        Assert.Contains(document.SelfWeight!.Id, document.EntityIds);
        var explicitLoad = document with { SelfWeight = null };
        explicitLoad = explicitLoad.WithDistributedLoads([new(Guid.NewGuid(), M(0), M(3), load.Intensity, "q1")]);
        var solution = SolverTestSupport.Solve(document.ToBeamModel());
        SolverTestSupport.SameResults(SolverTestSupport.Solve(explicitLoad.ToBeamModel()), solution);
        SolverTestSupport.ForceClose(4618.93215, SolverTestSupport.At(solution, 0).ReactionY);
        double ei = 210e9 * document.Section.GetAxis(document.BendingAxis).SecondMomentOfArea.MetersToTheFourth;
        SolverTestSupport.DisplacementClose(-1539.64405 * 81 / (8 * ei), SolverTestSupport.At(solution, 3).TransverseDisplacement);
    }

    [Fact]
    public void ToggleAndPresentationUseHistoryWithoutLosingIdentityOrReanalyzingMetadata()
    {
        var s = new Observed(Document()); var editor = s.Editor;
        editor.Session.MarkSaved(editor.Session.CurrentRevision, TestPath("saved.spandraft"));
        editor.ToggleDistributedLoadTool();
        Assert.True(editor.ToggleSelfWeight());
        Assert.True(editor.IsEditorNeutral); Assert.False(editor.IsDistributedLoadTool);
        Assert.True(editor.IsSelfWeightEnabled); Assert.Equal(2, s.Calls);
        var id = editor.Document.SelfWeight!.Id;
        Assert.True(editor.IsSelfWeightVisible);
        Assert.True(editor.SetAnnotationOffset(id, new(35, -18)));
        Assert.True(editor.EditSelfWeight(id));
        editor.ToggleSelfWeightVisibility();
        Assert.True(editor.IsSelfWeightFlyoutVisible); Assert.False(editor.IsSelfWeightVisible);
        Assert.Equal(2, s.Calls); Assert.True(editor.Session.IsDirty);
        Assert.Single(editor.Overview.DistributedLoads);
        Assert.True(editor.Undo()); Assert.True(editor.IsSelfWeightVisible); Assert.Equal(2, s.Calls);
        Assert.True(editor.Redo()); Assert.False(editor.IsSelfWeightVisible); Assert.Equal(2, s.Calls);
        var hidden = editor.Session.CurrentRevision.State;
        Assert.Equal(EditorChangeKind.MetadataOnly, EditorChangeClassifier.Classify(hidden,
            hidden with { Presentation = hidden.Presentation.WithSelfWeightVisibility(true) }));
        editor.DisableSelfWeight();
        Assert.False(editor.IsSelfWeightEnabled); Assert.Empty(editor.Overview.DistributedLoads); Assert.Equal(3, s.Calls);
        Assert.Equal(id, editor.Document.SelfWeight!.Id);
        Assert.Equal(new AnnotationOffset(35, -18), editor.EditorPresentation.AnnotationOffsets[id]);
        Assert.True(editor.ToggleSelfWeight()); Assert.Equal(4, s.Calls);
        Assert.Equal(id, editor.Document.SelfWeight.Id); Assert.False(editor.IsSelfWeightVisible);
        Assert.Equal(new AnnotationOffset(35, -18), editor.EditorPresentation.AnnotationOffsets[id]);
        Assert.True(editor.Undo()); Assert.False(editor.IsSelfWeightEnabled);
        Assert.True(editor.Redo()); Assert.True(editor.IsSelfWeightEnabled); Assert.Equal(6, s.Calls);
        editor.Undo(); editor.Undo(); editor.Undo(); editor.Undo(); editor.Undo();
        Assert.False(editor.Session.IsDirty); Assert.Null(editor.Document.SelfWeight);
        Assert.Empty(editor.EditorPresentation.AnnotationOffsets);
        SolverTestSupport.DisplacementClose(0, SolverTestSupport.At(editor.Presentation.Result!.Solution, 3).TransverseDisplacement);
    }

    [Fact]
    public void MissingDensityRejectsActivationWithoutIdentityHistoryOrAnalysis()
    {
        using var culture = new UiCultureScope("de-DE");
        var s = new Observed(Document() with { Material = Steel(null) }); var editor = s.Editor;
        var revision = editor.Session.CurrentRevision;
        Assert.False(editor.ToggleSelfWeight());
        Assert.Equal("Eigengewicht kann nicht berücksichtigt werden. Für den Werkstoff ist keine Dichte angegeben.", editor.SelfWeightFeedback);
        Assert.Same(revision, editor.Session.CurrentRevision); Assert.Empty(editor.Session.UndoHistory);
        Assert.Null(editor.Document.SelfWeight); Assert.False(editor.IsSelfWeightEnabled); Assert.Equal(1, s.Calls);
        Assert.Throws<ArgumentException>(() => (editor.Document with { SelfWeight = new(Guid.NewGuid(), true) }).ToBeamModel());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void DensityHasMechanicalEffectOnlyWhenEnabled(bool enabled)
    {
        var s = new Observed(Document(enabled)); var editor = s.Editor;
        var before = editor.Session.CurrentRevision.State;
        editor.ApplySetup(editor.Document.Section, editor.Document.BendingAxis, Steel(8000));
        Assert.Equal(enabled ? 2 : 1, s.Calls);
        Assert.Equal(enabled ? EditorChangeKind.Mechanical : EditorChangeKind.MetadataOnly,
            EditorChangeClassifier.Classify(before, editor.Session.CurrentRevision.State));
        if (enabled) Assert.Equal(-1569.064, editor.Document.SelfWeightLoad!.Intensity.NewtonsPerMeter, 8);
        Assert.True(editor.Undo()); Assert.Equal(enabled ? 3 : 1, s.Calls);
        Assert.True(editor.Redo()); Assert.Equal(enabled ? 4 : 1, s.Calls);
    }

    [Fact]
    public void SectionAndLengthChangesDeriveCurrentLoadAndAllDocumentCopiesPreserveIt()
    {
        var s = new Observed(Document(true)); var editor = s.Editor;
        var identity = editor.Document.SelfWeight;
        editor.ApplySetup(new RectangleSection(M(.2), M(.2)), Steel());
        Assert.Equal(-3079.2881, editor.Document.SelfWeightLoad!.Intensity.NewtonsPerMeter, 8);
        Assert.Equal(2, s.Calls);
        editor.DimensionLength.Begin(); editor.DimensionLength.Text = "4000";
        Assert.True(editor.DimensionLength.Confirm());
        Assert.Equal(-3079.2881, editor.Document.SelfWeightLoad.Intensity.NewtonsPerMeter, 8);
        Assert.Equal(M(4), editor.Document.SelfWeightLoad.EndPosition); Assert.Equal(3, s.Calls);
        var d = editor.Document;
        foreach (var copy in new[] { d.WithLoads([]), d.WithSupports([]), d.WithDistributedLoads([]),
            d.WithSection(d.Section, d.BendingAxis), d.WithBendingAxis(d.BendingAxis), d with { Length = M(5) } })
            Assert.Equal(identity, copy.SelfWeight);
    }

    [Fact]
    public void DirectAndSetupMaterialFlowsRejectMissingDensityAndKeepActiveWeight()
    {
        var s = new Observed(Document(true)); var editor = s.Editor;
        var revision = editor.Session.CurrentRevision;
        editor.ApplySetup(editor.Document.Section, editor.Document.BendingAxis, Steel(null));
        Assert.False(editor.ApplySetup(M(3), editor.Document.Section, editor.Document.BendingAxis, Steel(null), null));
        Assert.Same(revision, editor.Session.CurrentRevision); Assert.True(editor.IsSelfWeightEnabled); Assert.Equal(1, s.Calls);
        var d = editor.Document;
        var setup = new ProjectSetupViewModel(ProjectSetupMode.Edit, d.Section, d.BendingAxis, d.Material,
            _ => Task.FromResult(true), () => { }, originalDocument: d);
        setup.SelectedMaterial = Steel(null);
        Assert.False(setup.CanApply); Assert.Equal(Strings.SelfWeightMissingDensity, setup.MaterialError);
        setup.ShowMaterials();
        var selection = Assert.IsType<MaterialSelectionViewModel>(setup.CurrentStep);
        selection.UseSelected();
        Assert.Equal(ProjectSetupPage.MaterialSelection, setup.Page);
        Assert.Equal(Strings.SelfWeightMissingDensity, selection.Error);
    }

    [Fact]
    public async Task ProjectMaterialEditorRejectsMissingDensityButLibraryEditorStillAllowsIt()
    {
        var d = Document(true);
        var setup = new ProjectSetupViewModel(ProjectSetupMode.Edit, d.Section, d.BendingAxis, d.Material,
            _ => Task.FromResult(true), () => { }, originalDocument: d);
        setup.ShowMaterials(); var selection = Assert.IsType<MaterialSelectionViewModel>(setup.CurrentStep);
        selection.EditSelectedCommand.Execute(null);
        var editor = Assert.IsType<MaterialEditorViewModel>(selection.Editor);
        editor.Inputs.Single(i => i.Key == "rho").Text = "";
        Assert.False(await editor.ConfirmAsync());
        Assert.Equal(Strings.SelfWeightMissingDensity, editor.Error); Assert.True(editor.HasFormError);
        Assert.Same(d.Material, setup.SelectedMaterial);
        var libraryEditor = new MaterialEditorViewModel(setup, Steel(null), SetupEditorMode.UserLibraryCreate);
        Assert.True(await libraryEditor.ConfirmAsync());
    }

    [Theory]
    [InlineData("g")] [InlineData("G")]
    public void ReservedNamesKeepLegacyEntitiesButBlockActivationUntilRenamed(string name)
    {
        var d = Document(); var support = d.Supports[0] with { Name = name };
        var editor = new EditorViewModel(d.WithSupports([support]), () => { });
        Assert.False(editor.ToggleSelfWeight()); Assert.Equal(Strings.SelfWeightNameConflict, editor.SelfWeightFeedback);
        Assert.Null(editor.Document.SelfWeight);
        Assert.True(editor.EditSupport(support.Id));
        Assert.True(editor.ConfirmSupport()); // An unchanged legacy name remains editable.
        editor.EditSupport(support.Id); editor.SupportDraft!.NameText = "A"; Assert.True(editor.ConfirmSupport());
        Assert.True(editor.ToggleSelfWeight());
        Assert.False(EntityNaming.TryNormalize(editor.Document, name, null, out _, out var error));
        Assert.Equal(NameValidationError.Reserved, error);
        var next = EntityNaming.Peek(editor.Document.WithSupports([], new(nextSupportOrdinal: 7)), AutoNameKind.Support);
        Assert.Equal("H", next.Name); Assert.Equal(8, next.Ordinal);
    }

    [Fact]
    public void DisplayOptionsRefreshActiveWeightWithoutChangingTheProjectOrAnalysis()
    {
        var s = new Observed(Document(true)); var editor = s.Editor;
        var revision = editor.Session.CurrentRevision; var result = editor.Presentation.Result;
        string original = editor.SelfWeightLabel;
        var changed = new List<string?>();
        editor.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        var options = new ResultPresentationOptions(UnitProfile.UnitedStates, 6, EngineeringNotation.Off);
        editor.ApplyResultPresentation(options);
        Assert.NotEqual(original, editor.SelfWeightLabel);
        Assert.Equal("g = " + QuantityFormatter.Format(editor.Document.SelfWeightLoad!.Intensity.NewtonsPerMeter,
            QuantityKind.DistributedLoad, options), editor.SelfWeightLabel);
        Assert.Equal(editor.SelfWeightLabel[4..], editor.Overview.DistributedLoads[0].Value);
        Assert.Equal(editor.SelfWeightLabel, editor.SelfWeightValue.PlainText);
        Assert.Equal("A = " + QuantityFormatter.Format(editor.Document.Section.Area.SquareMeters, QuantityKind.Area, options), editor.SelfWeightArea.PlainText);
        Assert.Equal("ρ = " + QuantityFormatter.Format(editor.Document.Material.Density!.Value.KilogramsPerCubicMeter, QuantityKind.Density, options), editor.SelfWeightDensity.PlainText);
        Assert.Contains(nameof(editor.SelfWeightArea), changed); Assert.Contains(nameof(editor.SelfWeightDensity), changed);
        Assert.Same(revision, editor.Session.CurrentRevision); Assert.Same(result, editor.Presentation.Result);
        Assert.Equal(1, s.Calls); Assert.Empty(editor.Session.UndoHistory);
    }

    [Theory]
    [InlineData("de-DE", "g = -28,2 N/m")]
    [InlineData("en-US", "g = -28.2 N/m")]
    public void FlyoutValuesUseCurrentSectionAndMaterialAndFollowUndo(string culture, string expectedWeight)
    {
        using var scope = new UiCultureScope(culture);
        var editor = new EditorViewModel(Document(true), () => { });
        string initialArea = editor.SelfWeightArea.PlainText;
        string initialDensity = editor.SelfWeightDensity.PlainText;
        var changed = new List<string?>(); editor.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        editor.ApplySetup(new RectangleSection(M(.06), M(.06)), Steel(800));
        Assert.Equal(expectedWeight, editor.SelfWeightValue.PlainText);
        Assert.Equal("A = 3\u202f600 mm²", editor.SelfWeightArea.PlainText);
        Assert.Equal("ρ = 800 kg/m³", editor.SelfWeightDensity.PlainText);
        Assert.Contains(nameof(editor.SelfWeightArea), changed); Assert.Contains(nameof(editor.SelfWeightDensity), changed);
        Assert.True(editor.Undo());
        Assert.Equal(initialArea, editor.SelfWeightArea.PlainText);
        Assert.Equal(initialDensity, editor.SelfWeightDensity.PlainText);
    }

    [Fact]
    public void WeightIsFirstInOverviewAndDoesNotConsumeDistributedLoadNames()
    {
        var editor = new EditorViewModel(Document(), () => { });
        void Add()
        {
            editor.ToggleDistributedLoadTool(); editor.HoverDistributedLoadPlacement(M(.5)); editor.PlaceDistributedLoadEndpoint();
            editor.HoverDistributedLoadPlacement(M(2)); editor.PlaceDistributedLoadEndpoint(); Assert.True(editor.ConfirmDistributedLoad());
        }
        Add(); Assert.True(editor.ToggleSelfWeight()); Add();
        Assert.Equal(new[] { "g", "q1", "q2" }, editor.Overview.DistributedLoads.Select(l => l.Name));
        Assert.Equal(new[] { "q1", "q2" }, editor.Document.DistributedLoads.Select(l => l.Name));
        Assert.Equal(3, editor.Document.NamingState.NextDistributedLoadNumber);
        editor.ToggleSelfWeightVisibility(); Assert.Equal("g", editor.Overview.DistributedLoads[0].Name);
        editor.DisableSelfWeight(); Assert.Equal(new[] { "q1", "q2" }, editor.Overview.DistributedLoads.Select(l => l.Name));
        Assert.True(editor.ToggleSelfWeight()); Assert.Equal(3, editor.Document.NamingState.NextDistributedLoadNumber);
    }
}
