using System.Text.Json.Nodes;
using SpanDraft.Core.Materials;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Sections.Parametric;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.Persistence;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

public sealed class ProjectSectionStateTests
{
    private static ProjectState Baseline() => State() with
    {
        Document = State().Document.WithSection(new CircleSectionGeometry(M(.125)), SectionAxisDesignation.Y)
    };

    private static ProjectState Change(ProjectState state, string field)
    {
        var d = state.Document; var m = d.Material;
        var document = field switch
        {
            "parameter" => d.WithSection(new CircleSectionGeometry(M(.126)), d.BendingAxis),
            "parametric" => d.WithSection(new ISectionGeometry(M(.2), M(.1), M(.01), M(.02), M(.005)), SectionAxisDesignation.Z),
            "manual" => d.WithSection(new ManualSectionDefinition(Area.FromSquareMeters(.003),
                new(SectionAxisDesignation.V, SecondMomentOfArea.FromMetersToTheFourth(8e-6), SectionModulus.FromCubicMeters(.0002))), SectionAxisDesignation.V),
            "axis" => d.WithBendingAxis(SectionAxisDesignation.Z),
            _ => d with { Material = new(field == "name" ? "Renamed" : m.Name,
                field == "E" ? Pressure.FromPascals(200e9) : m.YoungsModulus,
                field == "fy" ? Pressure.FromPascals(355e6) : m.YieldStrength,
                field == "density" ? MassDensity.FromKilogramsPerCubicMeter(7850) : m.Density,
                field == "poisson" ? PoissonRatio.FromValue(.3) : m.PoissonRatio) }
        };
        return state with { Document = document };
    }

    [Theory]
    [InlineData("density", false)]
    [InlineData("poisson", false)]
    [InlineData("name", false)]
    [InlineData("E", true)]
    [InlineData("fy", true)]
    [InlineData("axis", true)]
    [InlineData("parameter", true)]
    [InlineData("parametric", true)]
    [InlineData("manual", true)]
    public async Task ChangesUseOneClassifierAndSurviveHistorySavepointsSaveAsAndRecovery(string field, bool mechanical)
    {
        var app = new App(create: false); var path = TestPath("baseline.spandraft");
        var original = ProjectFileCodec.Serialize(Baseline());
        app.Files.Data[path] = original; app.Dialogs.OpenPath = path;
        Assert.True(await app.Main.OpenAsync());
        var session = app.Main.Session!; var before = session.CurrentRevision.State;
        var after = Change(before, field); var calls = app.Analyses;
        Assert.False(before.ContentEquals(after));
        Assert.Equal(!mechanical, BeamModelMechanicalComparer.AreEquivalent(before.Document.ToBeamModel(), after.Document.ToBeamModel()));
        Assert.Equal(mechanical ? EditorChangeKind.Mechanical : EditorChangeKind.MetadataOnly,
            EditorChangeClassifier.Classify(before.Document, after.Document, before.Presentation, after.Presentation));
        Assert.True(session.Commit(after));
        Assert.True(session.IsDirty); Assert.Equal(calls + (mechanical ? 1 : 0), app.Analyses);
        Assert.Single(session.UndoHistory);
        app.Delay.ReleaseAll(); await WaitFor(app.Recovery.DrainAsync());
        var recovered = (await app.Recovery.ReadAsync())!;
        Assert.True(after.ContentEquals(recovered.State));
        Assert.Equal(path, recovered.OriginalFilePath);
        Assert.Equal(2, JsonNode.Parse(app.Files.Data[App.Slot])!["project"]!["formatVersion"]!.GetValue<int>());
        Assert.True(ProjectSession.Restore(recovered.State, recovered.OriginalFilePath).IsDirty);
        Assert.True(await app.Main.SaveAsync(saveAs: true));
        var savedPath = app.Dialogs.SavePath!;
        Assert.Equal(savedPath, session.FilePath); Assert.False(session.IsDirty);
        Assert.Equal(original, app.Files.Data[path]);
        Assert.Equal(2, JsonNode.Parse(app.Files.Data[savedPath])!["formatVersion"]!.GetValue<int>());
        Assert.True(after.ContentEquals(ProjectFileCodec.Deserialize(app.Files.Data[savedPath])));
        Assert.Null(await app.Recovery.ReadAsync());
        Assert.True(app.Main.Undo()); Assert.True(session.IsDirty); Assert.True(before.ContentEquals(session.CurrentRevision.State));
        Assert.Equal(calls + (mechanical ? 2 : 0), app.Analyses);
        app.Delay.ReleaseAll(); await WaitFor(app.Recovery.DrainAsync());
        Assert.True(before.ContentEquals((await app.Recovery.ReadAsync())!.State));
        Assert.True(app.Main.Redo()); Assert.False(session.IsDirty); Assert.True(after.ContentEquals(session.CurrentRevision.State));
        Assert.Equal(calls + (mechanical ? 3 : 0), app.Analyses);
        await WaitFor(app.Recovery.DrainAsync()); Assert.Null(await app.Recovery.ReadAsync());
        var reopened = new App(create: false); reopened.Files.Data[savedPath] = app.Files.Data[savedPath];
        reopened.Dialogs.OpenPath = savedPath; Assert.True(await reopened.Main.OpenAsync());
        Assert.True(after.ContentEquals(reopened.Main.Session!.CurrentRevision.State));
        Assert.False(reopened.Main.Session.IsDirty); Assert.Empty(reopened.Main.Session.UndoHistory);
        Assert.True(await app.Main.NewAsync()); app.Main.Setup.ApplyCommand.Execute(null);
        Assert.True(app.Main.Session!.IsDirty); Assert.Empty(app.Main.Session.UndoHistory); Assert.Null(app.Main.Session.FilePath);
        Assert.Equal(SectionAxisDesignation.Y, app.Main.Editor!.Document.BendingAxis);
        Assert.Equal(7850, app.Main.Editor.Document.Material.Density!.Value.KilogramsPerCubicMeter);
        Assert.Equal(.3, app.Main.Editor.Document.Material.PoissonRatio!.Value.Value);
        app.Delay.ReleaseAll(); await WaitFor(app.Recovery.DrainAsync());
    }

