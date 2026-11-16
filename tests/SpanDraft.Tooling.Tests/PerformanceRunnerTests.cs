using System.Text;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using SpanDraft.Analysis;
using SpanDraft.Desktop.Diagnostics;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using SpanDraft.Performance;
using Xunit;

namespace SpanDraft.Tests;

public sealed class PerformanceRunnerTests
{
    private static InteractionPerformanceEntry Entry(int scale = 1) => new(DateTimeOffset.UnixEpoch,
        InteractionAction.PointForce, TimeSpan.FromMilliseconds(scale), TimeSpan.FromMilliseconds(2 * scale),
        TimeSpan.FromMilliseconds(3 * scale), TimeSpan.FromMilliseconds(4 * scale), TimeSpan.FromMilliseconds(10 * scale));

    private sealed class Frames : IInteractionFrameProbe
    {
        public Task<long?> ObserveNextFrameAsync(TimeProvider clock, CancellationToken cancellationToken) => Task.FromResult<long?>(clock.GetTimestamp());
    }

    [Theory]
    [InlineData("small", "7f96c7c1f24349b9f04998614c30a070cc6fd2bc54ab041058ca1f23bcc149eb")]
    [InlineData("typical", "56202090d80f374cdad1aef86a23a6111e216e146a26bdbc550ba1f651f6e939")]
    [InlineData("large", "0a2ccffeb582ed0fc6292018bbc7ad1b63cbd088e46bb98ffc89e12f9897c762")]
    public void ReferenceInputSnapshotsCannotSilentlyChangeWithinTheProtocol(string scenario, string expected)
    {
        // A project-format-only change may update this fingerprint after checking unchanged reference content.
        string json = Encoding.UTF8.GetString(ProjectFileCodec.Serialize(ReferenceScenarios.Create(scenario))).Replace("\r\n", "\n");
        string actual = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        Assert.True(expected == actual, scenario + ": " + actual);
    }

    [Theory]
    [InlineData("small", 1, 2, 1, 0, 1)]
    [InlineData("typical", 4, 5, 4, 2, 4)]
    [InlineData("large", 14, 15, 20, 2, 4)]
    public void ReferenceProjectsAreDeterministicValidAndFrozen(string id, int segments, int supports, int forces, int moments, int distributed)
    {
        var first = ReferenceScenarios.Create(id); var second = ReferenceScenarios.Create(id);
        Assert.True(first.ContentEquals(second));
        Assert.Equal(ProjectFileCodec.Serialize(first), ProjectFileCodec.Serialize(second));
        var doc = first.Document;
        Assert.Equal(segments, doc.Segments.Count); Assert.Equal(supports, doc.Supports.Count);
        Assert.Equal(forces, doc.Loads.Count(l => l.Kind == PointLoadKind.Force));
        Assert.Equal(moments, doc.Loads.Count(l => l.Kind == PointLoadKind.Moment)); Assert.Equal(distributed, doc.DistributedLoads.Count);
        Assert.Equal(id == "small" ? 6 : id == "typical" ? 12 : 14, doc.Length.Meters);
        Assert.Equal(-10000, doc.Loads.First(l => l.Kind == PointLoadKind.Force).Value);
        Assert.Equal(-2000, doc.DistributedLoads[0].StartIntensity.NewtonsPerMeter);
        Assert.True(BeamAnalysis.Analyze(doc.ToBeamModel()).IsSuccess);
    }

