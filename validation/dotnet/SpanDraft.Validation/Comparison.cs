namespace SpanDraft.Validation;

public sealed record Tolerance(double AbsTol, double RelTol)
{
    public double Limit(double reference) => AbsTol + RelTol * Math.Abs(reference);
    public bool Contains(double actual, double reference) => double.IsFinite(actual) && double.IsFinite(reference) &&
        Math.Abs(actual - reference) <= Limit(reference);
}

public sealed record ValidationFailure(string CaseId, string Quantity, double? Position, string? Side,
    double? SpanDraftValue, double? ReferenceValue, double? AbsoluteDeviation, double? RelativeDeviation,
    double AbsTol, double RelTol, string ReferenceSolver, string Reason);

public sealed record NotApplicableComparison(string Quantity, string Reason)
{
    public string Status => "NOT APPLICABLE";
}

public sealed class CheckReport(string caseId, string referenceSolver)
{
    public string CaseId { get; } = caseId;
    public string ReferenceSolver { get; } = referenceSolver;
    public int Comparisons { get; private set; }
    public List<ValidationFailure> Failures { get; } = [];
    public List<NotApplicableComparison> NotApplicable { get; } = [];
    public string Status { get; set; } = "PASS";
    public void Partial(string quantity, string reason, double? position = null, string? side = null)
    {
        if (Status != "FAIL") Status = "PARTIAL";
        Failures.Add(new(CaseId,quantity,position,side,null,null,null,null,0,0,ReferenceSolver,reason));
    }
    public void Require(bool condition, string reason)
    {
        if (condition) return;
        Status = "FAIL";
        Failures.Add(new(CaseId, "coverage/provenance", null, null, null, null, null, null, 0, 0, ReferenceSolver, reason));
    }
    public void Number(string quantity, double actual, double reference, Tolerance tolerance,
        double? position = null, string? side = null, string reason = "Tolerance exceeded")
    {
        Comparisons++;
        if (tolerance.Contains(actual, reference)) return;
        Status = "FAIL";
        double error = Math.Abs(actual-reference);
        Failures.Add(new(CaseId, quantity, position, side, double.IsFinite(actual) ? actual : null,
            double.IsFinite(reference) ? reference : null, double.IsFinite(error) ? error : null,
            reference != 0 && double.IsFinite(error/Math.Abs(reference)) ? error/Math.Abs(reference) : null,
            tolerance.AbsTol, tolerance.RelTol, ReferenceSolver, reason));
    }
    public void Position(string quantity, double actual, double reference, string? side, bool belongs)
    {
        Comparisons++;
        if (belongs) return;
        Status = "FAIL";
        double error = Math.Abs(actual-reference);
        Failures.Add(new(CaseId, quantity+".position", actual, side, actual, reference, error,
            reference != 0 ? error/Math.Abs(reference) : null, Comparison.Position.AbsTol,
            Comparison.Position.RelTol, ReferenceSolver, "Extremum position/side outside reference location set"));
    }
}

public static class Comparison
{
    private static readonly string[] ExtremaKeys = ["wMin", "wMax", "VMin", "VMax", "MMin", "MMax"];
    private const string V14LocationNote = "V14 native residual-only field: physical extremum location set is not established by IndeterminateBeam 2.4.0. Raw locations retained; no zero/plateau substitution.";
    public static readonly Tolerance Force = new(1e-5, 1e-6);
    public static readonly Tolerance Moment = new(1e-5, 1e-6);
    public static readonly Tolerance Displacement = new(1e-10, 1e-6);
    public static readonly Tolerance Rotation = new(1e-10, 1e-6);
    public static readonly Tolerance Position = new(1e-8, 1e-7);
    public static Tolerance For(string q) => q switch {
        "Rx" or "Ry" or "N" or "V" => Force, "Rm" or "M" => Moment,
        "u" or "w" => Displacement, "theta" => Rotation, _ => throw new ArgumentException(q)
    };
    private static readonly Dictionary<string, string[]> Supported = new() {
        ["analytical"] = Catalog.Quantities,
        ["indeterminatebeam"] = ["Rx", "Ry", "Rm", "w", "theta", "N", "V", "M"],
        ["pycba"] = ["Ry", "Rm", "w", "theta", "V", "M"]
    };

