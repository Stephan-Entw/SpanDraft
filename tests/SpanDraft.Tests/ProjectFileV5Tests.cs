using System.Text;
using System.Text.Json.Nodes;
using SpanDraft.Core.Loads;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.State;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

/// <summary>Project format V5 carries a start and an end intensity per distributed load.</summary>
public sealed class ProjectFileV5Tests
{
    private static byte[] Fixture(string version, string name) => File.ReadAllBytes(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", version, name + ".spandraft"));

    private static ProjectState Linear() => State() with
    {
        Document = State().Document.WithDistributedLoads([
            new(Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"), M(0), M(.75), ForcePerLength.FromNewtonsPerMeter(-1234.5678901234567),
                ForcePerLength.FromNewtonsPerMeter(987.65432109876543), "q8"),
            new(Guid.Parse("99999999-9999-9999-9999-999999999999"), M(.25), M(1), ForcePerLength.FromNewtonsPerMeter(0),
                ForcePerLength.FromNewtonsPerMeter(0), "q9")])
    };

    [Fact]
    public void LinearLoadsRoundtripBitForBitWithBothIntensitiesAndNoSingleIntensityField()
    {
        var state = Linear();
        byte[] bytes = ProjectFileCodec.Serialize(state);
        var loaded = ProjectFileCodec.Deserialize(bytes);
        Assert.True(state.ContentEquals(loaded));
        Assert.Equal(5, JsonNode.Parse(bytes)!["formatVersion"]!.GetValue<int>());
        var loads = JsonNode.Parse(bytes)!["document"]!["distributedLoads"]!.AsArray();
        Assert.Equal(new[] { "id", "name", "startPosition", "endPosition", "startIntensity", "endIntensity" },
            loads[0]!.AsObject().Select(p => p.Key));
        Assert.All(loads, load => Assert.Null(load!["intensity"]));
        for (int i = 0; i < 2; i++)
        {
            Assert.Equal(BitConverter.DoubleToInt64Bits(state.Document.DistributedLoads[i].StartIntensity.NewtonsPerMeter),
                BitConverter.DoubleToInt64Bits(loaded.Document.DistributedLoads[i].StartIntensity.NewtonsPerMeter));
            Assert.Equal(BitConverter.DoubleToInt64Bits(state.Document.DistributedLoads[i].EndIntensity.NewtonsPerMeter),
                BitConverter.DoubleToInt64Bits(loaded.Document.DistributedLoads[i].EndIntensity.NewtonsPerMeter));
        }
    }

    [Fact]
    public void ContentComparisonSeesAChangeOfTheEndIntensityAlone()
    {
        var state = Linear();
        var load = state.Document.DistributedLoads[0];
        var changed = state with { Document = state.Document.WithDistributedLoads(
            [load with { EndIntensity = ForcePerLength.FromNewtonsPerMeter(load.EndIntensity.NewtonsPerMeter + 1) },
                state.Document.DistributedLoads[1]]) };
        Assert.False(state.ContentEquals(changed));
        Assert.False(BeamModelMechanicalComparer.AreEquivalent(state.Document.ToBeamModel(), changed.Document.ToBeamModel()));
    }

    [Fact]
    public void ImmutableVersionFourFixtureLoadsItsSingleIntensityAsBothEnds()
    {
        byte[] original = Fixture("V4", "constant-load");
        var loaded = ProjectFileCodec.Deserialize(original);
        var load = Assert.Single(loaded.Document.DistributedLoads);
        Assert.Equal(-500, load.StartIntensity.NewtonsPerMeter);
        Assert.Equal(-500, load.EndIntensity.NewtonsPerMeter);
        Assert.True(load.IsConstant);
        Assert.Equal("Cantilever with constant load", loaded.Info!.CalculationTitle);
        // Statics of the cantilever: the root carries the tip force and the load, 1000 N + 500 N/m * 2 m.
        var solution = SolverTestSupport.Solve(loaded.Document.ToBeamModel());
        SolverTestSupport.ForceClose(2000, SolverTestSupport.At(solution, 0).ReactionY);
        SolverTestSupport.MomentClose(1000 * 2 + 500 * 2 * 1, SolverTestSupport.At(solution, 0).ReactionMoment);
        // Saving writes the current version with equal end intensities and keeps every engineering value.
        var saved = JsonNode.Parse(ProjectFileCodec.Serialize(loaded))!;
        Assert.Equal(5, saved["formatVersion"]!.GetValue<int>());
        var wire = saved["document"]!["distributedLoads"]![0]!;
        Assert.Equal(-500, wire["startIntensity"]!.GetValue<double>());
        Assert.Equal(-500, wire["endIntensity"]!.GetValue<double>());
        Assert.True(loaded.ContentEquals(ProjectFileCodec.Deserialize(Encoding.UTF8.GetBytes(saved.ToJsonString()))));
    }

    [Fact]
    public void ImmutableVersionFiveFixtureLoadsLinearLoadsAndSolvesThem()
    {
        var loaded = ProjectFileCodec.Deserialize(Fixture("V5", "linear-loads"));
        Assert.Equal(2, loaded.Document.DistributedLoads.Count);
        var first = loaded.Document.DistributedLoads[0];
        Assert.Equal((-1000, 1000), (first.StartIntensity.NewtonsPerMeter, first.EndIntensity.NewtonsPerMeter));
        Assert.False(first.IsConstant);
        var second = loaded.Document.DistributedLoads[1];
        Assert.Equal((0, -400), (second.StartIntensity.NewtonsPerMeter, second.EndIntensity.NewtonsPerMeter));
        BeamModelLoads(loaded);
        // The antisymmetric first load has no resultant; the triangle q2 has the resultant -400 N/m * 1 m / 2 = -200 N.
        var solution = SolverTestSupport.Solve(loaded.Document.ToBeamModel());
        double total = SolverTestSupport.At(solution, 0).ReactionY!.Value.Newtons + SolverTestSupport.At(solution, 2).ReactionY!.Value.Newtons;
        SolverTestSupport.Close(200, total, 1e-6);
    }

    private static void BeamModelLoads(ProjectState state)
    {
        var loads = state.Document.ToBeamModel().Loads.OfType<DistributedLoad>().ToArray();
        Assert.Equal(2, loads.Length);
        Assert.False(loads[0].IsConstant);
    }

    [Theory]
    [InlineData("V4", "constant-load")]
    [InlineData("V5", "linear-loads")]
    public async Task OpeningAFixtureNeitherWritesItNorMakesTheProjectDirty(string version, string name)
    {
        var app = new App(create: false);
        var path = TestPath("fixture.spandraft");
        byte[] original = Fixture(version, name);
        app.Files.Data[path] = original;
        app.Dialogs.OpenPath = path;
        Assert.True(await app.Main.OpenAsync());
        Assert.Empty(app.Files.Writes);
        Assert.Equal(original, app.Files.Data[path]);
        Assert.False(app.Main.Session!.IsDirty);
    }

    [Theory]
    [InlineData("document.distributedLoads.0.startIntensity")]
    [InlineData("document.distributedLoads.0.endIntensity")]
    public void BothIntensitiesAreRequiredInTheCurrentVersion(string path)
    {
        var root = JsonNode.Parse(Fixture("V5", "linear-loads"))!;
        var (parent, key) = (root["document"]!["distributedLoads"]![0]!.AsObject(), path.Split('.')[^1]);
        parent.Remove(key);
        Assert.Throws<ProjectFormatException>(() => ProjectFileCodec.Deserialize(Encoding.UTF8.GetBytes(root.ToJsonString())));
    }

    [Fact]
    public void ASingleIntensityIsNotAcceptedInTheCurrentVersion()
    {
        var root = JsonNode.Parse(Fixture("V4", "constant-load"))!;
        root["formatVersion"] = 5;
        Assert.Throws<ProjectFormatException>(() => ProjectFileCodec.Deserialize(Encoding.UTF8.GetBytes(root.ToJsonString())));
    }

    [Theory]
    [InlineData("startIntensity", "1e999")]
    [InlineData("endIntensity", "-1e999")]
    [InlineData("startIntensity", "\"x\"")]
    [InlineData("endIntensity", "null")]
    [InlineData("endPosition", "2.5")]
    [InlineData("startPosition", "-0.5")]
    [InlineData("endPosition", "0")]
    public void InvalidLinearLoadValuesAreRejected(string field, string json)
    {
        string text = Encoding.UTF8.GetString(Fixture("V5", "linear-loads"));
        var root = JsonNode.Parse(text)!;
        root["document"]!["distributedLoads"]![0]![field] = JsonNode.Parse(json.Contains("e999") ? "\"OVERFLOW\"" : json);
        string output = root.ToJsonString().Replace("\"OVERFLOW\"", json);
        Assert.Throws<ProjectFormatException>(() => ProjectFileCodec.Deserialize(Encoding.UTF8.GetBytes(output)));
    }

    [Fact]
    public async Task RecoveryRoundtripKeepsLinearLoadsAndDoesNotRewriteTheEnvelopeVersion()
    {
        var storage = new TestRecoveryStorage(); var slot = RecoverySlot.Project(RecoveryId.New());
        var recovery = new ProjectRecovery(storage, slot);
        var state = Linear();
        await recovery.FlushAsync(state, null);
        var root = JsonNode.Parse(storage.Data[slot])!;
        Assert.Equal(1, root["recoveryVersion"]!.GetValue<int>());
        Assert.Equal(5, root["project"]!["formatVersion"]!.GetValue<int>());
        Assert.True(state.ContentEquals((await recovery.ReadAsync())!.State));
    }

    [Fact]
    public void VersionFourAndFiveFilesOfTheSameModelLoadEquivalently()
    {
        var fromFour = ProjectFileCodec.Deserialize(Fixture("V4", "constant-load"));
        var asFive = JsonNode.Parse(ProjectFileCodec.Serialize(fromFour))!;
        Assert.True(fromFour.ContentEquals(ProjectFileCodec.Deserialize(Encoding.UTF8.GetBytes(asFive.ToJsonString()))));
        Assert.True(fromFour.ContentEquals(ProjectFileCodec.Deserialize(Encoding.UTF8.GetBytes(
            AsHistorical(JsonNode.Parse(asFive.ToJsonString())!, 4).ToJsonString()))));
    }
}
