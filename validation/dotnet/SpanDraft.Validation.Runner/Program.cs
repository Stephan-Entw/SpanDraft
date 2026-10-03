using System.Text.Json;
using SpanDraft.Validation;

try
{
    string command = args.FirstOrDefault() ?? "help";
    string? Option(string name)
    {
        int index = Array.IndexOf(args,name);
        if (index < 0) return null;
        if (index+1 >= args.Length) throw new ArgumentException("Missing value for " + name);
        return Path.GetFullPath(args[index+1]);
    }
    switch (command)
    {
        case "export":
            string exportPath = Option("--output") ?? Path.Combine(Catalog.Root,"validation","results","spandraft.json");
            Directory.CreateDirectory(Path.GetDirectoryName(exportPath)!);
            File.WriteAllText(exportPath,JsonSerializer.Serialize(Catalog.CaseIds.Select(id => Results.Export(Catalog.Load(id))).ToArray(),Catalog.Json)+"\n");
            Console.WriteLine("Exported 18 cases: " + exportPath);
            break;
        case "compare":
            string? actualPath = Option("--actual");
            var actual = actualPath is null ? null : JsonSerializer.Deserialize<ReferenceData[]>(File.ReadAllText(actualPath),Catalog.Json);
            if (actual is not null && (actual.Length != Catalog.CaseIds.Length ||
                !actual.Select(a => a.CaseId).ToHashSet().SetEquals(Catalog.CaseIds) ||
                actual.Any(a => a.Solver != "SpanDraft" || a.InputSha256 != Catalog.Hash(a.CaseId))))
                throw new InvalidDataException("Stale/incomplete SpanDraft export");
            var report = Acceptance.Run(Option("--references"),actual);
            string reportPath = Option("--report") ?? Path.Combine(Catalog.Root,"validation","results","run-report.json");
            if (reportPath.StartsWith(Path.Combine(Catalog.Root,"validation","references")+Path.DirectorySeparatorChar,StringComparison.Ordinal))
                throw new ArgumentException("Reports must not overwrite golden references");
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
            File.WriteAllText(reportPath,JsonSerializer.Serialize(report,Catalog.Json)+"\n");
            foreach (var c in report.Results) Console.WriteLine($"{c.CaseId}: {c.Status}; "+string.Join(", ",c.References.Select(r => $"{r.ReferenceSolver}={r.Status}")));
            Console.WriteLine($"External scalar comparisons: {report.ExternalComparisons}; analytical: {report.AnalyticalComparisons}; metamorphic tests: {report.MetamorphicTests}");
            // This command verifies validation checks. The final documented gate also requires
            // a successful normal build and the complete .NET test run.
            Console.WriteLine(report.Validated ? "Validation comparisons: PASS (full build/test gate recorded separately)." : "SpanDraft Euler-Bernoulli solver: validation incomplete.");
            Environment.ExitCode = report.Validated ? 0 : 1;
            break;
        default:
            Console.WriteLine("export [--output PATH]\ncompare [--references DIR] [--actual PATH] [--report PATH]");
            Environment.ExitCode = command == "help" ? 0 : 2;
            break;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    Environment.ExitCode = 2;
}