    [Theory]
    [InlineData("small")] [InlineData("typical")] [InlineData("large")]
    public async Task EveryReferenceActionAndUndoRestoreTheSameStateAndBoundedHistory(string scenario)
    {
        var reference = ReferenceScenarios.Create(scenario);
        var files = new MemoryProjectStore(); var recorder = new InteractionPerformance(new Frames());
        var main = new MainWindowViewModel(files: files, performance: recorder);
        string path = Path.Combine(Path.GetTempPath(), "SpanDraft-performance-test.spandraft");
        await files.WriteAtomicAsync(path, ProjectFileCodec.Serialize(reference), TestContext.Current.CancellationToken);
        foreach (string action in ReferenceActions.Ids)
        {
            Assert.True(await main.OpenAsync(path));
            ReferenceActions.AssertReference(main, reference, false);
            ProjectState? changed = null;
            for (int i = 0; i < 4; i++)
            {
                ReferenceActions.AssertReference(main, reference, i > 0);
                int terminal = 0; InteractionPerformanceEntry? sample = null;
                void Finished(InteractionPerformanceEntry? entry) { terminal++; sample = entry; }
                recorder.MeasurementFinished += Finished;
                Assert.True(await ReferenceActions.Prepare(main, action)());
                recorder.MeasurementFinished -= Finished;
                Assert.Equal(1, terminal); Assert.NotNull(sample); Assert.Equal(ReferenceActions.Kind(action), sample.Action);
                Assert.Single(main.Session!.UndoHistory); Assert.Empty(main.Session.RedoHistory);
                Assert.True(main.Editor!.Presentation.IsSuccess);
                if (changed is null) changed = main.Session.CurrentRevision.State;
                else Assert.True(changed.ContentEquals(main.Session.CurrentRevision.State));
                Assert.Equal(reference.Document.Loads.Count, changed.Document.Loads.Count);
                Assert.Equal(reference.Document.Supports.Count, changed.Document.Supports.Count);
                Assert.Equal(reference.Document.Segments.Count, changed.Document.Segments.Count);
                Assert.True(main.Undo());
                ReferenceActions.AssertReference(main, reference, true);
                Assert.False(main.Session.IsDirty);
            }
        }
    }

    [Fact]
    public async Task WarmupPrimingAndResetAreExcludedAndDiscardsAreSeparate()
    {
        InteractionPerformanceEntry?[] attempts = [Entry(99), Entry(100), Entry(1), null, Entry(3)];
        int measures = 0, resets = 0;
        var result = await MeasurementLoop.RunAsync("small", ReferenceActions.Ids[0], 1, 3,
            () => Task.FromResult(attempts[measures++]),
            () => { resets++; return Task.FromResult<InteractionPerformanceEntry?>(null); });
        Assert.Equal(5, measures); Assert.Equal(5, resets);
        Assert.Equal(2, result.ValidSampleCount); Assert.Equal(1, result.DiscardedSampleCount);
        Assert.Equal(0, result.WarmupDiscardedCount); Assert.Equal(5, result.PreparationDiscardedCount);
        Assert.Equal(new[] { 10m, 30m }, result.Samples.Select(s => s.Total));
        Assert.Equal(new Statistics(10, 20, 30, 30), result.Statistics[Phase.Total]);
    }

    [Fact]
    public async Task ANewAttemptWaitsForThePreviousTerminalOutcomeAndReset()
    {
        var pending = new TaskCompletionSource<InteractionPerformanceEntry?>();
        var reset = new TaskCompletionSource<InteractionPerformanceEntry?>();
        int attempts = 0, resets = 0;
        var run = MeasurementLoop.RunAsync("small", ReferenceActions.Ids[0], 0, 1,
            () => ++attempts == 1 ? pending.Task : Task.FromResult<InteractionPerformanceEntry?>(Entry()),
            () => ++resets == 1 ? reset.Task : Task.FromResult<InteractionPerformanceEntry?>(Entry()));
        Assert.Equal(1, attempts); Assert.Equal(0, resets); Assert.False(run.IsCompleted);
        pending.SetResult(null);
        Assert.Equal(1, attempts); Assert.Equal(1, resets); Assert.False(run.IsCompleted);
        reset.SetResult(Entry());
        var result = await run;
        Assert.Equal(2, attempts); Assert.Equal(2, resets); Assert.Single(result.Samples);
    }

    [Fact]
    public async Task AllDiscardedSamplesRemainAValidResultWithoutTimingValues()
    {
        var result = await MeasurementLoop.RunAsync("small", ReferenceActions.Ids[0], 2, 3,
            () => Task.FromResult<InteractionPerformanceEntry?>(null), () => Task.FromResult<InteractionPerformanceEntry?>(null));
        Assert.Empty(result.Samples); Assert.Equal(3, result.DiscardedSampleCount); Assert.Equal(2, result.WarmupDiscardedCount);
        Assert.All(result.Statistics.Values, Assert.Null);
    }

