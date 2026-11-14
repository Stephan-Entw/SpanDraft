using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpanDraft.Core.Beams;
using SpanDraft.Core.Loads;
using SpanDraft.Core.Materials;
using SpanDraft.Core.Releases;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Solver;

namespace SpanDraft.Validation;

public sealed record SupportInput(double Position, string Type)
{
    /// <summary>Spring supports only; optional, so the inputs and hashes of the earlier cases stay unchanged.</summary>
    public double? TranslationalStiffness { get; init; }
    public double? RotationalStiffness { get; init; }
    public bool? RigidTranslation { get; init; }

    /// <summary>The reaction quantities the support provides; a spring support is axially rigid.</summary>
    public string[] ReactionQuantities => Type switch
    {
        "Fixed" => ["Rx", "Ry", "Rm"],
        "Pinned" => ["Rx", "Ry"],
        "Spring" => ["Rx", ..(TranslationalStiffness is not null || RigidTranslation == true ? new[] { "Ry" } : []),
            ..(RotationalStiffness is not null ? new[] { "Rm" } : [])],
        _ => ["Ry"]
    };

    /// <summary>The springs scaled by a factor; other supports are unchanged.</summary>
    public SupportInput ScaleStiffness(double factor) => Type != "Spring" ? this : this with
    {
        TranslationalStiffness = TranslationalStiffness * factor,
        RotationalStiffness = RotationalStiffness * factor
    };

    public Support ToSupport() => Type == "Spring"
        ? Support.CreateSpring(ValidationCase.M(Position),
            TranslationalStiffness is { } k ? SpanDraft.Core.Units.TranslationalStiffness.FromNewtonsPerMeter(k) : null,
            RotationalStiffness is { } kt ? SpanDraft.Core.Units.RotationalStiffness.FromNewtonMetersPerRadian(kt) : null, RigidTranslation == true)
        : new Support(ValidationCase.M(Position), Enum.Parse<SupportType>(Type));
}
/// <summary>One beam segment of a case with schema version 2: exact positions and its own E, A and I.</summary>
public sealed record SegmentInput(double Start, double End, double E, double A, double I);
/// <summary>An internal moment release (hinge) at a position strictly inside the beam.</summary>
public sealed record MomentReleaseInput(double Position);
public sealed record PointInput(double Position, double Value);
public sealed record UdlInput(double Start, double End, double Value);
public sealed record LineInput(double Start, double End, double StartValue, double EndValue);
public sealed record EvaluationInput(double Position, string Side);
public sealed record ValidationCase(int SchemaVersion, int CaseVersion, string CaseId, string Description,
    double Length, double E, double A, double I, SupportInput[] Supports, PointInput[] PointForces,
    PointInput[] PointMoments, UdlInput[] Udls, EvaluationInput[] Evaluations)
{
    /// <summary>Linearly varying line loads; optional, so the inputs and hashes of the earlier cases stay unchanged.</summary>
    public LineInput[] LineLoads { get; init; } = [];

    /// <summary>
    /// Beam segments; optional (schema version 2), so the inputs and hashes of the earlier cases stay unchanged.
    /// A case without segments has one over the whole length from its top-level E, A and I.
    /// </summary>
    public SegmentInput[] Segments { get; init; } = [];

    /// <summary>Internal moment releases; optional, so the inputs and hashes of the earlier cases stay unchanged.</summary>
    public MomentReleaseInput[] MomentReleases { get; init; } = [];

    public SegmentInput[] SegmentList => Segments.Length > 0 ? Segments : [new(0, Length, E, A, I)];

    /// <summary>The smallest bending stiffness EI of any segment.</summary>
    public double LeastStiffness => SegmentList.Min(s => s.E * s.I);

    public ValidationCase ScaleE(double factor) => Segments.Length > 0
        ? this with { Segments = Segments.Select(s => s with { E = factor * s.E }).ToArray() } : this with { E = factor * E };

    public ValidationCase ScaleI(double factor) => Segments.Length > 0
        ? this with { Segments = Segments.Select(s => s with { I = factor * s.I }).ToArray() } : this with { I = factor * I };

    public BeamModel ToBeam()
    {
        // Yield strength and W are required by Core but play no role in this scope.
        var segments = SegmentList.Select(g => new BeamSegment(M(g.Start), M(g.End),
            new Material("Validation only", Pressure.FromPascals(g.E), Pressure.FromPascals(235e6), MaterialStrengthType.Re),
            new CustomSection(Area.FromSquareMeters(g.A), SecondMomentOfArea.FromMetersToTheFourth(g.I),
                SectionModulus.FromCubicMeters(1)), SectionAxisDesignation.Y)).ToArray();
        return new BeamModel(M(Length), segments,
            Supports.Select(s => s.ToSupport()),
            PointForces.Select(p => (BeamLoad)new PointForce(M(p.Position), Force.FromNewtons(p.Value)))
                .Concat(PointMoments.Select(p => (BeamLoad)new PointMoment(M(p.Position), Moment.FromNewtonMeters(p.Value))))
                .Concat(Udls.Select(q => (BeamLoad)new DistributedLoad(M(q.Start), M(q.End), ForcePerLength.FromNewtonsPerMeter(q.Value))))
                .Concat(LineLoads.Select(q => (BeamLoad)new DistributedLoad(M(q.Start), M(q.End),
                    ForcePerLength.FromNewtonsPerMeter(q.StartValue), ForcePerLength.FromNewtonsPerMeter(q.EndValue)))),
            MomentReleases.Select(r => new MomentRelease(M(r.Position))));
    }

    public double[] Events() => new[] { 0.0, Length }.Concat(Segments.Skip(1).Select(s => s.Start))
        .Concat(Supports.Select(s => s.Position)).Concat(MomentReleases.Select(r => r.Position))
        .Concat(PointForces.Select(p => p.Position)).Concat(PointMoments.Select(p => p.Position))
        .Concat(Udls.SelectMany(q => new[] { q.Start, q.End }))
        .Concat(LineLoads.SelectMany(q => new[] { q.Start, q.End })).Distinct().Order().ToArray();

    public ValidationCase ScaleLoads(double factor) => this with {
        PointForces = PointForces.Select(p => p with { Value = factor * p.Value }).ToArray(),
        PointMoments = PointMoments.Select(p => p with { Value = factor * p.Value }).ToArray(),
        Udls = Udls.Select(q => q with { Value = factor * q.Value }).ToArray(),
        LineLoads = LineLoads.Select(q => q with { StartValue = factor * q.StartValue, EndValue = factor * q.EndValue }).ToArray()
    };

    /// <summary>The summed intensity of all distributed loads at x inside their open intervals.</summary>
    public double IntensityAt(double x) => Udls.Where(q => q.Start < x && x < q.End).Sum(q => q.Value) +
        LineLoads.Where(q => q.Start < x && x < q.End)
            .Sum(q => q.StartValue + (q.EndValue - q.StartValue) * (x - q.Start) / (q.End - q.Start));

    public static Length M(double meters) => SpanDraft.Core.Units.Length.FromMeters(meters);
    public static BeamSolution Solve(ValidationCase input) => new EulerBernoulliBeamSolver().Solve(input.ToBeam());
}

