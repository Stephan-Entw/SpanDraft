using SpanDraft.Solver.Kernel;
using static SpanDraft.Tests.SolverExactNumber;
using static SpanDraft.Tests.SolverBendingTestSupport;
using static SpanDraft.Solver.Kernel.RestrainedDirections;

namespace SpanDraft.Tests;

// Fixed statics/compatibility solutions, independent of the kernel's station equations.
// Field expectations are exact Macaulay expressions on the original SI doubles; no numerical solve.
internal sealed record SolverOutputOracle(MechanicalInput Input, SolverExactNumber W0, SolverExactNumber Theta0,
    (double X, RestrainedDirections Direction, SolverExactNumber Value)[] Reactions)
{
    internal static SolverExactNumber Q(long a, long b = 1) => Integer(a) / Integer(b);

    // Global force method: unknown initial kinematics and support reactions, rather than
    // the kernel's local right-state unknowns. Exact compatibility plus global equilibrium.
    internal static SolverOutputOracle FromCompatibility(MechanicalInput input)
    {
        var keys = input.Restraints.SelectMany(r => new[] { W, Theta }.Where(d => r.Directions.HasFlag(d))
            .Select(d => (X: r.Position, Direction: d))).Distinct().OrderBy(r => r.X).ThenBy(r => r.Direction).ToArray();
        int n = keys.Length + 2;
        var zeroLoads = new MechanicalInput(input.Length, input.EI, input.Restraints, [], [], []);
        var basis = new SolverOutputOracle[n];
        basis[0] = new(zeroLoads, Q(1), Q(0), []);
        basis[1] = new(zeroLoads, Q(0), Q(1), []);
        for (int j = 2; j < n; j++)
            basis[j] = new(zeroLoads, Q(0), Q(0), [(keys[j - 2].X, keys[j - 2].Direction, Q(1))]);
        var applied = new SolverOutputOracle(input, Q(0), Q(0), []);
        var a = new SolverExactNumber[n, n];
        var b = new SolverExactNumber[n];
        SolverExactNumber[] Conditions(SolverOutputOracle oracle)
        {
            SolverExactNumber force = Q(0), moment = Q(0);
            foreach (var (x, value) in oracle.Input.PointForces.Select(p => (p.Position, From(p.Newtons)))
                .Concat(oracle.Reactions.Where(r => r.Direction == W).Select(r => (r.X, r.Value))))
            {
                force += value;
                moment += value * From(x);
            }
            foreach (var value in oracle.Input.PointMoments.Select(p => From(p.NewtonMeters))
                .Concat(oracle.Reactions.Where(r => r.Direction == Theta).Select(r => r.Value)))
                moment += value;
            foreach (var line in oracle.Input.LineLoads)
            {
                SolverExactNumber resultant = From(line.NewtonsPerMeter) * (From(line.End) - From(line.Start));
                force += resultant;
                moment += resultant * (From(line.Start) + From(line.End)) / Q(2);
            }
            return [.. keys.Select(k => oracle.Field(k.X, FieldSide.Right)[k.Direction == W ? 0 : 1]), force, moment];
        }
        SolverExactNumber[] loads = Conditions(applied);
        for (int j = 0; j < n; j++)
        {
            SolverExactNumber[] column = Conditions(basis[j]);
            for (int i = 0; i < n; i++) a[i, j] = column[i];
        }
        for (int i = 0; i < n; i++) b[i] = Q(0) - loads[i];
        SolverExactNumber[] solved = SolverExactLinearAlgebra.Solve(a, b);
        return new(input, solved[0], solved[1], keys.Select((k, i) => (k.X, k.Direction, solved[i + 2])).ToArray());
    }
    internal static SolverOutputOracle Case(string name)
    {
        SolverExactNumber z = Q(0);
        return name switch
        {
            "end-force" => new(Model(forces: [new(2, -7)]), z, z, [(0, W, Q(7)), (0, Theta, Q(14))]),
            "end-moment" => new(Model(moments: [new(2, 3)]), z, z, [(0, W, z), (0, Theta, Q(-3))]),
            "udl" => new(Model(lines: [new(0, 2, -3)]), z, z, [(0, W, Q(6)), (0, Theta, Q(6))]),
            "pinned" => new(Model(restraints: [new(0, U | W), new(2, W)], forces: [new(1, -3)]),
                z, Q(-3, 4), [(0, W, Q(3, 2)), (2, W, Q(3, 2))]),
            "indeterminate" => new(SolverBendingSystemTests.Case("indeterminate"), z, Q(-7, 32),
                [(0, W, Q(27, 16)), (1, W, Q(-11, 8)), (2, W, Q(-37, 16))]),
            "coincident" => new(SolverBendingSystemTests.Case("coincidence"), Q(3, 8), Q(-1, 2),
                [(1, W, Q(-6)), (1, Theta, z)]),
            "support-load" => new(SolverBendingSystemTests.Case("support-load"), z, z,
                [(0, W, z), (1, W, Q(3)), (2, W, z)]),
            "mixed" => new(Model(forces: [new(1, -2)], moments: [new(1, 3)], lines: [new(0.25, 1.75, 4)]),
                z, z, [(0, W, Q(-4)), (0, Theta, Q(-7))]),
            "root-loads" => new(Model(forces: [new(0, -2)], moments: [new(0, 3)]), z, z,
                [(0, W, Q(2)), (0, Theta, Q(-3))]),
            "right-fixed" => new(Model(restraints: [new(2, U | W | Theta)], forces: [new(0, 3)]),
                Q(8), Q(-6), [(2, W, Q(-3)), (2, Theta, Q(6))]),
            "short-udl" => new(SolverBendingSystemTests.Case("short-udl"), z, z,
                [(0, W, Q(-1)), (0, Theta, Q(-1) - PowerOfTwo(-53))]),
            "near" => Near(-35),
            "graded" => Near(-200),
            "scaled" => Scaled(),
            "compensated" => new(Model(forces: [new(1, 3), new(1, -3)]), z, z, [(0, W, z), (0, Theta, z)]),
            "unloaded" => new(Model(), z, z, [(0, W, z), (0, Theta, z)]),
            "signed-zero" => new(SolverBendingSystemTests.Case("signed-zero"), z, z, [(0, W, z), (0, Theta, z)]),
            _ => throw new ArgumentOutOfRangeException(nameof(name))
        };
    }

    private static SolverOutputOracle Near(int exponent)
    {
        double a = Math.ScaleB(1, exponent);
        SolverExactNumber r = Q(2) / From(a), r0 = Q(1) - r;
        return new(Model(restraints: [new(0, U | W), new(a, W)], forces: [new(2, -1)]), Q(0),
            Q(-1) * r0 * From(a) * From(a) / Q(6), [(0, W, r0), (a, W, r)]);
    }

    private static SolverOutputOracle Scaled()
    {
        double l = Math.ScaleB(1, 200), p = Math.ScaleB(1, -300);
        return new(Model(length: l, ei: Math.ScaleB(1.25, -200), forces: [new(l / 2, p)]), Q(0), Q(0),
            [(0, W, From(-p)), (0, Theta, From(-p) * From(l / 2))]);
    }

    internal SolverExactNumber[] Field(double position, FieldSide side)
    {
        if (position == 0) side = FieldSide.Right;
        if (position == Input.Length) side = FieldSide.Left;
        SolverExactNumber x = From(position), ei = From(Input.EI);
        SolverExactNumber w = W0 + Theta0 * x, theta = Theta0, m = Q(0), v = Q(0);
        foreach (var (a, value) in Input.PointForces.Select(p => (p.Position, From(p.Newtons)))
            .Concat(Reactions.Where(r => r.Direction == W).Select(r => (r.X, r.Value))))
            if (position > a || position == a && side == FieldSide.Right)
            {
                SolverExactNumber d = x - From(a);
                v += value;
                m += value * d;
                theta += value * d * d / Q(2) / ei;
                w += value * d * d * d / Q(6) / ei;
            }
        foreach (var (a, value) in Input.PointMoments.Select(p => (p.Position, From(p.NewtonMeters)))
            .Concat(Reactions.Where(r => r.Direction == Theta).Select(r => (r.X, r.Value))))
            if (position > a || position == a && side == FieldSide.Right)
            {
                SolverExactNumber d = x - From(a);
                m -= value;
                theta -= value * d / ei;
                w -= value * d * d / Q(2) / ei;
            }
        foreach (ConstantLineLoad line in Input.LineLoads)
        {
            SolverExactNumber near = position > line.Start ? x - From(line.Start) : Q(0);
            SolverExactNumber far = position > line.End ? x - From(line.End) : Q(0);
            SolverExactNumber q = From(line.NewtonsPerMeter);
            v += q * (near - far);
            m += q * (Pow(near, 2) - Pow(far, 2)) / Q(2);
            theta += q * (Pow(near, 3) - Pow(far, 3)) / Q(6) / ei;
            w += q * (Pow(near, 4) - Pow(far, 4)) / Q(24) / ei;
        }
        return [w, theta, m, v];
    }

    internal SolverExactNumber[] Scales(IEnumerable<double> events)
    {
        SolverExactNumber[] mx = [Q(0), Q(0), Q(0), Q(0)];
        foreach (double x in events)
            foreach (FieldSide side in new[] { FieldSide.Left, FieldSide.Right })
            {
                if (x == 0 && side == FieldSide.Left || x == Input.Length && side == FieldSide.Right) continue;
                SolverExactNumber[] f = Field(x, side);
                for (int q = 0; q < 4; q++) mx[q] = Max(mx[q], f[q].Abs());
            }
        SolverExactNumber force = Q(0), l = From(Input.Length), ei = From(Input.EI);
        foreach (var p in Input.PointForces) force += From(p.Newtons).Abs();
        foreach (var p in Input.PointMoments) force += From(p.NewtonMeters).Abs() / l;
        foreach (var q in Input.LineLoads) force += From(q.NewtonsPerMeter).Abs() * (From(q.End) - From(q.Start));
        SolverExactNumber sm = Max(mx[2], force * l), sv = Max(sm / l, force);
        SolverExactNumber st = Max(mx[1], sm * l / ei), sw = Max(Max(mx[0], sm * l * l / ei), st * l);
        return [sw, st, sm, sv];
    }

    internal static SolverExactNumber Max(SolverExactNumber a, SolverExactNumber b) => a.CompareTo(b) >= 0 ? a : b;
}
