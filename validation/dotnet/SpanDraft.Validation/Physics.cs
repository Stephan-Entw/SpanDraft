using SpanDraft.Solver;
using static SpanDraft.Validation.ValidationCase;

namespace SpanDraft.Validation;

public static class Physics
{
    public static Tolerance Strict(string q) => new(q is "u" or "w" or "theta" ? 1e-12 : 1e-7, 1e-9);

    public static CheckReport Equilibrium(ValidationCase input, BeamSolution solution, string source = "SpanDraft")
    {
        var report = new CheckReport(input.CaseId, source+" equilibrium");
        double horizontal = 0, vertical = 0, moment = 0;
        foreach (var node in solution.Nodes)
        {
            horizontal += node.ReactionX?.Newtons ?? 0;
            double ry = node.ReactionY?.Newtons ?? 0;
            vertical += ry;
            moment += ry * node.Position.Meters + (node.ReactionMoment?.NewtonMeters ?? 0);
        }
        AddLoads(input, ref vertical, ref moment);
        report.Number("sumFx", horizontal, 0, Strict("Rx"));
        report.Number("sumFy", vertical, 0, Strict("Ry"));
        report.Number("sumMoment0", moment, 0, Strict("Rm"));
        return report;
    }

    public static CheckReport ReferenceEquilibrium(ValidationCase input, ReferenceData reference)
    {
        var report = new CheckReport(input.CaseId, reference.Solver+" equilibrium");
        double horizontal = 0, vertical = 0, moment = 0;
        foreach (var node in reference.Reactions)
        {
            horizontal += node.Values.GetValueOrDefault("Rx");
            double ry = node.Values.GetValueOrDefault("Ry");
            vertical += ry;
            moment += ry*node.Position + node.Values.GetValueOrDefault("Rm");
        }
        AddLoads(input, ref vertical, ref moment);
        if (reference.Availability["Rx"] == "supported") report.Number("sumFx", horizontal, 0, Comparison.Force);
        report.Number("sumFy", vertical, 0, Comparison.Force);
        report.Number("sumMoment0", moment, 0, Comparison.Moment);
        return report;
    }

    private static void AddLoads(ValidationCase input, ref double vertical, ref double moment)
    {
        foreach (var force in input.PointForces) { vertical += force.Value; moment += force.Value*force.Position; }
        foreach (var couple in input.PointMoments) moment += couple.Value;
        foreach (var q in input.Udls)
        {
            double resultant = q.Value*(q.End-q.Start);
            vertical += resultant;
            moment += resultant*(q.Start+q.End)/2;
        }
    }

    public static CheckReport Fields(ValidationCase input, BeamSolution solution)
    {
        var report = new CheckReport(input.CaseId, "physical fields");
        foreach (var evaluation in input.Evaluations)
        {
            var section = solution.EvaluateAt(M(evaluation.Position), Enum.Parse<EvaluationSide>(evaluation.Side));
            report.Number("u", section.AxialDisplacement.Meters, 0, Strict("u"), evaluation.Position, evaluation.Side);
            report.Number("N", section.AxialForce.Newtons, 0, Strict("N"), evaluation.Position, evaluation.Side);
        }
        double[] events = input.Events();
        foreach (double x in events.Skip(1).SkipLast(1))
        {
            var before = solution.EvaluateAt(M(x), EvaluationSide.Left);
            var after = solution.EvaluateAt(M(x), EvaluationSide.Right);
            var node = solution.Nodes.Single(n => n.Position.Meters == x);
            double force = input.PointForces.Where(p => p.Position == x).Sum(p => p.Value) + (node.ReactionY?.Newtons ?? 0);
            double couple = input.PointMoments.Where(p => p.Position == x).Sum(p => p.Value) + (node.ReactionMoment?.NewtonMeters ?? 0);
            report.Number("V jump", after.ShearForce.Newtons-before.ShearForce.Newtons, force, Strict("V"), x, "Right-Left");
            report.Number("M jump", after.BendingMoment.NewtonMeters-before.BendingMoment.NewtonMeters, -couple, Strict("M"), x, "Right-Left");
            report.Number("w continuity", after.TransverseDisplacement.Meters, before.TransverseDisplacement.Meters, Strict("w"), x);
            report.Number("theta continuity", after.RotationRadians, before.RotationRadians, Strict("theta"), x);
        }
        for (int i = 1; i < events.Length; i++)
        {
            double a = events[i-1], b = events[i];
            foreach (double fraction in new[] { 0.37, 0.63 })
            {
                double x = a+(b-a)*fraction;
                double q = input.Udls.Where(q => q.Start < x && x < q.End).Sum(q => q.Value);
                var middle = solution.EvaluateAt(M(x), EvaluationSide.Right);
                foreach (double h in new[] { 0.01*(b-a), 0.005*(b-a) })
                {
                    var left = solution.EvaluateAt(M(x-h), EvaluationSide.Right);
                    var right = solution.EvaluateAt(M(x+h), EvaluationSide.Right);
                    report.Number("dM/dx", (right.BendingMoment.NewtonMeters-left.BendingMoment.NewtonMeters)/(2*h),
                        middle.ShearForce.Newtons, Strict("V"), x);
                    report.Number("dV/dx", (right.ShearForce.Newtons-left.ShearForce.Newtons)/(2*h),
                        q, Strict("q"), x);
                }
            }
        }
        return report;
    }