public static class Catalog
{
    public static readonly JsonSerializerOptions Json = new() {
        PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    public static readonly string[] CaseIds = Enumerable.Range(1, 37).Select(i => $"V{i:00}")
        .Concat(new[] { "V04-S01", "V04-S02", "V04-S03" }).ToArray();
    public static readonly string[] LinearLoadIds = ["V16", "V17", "V18", "V19"];
    /// <summary>Spring supports; V24 is the very stiff spring whose rigid limit is a metamorphic relation.</summary>
    public static readonly string[] SpringIds = ["V20", "V21", "V22", "V23", "V24"];
    /// <summary>Beams with several segments (schema version 2); V28 has section changes on springs.</summary>
    public static readonly string[] SegmentIds = ["V25", "V26", "V27", "V28", "V29"];
    /// <summary>Beams with internal moment releases (hinges); V35 has the release on a section change.</summary>
    public static readonly string[] MomentReleaseIds = ["V30", "V31", "V32", "V33", "V34", "V35", "V36", "V37"];
    public static readonly string[] AnalyticalIds = new[] { "V01", "V02", "V03", "V04", "V05", "V06", "V04-S01", "V04-S02", "V04-S03" }
        .Concat(LinearLoadIds).Concat(SpringIds).Concat(SegmentIds).Concat(MomentReleaseIds).ToArray();
    public static readonly string[] ExternalSolvers = ["indeterminatebeam", "pycba"];

    /// <summary>
    /// The external solvers that can represent a case. IndeterminateBeam has no rotational spring, and at k L³/EI = 10¹²
    /// both external solvers lose the digits that the 10⁻⁵ comparison tolerance needs, so V24 keeps its analytical reference.
    /// IndeterminateBeam has one E and one I for the whole beam and cannot represent segments, and it has no internal
    /// moment release, so these cases are compared with PyCBA and the analytical reference.
    /// </summary>
    public static string[] ExternalSolversFor(string id) => id switch
    {
        "V22" or "V23" => ["pycba"],
        "V24" => [],
        _ when SegmentIds.Contains(id) || MomentReleaseIds.Contains(id) => ["pycba"],
        _ => ExternalSolvers
    };
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
        if (input.SchemaVersion is not (1 or 2) || input.SchemaVersion == 2 != (input.Segments.Length > 0) || input.CaseVersion != 1 || input.CaseId != id || string.IsNullOrWhiteSpace(input.Description))
            throw new InvalidDataException("Invalid case identity/version: " + id);
        double expectedStart = 0;
        foreach (SegmentInput segment in input.Segments)
        {
            if (segment.Start != expectedStart || !(segment.End > segment.Start) || segment.E <= 0 || segment.A <= 0 || segment.I <= 0)
                throw new InvalidDataException("Invalid segment: " + id);
            expectedStart = segment.End;
        }
        if (input.Segments.Length > 0 && (expectedStart != input.Length || input.E != 0 || input.A != 0 || input.I != 0))
            throw new InvalidDataException("Segments must tile the beam and replace the top-level E, A and I: " + id);
        if (input.MomentReleases.Any(r => !(r.Position > 0 && r.Position < input.Length)))
            throw new InvalidDataException("A moment release must lie strictly inside the beam: " + id);
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
