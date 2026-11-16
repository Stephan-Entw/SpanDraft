using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpanDraft.Desktop.Diagnostics;

namespace SpanDraft.Performance;

internal enum Phase { Preparation, Analysis, Presentation, UI, Total }

internal sealed record PhaseValues(decimal Preparation, decimal Analysis, decimal Presentation, decimal UI, decimal Total)
{
    public decimal this[Phase phase] => phase switch
    {
        Phase.Preparation => Preparation, Phase.Analysis => Analysis, Phase.Presentation => Presentation,
        Phase.UI => UI, Phase.Total => Total, _ => throw new ArgumentOutOfRangeException(nameof(phase))
    };
    public static PhaseValues From(InteractionPerformanceEntry entry) => new(entry.Preparation.Ticks / 10000m,
        entry.Analysis.Ticks / 10000m, entry.Presentation.Ticks / 10000m, entry.UI.Ticks / 10000m, entry.Total.Ticks / 10000m);
}

internal sealed record Statistics(decimal Minimum, decimal Median, decimal P95, decimal Maximum)
{
    public static Statistics? Calculate(IEnumerable<decimal> values)
    {
        var sorted = values.Order().ToArray();
        int count = sorted.Length;
        if (count == 0) return null;
        decimal median = count % 2 == 1 ? sorted[count / 2] : sorted[count / 2 - 1] / 2 + sorted[count / 2] / 2;
        return new(sorted[0], median, sorted[(int)((95L * count + 99) / 100) - 1], sorted[^1]);
    }
}

internal sealed record ActionResult(string ScenarioId, string ActionId, int ValidSampleCount, int DiscardedSampleCount,
    int WarmupDiscardedCount, int PreparationDiscardedCount, PhaseValues[] Samples, Dictionary<Phase, Statistics?> Statistics)
{
    public static ActionResult Create(string scenarioId, string actionId, IEnumerable<PhaseValues> samples,
        int discarded, int warmupDiscarded, int preparationDiscarded)
    {
        var raw = samples.ToArray();
        return new(scenarioId, actionId, raw.Length, discarded, warmupDiscarded, preparationDiscarded, raw,
            Enum.GetValues<Phase>().ToDictionary(p => p, p => Performance.Statistics.Calculate(raw.Select(s => s[p]))));
    }
}

internal sealed record BuildSource(string? Revision, bool? Dirty)
{
    public static BuildSource Read(Assembly assembly)
    {
        var metadata = assembly.GetCustomAttributes<AssemblyMetadataAttribute>().ToArray();
        string? Value(string key)
        {
            var matches = metadata.Where(a => a.Key == key).ToArray();
            return matches.Length == 1 ? matches[0].Value : null;
        }
        string? revision = Value("PerformanceSourceRevision");
        if (!IsRevision(revision)) revision = null;
        return new(revision, bool.TryParse(Value("PerformanceSourceDirty"), out bool dirty) ? dirty : null);
    }
    public static bool IsRevision(string? revision) => revision is { Length: 40 or 64 } && revision.All(char.IsAsciiHexDigit);
}

internal sealed record RunEnvironment(string OperatingSystem, string Architecture, string DotNetVersion,
    double RenderScaling, double WindowWidth, double WindowHeight, int? DisplayWidth, int? DisplayHeight,
    double? RefreshRateHz, string? EnvironmentId, string UiCulture, string FormattingCulture, string UnitProfile);

internal sealed record RunConfiguration(string Scenario, int Warmup, int Iterations, string ResetStrategy);
internal sealed record RunFile(int SchemaVersion, int ProtocolVersion, string DurationUnit, string SpanDraftVersion,
    BuildSource Build, DateTimeOffset StartedAtUtc, RunEnvironment Environment, RunConfiguration Configuration, ActionResult[] Results);