    public static CheckReport Compare(ValidationCase input, ReferenceData actual, ReferenceData reference, string expectedSolver)
    {
        var report = new CheckReport(input.CaseId, expectedSolver);
        report.Require(reference.SchemaVersion == 1 && reference.CaseVersion == input.CaseVersion &&
            reference.CaseId == input.CaseId && reference.Solver == expectedSolver, "Reference identity/schema mismatch");
        report.Require(reference.InputSha256 == Catalog.Hash(input.CaseId), "Stale reference input SHA-256");
        foreach (string key in new[] { "package", "version", "python", "generatedAt", "theory", "signMapping", "options", "adapterVersion", "source" })
            report.Require(reference.Provenance.TryGetValue(key, out object? value) && !string.IsNullOrWhiteSpace(value?.ToString()), "Missing provenance: " + key);
        string expectedVersion = expectedSolver switch { "pycba" => "1.0.2", "indeterminatebeam" => "2.4.0", _ => "1" };
        report.Require(reference.Provenance.GetValueOrDefault("version")?.ToString() == expectedVersion, "Unexpected reference version");
        report.Require(reference.Provenance.GetValueOrDefault("python")?.ToString() == "3.9.6", "Unexpected Python version");
        report.Require(reference.Provenance.GetValueOrDefault("requirementsSha256")?.ToString() == PythonLockHash(), "Reference Python lock hash differs");
        report.Require(reference.Provenance.GetValueOrDefault("adapterSourcesSha256")?.ToString() == PythonSourcesHash(), "Reference adapter source hash differs");
        report.Require(DateTimeOffset.TryParse(reference.Provenance.GetValueOrDefault("generatedAt")?.ToString(), out _), "Invalid generation time");
        foreach (string q in Catalog.Quantities)
            report.Require(reference.Availability.GetValueOrDefault(q) == (Supported[expectedSolver].Contains(q) ? "supported" : "notSupported"), "Unexpected/missing availability: " + q);
        report.Require(reference.Availability.Count == Catalog.Quantities.Length, "Unexpected availability entries");
        if (reference.Status != "PASS")
        {
            // The unchanged legacy golden marks extraction PARTIAL solely for
            // these six documented position limitations. Classify applicability
            // in the validation layer; retain its native numbers and locations.
            if (reference.Status == "PARTIAL")
            {
                if (!ExtremaKeys.All(key => reference.Extrema.TryGetValue(key, out var ext) &&
                    IsV14PositionNotApplicable(input, reference, expectedSolver, ext)))
                    report.Partial("reference extraction", "Reference extraction is PARTIAL");
            }
            else report.Require(false, "Reference extraction is " + reference.Status);
        }
        if (expectedSolver == "pycba") CheckConvergence(reference, report);
        var expectedSamples = input.Evaluations.Select(e => (e.Position, e.Side)).ToHashSet();
        report.Require(reference.Samples.Length == expectedSamples.Count &&
            reference.Samples.Select(r => (r.Position, r.Side!)).ToHashSet().SetEquals(expectedSamples), "Incomplete/duplicate sample positions or sides");
        foreach (var row in reference.Samples)
        {
            var match = actual.Samples.SingleOrDefault(r => r.Position == row.Position && r.Side == row.Side);
            report.Require(match is not null, "Unexpected sample position/side");
            foreach (string q in new[] { "u", "w", "theta", "N", "V", "M" }.Where(Supported[expectedSolver].Contains))
            {
                bool present = row.Values.TryGetValue(q, out double target);
                report.Require(present, "Missing sample quantity: " + q);
                if (present && match is not null) report.Number(q, match.Values[q], target, For(q), row.Position, row.Side);
            }
            report.Require(row.Values.Keys.All(q => Supported[expectedSolver].Contains(q) && q is not ("Rx" or "Ry" or "Rm")), "Unexpected sample quantity");
        }
        report.Require(reference.Reactions.Length == input.Supports.Length &&
            reference.Reactions.Select(r => r.Position).ToHashSet().SetEquals(input.Supports.Select(s => s.Position)), "Incomplete/duplicate support reactions");
        foreach (SupportInput support in input.Supports)
        {
            ResultRow? row = reference.Reactions.SingleOrDefault(r => r.Position == support.Position);
            ResultRow match = actual.Reactions.Single(r => r.Position == support.Position);
            string[] quantities = support.Type switch { "Fixed" => ["Rx", "Ry", "Rm"], "Pinned" => ["Rx", "Ry"], _ => ["Ry"] };
            var required = quantities.Where(Supported[expectedSolver].Contains).ToHashSet();
            report.Require(row is not null && row.Values.Keys.ToHashSet().SetEquals(required), "Reaction DOF coverage differs");
            if (row is not null)
                foreach (string q in required)
                    if (row.Values.TryGetValue(q, out double target)) report.Number(q, match.Values[q], target, For(q), support.Position);
        }
        report.Require(reference.Extrema.Keys.ToHashSet().SetEquals(ExtremaKeys), "Incomplete extrema coverage");
        foreach (string key in ExtremaKeys)
        {
            if (!reference.Extrema.TryGetValue(key, out var ext)) continue;
            string q = key[..1];
            var observed = actual.Extrema[key];
            var loc = observed.Locations.Single();
            report.Number(key, observed.Value, ext.Value, For(q), loc.Position, loc.Side);
            report.Require(ext.Locations.Length + ext.Plateaus.Length > 0, "Missing extremum location set: " + key);
            foreach (var target in ext.Locations)
                report.Require(double.IsFinite(target.Position) && target.Position >= 0 && target.Position <= input.Length &&
                    target.Side is null or "Left" or "Right", "Invalid extremum location");
            foreach (var plateau in ext.Plateaus)
                report.Require(double.IsFinite(plateau.Start) && double.IsFinite(plateau.End) && plateau.Start >= 0 &&
                    plateau.End <= input.Length && plateau.Start < plateau.End, "Invalid plateau interval");
            report.Require(ext.LocationStatus is "supported" or "notComparable", "Invalid extremum location availability");
            if (ext.LocationStatus == "notComparable")
            {
                report.Require(!string.IsNullOrWhiteSpace(ext.LocationNote), "Missing location limitation explanation");
                if (IsV14PositionNotApplicable(input, reference, expectedSolver, ext))
                    report.NotApplicable.Add(new(key+".position", ext.LocationNote! +
                        " Native w/M are nonconstant roundoff polynomials; V has unequal nonzero constant branches. " +
                        "A physical zero-field extremum set cannot be derived. The extremum value remains a mandatory comparison."));
                else
                    report.Partial(key+".position", ext.LocationNote ?? "Reference location unavailable",loc.Position,loc.Side);
                continue;
            }
            bool belongs = ext.Locations.Any(t => Position.Contains(loc.Position, t.Position) &&
                (t.Side is null || t.Side == loc.Side)) || ext.Plateaus.Any(p => InPlateau(loc, p) ||
                    BoundaryLimitMatches(reference,q,ext.Value,loc,p));
            double closest = ext.Locations.Select(t => t.Position)
                .Concat(ext.Plateaus.Select(p => Math.Clamp(loc.Position, p.Start, p.End)))
                .OrderBy(x => Math.Abs(x-loc.Position)).FirstOrDefault();
            report.Position(key, loc.Position, closest, loc.Side, belongs);
        }
        return report;
    }