    [Fact]
    public void StatisticsDefineMedianNearestRankP95AndExtremaWithoutDroppingOutliers()
    {
        Assert.Null(Statistics.Calculate([]));
        Assert.Equal(new Statistics(7, 7, 7, 7), Statistics.Calculate([7]));
        Assert.Equal(new Statistics(1, 2.5m, 4, 4), Statistics.Calculate([4, 1, 3, 2]));
        Assert.Equal(new Statistics(1, 10.5m, 19, 20), Statistics.Calculate(Enumerable.Range(1, 20).Select(i => (decimal)i)));
        Assert.Equal(48, Statistics.Calculate(Enumerable.Range(1, 50).Select(i => (decimal)i))!.P95);
        Assert.Equal(new Statistics(1, 2, 10000, 10000), Statistics.Calculate([1, 2, 10000]));
    }

    internal static RunFile Run(int scale = 1)
    {
        var samples = new[] { PhaseValues.From(Entry(scale)) };
        return new(1, 1, "milliseconds", "1.1.0", new(new string('a', 40), false), DateTimeOffset.UnixEpoch,
            new("Test OS", "Arm64", ".NET 10", 1, 1300, 800, 1920, 1080, null, null, "en-US", "en-US", "StructuralEngineering"),
            new("small", 0, 1, "undo-to-reference"), ReferenceActions.Ids.Select(a => ActionResult.Create("small", a, samples, 0, 0, 0)).ToArray());
    }

