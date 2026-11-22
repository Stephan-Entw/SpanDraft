using System.Text;
using System.Text.Json.Nodes;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.State;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

/// <summary>Spring supports extend format V5 additively with optional stiffness fields.</summary>
public sealed class ProjectFileSpringSupportTests
{
    private static byte[] Fixture(string version, string name) => File.ReadAllBytes(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", version, name + ".spandraft"));

    private static byte[] Bytes(JsonNode node) => Encoding.UTF8.GetBytes(node.ToJsonString());

    private static ProjectState WithSprings() => State() with
    {
        Document = State().Document.WithSupports([
            new(SupportId, M(0), SupportType.Pinned, "A"),
            new(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"), M(.3), SupportType.Spring, "B",
                new(TranslationalStiffness.FromNewtonsPerMeter(1234567.8901234567), null)),
            new(Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"), M(.6), SupportType.Spring, "C",
                new(null, RotationalStiffness.FromNewtonMetersPerRadian(0.1))),
            // A translational and a rotational spring at one position: two supports of their own.
            new(Guid.Parse("99999999-9999-9999-9999-999999999999"), M(.8), SupportType.Spring, "D",
                new(TranslationalStiffness.FromNewtonsPerMeter(1e12), null)),
            new(Guid.Parse("77777777-7777-7777-7777-777777777777"), M(.8), SupportType.Spring, "F",
                new(null, RotationalStiffness.FromNewtonMetersPerRadian(1e9))),
            new(Guid.Parse("88888888-8888-8888-8888-888888888888"), M(1), SupportType.Spring, "E",
                new(null, RotationalStiffness.FromNewtonMetersPerRadian(5e4), true))])
    };

    [Fact]
    public void SpringSupportsRoundtripBitForBitAndWriteOnlyTheFieldsTheyUse()
    {
        var state = WithSprings();
        byte[] bytes = ProjectFileCodec.Serialize(state);
        var loaded = ProjectFileCodec.Deserialize(bytes);
        Assert.True(state.ContentEquals(loaded));
        var root = JsonNode.Parse(bytes)!;
        Assert.Equal(5, root["formatVersion"]!.GetValue<int>());
        var supports = root["document"]!["supports"]!.AsArray();
        Assert.Equal(new[] { "id", "name", "type", "position" }, supports[0]!.AsObject().Select(p => p.Key));
        Assert.Equal(new[] { "id", "name", "type", "position", "translationalStiffness" }, supports[1]!.AsObject().Select(p => p.Key));
        Assert.Equal(new[] { "id", "name", "type", "position", "rotationalStiffness" }, supports[2]!.AsObject().Select(p => p.Key));
        Assert.Equal(new[] { "id", "name", "type", "position", "translationalStiffness" }, supports[3]!.AsObject().Select(p => p.Key));
        Assert.Equal(new[] { "id", "name", "type", "position", "rotationalStiffness" }, supports[4]!.AsObject().Select(p => p.Key));
        Assert.Equal(new[] { "id", "name", "type", "position", "rotationalStiffness", "rigidTranslation" },
            supports[5]!.AsObject().Select(p => p.Key));
        // Both springs of the shared station keep their own identity, name and position.
        Assert.Equal(supports[3]!["position"]!.GetValue<double>(), supports[4]!["position"]!.GetValue<double>());
        Assert.NotEqual(supports[3]!["id"]!.GetValue<Guid>(), supports[4]!["id"]!.GetValue<Guid>());
        Assert.Equal(new[] { "D", "F" }, new[] { supports[3]!["name"]!.GetValue<string>(), supports[4]!["name"]!.GetValue<string>() });
        Assert.All(supports.Skip(1), s => Assert.Equal("spring", s!["type"]!.GetValue<string>()));
        Assert.Equal(BitConverter.DoubleToInt64Bits(1234567.8901234567),
            BitConverter.DoubleToInt64Bits(loaded.Document.Supports[1].Spring!.Translational!.Value.NewtonsPerMeter));
        Assert.Equal(BitConverter.DoubleToInt64Bits(0.1),
            BitConverter.DoubleToInt64Bits(loaded.Document.Supports[2].Spring!.Rotational!.Value.NewtonMetersPerRadian));
    }

    [Fact]
    public void NormalWriterRejectsASingleEditorSupportWithBothSpringStiffnesses()
    {
        var state = WithSprings();
        var support = state.Document.Supports[1];
        // Bypass the document constructor's guard to test the writer against an invalid in-memory state.
        var combined = support.Spring! with { Rotational = RotationalStiffness.FromNewtonMetersPerRadian(1e4) };
        typeof(EditorSupport).GetProperty(nameof(EditorSupport.Spring))!.SetValue(support, combined);
        Assert.Throws<ProjectFormatException>(() => ProjectFileCodec.Serialize(state));
        Assert.Throws<ProjectFormatException>(() => ProjectFileCodec.ToDto(state));
        Assert.Same(combined, support.Spring);
    }

    [Fact]
    public void ImmutableFixtureWithACombinedEntryLoadsAsTwoSpringsAndSolves()
    {
        var loaded = ProjectFileCodec.Deserialize(Fixture("V5", "spring-supports"));
        var springs = loaded.Document.Supports.Where(s => s.Type == SupportType.Spring).ToArray();
        // The entry D with both stiffnesses is read as two supports; the rotational one follows its translational spring.
        Assert.Equal(5, springs.Length);
        Assert.Equal(2.5e6, springs[0].Spring!.Translational!.Value.NewtonsPerMeter);
        Assert.Null(springs[0].Spring!.Rotational);
        Assert.Equal(3e4, springs[1].Spring!.Rotational!.Value.NewtonMetersPerRadian);
        Assert.Null(springs[1].Spring!.Translational);
        var (translational, rotational) = (springs[2], springs[3]);
        Assert.Equal(("D", Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"), 1.5e6), (translational.Name, translational.Id,
            translational.Spring!.Translational!.Value.NewtonsPerMeter));
        Assert.Null(translational.Spring.Rotational);
        Assert.Equal(8e3, rotational.Spring!.Rotational!.Value.NewtonMetersPerRadian);
        Assert.Null(rotational.Spring.Translational);
        Assert.Equal(translational.Position, rotational.Position);
        // The next free support name, and an identity of its own that is the same every time the file is read.
        Assert.Equal("F", rotational.Name);
        Assert.NotEqual(translational.Id, rotational.Id);
        Assert.NotEqual(Guid.Empty, rotational.Id);
        Assert.Equal(7, loaded.Document.NamingState.NextSupportOrdinal);
        Assert.Equal(rotational.Id, ProjectFileCodec.Deserialize(Fixture("V5", "spring-supports")).Document.Supports.Single(s => s.Name == "F").Id);
        Assert.True(springs[4].Spring!.RigidTranslation);
        // Core sees the two springs of the station as one support with both springs.
        Assert.Equal(5, loaded.Document.ToBeamModel().Supports.Count);
        var solution = SolverTestSupport.Solve(loaded.Document.ToBeamModel());
        // The load -1000 N is carried by the reactions of all supports that hold the transverse translation.
        double total = solution.Nodes.Sum(n => n.ReactionY?.Newtons ?? 0);
        SolverTestSupport.Close(1000, total, 1e-6);
        // Saving writes the same content.
        Assert.True(loaded.ContentEquals(ProjectFileCodec.Deserialize(ProjectFileCodec.Serialize(loaded))));
    }

    [Fact]
    public async Task OpeningTheSpringFixtureNeitherWritesItNorMakesTheProjectDirty()
    {
        var app = new App(create: false);
        var path = TestPath("springs.spandraft");
        byte[] original = Fixture("V5", "spring-supports");
        app.Files.Data[path] = original;
        app.Dialogs.OpenPath = path;
        Assert.True(await app.Main.OpenAsync());
        Assert.Empty(app.Files.Writes);
        Assert.Equal(original, app.Files.Data[path]);
        Assert.False(app.Main.Session!.IsDirty);
    }

    [Theory]
    [InlineData("fixed")]
    [InlineData("pinned")]
    [InlineData("roller")]
    public void RigidSupportsCannotCarryStiffnessFields(string type)
    {
        foreach (var (field, value) in new (string, JsonNode)[]
            { ("translationalStiffness", 1000), ("rotationalStiffness", 1000), ("rigidTranslation", false) })
        {
            var root = JsonNode.Parse(Fixture("V5", "spring-supports"))!;
            var support = root["document"]!["supports"]![0]!.AsObject();
            support["type"] = type;
            support[field] = value;
            Assert.Throws<ProjectFormatException>(() => ProjectFileCodec.Deserialize(Bytes(root)));
        }
    }

    [Theory]
    [InlineData("translationalStiffness", "0")]
    [InlineData("translationalStiffness", "-5")]
    [InlineData("rotationalStiffness", "0")]
    [InlineData("rotationalStiffness", "-1")]
    [InlineData("translationalStiffness", "\"x\"")]
    [InlineData("translationalStiffness", "1e999")]
    [InlineData("rotationalStiffness", "-1e999")]
    public void InvalidStiffnessValuesAreRejected(string field, string json)
    {
        var root = JsonNode.Parse(Fixture("V5", "spring-supports"))!;
        // Support D carries both stiffnesses, so replacing one keeps the combination valid.
        root["document"]!["supports"]![3]![field] = JsonNode.Parse(json.Contains("e999") ? "\"OVERFLOW\"" : json);
        string output = root.ToJsonString().Replace("\"OVERFLOW\"", json);
        Assert.Throws<ProjectFormatException>(() => ProjectFileCodec.Deserialize(Encoding.UTF8.GetBytes(output)));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    public void SpringCombinationsOutsideTheTableAreRejected(int index, bool translational)
    {
        // Support index 1 = translational only, 2 = rotational only, 3 = both, 4 = partial restraint.
        var root = JsonNode.Parse(Fixture("V5", "spring-supports"))!;
        var supports = root["document"]!["supports"]!.AsArray();
        var (target, mutate) = (index, translational) switch
        {
            (0, _) => (1, (Action<JsonObject>)(s => s.Remove("translationalStiffness"))),
            (1, _) => (2, s => s.Remove("rotationalStiffness")),
            (2, _) => (4, s => s["translationalStiffness"] = 1000),
            _ => (3, s => s["rigidTranslation"] = true)
        };
        mutate(supports[target]!.AsObject());
        Assert.Throws<ProjectFormatException>(() => ProjectFileCodec.Deserialize(Bytes(root)));
    }

    [Fact]
    public void RigidTranslationMustBeAnExplicitBooleanAndMayBeAbsentOrFalseOnASpring()
    {
        var root = JsonNode.Parse(Fixture("V5", "spring-supports"))!;
        var support = root["document"]!["supports"]![3]!.AsObject();
        support["rigidTranslation"] = false;
        Assert.False(ProjectFileCodec.Deserialize(Bytes(root)).Document.Supports[3].Spring!.RigidTranslation);
        support["rigidTranslation"] = "yes";
        Assert.Throws<ProjectFormatException>(() => ProjectFileCodec.Deserialize(Bytes(root)));
    }

    [Fact]
    public void CoincidentSpringAndRigidSupportAreRejectedAndHistoricalVersionsKnowNoSpring()
    {
        var root = JsonNode.Parse(Fixture("V5", "spring-supports"))!;
        root["document"]!["supports"]![1]!["position"] = 0;
        Assert.Throws<ProjectFormatException>(() => ProjectFileCodec.Deserialize(Bytes(root)));

        var historical = JsonNode.Parse(Fixture("V5", "spring-supports"))!;
        historical["formatVersion"] = 4;
        foreach (var load in historical["document"]!["distributedLoads"]!.AsArray()) load!.AsObject().Clear();
        Assert.Throws<ProjectFormatException>(() => ProjectFileCodec.Deserialize(Bytes(historical)));
    }

    [Fact]
    public void FilesWithoutSpringFieldsStayValidAndReadAsRigidSupports()
    {
        foreach (var (version, name) in new[] { ("V4", "constant-load"), ("V5", "linear-loads") })
        {
            var loaded = ProjectFileCodec.Deserialize(Fixture(version, name));
            Assert.All(loaded.Document.Supports, s =>
            {
                Assert.NotEqual(SupportType.Spring, s.Type);
                Assert.Null(s.Spring);
            });
            var wire = JsonNode.Parse(ProjectFileCodec.Serialize(loaded))!["document"]!["supports"]!.AsArray();
            Assert.All(wire, s => Assert.Null(s!["translationalStiffness"] ?? s["rotationalStiffness"] ?? s["rigidTranslation"]));
        }
    }

    [Fact]
    public async Task RecoveryRoundtripKeepsSpringSupports()
    {
        var files = new ProjectTestSupport.Files();
        var recovery = new ProjectRecovery(new TestRecoveryStorage(), RecoverySlot.Project(RecoveryId.New()));
        var state = WithSprings();
        await recovery.FlushAsync(state, null);
        Assert.True(state.ContentEquals((await recovery.ReadAsync())!.State));
    }

    [Fact]
    public void ChangingOnlyAStiffnessIsAMechanicalChangeAndChangingAKindIsToo()
    {
        var state = WithSprings();
        var spring = state.Document.Supports[1];
        var stiffer = state with { Document = state.Document.WithSupports(state.Document.Supports.Select(s =>
            s.Id == spring.Id ? s with { Spring = new(TranslationalStiffness.FromNewtonsPerMeter(2e6), null) } : s)) };
        Assert.False(state.ContentEquals(stiffer));
        Assert.Equal(EditorChangeKind.Mechanical, EditorChangeClassifier.Classify(state, stiffer));
        var renamed = state with { Document = state.Document.WithSupports(state.Document.Supports.Select(s =>
            s.Id == spring.Id ? s with { Name = "Z" } : s)) };
        Assert.Equal(EditorChangeKind.MetadataOnly, EditorChangeClassifier.Classify(state, renamed));
    }
}
