using System.Text;
using System.Text.Json.Nodes;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Sections.Parametric;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.State;
using SpanDraft.Engineering;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

public sealed class ProjectFileV1MigrationTests
{
    private static readonly string[] Types = ["rectangle", "rectangularHollow", "circle", "circularHollow", "custom"];

    internal static byte[] HistoricalBytes(int kind) => File.ReadAllBytes(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "V1", Types[kind] + ".spandraft"));

    private static Section Legacy(int kind) => kind switch
    {
        0 => new RectangleSection(M(.04), M(.08)),
        1 => new RectangularHollowSection(M(.04), M(.08), M(.004)),
        2 => new CircleSection(M(.06)),
        3 => new CircularHollowSection(M(.06), M(.004)),
        _ => new CustomSection(Area.FromSquareMeters(.003), SecondMomentOfArea.FromMetersToTheFourth(8e-6),
            SectionModulus.FromCubicMeters(.0002))
    };

    // Frozen numerical expectations already used by LegacySectionAxisRegressionTests.
    [Theory]
    [InlineData(0, -.010230654761904778, -.007440476190476204, 70312500.00000007, 3.342222222222219)]
    [InlineData(1, -.024545716799195684, -.01785143039941504, 168696017.2744722, 1.393038222222222)]
    [InlineData(2, -.02744588475815559, -.019960643460476796, 141471060.52612922, 1.6611171155856028)]
    [InlineData(3, -.06297352773212586, -.0457989292597279, 324599911.12832147, .7239681587808549)]
    [InlineData(4, -.0021825396825396826, -.0015873015873015873, 15000000.000000002, 15.666666666666664)]
    public void HistoricalV1AndSavedV2PreserveFrozenMechanicalResults(int kind, double endW, double endTheta,
        double stress, double safety)
    {
        var bytes = HistoricalBytes(kind);
        var loaded = ProjectFileCodec.Deserialize(bytes);
        var doc = loaded.Document;
        var expectedType = kind switch { 0 => typeof(RectangleSectionGeometry), 1 => typeof(RectangularHollowSectionGeometry),
            2 => typeof(CircleSectionGeometry), 3 => typeof(CircularHollowSectionGeometry), _ => typeof(ManualSectionDefinition) };
        Assert.Equal(expectedType, doc.Section.GetType());
        Assert.Equal(SectionAxisDesignation.Y, doc.BendingAxis);
        Assert.Null(doc.Material.Density); Assert.Null(doc.Material.PoissonRatio);
        if (doc.Section is RectangularHollowSectionGeometry hollow) Assert.Equal(M(0), hollow.OuterRadius);
        var legacy = Legacy(kind);
        NumericAssert.Close(legacy.Area.SquareMeters, doc.Section.Area.SquareMeters);
        var axis = doc.Section.GetAxis(doc.BendingAxis);
        NumericAssert.Close(legacy.SecondMomentOfArea.MetersToTheFourth, axis.SecondMomentOfArea.MetersToTheFourth);
        NumericAssert.Close(legacy.SectionModulus.CubicMeters, axis.PositiveSectionModulus.CubicMeters);
        NumericAssert.Close(legacy.SectionModulus.CubicMeters, axis.NegativeSectionModulus.CubicMeters);
        var oldDocument = new EditorDocument(doc.Length, doc.Material, legacy, doc.Supports, doc.Loads, doc.NamingState, doc.DistributedLoads);
        Assert.True(loaded.ContentEquals(new(oldDocument, loaded.Presentation)));
        var baseline = SolverTestSupport.Solve(oldDocument.ToBeamModel());
        var solution = SolverTestSupport.Solve(doc.ToBeamModel());
        SolverTestSupport.SameResults(baseline, solution);
        var end = SolverTestSupport.At(solution, 2);
        SolverTestSupport.DisplacementClose(endW, end.TransverseDisplacement);
        SolverTestSupport.RotationClose(endTheta, end.RotationRadians);
        var engineering = BeamEngineeringAnalysis.Analyze(solution);
        NumericAssert.Close(stress, engineering.MaximumBendingStress.Pascals);
        NumericAssert.Close(safety, engineering.SafetyFactor);
        var v2 = ProjectFileCodec.Serialize(loaded);
        var again = ProjectFileCodec.Deserialize(v2);
        Assert.True(loaded.ContentEquals(again));
        Assert.True(BeamModelMechanicalComparer.AreEquivalent(doc.ToBeamModel(), again.Document.ToBeamModel()));
        SolverTestSupport.SameResults(solution, SolverTestSupport.Solve(again.Document.ToBeamModel()));
        var wire = JsonNode.Parse(v2)!;
        Assert.Equal(2, wire["formatVersion"]!.GetValue<int>());
        Assert.Equal(kind == 4 ? "manual" : Types[kind], wire["document"]!["section"]!["type"]!.GetValue<string>());
        Assert.Null(wire["document"]!["material"]!["density"]);
        Assert.Null(wire["document"]!["material"]!["poissonRatio"]);
        Assert.Equal(HistoricalBytes(kind), bytes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void LegacyWriterUsesOnlyCanonicalV2Types(int kind)
    {
        var migrated = ProjectFileCodec.Deserialize(HistoricalBytes(kind));
        var legacy = migrated with { Document = migrated.Document.WithSection(Legacy(kind), SectionAxisDesignation.Y) };
        var bytes = ProjectFileCodec.Serialize(legacy);
        var loaded = ProjectFileCodec.Deserialize(bytes);
        Assert.True(legacy.ContentEquals(loaded));
        Assert.Equal(migrated.Document.Section.GetType(), loaded.Document.Section.GetType());
        Assert.DoesNotContain("\"custom\"", Encoding.UTF8.GetString(bytes));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task OpeningHistoricalFileDoesNotWriteItAndSaveUpgradesSamePath(int kind)
    {
        var app = new App(create: false);
        var path = TestPath("historical.spandraft");
        var original = HistoricalBytes(kind);
        app.Files.Data[path] = original;
        app.Dialogs.OpenPath = path;
        Assert.True(await app.Main.OpenAsync());
        Assert.Empty(app.Files.Writes);
        Assert.Equal(original, app.Files.Data[path]);
        Assert.Equal(path, app.Main.Session!.FilePath);
        Assert.False(app.Main.Session.IsDirty); Assert.Empty(app.Main.Session.UndoHistory);
        Assert.True(await app.Main.SaveAsync());
        Assert.Equal(path, app.Main.Session.FilePath);
        Assert.Equal(2, JsonNode.Parse(app.Files.Data[path])!["formatVersion"]!.GetValue<int>());
        Assert.True(ProjectFileCodec.Deserialize(original).ContentEquals(ProjectFileCodec.Deserialize(app.Files.Data[path])));
        Assert.False(app.Main.Session.IsDirty);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task HistoricalRecoveryUsesTheGeneralReaderAndSubsequentRecoveryWritesV2(int kind)
    {
        var files = new Files(); var path = TestPath("historical-recovery.json");
        files.Data[path] = Encoding.UTF8.GetBytes(new JsonObject
        {
            ["recoveryVersion"] = 1, ["originalFilePath"] = TestPath("old.spandraft"),
            ["writtenAtUtc"] = Now, ["project"] = JsonNode.Parse(HistoricalBytes(kind))
        }.ToJsonString());
        var recovery = new ProjectRecovery(files, path);
        var snapshot = (await recovery.ReadAsync())!;
        Assert.True(ProjectFileCodec.Deserialize(HistoricalBytes(kind)).ContentEquals(snapshot.State));
        Assert.Empty(files.Writes);
        var session = ProjectSession.Restore(snapshot.State, snapshot.OriginalFilePath);
        Assert.True(session.IsDirty);
        await recovery.FlushAsync(session.CurrentRevision.State, session.FilePath);
        var root = JsonNode.Parse(files.Data[path])!;
        Assert.Equal(1, root["recoveryVersion"]!.GetValue<int>());
        Assert.Equal(2, root["project"]!["formatVersion"]!.GetValue<int>());
        Assert.True(snapshot.State.ContentEquals((await recovery.ReadAsync())!.State));
    }

    [Theory]
    [InlineData("document.material", "youngsModulus")]
    [InlineData("document.material", "yieldStrength")]
    [InlineData("document", "supports")]
    [InlineData("document.section", "width")]
    [InlineData("document.section", "height")]
    public void FrozenV1RequiredFieldsRemainRequired(string parentPath, string field)
    {
        var root = JsonNode.Parse(HistoricalBytes(0))!;
        var parent = root;
        foreach (var p in parentPath.Split('.')) parent = parent[p]!;
        parent.AsObject().Remove(field);
        Assert.Throws<ProjectFormatException>(() => ProjectFileCodec.Deserialize(Encoding.UTF8.GetBytes(root.ToJsonString())));
    }

    [Fact]
    public void V1KnownIrrelevantFieldsRemainFiniteAndV2OnlyFieldsAreIgnored()
    {
        var root = JsonNode.Parse(HistoricalBytes(0))!;
        root["document"]!["material"]!["density"] = 7850;
        root["document"]!["material"]!["poissonRatio"] = .3;
        root["document"]!["bendingAxis"] = "z";
        var loaded = ProjectFileCodec.Deserialize(Encoding.UTF8.GetBytes(root.ToJsonString()));
        Assert.Null(loaded.Document.Material.Density); Assert.Null(loaded.Document.Material.PoissonRatio);
        Assert.Equal(SectionAxisDesignation.Y, loaded.Document.BendingAxis);
        root["document"]!["section"]!["diameter"] = "TOKEN";
        var json = root.ToJsonString().Replace("\"TOKEN\"", "1e999", StringComparison.Ordinal);
        Assert.Throws<ProjectFormatException>(() => ProjectFileCodec.Deserialize(Encoding.UTF8.GetBytes(json)));
    }
}
