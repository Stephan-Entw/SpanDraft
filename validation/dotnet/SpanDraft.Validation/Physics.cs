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

    /// <summary>Global equilibrium of the published reactions; strict applies the SpanDraft tolerances instead of the external ones.</summary>
    public static CheckReport ReferenceEquilibrium(ValidationCase input, ReferenceData reference, bool strict = false)
    {
        Tolerance forceTol = strict ? Strict("Ry") : Comparison.Force, momentTol = strict ? Strict("Rm") : Comparison.Moment;
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
        if (reference.Availability.GetValueOrDefault("Rx") == "supported") report.Number("sumFx", horizontal, 0, forceTol);
        report.Number("sumFy", vertical, 0, forceTol);
        report.Number("sumMoment0", moment, 0, momentTol);
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
        foreach (var q in input.LineLoads)
        {
            // Trapezoid: R = l (q1 + q2) / 2 and the moment about x = 0 is l (q1 (a + b) / 2 + (q2 - q1) (a / 2 + l / 3)).
            double width = q.End-q.Start, rise = q.EndValue-q.StartValue;
            vertical += width*(q.StartValue+q.EndValue)/2;
            moment += width*(q.StartValue*(q.Start+q.End)/2 + rise*(q.Start/2 + width/3));
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
            var node = solution.Nodes.SingleOrDefault(n => n.Position.Meters == x);
            report.Require(node is not null, $"No node at the event position {x}");
            if (node is null) continue;
            double force = input.PointForces.Where(p => p.Position == x).Sum(p => p.Value) + (node.ReactionY?.Newtons ?? 0);
            double couple = input.PointMoments.Where(p => p.Position == x).Sum(p => p.Value) + (node.ReactionMoment?.NewtonMeters ?? 0);
            report.Number("V jump", after.ShearForce.Newtons-before.ShearForce.Newtons, force, Strict("V"), x, "Right-Left");
            report.Number("M jump", after.BendingMoment.NewtonMeters-before.BendingMoment.NewtonMeters, -couple, Strict("M"), x, "Right-Left");
            report.Number("w continuity", after.TransverseDisplacement.Meters, before.TransverseDisplacement.Meters, Strict("w"), x);
            if (input.MomentReleases.Any(r => r.Position == x))
            {
                // A moment release carries no moment on either side and lets the rotation jump; the node reports
                // the right-hand rotation and, only here, the left-hand one.
                report.Number("release M left", before.BendingMoment.NewtonMeters, 0, Strict("M"), x, "Left");
                report.Number("release M right", after.BendingMoment.NewtonMeters, 0, Strict("M"), x, "Right");
                report.Number("release theta right", node.RotationRadians, after.RotationRadians, Strict("theta"), x, "Right");
                report.Require(node.RotationLeftRadians.HasValue, "A moment release node lacks its left rotation");
                report.Number("release theta left", node.RotationLeftRadians ?? double.NaN, before.RotationRadians, Strict("theta"), x, "Left");
            }
            else
            {
                report.Number("theta continuity", after.RotationRadians, before.RotationRadians, Strict("theta"), x);
                report.Require(node.RotationLeftRadians is null, "Only a moment release node has a left rotation");
            }
        }
        // A spring reaction is the spring law -k w (-k_theta theta) evaluated on the solved deformation. The check applies
        // while k L³/EI and k_theta L/EI stay below 10⁸: the 10⁻⁸ relative output contract bounds w only against the field
        // scale, which the product k·w would amplify for stiffer springs (V24 has its own rigid-limit relation).
        double stiffnessScale = input.LeastStiffness;
        foreach (var support in input.Supports.Where(s => s.Type == "Spring"))
        {
            var node = solution.Nodes.Single(n => n.Position.Meters == support.Position);
            var at = solution.EvaluateAt(M(support.Position), support.Position == input.Length ? EvaluationSide.Left : EvaluationSide.Right);
            if (support.TranslationalStiffness is { } k && k*Math.Pow(input.Length,3)/stiffnessScale <= 1e8)
                report.Number("spring law Ry", node.ReactionY!.Value.Newtons, -k*at.TransverseDisplacement.Meters, Strict("Ry"), support.Position);
            if (support.RotationalStiffness is { } kt && kt*input.Length/stiffnessScale <= 1e8)
                report.Number("spring law Rm", node.ReactionMoment!.Value.NewtonMeters, -kt*at.RotationRadians, Strict("Rm"), support.Position);
        }
        for (int i = 1; i < events.Length; i++)
        {
            double a = events[i-1], b = events[i];
            foreach (double fraction in new[] { 0.37, 0.63 })
            {
                double x = a+(b-a)*fraction;
                double q = input.IntensityAt(x);
                var middle = solution.EvaluateAt(M(x), EvaluationSide.Right);
                double[] centralMoment = new double[2];
                double[] steps = [0.01*(b-a), 0.005*(b-a)];
                for (int k = 0; k < steps.Length; k++)
                {
                    double h = steps[k];
                    var left = solution.EvaluateAt(M(x-h), EvaluationSide.Right);
                    var right = solution.EvaluateAt(M(x+h), EvaluationSide.Right);
                    centralMoment[k] = (right.BendingMoment.NewtonMeters-left.BendingMoment.NewtonMeters)/(2*h);
                    report.Number("dV/dx", (right.ShearForce.Newtons-left.ShearForce.Newtons)/(2*h),
                        q, Strict("q"), x);
                }
                // The central difference of a cubic moment has the error h² q' / 6 under a linear load. Richardson
                // extrapolation of the two step sizes removes it and is exact for every polynomial of degree three.
                report.Number("dM/dx", (4*centralMoment[1]-centralMoment[0])/3, middle.ShearForce.Newtons, Strict("V"), x);
            }
        }
        return report;
    }

    /// <param name="original">The solution of <paramref name="input"/> when the caller already has it.</param>
    public static CheckReport Metamorphic(ValidationCase input, string operation, BeamSolution? original = null)
    {
        original ??= Solve(input);
        ValidationCase transformed;
        Func<string, double> scale;
        switch (operation)
        {
            case "load2": transformed = input.ScaleLoads(2); scale = _ => 2; break;
            case "load-1": transformed = input.ScaleLoads(-1); scale = _ => -1; break;
            // Springs scale with the bending stiffness, so every support force stays and the deformations halve.
            case "E2": transformed = input.ScaleE(2) with { Supports = input.Supports.Select(s => s.ScaleStiffness(2)).ToArray() };
                scale = q => q is "u" or "w" or "theta" ? 0.5 : 1; break;
            case "I2": transformed = input.ScaleI(2) with { Supports = input.Supports.Select(s => s.ScaleStiffness(2)).ToArray() };
                scale = q => q is "w" or "theta" ? 0.5 : 1; break;
            case "rigidLimit":
                // A translational spring of k L³/EI = 10¹² is, within 1 / (1 + k f) of the rigid result, a pinned support
                // at the same position: the relation between the elastic model and its rigid limit.
                transformed = input with { Supports = input.Supports.Select(s => s.Type == "Spring" ? new SupportInput(s.Position, "Pinned") : s).ToArray() };
                scale = _ => 1;
                break;
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
        // Both solves meet the 10⁻⁸ output contract against their field scales, so the rigid limit can only be resolved to
        // that contract (the physical distance 1 / (1 + k f) of V24 is about 5·10⁻¹¹, far below it).
        var report = CompareSolutions(input, solved, [(original,1.0)], scale, operation,
            operation == "rigidLimit" ? ContractTolerance(input, original) : null);
        if (operation == "zeroNode") report.Require(solved.Nodes.Count == original.Nodes.Count+1, "Null force did not create an extra node");
        return report;
    }

    /// <summary>The transformations applied to every case; "rigidLimit" is added for <see cref="RigidLimitIds"/>.</summary>
    public static readonly string[] MetamorphicOperations = ["load2", "load-1", "E2", "I2", "zeroNode"];
    public static readonly string[] SuperpositionIds = ["V04", "V08", "V09", "V11", "V13", "V15", "V16", "V17", "V18", "V19",
        "V20", "V21", "V22", "V23", "V24", "V25", "V26", "V27", "V28", "V30", "V31", "V32", "V33", "V34", "V35", "V36", "V37"];
    public static readonly string[] RigidLimitIds = ["V24"];
    /// <summary>
    /// Two load groups whose union is the combined load. Most cases split the point loads and moments from the
    /// distributed loads; V04 and V09 add a load of the other kind to the original, V13 separates its two UDLs and a
    /// linear load is its constant part plus the triangle of its rise.
    /// </summary>
    public static (ValidationCase A, ValidationCase B) SplitLoads(ValidationCase input)
    {
        if (input.CaseId == "V13")
            return (input with { Udls = [input.Udls[0]] }, input with { Udls = [input.Udls[1]] });
        if (Catalog.LinearLoadIds.Contains(input.CaseId))
            // A linear load is its constant part at the start intensity plus the triangle of the rise.
            return (input with { LineLoads = [], Udls = [..input.Udls, ..input.LineLoads.Select(q => new UdlInput(q.Start, q.End, q.StartValue))] },
                input with { PointForces = [], PointMoments = [], Udls = [],
                    LineLoads = input.LineLoads.Select(q => q with { StartValue = 0, EndValue = q.EndValue-q.StartValue }).ToArray() });
        if (input.CaseId == "V04")
            return (input, input with { Udls = [], PointForces = [new(0.41*input.Length,-150)] });
        if (input.CaseId == "V09")
            return (input, input with { PointForces = [], Udls = [new(0,3,-100)] });
        return (input with { Udls = [], LineLoads = [] }, input with { PointForces = [], PointMoments = [] });
    }

    /// <param name="original">The solution of <paramref name="input"/> when the caller already has it.</param>
    public static CheckReport Superposition(ValidationCase input, BeamSolution? original = null)
    {
        var (a, b) = SplitLoads(input);
        var combined = input with { PointForces = [..a.PointForces,..b.PointForces],
            PointMoments = [..a.PointMoments,..b.PointMoments], Udls = [..a.Udls,..b.Udls],
            LineLoads = [..a.LineLoads,..b.LineLoads] };
        return CompareSolutions(input, Solve(combined), [(ReferenceEquals(a,input) ? original ?? Solve(a) : Solve(a),1.0),(ReferenceEquals(b,input) ? original ?? Solve(b) : Solve(b),1.0)], _ => 1, "superposition");
    }

    /// <summary>Twice the 10⁻⁸ output contract against the largest value of each quantity, plus the strict relative part.</summary>
    private static Func<string, Tolerance> ContractTolerance(ValidationCase input, BeamSolution solution)
    {
        var largest = new Dictionary<string, double>();
        void Note(string q, double value) => largest[q] = Math.Max(largest.GetValueOrDefault(q), Math.Abs(value));
        foreach (var evaluation in input.Evaluations)
            foreach (var (q, value) in Results.Fields(solution.EvaluateAt(M(evaluation.Position), Enum.Parse<EvaluationSide>(evaluation.Side))))
                Note(q, value);
        foreach (var node in solution.Nodes)
        {
            Note("Rx", node.ReactionX?.Newtons ?? 0);
            Note("Ry", node.ReactionY?.Newtons ?? 0);
            Note("Rm", node.ReactionMoment?.NewtonMeters ?? 0);
        }
        return q => new Tolerance(Math.Max(Strict(q).AbsTol, 2e-8 * largest.GetValueOrDefault(q)), 1e-9);
    }

    internal static CheckReport CompareSolutions(ValidationCase input, BeamSolution actual,
        (BeamSolution Solution, double Weight)[] expected, Func<string,double> scale, string operation,
        Func<string, Tolerance>? tolerance = null)
    {
        tolerance ??= Strict;
        var report = new CheckReport(input.CaseId, operation);
        foreach (SupportInput support in input.Supports)
        {
            var observed = actual.Nodes.SingleOrDefault(n => n.Position.Meters == support.Position);
            var expectedNodes = expected.Select(e => e.Solution.Nodes.SingleOrDefault(n => n.Position.Meters == support.Position)).ToArray();
            report.Require(observed is not null && expectedNodes.All(n => n is not null), $"No node at the support position {support.Position}");
            if (observed is null || expectedNodes.Any(n => n is null)) continue;
            foreach (string q in new[] { "Rx", "Ry", "Rm" })
            {
                double? value = Reaction(observed,q);
                double?[] components = expectedNodes.Select(n => Reaction(n!,q)).ToArray();
                bool same = components.All(v => v.HasValue == value.HasValue);
                report.Require(same, "Reaction restraint changed");
                if (same && value.HasValue) report.Number(q, value.Value, components.Select((v,i) => v!.Value*expected[i].Weight).Sum()*scale(q), tolerance(q), support.Position);
            }
        }
        foreach (var evaluation in input.Evaluations)
        {
            var side = Enum.Parse<EvaluationSide>(evaluation.Side);
            var observed = Results.Fields(actual.EvaluateAt(M(evaluation.Position), side));
            var fields = expected.Select(e => Results.Fields(e.Solution.EvaluateAt(M(evaluation.Position), side))).ToArray();
            foreach (string q in observed.Keys)
                report.Number(q, observed[q], fields.Select((f,i) => f[q]*expected[i].Weight).Sum()*scale(q), tolerance(q), evaluation.Position, evaluation.Side);
        }
        return report;
    }

    private static double? Reaction(BeamNodeResult node, string q) => q switch {
        "Rx" => node.ReactionX?.Newtons, "Ry" => node.ReactionY?.Newtons, "Rm" => node.ReactionMoment?.NewtonMeters, _ => throw new ArgumentException(q)
    };
}