    [Theory]
    [InlineData(4, SectionAxisDesignation.Y)]
    [InlineData(4, SectionAxisDesignation.Z)]
    [InlineData(7, SectionAxisDesignation.U)]
    [InlineData(7, SectionAxisDesignation.V)]
    public void DocumentMutationsPreserveTheExplicitAxis(int kind, SectionAxisDesignation axis)
    {
        var section = ParametricSectionTestSupport.Shape(kind);
        var d = State().Document.WithSection(section, axis);
        foreach (var changed in new[] { d.WithSupports(d.Supports), d.WithLoads(d.Loads), d.WithDistributedLoads(d.DistributedLoads),
            d with { Length = M(2) }, d with { Material = ProjectTemplates.Material } })
        {
            Assert.Same(section, changed.Section); Assert.Equal(axis, changed.BendingAxis);
            Assert.Equal(axis, changed.ToBeamModel().BendingAxis);
            Assert.Same(section.GetAxis(axis), changed.ToBeamModel().BendingAxisProperties);
        }
    }

    [Fact]
    public void InvalidSectionAxisPairIsRejectedDuringConstructionAndMutation()
    {
        var i = ParametricSectionTestSupport.Shape(4); var angle = ParametricSectionTestSupport.Shape(7);
        Assert.Throws<ArgumentException>(() => new EditorDocument(M(1), ProjectTemplates.Material, i, SectionAxisDesignation.U));
        Assert.Throws<ArgumentException>(() => new EditorDocument(M(1), ProjectTemplates.Material, angle, SectionAxisDesignation.Y));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EditorDocument(M(1), ProjectTemplates.Material, i, (SectionAxisDesignation)999));
        var d = new EditorDocument(M(1), ProjectTemplates.Material, i, SectionAxisDesignation.Z);
        Assert.Throws<ArgumentException>(() => d.WithSection(angle, SectionAxisDesignation.Z));
        Assert.Throws<ArgumentException>(() => d.WithBendingAxis(SectionAxisDesignation.U));
        Assert.Equal(SectionAxisDesignation.Z, d.BendingAxis);
        var manual = new ManualSectionDefinition(Area.FromSquareMeters(1), new(SectionAxisDesignation.Y,
            SecondMomentOfArea.FromMetersToTheFourth(1), SectionModulus.FromCubicMeters(1)));
        Assert.Throws<ArgumentException>(() => d.WithSection(manual, SectionAxisDesignation.Z));
    }

    [Fact]
    public void AxisIdentityIsMechanicalEvenForEqualCircleProperties()
    {
        var before = Baseline(); var after = before with { Document = before.Document.WithBendingAxis(SectionAxisDesignation.Z) };
        Assert.Equal(before.Document.Section.GetAxis(SectionAxisDesignation.Y).SecondMomentOfArea,
            after.Document.Section.GetAxis(SectionAxisDesignation.Z).SecondMomentOfArea);
        Assert.False(before.ContentEquals(after));
        Assert.False(BeamModelMechanicalComparer.AreEquivalent(before.Document.ToBeamModel(), after.Document.ToBeamModel()));
        Assert.Equal(SectionAxisDesignation.Y, ProjectFileCodec.Deserialize(ProjectFileCodec.Serialize(before)).Document.BendingAxis);
        Assert.Equal(SectionAxisDesignation.Z, ProjectFileCodec.Deserialize(ProjectFileCodec.Serialize(after)).Document.BendingAxis);
    }

    [Fact]
    public void DefinitionChangesAreContentChangesEvenWhenAllSelectedMechanicalValuesMatch()
    {
        var before = Baseline(); var d = before.Document; var axis = d.Section.GetAxis(d.BendingAxis);
        var manual = new ManualSectionDefinition(d.Section.Area,
            new(d.BendingAxis, axis.SecondMomentOfArea, axis.PositiveSectionModulus));
        var after = before with { Document = d.WithSection(manual, d.BendingAxis) };
        Assert.False(before.ContentEquals(after));
        Assert.True(BeamModelMechanicalComparer.AreEquivalent(d.ToBeamModel(), after.Document.ToBeamModel()));
        Assert.Equal(EditorChangeKind.MetadataOnly, EditorChangeClassifier.Classify(d, after.Document, before.Presentation, after.Presentation));
    }

    public static IEnumerable<object[]> ParameterChanges()
    {
        for (int kind = 0; kind < 8; kind++)
            foreach (var field in kind switch { 0 => new[] { "b", "h" }, 1 => ["b", "h", "t", "r"],
                2 => ["b"], 3 => ["b", "t"], 7 => ["b", "h", "t", "r"], _ => ["b", "h", "t", "tf", "r"] })
                yield return [kind, field];
    }

    [Theory]
    [MemberData(nameof(ParameterChanges))]
    public void EachOriginalShapeParameterAffectsContentEquality(int kind, string field)
    {
        var shape = ParametricSectionTestSupport.Shape(kind, r: .5);
        var next = ParametricSectionTestSupport.Shape(kind, b: field == "b" ? 6.1 : 6, h: field == "h" ? 10.1 : 10,
            t: field == "t" ? 1.1 : 1, tf: field == "tf" ? 1.1 : 1, r: field == "r" ? .6 : .5);
        var before = State() with { Document = State().Document.WithSection(shape, shape.Axes[0].AxisDesignation) };
        var same = before with { Document = before.Document.WithSection(ParametricSectionTestSupport.Shape(kind, r: .5), before.Document.BendingAxis) };
        var after = before with { Document = before.Document.WithSection(next, before.Document.BendingAxis) };
        Assert.True(before.ContentEquals(same));
        Assert.False(before.ContentEquals(after)); Assert.False(after.ContentEquals(before));
    }

    [Fact]
    public void ManualContentEqualityIncludesAreaDesignationIAndWAndCanonicalOrder()
    {
        ManualSectionDefinition Manual(double a = 1, double i = 1, double w = 1, SectionAxisDesignation axis = SectionAxisDesignation.Y) =>
            new(Area.FromSquareMeters(a), new(axis, SecondMomentOfArea.FromMetersToTheFourth(i), SectionModulus.FromCubicMeters(w)));
        var baseline = State() with { Document = State().Document.WithSection(Manual(), SectionAxisDesignation.Y) };
        foreach (var section in new[] { Manual(a: 2), Manual(i: 2), Manual(w: 2), Manual(axis: SectionAxisDesignation.Z) })
            Assert.False(baseline.ContentEquals(baseline with { Document = baseline.Document.WithSection(section, section.Axes[0].AxisDesignation) }));
        var y = new ManualSectionAxis(SectionAxisDesignation.Y, SecondMomentOfArea.FromMetersToTheFourth(1), SectionModulus.FromCubicMeters(1));
        var z = new ManualSectionAxis(SectionAxisDesignation.Z, SecondMomentOfArea.FromMetersToTheFourth(2), SectionModulus.FromCubicMeters(2));
        var yz = baseline with { Document = baseline.Document.WithSection(new ManualSectionDefinition(Area.FromSquareMeters(1), y, z), SectionAxisDesignation.Y) };
        var zy = baseline with { Document = baseline.Document.WithSection(new ManualSectionDefinition(Area.FromSquareMeters(1), z, y), SectionAxisDesignation.Y) };
        Assert.True(yz.ContentEquals(zy)); Assert.False(baseline.ContentEquals(yz));
    }
}
