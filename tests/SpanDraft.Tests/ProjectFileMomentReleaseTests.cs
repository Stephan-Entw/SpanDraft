using System.Text;
using System.Text.Json.Nodes;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.State;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

/// <summary>Internal moment releases extend format V5 additively; files without them read and write as before.</summary>
public sealed class ProjectFileMomentReleaseTests
{
    private static byte[] Fixture(string version, string name) => File.ReadAllBytes(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", version, name + ".spandraft"));

    private static byte[] Bytes(JsonNode node) => Encoding.UTF8.GetBytes(node.ToJsonString());

    private static JsonNode Root() => JsonNode.Parse(Fixture("V5", "moment-releases"))!;
    private static JsonArray Releases(JsonNode root) => root["document"]!["momentReleases"]!.AsArray();

    private static ProjectState NoOffsets(ProjectState state) =>
        state with { Presentation = new(new Dictionary<Guid, AnnotationOffset>(), true) };

    private static void Rejected(JsonNode root) => Assert.Throws<ProjectFormatException>(() => ProjectFileCodec.Deserialize(Bytes(root)));

    [Fact]
    public void TheImmutableFixtureLoadsItsReleasesWithIdentityNamesAndCounter()
    {
        var loaded = ProjectFileCodec.Deserialize(Fixture("V5", "moment-releases"));
        Assert.Equal([5.5, 8.5], loaded.Document.MomentReleases.Select(r => r.Position.Meters));
        Assert.Equal(["G1", "Hinge 2"], loaded.Document.MomentReleases.Select(r => r.Name));
        Assert.Equal(Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"), loaded.Document.MomentReleases[0].Id);
        Assert.Equal(3, loaded.Document.NamingState.NextMomentReleaseNumber);
        Assert.Contains(Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"), loaded.Presentation.AnnotationOffsets.Keys);
        Assert.Equal(2, loaded.Document.ToBeamModel().MomentReleases.Count);
    }

    [Fact]
    public void TheFixtureSolvesAsAGerberBeamAndSavingWritesTheSameContent()
    {
        var loaded = ProjectFileCodec.Deserialize(Fixture("V5", "moment-releases"));
        var solution = SolverTestSupport.Solve(loaded.Document.ToBeamModel());
        foreach (var release in loaded.Document.MomentReleases)
            Assert.Equal(0, solution.EvaluateAt(release.Position, SpanDraft.Solver.EvaluationSide.Right).BendingMoment.NewtonMeters);
        Assert.Equal(2, solution.Nodes.Count(n => n.RotationLeftRadians.HasValue));
        Assert.True(loaded.ContentEquals(ProjectFileCodec.Deserialize(ProjectFileCodec.Serialize(loaded))));
    }

    [Fact]
    public void ReleasesRoundtripBitForBitAndOnlyAReleaseProjectWritesTheOptionalFields()
    {
        var state = ProjectFileCodec.Deserialize(Fixture("V5", "moment-releases"));
        byte[] bytes = ProjectFileCodec.Serialize(state);
        var root = JsonNode.Parse(bytes)!;
        Assert.Equal(5, root["formatVersion"]!.GetValue<int>());
        Assert.Equal(new[] { "id", "name", "position" }, Releases(root)[0]!.AsObject().Select(p => p.Key));
        Assert.Equal(3, root["document"]!["namingState"]!["nextMomentReleaseNumber"]!.GetValue<long>());
        Assert.True(state.ContentEquals(ProjectFileCodec.Deserialize(bytes)));
        var odd = NoOffsets(state) with { Document = state.Document.WithMomentReleases([new(Guid.NewGuid(), M(0.1 + 0.2), "G1")]) };
        var back = ProjectFileCodec.Deserialize(ProjectFileCodec.Serialize(odd));
        Assert.Equal(BitConverter.DoubleToInt64Bits(0.1 + 0.2), BitConverter.DoubleToInt64Bits(back.Document.MomentReleases[0].Position.Meters));

        // Without any release neither field is written, so the file equals the output of earlier V5 writers.
        var plain = ProjectFileCodec.Deserialize(Fixture("V5", "spring-supports"));
        var plainRoot = JsonNode.Parse(ProjectFileCodec.Serialize(plain))!;
        Assert.Null(plainRoot["document"]!["momentReleases"]);
        Assert.Null(plainRoot["document"]!["namingState"]!["nextMomentReleaseNumber"]);
    }

    [Fact]
    public void ADeletedReleaseStillKeepsItsCounter()
    {
        var state = ProjectFileCodec.Deserialize(Fixture("V5", "moment-releases"));
        var none = NoOffsets(state) with { Document = state.Document.WithMomentReleases([]) };
        var root = JsonNode.Parse(ProjectFileCodec.Serialize(none))!;
        Assert.Null(root["document"]!["momentReleases"]);
        Assert.Equal(3, root["document"]!["namingState"]!["nextMomentReleaseNumber"]!.GetValue<long>());
        Assert.Equal(3, ProjectFileCodec.Deserialize(Bytes(root)).Document.NamingState.NextMomentReleaseNumber);
    }

    [Fact]
    public void EarlierVersionsAndAnOlderV5FileHaveNoReleasesAndReadWithTheDefaultCounter()
    {
        foreach (var (version, name) in new[] { ("V1", "rectangle"), ("V4", "constant-load"), ("V5", "linear-loads"), ("V5", "spring-supports"), ("V5", "segments") })
        {
            var loaded = ProjectFileCodec.Deserialize(Fixture(version, name));
            Assert.Empty(loaded.Document.MomentReleases);
            Assert.Equal(1, loaded.Document.NamingState.NextMomentReleaseNumber);
        }
        var historical = Root();
        historical["formatVersion"] = 4;
        // A V4 reader knows no releases: the field is ignored there, never interpreted.
        foreach (var load in historical["document"]!["distributedLoads"]!.AsArray()) load!.AsObject().Clear();
        historical["document"]!["distributedLoads"] = new JsonArray();
        historical["presentation"]!["annotationOffsets"] = new JsonArray();
        Assert.Empty(ProjectFileCodec.Deserialize(Bytes(historical)).Document.MomentReleases);
    }

    [Fact]
    public async Task OpeningTheFixtureNeitherWritesItNorMakesTheProjectDirty()
    {
        var app = new App(create: false);
        var path = TestPath("releases.spandraft");
        byte[] original = Fixture("V5", "moment-releases");
        app.Files.Data[path] = original;
        app.Dialogs.OpenPath = path;
        Assert.True(await app.Main.OpenAsync());
        Assert.Empty(app.Files.Writes);
        Assert.Equal(original, app.Files.Data[path]);
        Assert.False(app.Main.Session!.IsDirty);
        Assert.Equal(2, app.Main.Session.CurrentRevision.State.Document.MomentReleases.Count);
    }

    [Fact]
    public async Task RecoveryRoundtripKeepsReleasesAndTheirCounter()
    {
        var files = new ProjectTestSupport.Files();
        var recovery = new ProjectRecovery(files, "project-recovery");
        var state = ProjectFileCodec.Deserialize(Fixture("V5", "moment-releases"));
        await recovery.FlushAsync(state, null);
        var restored = (await recovery.ReadAsync())!.State;
        Assert.True(state.ContentEquals(restored));
        Assert.Equal(3, restored.Document.NamingState.NextMomentReleaseNumber);
    }

    [Theory]
    [InlineData("zero position")]
    [InlineData("at the end")]
    [InlineData("beyond the end")]
    [InlineData("duplicate position")]
    [InlineData("on a fixed support")]
    [InlineData("on a point moment")]
    [InlineData("on a rotational spring")]
    [InlineData("duplicate id")]
    [InlineData("id reused by a support")]
    [InlineData("empty id")]
    [InlineData("duplicate name")]
    [InlineData("name of a support")]
    [InlineData("reserved name")]
    [InlineData("untrimmed name")]
    [InlineData("empty name")]
    [InlineData("missing position")]
    [InlineData("infinite position")]
    [InlineData("null entry")]
    [InlineData("counter below one")]
    [InlineData("annotation of an unknown release")]
    public void CoreInvalidAndMalformedReleaseDataAreRejectedWithAFormatError(string defect)
    {
        var root = Root();
        var releases = Releases(root);
        var supports = root["document"]!["supports"]!.AsArray();
        switch (defect)
        {
            case "zero position": releases[0]!["position"] = 0; break;
            case "at the end": releases[0]!["position"] = 10; break;
            case "beyond the end": releases[0]!["position"] = 10.5; break;
            case "duplicate position": releases[1]!["position"] = 5.5; break;
            case "on a fixed support": supports[1]!["type"] = "fixed"; releases[0]!["position"] = 4; break;
            case "on a point moment":
                root["document"]!["pointLoads"]!.AsArray().Add(new JsonObject
                {
                    ["id"] = "33333333-3333-3333-3333-333333333333", ["name"] = "M1", ["type"] = "moment", ["position"] = 5.5, ["value"] = 0
                });
                break;
            case "on a rotational spring":
                supports[2]!["type"] = "spring"; supports[2]!["rotationalStiffness"] = 5000; releases[0]!["position"] = 7; break;
            case "duplicate id": releases[1]!["id"] = releases[0]!["id"]!.GetValue<string>(); break;
            case "id reused by a support": releases[1]!["id"] = supports[0]!["id"]!.GetValue<string>(); break;
            case "empty id": releases[1]!["id"] = Guid.Empty.ToString(); break;
            case "duplicate name": releases[1]!["name"] = "g1"; break;
            case "name of a support": releases[1]!["name"] = "A"; break;
            case "reserved name": releases[1]!["name"] = "g"; root["document"]!["selfWeight"] = new JsonObject
                { ["id"] = "44444444-4444-4444-4444-444444444444", ["enabled"] = true }; break;
            case "untrimmed name": releases[1]!["name"] = " G2 "; break;
            case "empty name": releases[1]!["name"] = ""; break;
            case "missing position": releases[1]!.AsObject().Remove("position"); break;
            case "infinite position": releases[1]!["position"] = JsonNode.Parse("1e999"); break;
            case "null entry": releases[1] = null; break;
            case "counter below one": root["document"]!["namingState"]!["nextMomentReleaseNumber"] = 0; break;
            case "annotation of an unknown release":
                root["presentation"]!["annotationOffsets"]!.AsArray().Add(new JsonObject
                {
                    ["entityId"] = "55555555-5555-5555-5555-555555555555", ["dx"] = 1, ["dy"] = 1
                });
                break;
            default: throw new ArgumentOutOfRangeException(nameof(defect));
        }
        string json = root.ToJsonString();
        Assert.Throws<ProjectFormatException>(() => ProjectFileCodec.Deserialize(Encoding.UTF8.GetBytes(
            defect == "infinite position" ? json.Replace("\"1e999\"", "1e999") : json)));
    }

    [Fact]
    public void AMechanismFileLoadsBecauseStabilityIsTheSolversBusinessNotTheFormats()
    {
        var root = Root();
        // A third hinge in the second span: pinned and three rollers keep it a mechanism only for the solver.
        Releases(root).Add(new JsonObject { ["id"] = "66666666-6666-6666-6666-666666666666", ["name"] = "G3", ["position"] = 8 });
        var loaded = ProjectFileCodec.Deserialize(Bytes(root));
        Assert.Equal(3, loaded.Document.MomentReleases.Count);
        Assert.Equal(SpanDraft.Solver.SolverErrorCode.UnstableModel,
            Assert.Throws<SpanDraft.Solver.BeamSolverException>(() => SolverTestSupport.Solve(loaded.Document.ToBeamModel())).Code);
    }

    [Fact]
    public void ReleaseFieldsInUnrelatedFormatVersionsAreIgnoredNotInterpreted()
    {
        var root = JsonNode.Parse(Fixture("V5", "linear-loads"))!;
        root["document"]!["momentReleases"] = new JsonArray(new JsonObject
        {
            ["id"] = "77777777-7777-7777-7777-777777777777", ["name"] = "G1", ["position"] = 0.5
        });
        // In V5 the field is real; in V4 the same text is an unknown property.
        Assert.Single(ProjectFileCodec.Deserialize(Bytes(root)).Document.MomentReleases);
        root["formatVersion"] = 4;
        root["document"]!["distributedLoads"] = new JsonArray();
        Assert.Empty(ProjectFileCodec.Deserialize(Bytes(root)).Document.MomentReleases);
    }

    [Fact]
    public void ChangingOnlyAReleasePositionIsMechanicalAndRenamingItIsMetadataOnly()
    {
        var state = ProjectFileCodec.Deserialize(Fixture("V5", "moment-releases"));
        var first = state.Document.MomentReleases[0];
        Assert.Equal(EditorChangeKind.Mechanical, EditorChangeClassifier.Classify(state, state with
            { Document = state.Document.WithMomentReleases(state.Document.MomentReleases.Select(r => r.Id == first.Id ? r with { Position = M(5.25) } : r)) }));
        Assert.Equal(EditorChangeKind.MetadataOnly, EditorChangeClassifier.Classify(state, state with
            { Document = state.Document.WithMomentReleases(state.Document.MomentReleases.Select(r => r.Id == first.Id ? r with { Name = "Z" } : r)) }));
    }
}
