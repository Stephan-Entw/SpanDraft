using System.Globalization;

namespace SpanDraft.Performance;

internal static class Comparison
{
    public static decimal? Percent(decimal baseline, decimal current) => baseline == 0 ? current == 0 ? 0 : null
        : (current - baseline) / baseline * 100;

    public static void Write(RunFile baseline, RunFile current, TextWriter output)
    {
        RunFiles.Validate(baseline); RunFiles.Validate(current);
        var a = baseline.Environment; var b = current.Environment;
        var differences = new List<string>();
        void Check<T>(string name, T oldValue, T newValue)
        { if (!EqualityComparer<T>.Default.Equals(oldValue, newValue)) differences.Add($"{name}: {oldValue?.ToString() ?? "unavailable"} -> {newValue?.ToString() ?? "unavailable"}"); }
        Check("OS", a.OperatingSystem, b.OperatingSystem); Check("architecture", a.Architecture, b.Architecture);
        Check(".NET", a.DotNetVersion, b.DotNetVersion); Check("render scaling", a.RenderScaling, b.RenderScaling);
        Check("window width", a.WindowWidth, b.WindowWidth); Check("window height", a.WindowHeight, b.WindowHeight);
        Check("display width", a.DisplayWidth, b.DisplayWidth); Check("display height", a.DisplayHeight, b.DisplayHeight);
        Check("refresh rate", a.RefreshRateHz, b.RefreshRateHz); Check("environment ID", a.EnvironmentId, b.EnvironmentId);
        Check("UI culture", a.UiCulture, b.UiCulture); Check("formatting culture", a.FormattingCulture, b.FormattingCulture);
        Check("unit profile", a.UnitProfile, b.UnitProfile);
        foreach (string difference in differences) output.WriteLine("Environment mismatch: " + difference);
        if (differences.Count > 0) output.WriteLine("UI/E2E values are not directly equivalent across these environments; comparison continues.");
        if (baseline.Configuration != current.Configuration) output.WriteLine("Measurement configuration differs between runs.");
        foreach (var pair in new[] { ("Baseline", baseline), ("Current", current) })
        {
            output.WriteLine($"{pair.Item1}: SpanDraft {pair.Item2.SpanDraftVersion}, source {pair.Item2.Build.Revision ?? "unavailable"}.");
            if (pair.Item2.Build.Dirty == true) output.WriteLine(pair.Item1 + " build contains uncommitted changes.");
            if (pair.Item2.Build.Revision is null || pair.Item2.Build.Dirty is null) output.WriteLine(pair.Item1 + " build provenance is partially unavailable.");
        }
        if (a.RefreshRateHz is null || b.RefreshRateHz is null) output.WriteLine("Refresh rate is unavailable for at least one run.");
        output.WriteLine("All durations and absolute deltas are milliseconds; percent is relative to baseline. n/a = unavailable or undefined.");
        output.WriteLine("Scenario/Action Phase | Median baseline current delta percent | p95 baseline current delta percent");
        var oldResults = baseline.Results.ToDictionary(r => (r.ScenarioId, r.ActionId));
        var newResults = current.Results.ToDictionary(r => (r.ScenarioId, r.ActionId));
        foreach (var key in oldResults.Keys.Union(newResults.Keys).Order())
        {
            string label = key.ScenarioId + "/" + key.ActionId;
            if (!oldResults.TryGetValue(key, out var old) || !newResults.TryGetValue(key, out var next))
            { output.WriteLine(label + ": present in only one run."); continue; }
            output.WriteLine($"{label}: valid {old.ValidSampleCount} -> {next.ValidSampleCount}; discarded {old.DiscardedSampleCount} -> {next.DiscardedSampleCount}");
            foreach (var phase in Enum.GetValues<Phase>())
            {
                var x = old.Statistics[phase]; var y = next.Statistics[phase];
                output.WriteLine($"{label} {phase} | {Changes(x?.Median, y?.Median)} | {Changes(x?.P95, y?.P95)}");
            }
        }
    }

    private static string Changes(decimal? baseline, decimal? current) => baseline is { } a && current is { } b
        ? $"{Number(a)} {Number(b)} {Number(b - a)} {Number(Percent(a, b))}%"
        : $"{Number(baseline)} {Number(current)} n/a n/a";
    private static string Number(decimal? value) => value?.ToString("0.#####", CultureInfo.InvariantCulture) ?? "n/a";
}
