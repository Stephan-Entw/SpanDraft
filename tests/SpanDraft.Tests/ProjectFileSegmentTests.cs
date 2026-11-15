using System.Text;
using System.Text.Json.Nodes;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.State;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

/// <summary>Beam segments extend format V5 with the optional list of further segments, each with a length and an identity.</summary>
public sealed class ProjectFileSegmentTests
{
    private static byte[] Fixture(string version, string name) => File.ReadAllBytes(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", version, name + ".spandraft"));

    private static byte[] Bytes(JsonNode node) => Encoding.UTF8.GetBytes(node.ToJsonString());

    [Fact]
    public void ImmutableFixtureLoadsAllSegmentsWithTheirOwnPropertiesAndSolvesThem()
    {
        var loaded = ProjectFileCodec.Deserialize(Fixture("V5", "segments"));
        var document = loaded.Document;

        Assert.Equal([M(1.2), M(2)], document.SegmentBoundaries);
        Assert.Equal(3, document.Segments.Count);
        Assert.Equal(["Test steel", "Light alloy", "Test steel"], document.Segments.Select(s => s.Material.Name));
        Assert.Equal(["Shaft 40 x 80", null, "Shaft turned"], document.Segments.Select(s => s.SectionDisplayName));
        Assert.Equal([SectionAxisDesignation.Y, SectionAxisDesignation.Y, SectionAxisDesignation.Z],
            document.Segments.Select(s => s.BendingAxis));
        Assert.Equal(70e9, document.Segments[1].Material.YoungsModulus.Pascals);
        Assert.Equal(3, document.SelfWeightLoads.Count);
        var solution = SolverTestSupport.Solve(document.ToBeamModel());
        double loads = 2000 + document.SelfWeightLoads.Sum(l => -l.StartIntensity.NewtonsPerMeter *
            (l.EndPosition.Meters - l.StartPosition.Meters));
        SolverTestSupport.Close(loads, solution.Nodes.Sum(n => n.ReactionY?.Newtons ?? 0), 1e-6);
        Assert.True(loaded.ContentEquals(ProjectFileCodec.Deserialize(ProjectFileCodec.Serialize(loaded))));
    }

    [Fact]
    public void RoundtripKeepsSegmentsBitForBitAndWritesOnlyWhatIsNeeded()
    {
        var loaded = ProjectFileCodec.Deserialize(Fixture("V5", "segments"));
        byte[] bytes = ProjectFileCodec.Serialize(loaded);
        var root = JsonNode.Parse(bytes)!["document"]!;

        Assert.Equal(5, JsonNode.Parse(bytes)!["formatVersion"]!.GetValue<int>());
        var further = root["furtherSegments"]!.AsArray();
        // The draft form with starts is rewritten with the segment lengths and identities.
        Assert.Equal(["id", "length", "material", "section", "bendingAxis"], further[0]!.AsObject().Select(p => p.Key));
        Assert.Equal(["id", "length", "material", "section", "bendingAxis", "sectionDisplayName"], further[1]!.AsObject().Select(p => p.Key));
        Assert.Equal([1.2, 0.8, 1.0], new[] { root["length"]!.GetValue<double>(),
            further[0]!["length"]!.GetValue<double>(), further[1]!["length"]!.GetValue<double>() });
        Assert.Equal("Shaft 40 x 80", root["sectionDisplayName"]!.GetValue<string>());
        Assert.Equal(bytes, ProjectFileCodec.Serialize(ProjectFileCodec.Deserialize(bytes)));
    }

    [Fact]
    public void OneSegmentFilesNeitherWriteTheFieldNorChangeTheirContent()
    {
        foreach (var (version, name) in new[] { ("V4", "constant-load"), ("V5", "linear-loads"), ("V5", "spring-supports") })
        {
            var loaded = ProjectFileCodec.Deserialize(Fixture(version, name));
            Assert.Single(loaded.Document.Segments);
            Assert.Empty(loaded.Document.SegmentBoundaries);
            var written = JsonNode.Parse(ProjectFileCodec.Serialize(loaded))!["document"]!.AsObject();
            Assert.False(written.ContainsKey("furtherSegments"));
        }
    }

    [Fact]
    public async Task OpeningTheSegmentFixtureNeitherWritesItNorMakesTheProjectDirty()
    {
        var app = new App(create: false);
        var path = TestPath("segments.spandraft");
        byte[] original = Fixture("V5", "segments");
        app.Files.Data[path] = original;
        app.Dialogs.OpenPath = path;
        Assert.True(await app.Main.OpenAsync());
        Assert.Empty(app.Files.Writes);
        Assert.Equal(original, app.Files.Data[path]);
        Assert.False(app.Main.Session!.IsDirty);
        Assert.Equal(3, app.Main.Session.CurrentRevision.State.Document.Segments.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(1.2)]
    [InlineData(-1)]
    public void ASegmentStartMustLieStrictlyInsideTheBeamAndAscend(double start)
    {
        var root = JsonNode.Parse(Fixture("V5", "segments"))!;
        root["document"]!["furtherSegments"]![1]!["start"] = start;
        Assert.Throws<ProjectFormatException>(() => ProjectFileCodec.Deserialize(Bytes(root)));
    }

    [Fact]
    public void UnorderedSegmentsAreRejected()
    {
        var root = JsonNode.Parse(Fixture("V5", "segments"))!;
        var further = root["document"]!["furtherSegments"]!.AsArray();
        root["document"]!["furtherSegments"] = new JsonArray(further[1]!.DeepClone(), further[0]!.DeepClone());
        Assert.Throws<ProjectFormatException>(() => ProjectFileCodec.Deserialize(Bytes(root)));
    }

    [Theory]
    [InlineData("material")]
    [InlineData("section")]
    [InlineData("bendingAxis")]
    [InlineData("start")]
    public void ASegmentWithoutItsRequiredFieldsIsRejected(string field)
    {
        var root = JsonNode.Parse(Fixture("V5", "segments"))!;
        root["document"]!["furtherSegments"]![0]!.AsObject().Remove(field);
        Assert.Throws<ProjectFormatException>(() => ProjectFileCodec.Deserialize(Bytes(root)));
    }

    [Fact]
    public void ASegmentAxisThatItsSectionDoesNotProvideIsRejected()
    {
        var root = JsonNode.Parse(Fixture("V5", "segments"))!;
        root["document"]!["furtherSegments"]![0]!["bendingAxis"] = "u";
        Assert.Throws<ProjectFormatException>(() => ProjectFileCodec.Deserialize(Bytes(root)));
        root["document"]!["furtherSegments"]![0]!["bendingAxis"] = "x";
        Assert.Throws<ProjectFormatException>(() => ProjectFileCodec.Deserialize(Bytes(root)));
    }

    [Fact]
    public void SelfWeightNeedsTheDensityOfEverySegmentAndAnUnconfirmedStrengthBlocksSaving()
    {
        var root = JsonNode.Parse(Fixture("V5", "segments"))!;
        root["document"]!["furtherSegments"]![0]!["material"]!.AsObject().Remove("density");
        Assert.Throws<ProjectFormatException>(() => ProjectFileCodec.Deserialize(Bytes(root)));

        root = JsonNode.Parse(Fixture("V5", "segments"))!;
        root["document"]!["furtherSegments"]![1]!["material"]!.AsObject().Remove("strengthType");
        var legacy = ProjectFileCodec.Deserialize(Bytes(root));
        Assert.Null(legacy.Document.Segments[2].Material.StrengthType);
        Assert.Throws<ProjectFormatException>(() => ProjectFileCodec.Serialize(legacy));
    }

    [Fact]
    public async Task RecoveryRoundtripKeepsSegments()
    {
        var files = new ProjectTestSupport.Files();
        var recovery = new ProjectRecovery(files, "project-recovery");
        var state = ProjectFileCodec.Deserialize(Fixture("V5", "segments"));
        await recovery.FlushAsync(state, null);
        Assert.True(state.ContentEquals((await recovery.ReadAsync())!.State));
    }

    [Fact]
    public void ChangingASegmentOrABoundaryChangesTheContent()
    {
        var state = ProjectFileCodec.Deserialize(Fixture("V5", "segments"));
        Assert.False(state.ContentEquals(state with { Document = state.Document.WithBoundary(0, M(1.1)) }));
        Assert.False(state.ContentEquals(state with { Document = state.Document.WithoutBoundary(0) }));
        Assert.False(state.ContentEquals(state with { Document = state.Document.WithBoundary(1, M(2.1)) }));
    }
}
