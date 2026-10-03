namespace SpanDraft.Validation;

public sealed record CaseAcceptance(string CaseId, string Status, CheckReport[] References,
    CheckReport Equilibrium, CheckReport PhysicalFields, CheckReport[] Metamorphic, CheckReport[] ReferenceEquilibria);
public sealed record AcceptanceReport(string GeneratedAt, string SolverSourceSha256, int Cases, int AnalyticalCases,
    int ExternalComparisons, int AnalyticalComparisons, int MetamorphicTests, int PhysicalComparisons,
    int TotalComparisons, int NotApplicableComparisons, bool Validated, CaseAcceptance[] Results);

public static class Acceptance
{
    public static AcceptanceReport Run(string? references = null, ReferenceData[]? exported = null)
    {
        var results = new List<CaseAcceptance>();
        foreach (string id in Catalog.CaseIds)
        {
            var input = Catalog.Load(id);
            var solution = ValidationCase.Solve(input);
            var actual = exported is null ? Results.Export(input, solution) : exported.Single(a => a.CaseId == id);
            var referenceChecks = new List<CheckReport>();
            var referenceEquilibria = new List<CheckReport>();
            foreach (string provider in Catalog.ExternalSolvers.Concat(Catalog.AnalyticalIds.Contains(id) ? new[] { "analytical" } : []))
            {
                try
                {
                    var reference = Catalog.ReadReference(id, provider, references);
                    referenceChecks.Add(Comparison.Compare(input, actual, reference, provider));
                    referenceEquilibria.Add(Physics.ReferenceEquilibrium(input, reference));
                }
                catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or InvalidOperationException or KeyNotFoundException)
                {
                    var failed = new CheckReport(id,provider);
                    failed.Require(false, ex.Message);
                    referenceChecks.Add(failed);
                }
            }
            var equilibrium = Physics.Equilibrium(input, solution);
            var fields = Physics.Fields(input, solution);
            var metamorphic = new[] { "load2", "load-1", "E2", "I2", "zeroNode" }.Select(op => Physics.Metamorphic(input,op)).ToList();
            if (Physics.SuperpositionIds.Contains(id)) metamorphic.Add(Physics.Superposition(input));
            var all = referenceChecks.Concat(referenceEquilibria).Concat(metamorphic).Append(equilibrium).Append(fields).ToArray();
            string status = all.Any(r => r.Status == "FAIL") ? "FAIL" : all.Any(r => r.Status == "PARTIAL") ? "PARTIAL" : "PASS";
            results.Add(new(id,status,referenceChecks.ToArray(),equilibrium,fields,metamorphic.ToArray(),referenceEquilibria.ToArray()));
        }
        return new(DateTimeOffset.UtcNow.ToString("O"), SolverHash(), results.Count, Catalog.AnalyticalIds.Length,
            results.Sum(c => c.References.Where(r => Catalog.ExternalSolvers.Contains(r.ReferenceSolver)).Sum(r => r.Comparisons)),
            results.Sum(c => c.References.Where(r => r.ReferenceSolver == "analytical").Sum(r => r.Comparisons)),
            results.Sum(c => c.Metamorphic.Length),
            results.Sum(c => c.Equilibrium.Comparisons+c.PhysicalFields.Comparisons+c.ReferenceEquilibria.Sum(r => r.Comparisons)),
            results.Sum(c => c.References.Concat(c.ReferenceEquilibria).Concat(c.Metamorphic)
                .Append(c.Equilibrium).Append(c.PhysicalFields).Sum(r => r.Comparisons)),
            results.Sum(c => c.References.Sum(r => r.NotApplicable.Count)),
            results.All(c => c.Status == "PASS"), results.ToArray());
    }

    public static string SolverHash()
    {
        using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        foreach (string project in new[] { "SpanDraft.Core", "SpanDraft.Solver" })
            foreach (string file in Directory.GetFiles(Path.Combine(Catalog.Root,"src",project), "*", SearchOption.AllDirectories)
                .Where(f => (f.EndsWith(".cs") || f.EndsWith(".csproj")) && !f.Contains(Path.DirectorySeparatorChar+"obj"+Path.DirectorySeparatorChar) &&
                    !f.Contains(Path.DirectorySeparatorChar+"bin"+Path.DirectorySeparatorChar)).Order(StringComparer.Ordinal))
                hash.AppendData(File.ReadAllBytes(file));
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