internal static class RunFiles
{
    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true,
        RespectRequiredConstructorParameters = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter<Phase>(allowIntegerValues: false) }
    };

    public static byte[] Serialize(RunFile run) { Validate(run); return JsonSerializer.SerializeToUtf8Bytes(run, Json); }
    public static RunFile Deserialize(byte[] bytes)
    {
        var run = JsonSerializer.Deserialize<RunFile>(bytes, Json) ?? throw new InvalidDataException("Missing run object.");
        Validate(run); return run;
    }

    public static void Validate(RunFile run)
    {
        Require(run.SchemaVersion == 1 && run.ProtocolVersion == 1, "Unsupported schema or measurement protocol version.");
        Require(run.DurationUnit == "milliseconds", "Unsupported duration unit.");
        Require(!string.IsNullOrWhiteSpace(run.SpanDraftVersion) && run.Build is not null, "Missing build metadata.");
        Require(run.Build!.Revision is null || BuildSource.IsRevision(run.Build.Revision), "Invalid source revision.");
        Require(run.StartedAtUtc != default && run.StartedAtUtc.Offset == TimeSpan.Zero, "Invalid UTC start time.");
        var env = run.Environment;
        Require(env is not null, "Missing environment.");
        Require(!string.IsNullOrWhiteSpace(env!.OperatingSystem) && !string.IsNullOrWhiteSpace(env.Architecture)
            && !string.IsNullOrWhiteSpace(env.DotNetVersion) && !string.IsNullOrWhiteSpace(env.UiCulture)
            && !string.IsNullOrWhiteSpace(env.FormattingCulture) && !string.IsNullOrWhiteSpace(env.UnitProfile), "Incomplete environment.");
        Require(double.IsFinite(env.RenderScaling) && env.RenderScaling > 0 && double.IsFinite(env.WindowWidth)
            && env.WindowWidth > 0 && double.IsFinite(env.WindowHeight) && env.WindowHeight > 0, "Invalid window metrics.");
        Require((env.DisplayWidth is null && env.DisplayHeight is null) || (env.DisplayWidth > 0 && env.DisplayHeight > 0), "Invalid display size.");
        Require(env.RefreshRateHz is null || (double.IsFinite(env.RefreshRateHz.Value) && env.RefreshRateHz > 0), "Invalid refresh rate.");
        Require(IsEnvironmentId(env.EnvironmentId), "Environment ID must be a neutral label of at most 64 ASCII letters, digits, dots, underscores or hyphens.");
        var config = run.Configuration;
        Require(config is not null, "Missing configuration.");
        Require(config!.Warmup >= 0 && config.Iterations > 0 && config.ResetStrategy == "undo-to-reference", "Invalid iteration configuration.");
        var scenarios = ReferenceScenarios.Select(config.Scenario);
        Require(run.Results is not null && run.Results.Length == scenarios.Length * ReferenceActions.Ids.Length, "Incomplete scenario/action results.");
        var seen = new HashSet<(string, string)>();
        foreach (var result in run.Results!)
        {
            Require(result is not null && scenarios.Contains(result.ScenarioId) && ReferenceActions.Ids.Contains(result.ActionId)
                && seen.Add((result.ScenarioId, result.ActionId)), "Unknown or duplicate scenario/action.");
            Require(result!.Samples is not null && result.Statistics is not null, "Missing samples or statistics.");
            Require(result.ValidSampleCount == result.Samples!.Length && result.DiscardedSampleCount >= 0
                && (long)result.ValidSampleCount + result.DiscardedSampleCount == config.Iterations
                && result.WarmupDiscardedCount >= 0 && result.WarmupDiscardedCount <= config.Warmup
                && result.PreparationDiscardedCount >= 0 && result.PreparationDiscardedCount <= (long)config.Warmup + config.Iterations + 2,
                "Inconsistent sample counts.");
            foreach (var sample in result.Samples)
            {
                Require(sample is not null && Enum.GetValues<Phase>().All(p => sample[p] >= 0
                    && sample[p] <= TimeSpan.MaxValue.Ticks / 10000m && decimal.Truncate(sample[p] * 10000m) == sample[p] * 10000m),
                    "Sample duration is outside the recorder's TimeSpan range or 100-nanosecond precision.");
                Require(sample!.Total == sample.Preparation + sample.Analysis + sample.Presentation + sample.UI, "Inconsistent sample total.");
            }
            Require(result.Statistics!.Count == Enum.GetValues<Phase>().Length, "Missing phase statistics.");
            foreach (var phase in Enum.GetValues<Phase>())
                Require(result.Statistics.TryGetValue(phase, out var stats)
                    && stats == Statistics.Calculate(result.Samples.Select(s => s[phase])), "Statistics do not match raw samples.");
        }
    }

    public static bool IsEnvironmentId(string? value) => value is null || value.Length is > 0 and <= 64
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
}
