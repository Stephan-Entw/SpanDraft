using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpanDraft.Core.Beams;
using SpanDraft.Core.Loads;
using SpanDraft.Core.Materials;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Solver;

namespace SpanDraft.Validation;

public sealed record SupportInput(double Position, string Type);
public sealed record PointInput(double Position, double Value);
public sealed record UdlInput(double Start, double End, double Value);
public sealed record EvaluationInput(double Position, string Side);
public sealed record ValidationCase(int SchemaVersion, int CaseVersion, string CaseId, string Description,
    double Length, double E, double A, double I, SupportInput[] Supports, PointInput[] PointForces,
    PointInput[] PointMoments, UdlInput[] Udls, EvaluationInput[] Evaluations)
{
    public BeamModel ToBeam()
    {
        // Yield strength and W are required by Core but play no role in this scope.
        var material = new Material("Validation only", Pressure.FromPascals(E), Pressure.FromPascals(235e6));
        var section = new CustomSection(Area.FromSquareMeters(A),
            SecondMomentOfArea.FromMetersToTheFourth(I), SectionModulus.FromCubicMeters(1));
        return new BeamModel(M(Length), material, section,
            Supports.Select(s => new Support(M(s.Position), Enum.Parse<SupportType>(s.Type))),
            PointForces.Select(p => (BeamLoad)new PointForce(M(p.Position), Force.FromNewtons(p.Value)))
                .Concat(PointMoments.Select(p => (BeamLoad)new PointMoment(M(p.Position), Moment.FromNewtonMeters(p.Value))))
                .Concat(Udls.Select(q => (BeamLoad)new UniformDistributedLoad(M(q.Start), M(q.End), ForcePerLength.FromNewtonsPerMeter(q.Value)))));
    }

    public double[] Events() => new[] { 0.0, Length }.Concat(Supports.Select(s => s.Position))
        .Concat(PointForces.Select(p => p.Position)).Concat(PointMoments.Select(p => p.Position))
        .Concat(Udls.SelectMany(q => new[] { q.Start, q.End })).Distinct().Order().ToArray();

    public ValidationCase ScaleLoads(double factor) => this with {
        PointForces = PointForces.Select(p => p with { Value = factor * p.Value }).ToArray(),
        PointMoments = PointMoments.Select(p => p with { Value = factor * p.Value }).ToArray(),
        Udls = Udls.Select(q => q with { Value = factor * q.Value }).ToArray()
    };

    public static Length M(double meters) => SpanDraft.Core.Units.Length.FromMeters(meters);
    public static BeamSolution Solve(ValidationCase input) => new EulerBernoulliBeamSolver().Solve(input.ToBeam());
}

public static class Catalog
{
    public static readonly JsonSerializerOptions Json = new() {
        PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    public static readonly string[] CaseIds = Enumerable.Range(1, 15).Select(i => $"V{i:00}")
        .Concat(new[] { "V04-S01", "V04-S02", "V04-S03" }).ToArray();
    public static readonly string[] AnalyticalIds = new[] { "V01", "V02", "V03", "V04", "V05", "V06", "V04-S01", "V04-S02", "V04-S03" };
    public static readonly string[] ExternalSolvers = ["indeterminatebeam", "pycba"];
    public static readonly string[] Quantities = ["Rx", "Ry", "Rm", "u", "w", "theta", "N", "V", "M"];
    public static string Root => FindRoot();

    private static string FindRoot()
    {
        foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            for (DirectoryInfo? dir = new(start); dir is not null; dir = dir.Parent)
                if (Directory.Exists(Path.Combine(dir.FullName, "validation", "cases"))) return dir.FullName;
        throw new DirectoryNotFoundException("Run validation in the SpanDraft checkout.");
    }

    public static string CasePath(string id) => Path.Combine(Root, "validation", "cases", id + ".json");
    public static string Hash(string id) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(CasePath(id))));
    public static ValidationCase Load(string id)
    {
        ValidationCase input = JsonSerializer.Deserialize<ValidationCase>(File.ReadAllText(CasePath(id)), Json)
            ?? throw new InvalidDataException("Empty case " + id);
        if (input.SchemaVersion != 1 || input.CaseVersion != 1 || input.CaseId != id || string.IsNullOrWhiteSpace(input.Description))
            throw new InvalidDataException("Invalid case identity/version: " + id);
        double[] events = input.Events();
        if (input.Evaluations.Length == 0 || input.Evaluations.Distinct().Count() != input.Evaluations.Length)
            throw new InvalidDataException("Empty or duplicate evaluation positions: " + id);
        foreach (var row in input.Evaluations)
            if (!double.IsFinite(row.Position) || row.Position < 0 || row.Position > input.Length ||
                row.Side is not ("Left" or "Right")) throw new InvalidDataException("Invalid evaluation: " + id);
        foreach (double x in events)
        {
            string[] sides = x == 0 ? ["Right"] : x == input.Length ? ["Left"] : ["Left", "Right"];
            foreach (string side in sides)
                if (!input.Evaluations.Contains(new(x, side))) throw new InvalidDataException("Missing event side: " + id);
        }
        for (int i = 1; i < events.Length; i++)
            if (input.Evaluations.Count(p => p.Position > events[i-1] && p.Position < events[i]) < 2)
                throw new InvalidDataException("Missing nonnodal coverage: " + id);
        _ = input.ToBeam();
        return input;
    }

    public static ReferenceData ReadReference(string id, string solver, string? directory = null) =>
        JsonSerializer.Deserialize<ReferenceData>(File.ReadAllText(Path.Combine(directory ??
            Path.Combine(Root, "validation", "references"), $"{id}.{solver}.json")), Json)
        ?? throw new InvalidDataException("Empty reference");
}