    [Fact]
    public void JsonRoundtripPreservesExactPhasesProtocolAndUnknownProvenance()
    {
        var run = Run() with { Build = new(null, null) };
        byte[] first = RunFiles.Serialize(run);
        var restored = RunFiles.Deserialize(first);
        Assert.Equal(first, RunFiles.Serialize(restored));
        Assert.Equal(1, restored.SchemaVersion); Assert.Equal(1, restored.ProtocolVersion);
        Assert.Equal("en-US", restored.Environment.UiCulture); Assert.Equal("en-US", restored.Environment.FormattingCulture);
        Assert.Equal("StructuralEngineering", restored.Environment.UnitProfile);
        Assert.Null(restored.Build.Revision); Assert.Null(restored.Build.Dirty);
        string json = Encoding.UTF8.GetString(first);
        Assert.DoesNotContain(".spandraft", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("userName", json); Assert.DoesNotContain("hostname", json); Assert.DoesNotContain("filePath", json);
    }

    [Theory]
    [InlineData("schema")] [InlineData("protocol")] [InlineData("missing")] [InlineData("nullEnvironment")]
    [InlineData("counts")] [InlineData("negative")] [InlineData("total")] [InlineData("stats")]
    [InlineData("duplicate")] [InlineData("extraContent")] [InlineData("revision")] [InlineData("missingPhase")]
    [InlineData("durationRange")] [InlineData("durationPrecision")]
    public void InvalidAndIncompatibleFilesAreRejected(string invalid)
    {
        var node = JsonNode.Parse(RunFiles.Serialize(Run()))!;
        switch (invalid)
        {
            case "schema": node["schemaVersion"] = 2; break;
            case "protocol": node["protocolVersion"] = 2; break;
            case "missing": node.AsObject().Remove("startedAtUtc"); break;
            case "nullEnvironment": node["environment"] = null; break;
            case "counts": node["results"]![0]!["validSampleCount"] = 50; break;
            case "negative": node["results"]![0]!["samples"]![0]!["analysis"] = -1; break;
            case "total": node["results"]![0]!["samples"]![0]!["total"] = 20; break;
            case "stats": node["results"]![0]!["statistics"]!["Total"]!["median"] = 99; break;
            case "duplicate": node["results"]![1]!["actionId"] = ReferenceActions.Ids[0]; break;
            case "extraContent": node["project"] = "private data"; break;
            case "revision": node["build"]!["revision"] = "not a commit"; break;
            case "durationRange": node["results"]![0]!["samples"]![0]!["analysis"] = decimal.MaxValue; break;
            case "durationPrecision": node["results"]![0]!["samples"]![0]!["analysis"] = .00001m; break;
            default: node["results"]![0]!["statistics"]!.AsObject().Remove("UI"); break;
        }
        var error = Record.Exception(() => RunFiles.Deserialize(Encoding.UTF8.GetBytes(node.ToJsonString())));
        Assert.True(error is JsonException or InvalidDataException, error?.ToString() ?? "Invalid file was accepted.");
    }

    [Fact]
    public void CompareShowsDeltasInBothDirectionsAndNoPerformanceVerdict()
    {
        using var output = new StringWriter(); Comparison.Write(Run(), Run(2), output);
        Assert.Contains("10 20 10 100%", output.ToString());
        output.GetStringBuilder().Clear(); Comparison.Write(Run(2), Run(), output);
        Assert.Contains("20 10 -10 -50%", output.ToString());
        output.GetStringBuilder().Clear(); Comparison.Write(Run(), Run(), output);
        Assert.Contains("10 10 0 0%", output.ToString());
        Assert.DoesNotContain("PASS", output.ToString()); Assert.DoesNotContain("FAIL", output.ToString());
        Assert.Null(Comparison.Percent(0, 1)); Assert.Equal(0, Comparison.Percent(0, 0));
        Assert.True(Comparison.Percent(.0001m, TimeSpan.MaxValue.Ticks / 10000m) > 0);
    }

    [Fact]
    public void CompareWarnsAboutEnvironmentAndUnknownOrDirtyBuildsWithoutRejectingThem()
    {
        var baseline = Run();
        var current = Run() with { Build = new(null, true), Environment = Run().Environment with
        { OperatingSystem = "Another OS", Architecture = "X64", DotNetVersion = ".NET 11", RenderScaling = 2,
            RefreshRateHz = 144, EnvironmentId = "reference-b", UiCulture = "de-DE", FormattingCulture = "de-DE", UnitProfile = "MechanicalEngineering" } };
        using var output = new StringWriter(); Comparison.Write(baseline, current, output);
        foreach (string name in new[] { "OS", "architecture", ".NET", "render scaling", "refresh rate", "environment ID", "UI culture", "formatting culture", "unit profile" })
            Assert.Contains("Environment mismatch: " + name, output.ToString());
        Assert.Contains("not directly equivalent", output.ToString()); Assert.Contains("uncommitted", output.ToString());
        Assert.Contains("partially unavailable", output.ToString()); Assert.Contains("10 10 0 0%", output.ToString());
    }

    [Fact]
    public void CompareHandlesDiscardOnlyResultsAndScenarioSubsets()
    {
        var discarded = Run() with { Results = ReferenceActions.Ids.Select(a => ActionResult.Create("small", a, [], 1, 0, 0)).ToArray() };
        using var output = new StringWriter(); Comparison.Write(Run(), discarded, output);
        Assert.Contains("discarded 0 -> 1", output.ToString()); Assert.Contains("10 n/a n/a n/a", output.ToString());
        var all = Run() with { Configuration = Run().Configuration with { Scenario = "all" },
            Results = ReferenceScenarios.Ids.SelectMany(s => Run().Results.Select(r => r with { ScenarioId = s })).ToArray() };
        output.GetStringBuilder().Clear(); Comparison.Write(Run(), all, output);
        Assert.Contains("present in only one run", output.ToString()); Assert.Contains("small/point-force-value Total", output.ToString());
    }

    [Fact]
    public void CliDefaultsAndOverridesAreStrictAndDoNotCreateAWindowForCompare()
    {
        var defaults = MeasureOptions.Parse(["--output", "run.json"]);
        Assert.Equal("all", defaults.Scenario); Assert.Equal(10, defaults.Warmup); Assert.Equal(50, defaults.Iterations);
        var custom = MeasureOptions.Parse(["--scenario", "typical", "--warmup", "0", "--iterations", "3", "--output", "run.json", "--environment-id", "ref-a"]);
        Assert.Equal(0, custom.Warmup); Assert.Equal(3, custom.Iterations); Assert.Equal("ref-a", custom.EnvironmentId);
        foreach (var args in new[] { new[] { "--output", "run.json", "--other", "1" }, new[] { "--warmup", "-1", "--output", "run.json" },
            new[] { "--iterations", "0", "--output", "run.json" }, new[] { "--output", "one", "--output", "two" },
            new[] { "--scenario", "unknown", "--output", "run.json" }, new[] { "--output", "run.json", "--environment-id", "/private/path" } })
            Assert.NotNull(Record.Exception(() => MeasureOptions.Parse(args)));
        Assert.Null(BuildSource.Read(typeof(PerformanceRunnerTests).Assembly).Revision);
    }
}
