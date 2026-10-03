using SpanDraft.Solver;
using static SpanDraft.Validation.ValidationCase;

namespace SpanDraft.Validation;

public sealed record ResultRow(double Position, string? Side, Dictionary<string, double> Values);
public sealed record ExtremumLocation(double Position, string? Side);
public sealed record Plateau(double Start, double End);
public sealed record ExtremumData(double Value, ExtremumLocation[] Locations, Plateau[] Plateaus)
{
    public string LocationStatus { get; init; } = "supported";
    public string? LocationNote { get; init; }
}
public sealed record ReferenceData
{
    public int SchemaVersion { get; init; } = 1;
    public int CaseVersion { get; init; } = 1;
    public string CaseId { get; init; } = "";
    public string InputSha256 { get; init; } = "";
    public string Solver { get; init; } = "";
    public string Status { get; init; } = "PASS";
    public Dictionary<string, object> Provenance { get; init; } = [];
    public Dictionary<string, string> Availability { get; init; } = [];
    public ResultRow[] Reactions { get; set; } = [];
    public ResultRow[] Samples { get; set; } = [];
    public Dictionary<string, ExtremumData> Extrema { get; set; } = [];
    public Dictionary<string, object>? Convergence { get; set; }
}

public static class Results
{
    public static Dictionary<string, double> Fields(BeamSectionResult r) => new() {
        ["u"] = r.AxialDisplacement.Meters, ["w"] = r.TransverseDisplacement.Meters,
        ["theta"] = r.RotationRadians, ["N"] = r.AxialForce.Newtons,
        ["V"] = r.ShearForce.Newtons, ["M"] = r.BendingMoment.NewtonMeters
    };

    public static ReferenceData Export(ValidationCase input, BeamSolution? solution = null)
    {
        solution ??= Solve(input);
        var data = new ReferenceData {
            CaseId = input.CaseId, CaseVersion = input.CaseVersion, InputSha256 = Catalog.Hash(input.CaseId), Solver = "SpanDraft",
            Availability = Catalog.Quantities.ToDictionary(q => q, _ => "supported"),
            Provenance = new() { ["package"] = "SpanDraft.Solver", ["version"] = typeof(BeamSolution).Assembly.GetName().Version!.ToString(),
                ["generatedAt"] = DateTimeOffset.UtcNow.ToString("O"), ["theory"] = "Euler-Bernoulli",
                ["signMapping"] = "Native SI / SpanDraft", ["dotnet"] = Environment.Version.ToString() }
        };
        data.Reactions = solution.Nodes.Where(n => n.ReactionX.HasValue || n.ReactionY.HasValue || n.ReactionMoment.HasValue)
            .Select(n => {
                Dictionary<string, double> values = [];
                if (n.ReactionX.HasValue) values["Rx"] = n.ReactionX.Value.Newtons;
                if (n.ReactionY.HasValue) values["Ry"] = n.ReactionY.Value.Newtons;
                if (n.ReactionMoment.HasValue) values["Rm"] = n.ReactionMoment.Value.NewtonMeters;
                return new ResultRow(n.Position.Meters, null, values);
            }).ToArray();
        data.Samples = input.Evaluations.Select(e => new ResultRow(e.Position, e.Side,
            Fields(solution.EvaluateAt(M(e.Position), Enum.Parse<EvaluationSide>(e.Side))))).ToArray();
        BeamExtrema ext = solution.Extrema;
        data.Extrema = new() {
            ["wMin"] = Ext(ext.MinimumTransverseDisplacement.Value.Meters, ext.MinimumTransverseDisplacement.Position.Meters, ext.MinimumTransverseDisplacement.Side),
            ["wMax"] = Ext(ext.MaximumTransverseDisplacement.Value.Meters, ext.MaximumTransverseDisplacement.Position.Meters, ext.MaximumTransverseDisplacement.Side),
            ["VMin"] = Ext(ext.MinimumShearForce.Value.Newtons, ext.MinimumShearForce.Position.Meters, ext.MinimumShearForce.Side),
            ["VMax"] = Ext(ext.MaximumShearForce.Value.Newtons, ext.MaximumShearForce.Position.Meters, ext.MaximumShearForce.Side),
            ["MMin"] = Ext(ext.MinimumBendingMoment.Value.NewtonMeters, ext.MinimumBendingMoment.Position.Meters, ext.MinimumBendingMoment.Side),
            ["MMax"] = Ext(ext.MaximumBendingMoment.Value.NewtonMeters, ext.MaximumBendingMoment.Position.Meters, ext.MaximumBendingMoment.Side)
        };
        return data;
    }

    private static ExtremumData Ext(double value, double x, EvaluationSide? side) => new(value, [new(x, side?.ToString())], []);
}