    // Explicitly scoped to the investigated, version/input-bound V14 evidence
    // in validation/results/v14-native-fields.json. Unknown limitations remain
    // PARTIAL; an unavailable value, stale provenance or failed value still fails.
    private static bool IsV14PositionNotApplicable(ValidationCase input, ReferenceData reference,
        string expectedSolver, ExtremumData ext) => input.CaseId == "V14" && reference.CaseId == "V14" &&
        expectedSolver == "indeterminatebeam" && reference.Solver == expectedSolver &&
        reference.Provenance.GetValueOrDefault("version")?.ToString() == "2.4.0" &&
        reference.InputSha256 == "7c0512a80cc6e2122db82f774e3fb59b87ebcc667a8e3579bdf31aaf02e46d48" &&
        ext.LocationStatus == "notComparable" && ext.LocationNote == V14LocationNote;

    private static string PythonLockHash() => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
        File.ReadAllBytes(Path.Combine(Catalog.Root,"validation","python","requirements.txt"))));

    private static string PythonSourcesHash()
    {
        using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        foreach (string path in Directory.GetFiles(Path.Combine(Catalog.Root,"validation","python"),"*.py").Order(StringComparer.Ordinal))
        {
            hash.AppendData(System.Text.Encoding.UTF8.GetBytes(Path.GetFileName(path)));
            hash.AppendData(File.ReadAllBytes(path));
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void CheckConvergence(ReferenceData reference, CheckReport report)
    {
        report.Require(reference.Convergence is not null, "Missing PyCBA convergence evidence");
        if (reference.Convergence is null) return;
        var evidence = System.Text.Json.JsonSerializer.SerializeToElement(reference.Convergence,Catalog.Json);
        report.Require(evidence.GetProperty("fractionOfTolerance").GetDouble() == 0.1 &&
            evidence.GetProperty("requiredConsecutiveRefinements").GetInt32() == 2 &&
            evidence.GetProperty("maxNpts").GetInt32() == 131072, "Unexpected convergence policy");
        var trace = evidence.GetProperty("trace").EnumerateArray().ToArray();
        report.Require(trace.Length >= 3, "Insufficient convergence levels");
        for (int i=0; i<trace.Length; i++) report.Require(trace[i].GetProperty("npts").GetInt32() == 1024*(1<<i) &&
            trace[i].GetProperty("npts").GetInt32() <= 131072, "Invalid grid refinement sequence");
        if (reference.Status != "PASS") return;
        report.Require(evidence.GetProperty("converged").GetBoolean(), "PASS without convergence");
        foreach (var level in trace.TakeLast(2))
        {
            report.Require(level.GetProperty("topologyStable").GetBoolean() &&
                level.GetProperty("nativeEndpointMaxRatio").GetDouble() <= 0.1, "Endpoint/topology convergence failed");
            var ratios = level.GetProperty("maxRatioByKind");
            foreach (string kind in new[] { "displacement", "rotation", "force", "moment", "position" })
                report.Require(ratios.TryGetProperty(kind,out var ratio) && double.IsFinite(ratio.GetDouble()) && ratio.GetDouble() <= 0.1,
                    "Missing/failed convergence ratio: " + kind);
        }
    }

    private static bool BoundaryLimitMatches(ReferenceData reference, string q, double value, ExtremumLocation loc, Plateau plateau)
    {
        // A plateau's opposite boundary side can also attain the same extremum.
        // Verify that side against the explicitly stored native endpoint value;
        // never infer it by averaging or imposing a solver-specific convention.
        return (Position.Contains(loc.Position,plateau.Start) || Position.Contains(loc.Position,plateau.End)) &&
            reference.Samples.Any(s => s.Position == loc.Position && s.Side == loc.Side &&
                s.Values.TryGetValue(q,out double target) && For(q).Contains(target,value));
    }

    private static bool InPlateau(ExtremumLocation loc, Plateau plateau)
    {
        if (loc.Position > plateau.Start && loc.Position < plateau.End) return true;
        if (Position.Contains(loc.Position, plateau.Start) && loc.Side is null or "Right") return true;
        return Position.Contains(loc.Position, plateau.End) && loc.Side is null or "Left";
    }
}
