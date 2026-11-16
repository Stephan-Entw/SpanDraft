using System.Globalization;

namespace SpanDraft.Performance;

internal sealed record MeasureOptions(string Scenario, int Warmup, int Iterations, string Output, string? EnvironmentId)
{
    public static MeasureOptions Parse(string[] args)
    {
        if (args.Length % 2 != 0) throw new ArgumentException("Expected named argument/value pairs.");
        string[] allowed = ["--scenario", "--warmup", "--iterations", "--output", "--environment-id"];
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i += 2)
            if (!allowed.Contains(args[i]) || !options.TryAdd(args[i], args[i + 1])) throw new ArgumentException("Unknown or duplicate option.");
        string scenario = options.GetValueOrDefault("--scenario", "all");
        ReferenceScenarios.Select(scenario);
        int warmup = int.Parse(options.GetValueOrDefault("--warmup", "10"), NumberStyles.None, CultureInfo.InvariantCulture);
        int iterations = int.Parse(options.GetValueOrDefault("--iterations", "50"), NumberStyles.None, CultureInfo.InvariantCulture);
        if (warmup < 0 || iterations < 1) throw new ArgumentException("Warm-up must be nonnegative; iterations must be positive.");
        string output = options.GetValueOrDefault("--output") ?? throw new ArgumentException("--output is required.");
        if (string.IsNullOrWhiteSpace(output)) throw new ArgumentException("Output path cannot be empty.");
        string? environmentId = options.GetValueOrDefault("--environment-id");
        if (!RunFiles.IsEnvironmentId(environmentId)) throw new ArgumentException("Use a neutral environment ID: 1-64 ASCII letters, digits, dots, underscores or hyphens.");
        return new(scenario, warmup, iterations, Path.GetFullPath(output), environmentId);
    }
}

internal static class Program
{
    internal const string Usage = "measure [--scenario small|typical|large|all] [--warmup 10] [--iterations 50] --output run.json [--environment-id neutral-label]\ncompare baseline.json current.json";

    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            if (args is ["--help"]) { Console.WriteLine(Usage); return 0; }
            if (args is ["compare", var baseline, var current])
            {
                Comparison.Write(RunFiles.Deserialize(File.ReadAllBytes(baseline)), RunFiles.Deserialize(File.ReadAllBytes(current)), Console.Out);
                return 0;
            }
            if (args.Length > 0 && args[0] == "measure")
            {
                var options = MeasureOptions.Parse(args[1..]);
                if (File.Exists(options.Output)) throw new IOException("Output already exists; choose a new result file.");
                return PerformanceApplication.Run(options);
            }
            throw new ArgumentException(Usage);
        }
        catch (Exception error) { Console.Error.WriteLine("Performance runner: " + error.Message); return 1; }
    }
}