    public static CheckReport Metamorphic(ValidationCase input, string operation)
    {
        var original = Solve(input);
        ValidationCase transformed;
        Func<string, double> scale;
        switch (operation)
        {
            case "load2": transformed = input.ScaleLoads(2); scale = _ => 2; break;
            case "load-1": transformed = input.ScaleLoads(-1); scale = _ => -1; break;
            case "E2": transformed = input with { E = 2*input.E }; scale = q => q is "u" or "w" or "theta" ? 0.5 : 1; break;
            case "I2": transformed = input with { I = 2*input.I }; scale = q => q is "w" or "theta" ? 0.5 : 1; break;
            case "zeroNode":
                double[] events = input.Events();
                var widest = events.Zip(events.Skip(1), (a,b) => (a,b)).MaxBy(p => p.b-p.a);
                double x = widest.a+0.41*(widest.b-widest.a);
                transformed = input with { PointForces = [..input.PointForces, new(x,0)] };
                scale = _ => 1;
                break;
            default: throw new ArgumentException(operation);
        }
        var solved = Solve(transformed);
        var report = CompareSolutions(input, solved, [(original,1.0)], scale, operation);
        if (operation == "zeroNode") report.Require(solved.Nodes.Count == original.Nodes.Count+1, "Null force did not create an extra node");
        return report;
    }

    public static readonly string[] SuperpositionIds = ["V04", "V08", "V09", "V11", "V13", "V15"];
    public static CheckReport Superposition(ValidationCase input)
    {
        ValidationCase a, b;
        if (input.CaseId == "V13")
        {
            a = input with { Udls = [input.Udls[0]] };
            b = input with { Udls = [input.Udls[1]] };
        }
        else if (input.CaseId == "V04")
        {
            a = input;
            b = input with { Udls = [], PointForces = [new(0.41*input.Length,-150)] };
        }
        else if (input.CaseId == "V09")
        {
            a = input;
            b = input with { PointForces = [], Udls = [new(0,3,-100)] };
        }
        else
        {
            a = input with { Udls = [] };
            b = input with { PointForces = [], PointMoments = [] };
        }
        var combined = input with { PointForces = [..a.PointForces,..b.PointForces],
            PointMoments = [..a.PointMoments,..b.PointMoments], Udls = [..a.Udls,..b.Udls] };
        return CompareSolutions(input, Solve(combined), [(Solve(a),1.0),(Solve(b),1.0)], _ => 1, "superposition");
    }

    private static CheckReport CompareSolutions(ValidationCase input, BeamSolution actual,
        (BeamSolution Solution, double Weight)[] expected, Func<string,double> scale, string operation)
    {
        var report = new CheckReport(input.CaseId, operation);
        foreach (SupportInput support in input.Supports)
        {
            var observed = actual.Nodes.Single(n => n.Position.Meters == support.Position);
            foreach (string q in new[] { "Rx", "Ry", "Rm" })
            {
                double? value = Reaction(observed,q);
                double?[] components = expected.Select(e => Reaction(e.Solution.Nodes.Single(n => n.Position.Meters == support.Position),q)).ToArray();
                report.Require(components.All(v => v.HasValue == value.HasValue), "Reaction restraint changed");
                if (value.HasValue) report.Number(q, value.Value, components.Select((v,i) => v!.Value*expected[i].Weight).Sum()*scale(q), Strict(q), support.Position);
            }
        }
        foreach (var evaluation in input.Evaluations)
        {
            var side = Enum.Parse<EvaluationSide>(evaluation.Side);
            var observed = Results.Fields(actual.EvaluateAt(M(evaluation.Position), side));
            var fields = expected.Select(e => Results.Fields(e.Solution.EvaluateAt(M(evaluation.Position), side))).ToArray();
            foreach (string q in observed.Keys)
                report.Number(q, observed[q], fields.Select((f,i) => f[q]*expected[i].Weight).Sum()*scale(q), Strict(q), evaluation.Position, evaluation.Side);
        }
        return report;
    }

    private static double? Reaction(BeamNodeResult node, string q) => q switch {
        "Rx" => node.ReactionX?.Newtons, "Ry" => node.ReactionY?.Newtons, "Rm" => node.ReactionMoment?.NewtonMeters, _ => throw new ArgumentException(q)
    };
}
