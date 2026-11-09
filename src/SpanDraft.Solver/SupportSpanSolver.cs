using MathNet.Numerics.LinearAlgebra;
using SpanDraft.Core.Beams;
using SpanDraft.Core.Supports;

namespace SpanDraft.Solver;

/// <summary>Left-limit-free state of one segment between two stations, in local s = x - start.</summary>
internal readonly record struct SegmentState(
    double Displacement, double Rotation, double Moment, double Shear, double Intensity,
    double AxialDisplacement, double AxialForce, double AxialIntensity,
    double IntensitySlope = 0, double ImposedCurvature = 0, double BendingRigidity = 0);

/// <summary>A support with w = 0. Rotational is a spring stiffness at a support that is not clamped.</summary>
internal readonly record struct RigidSupport(double X, bool Clamped, double Rotational = 0);

internal sealed record StationResult(
    double[] Displacements, double[] Rotations, double[] RotationsLeft, double[] AxialDisplacements,
    SegmentState[] Segments, double?[] ReactionsX, double?[] ReactionsY, double?[] ReactionMoments);

/// <summary>
/// Solves a beam of constant EI and EA with equations only at supports. Loads between supports
/// enter through closed forms at their exact position, and V and M follow from equilibrium,
/// so the distance between two loads never appears in a stiffness term.
/// </summary>
internal static class SupportSpanSolver
{
    internal static void CheckModel(BeamModel beam, SolverLoads loads) => HingedBending.CheckModel(beam, loads);

    internal static StationResult Solve(BeamModel beam, SolverModel model, SolverLoads loads)
    {
        double ei = NumericalGuard.Positive(beam.Material.YoungsModulus.Pascals *
            beam.BendingAxisProperties.SecondMomentOfArea.MetersToTheFourth, "Bending rigidity EI");
        double ea = NumericalGuard.Positive(beam.Material.YoungsModulus.Pascals *
            beam.Section.Area.SquareMeters, "Axial rigidity EA");
        double length = beam.Length.Meters;

        double[] stations = model.Nodes.Select(n => n.Position.Meters).ToArray();
        var bending = new HingedBending(beam, loads, ei, length);
        var axial = new AxialField(beam, loads, ea, length);

        int count = stations.Length;
        var w = new double[count];
        var theta = new double[count];
        var thetaLeft = new double[count];
        var u = new double[count];
        var reactionX = new double?[count];
        var reactionY = new double?[count];
        var reactionMoment = new double?[count];
        var segments = new SegmentState[Math.Max(count - 1, 0)];
        for (int i = 0; i < count; i++)
        {
            double x = stations[i];
            (w[i], theta[i], double moment, double shear) = bending.At(x);
            thetaLeft[i] = bending.RotationLeft(x, theta[i]);
            (u[i], double normal) = axial.At(x);
            if (i < count - 1)
            {
                double start = x, end = stations[i + 1];
                double intensity = loads.Uniform.Where(l => l.Start <= start && l.End >= end)
                    .Sum(l => l.Intensity) + loads.Linear.Where(l => l.Start <= start && l.End >= end)
                    .Sum(l => l.StartIntensity + (l.EndIntensity - l.StartIntensity) * ((start - l.Start) / (l.End - l.Start)));
                double slope = loads.Linear.Where(l => l.Start <= start && l.End >= end)
                    .Sum(l => (l.EndIntensity - l.StartIntensity) / (l.End - l.Start));
                double curvature = loads.Curvature.Where(l => l.Start <= start && l.End >= end).Sum(l => l.Intensity);
                double axialIntensity = loads.AxialUniform.Where(l => l.Start <= start && l.End >= end)
                    .Sum(l => l.Intensity);
                segments[i] = new(w[i], theta[i], moment, shear,
                    NumericalGuard.Finite(intensity, "Resultant UDL"), u[i], normal, axialIntensity,
                    NumericalGuard.Finite(slope, "Line load slope"), NumericalGuard.Finite(curvature, "Imposed curvature"),
                    bending.RigidityAt(start));
            }
            if (bending.TryReaction(x, out double ry, out double? rm))
            {
                reactionY[i] = ry;
                reactionMoment[i] = rm;
            }
            if (axial.TryReaction(x, out double rx))
                reactionX[i] = rx;
        }
        return new(w, theta, thetaLeft, u, segments, reactionX, reactionY, reactionMoment);
    }

    /// <summary>
    /// Newton multipole of unit size on 1-3 close knots: order 1 is a point action at the first knot, order 2 the
    /// pair ±1/(t2-t1), order 3 the second divided difference. Its effect is written relative to the knots, so
    /// the near-equal, opposite spring forces of a stiff close group never meet as separate large numbers.
    /// </summary>
    internal sealed record Multipole(double[] Knots, int Order, bool Couple)
    {
        internal double First => Knots[0];
        internal double Last => Knots[Order - 1];

        /// <summary>Weight of knot j in this multipole; the spring force at knot j is the sum over orders.</summary>
        internal double Weight(int j)
        {
            if (j >= Order) return 0;
            if (Order == 1) return 1;
            double a = Knots[1] - Knots[0];
            if (Order == 2) return j == 0 ? -1 / a : 1 / a;
            double b = Knots[2] - Knots[1], c = Knots[2] - Knots[0];
            return j switch { 0 => 1 / (a * c), 1 => -1 / (a * b), _ => 1 / (b * c) };
        }

        // Force multipoles, EI-scaled like a point force: w ~ <x-t>^3/6, θ ~ <x-t>^2/2, M ~ <x-t>, V ~ H.
        // Couple multipoles like a point couple: w ~ -<x-t>^2/2, θ ~ -<x-t>, M ~ -H.
        internal double Deflection(double x)
        {
            double t1 = Knots[0];
            if (x <= t1) return 0;
            double d1 = x - t1;
            if (Couple)
            {
                if (Order == 1) return -d1 * d1 / 2;
                double a = Knots[1] - t1;
                return x <= Knots[1] ? d1 * d1 / (2 * a) : (d1 + (x - Knots[1])) / 2;
            }
            if (Order == 1) return d1 * d1 * d1 / 6;
            double t2 = Knots[1], A = t2 - t1;
            if (Order == 2)
            {
                if (x <= t2) return -d1 * d1 * d1 / (6 * A);
                double d2 = x - t2;
                return -(d1 * d1 + d1 * d2 + d2 * d2) / 6;
            }
            double t3 = Knots[2], B = t3 - t2, C = t3 - t1;
            if (x <= t2) return d1 * d1 * d1 / (6 * A * C);
            double sum = (d1 + (x - t2) + (x - t3)) / 6;
            if (x <= t3) { double r = t3 - x; return sum + r * r * r / (6 * B * C); }
            return sum;
        }

        internal double Slope(double x)
        {
            double t1 = Knots[0];
            if (x <= t1) return 0;
            double d1 = x - t1;
            if (Couple)
            {
                if (Order == 1) return -d1;
                double a = Knots[1] - t1;
                return x <= Knots[1] ? d1 / a : 1;
            }
            if (Order == 1) return d1 * d1 / 2;
            double t2 = Knots[1], A = t2 - t1;
            if (Order == 2) return x <= t2 ? -d1 * d1 / (2 * A) : -(d1 + (x - t2)) / 2;
            double t3 = Knots[2], B = t3 - t2, C = t3 - t1;
            if (x <= t2) return d1 * d1 / (2 * A * C);
            if (x <= t3) { double r = t3 - x; return 0.5 - r * r / (2 * B * C); }
            return 0.5;
        }

        internal double Moment(double x)
        {
            double t1 = Knots[0];
            if (Couple)
            {
                if (Order == 1) return x >= t1 ? -1 : 0;
                double a = Knots[1] - t1;
                return x >= t1 && x < Knots[1] ? 1 / a : 0;
            }
            if (x <= t1) return 0;
            double d1 = x - t1;
            if (Order == 1) return d1;
            double t2 = Knots[1], A = t2 - t1;
            if (Order == 2) return x <= t2 ? -d1 / A : -1;
            double t3 = Knots[2], B = t3 - t2, C = t3 - t1;
            if (x <= t2) return d1 / (A * C);
            return x <= t3 ? (t3 - x) / (B * C) : 0;
        }

        internal double Shear(double x)
        {
            if (Couple || x < Knots[0]) return 0;
            if (Order == 1) return 1;
            double t1 = Knots[0], t2 = Knots[1], A = t2 - t1;
            if (Order == 2) return x < t2 ? -1 / A : 0;
            double t3 = Knots[2], B = t3 - t2, C = t3 - t1;
            return x < t2 ? 1 / (A * C) : x < t3 ? -1 / (B * C) : 0;
        }
    }

    internal readonly record struct PoleAction(Multipole Pole, double Value);

    /// <summary>Action integrals of loads on one interval, in global x.</summary>
    private sealed class Actions(IEnumerable<PointAction> points, IEnumerable<PointAction> couples,
        IEnumerable<UniformAction> uniform, IEnumerable<PoleAction>? poles = null,
        IEnumerable<LinearAction>? linear = null, IEnumerable<UniformAction>? curvature = null)
    {
        // A linear load is its start intensity as a UDL plus a ramp from 0 to the rise; curvature holds EI*κ.
        private readonly LinearAction[] linear = linear?.ToArray() ?? [];
        private readonly UniformAction[] curvature = curvature?.ToArray() ?? [];

        /// <summary>
        /// n-th integral of a ramp 0..rise on [a,b]. Beyond b every term is positive in far = x-b and d = b-a,
        /// so a ramp a few nanometres long keeps its exact resultant and centroid.
        /// </summary>
        private static double Ramp(double x, double a, double b, double rise, int power)
        {
            if (x <= a) return 0;
            double d = b - a;
            if (x <= b)
            {
                double n = x - a;
                return rise / d * power switch { 1 => n * n / 2, 2 => n * n * n / 6, 3 => n * n * n * n / 24, _ => n * n * n * n * n / 120 };
            }
            double f = x - b;
            return rise * d * power switch
            {
                1 => 0.5,
                2 => (d + 3 * f) / 6,
                3 => (d * d + 4 * d * f + 6 * f * f) / 24,
                _ => (d * d * d + 5 * d * d * f + 10 * d * f * f + 10 * f * f * f) / 120
            };
        }

        private double LinearPart(double x, int power) => linear.Sum(l =>
            l.StartIntensity * Difference(x, l.Start, l.End, power) / power switch { 1 => 1, 2 => 2, 3 => 6, _ => 24 } +
            Ramp(x, l.Start, l.End, l.EndIntensity - l.StartIntensity, power));

        private readonly PointAction[] points = points.ToArray();
        private readonly PointAction[] couples = couples.ToArray();
        private readonly UniformAction[] uniform = uniform.ToArray();
        private readonly PoleAction[] poles = poles?.ToArray() ?? [];

        // <x-a>^n - <x-b>^n, written so that a short load keeps its exact length b-a.
        private static double Difference(double x, double a, double b, int power)
        {
            if (x <= a) return 0;
            double near = x - a;
            if (x <= b) return power switch { 1 => near, 2 => near * near, 3 => near * near * near, _ => near * near * near * near };
            double far = x - b, d = b - a;
            return power switch
            {
                1 => d,
                2 => d * (near + far),
                3 => d * (near * near + near * far + far * far),
                _ => d * (near + far) * (near * near + far * far)
            };
        }

        private static double Bracket(double x, double a, int power)
        {
            if (x <= a) return 0;
            double r = x - a;
            return power switch { 1 => r, 2 => r * r, _ => r * r * r };
        }

        /// <summary>EI*w contribution.</summary>
        internal double Deflection(double x) =>
            points.Sum(p => p.Value * Bracket(x, p.X, 3) / 6) - couples.Sum(c => c.Value * Bracket(x, c.X, 2) / 2)
            + uniform.Sum(q => q.Intensity * Difference(x, q.Start, q.End, 4) / 24) + poles.Sum(p => p.Value * p.Pole.Deflection(x))
            + LinearPart(x, 4) + curvature.Sum(k => k.Intensity * Difference(x, k.Start, k.End, 2) / 2);

        /// <summary>EI*θ contribution.</summary>
        internal double Slope(double x) =>
            points.Sum(p => p.Value * Bracket(x, p.X, 2) / 2) - couples.Sum(c => c.Value * Bracket(x, c.X, 1))
            + uniform.Sum(q => q.Intensity * Difference(x, q.Start, q.End, 3) / 6) + poles.Sum(p => p.Value * p.Pole.Slope(x))
            + LinearPart(x, 3) + curvature.Sum(k => k.Intensity * Difference(x, k.Start, k.End, 1));

        /// <summary>V contribution, including a load exactly at x.</summary>
        internal double Shear(double x) =>
            points.Where(p => p.X <= x).Sum(p => p.Value)
            + uniform.Sum(q => q.Intensity * Difference(x, q.Start, q.End, 1)) + poles.Sum(p => p.Value * p.Pole.Shear(x))
            + LinearPart(x, 1);

        /// <summary>M contribution, including a couple exactly at x.</summary>
        internal double Moment(double x) =>
            points.Sum(p => p.Value * Bracket(x, p.X, 1)) - couples.Where(c => c.X <= x).Sum(c => c.Value)
            + uniform.Sum(q => q.Intensity * Difference(x, q.Start, q.End, 2) / 2) + poles.Sum(p => p.Value * p.Pole.Moment(x))
            + LinearPart(x, 2);
    }

    private static IEnumerable<LinearAction> ClipLinear(IEnumerable<LinearAction> linear, double start, double end) =>
        linear.Where(l => Math.Min(l.End, end) > Math.Max(l.Start, start)).Select(l =>
        {
            double a = Math.Max(l.Start, start), b = Math.Min(l.End, end), rise = l.EndIntensity - l.StartIntensity;
            double qa = a == l.Start ? l.StartIntensity : l.StartIntensity + rise * ((a - l.Start) / (l.End - l.Start));
            double qb = b == l.End ? l.EndIntensity : l.StartIntensity + rise * ((b - l.Start) / (l.End - l.Start));
            return new LinearAction(a, b, qa, qb);
        });

    private static IEnumerable<UniformAction> Clip(IEnumerable<UniformAction> uniform, double start, double end) =>
        uniform.Select(q => new UniformAction(Math.Max(q.Start, start), Math.Min(q.End, end), q.Intensity))
            .Where(q => q.End > q.Start);

    /// <summary>
    /// One rigidly connected part. Unknowns are the rotations at supports that do not restrain rotation;
    /// w = 0 at every support, so the equations contain no 1/L³ term.
    /// </summary>
    private sealed class BendingField
    {
        private readonly double ei, start, end;
        private readonly double[] nodes;
        private readonly bool[] clamped;
        private readonly double[] rotational;
        private readonly UniformAction[] imposed;
        private readonly Actions[] spans;
        private readonly Actions? leftOverhang, rightOverhang;
        private readonly double[] rotation, shearRight, momentRight;
        private readonly PointAction[] forces, moments;

        internal BendingField(IReadOnlyList<RigidSupport> supports, IEnumerable<PointAction> forces,
            IEnumerable<PointAction> moments, IEnumerable<UniformAction> uniform, double ei, double start, double end,
            IEnumerable<PoleAction>? poles = null, IEnumerable<LinearAction>? linear = null,
            IEnumerable<UniformAction>? curvature = null)
        {
            PoleAction[] multipoles = poles?.ToArray() ?? [];
            LinearAction[] lines = linear?.ToArray() ?? [];
            imposed = curvature?.ToArray() ?? [];
            UniformAction[] scaled = imposed.Select(k => k with { Intensity = ei * k.Intensity }).ToArray();
            this.ei = ei;
            this.start = start;
            this.end = end;
            this.forces = forces.ToArray();
            this.moments = moments.ToArray();
            UniformAction[] distributed = uniform.ToArray();
            nodes = supports.Select(s => s.X).Distinct().Order().ToArray();
            clamped = nodes.Select(x => supports.Any(s => s.X == x && s.Clamped)).ToArray();
            rotational = nodes.Select(x => supports.Where(s => s.X == x).Sum(s => s.Rotational)).ToArray();
            int n = nodes.Length;

            spans = new Actions[n - 1];
            for (int i = 0; i < n - 1; i++)
            {
                double a = nodes[i], b = nodes[i + 1];
                spans[i] = new(this.forces.Where(p => p.X > a && p.X < b), this.moments.Where(p => p.X > a && p.X < b),
                    Clip(distributed, a, b), multipoles.Where(p => p.Pole.First > a && p.Pole.Last < b),
                    ClipLinear(lines, a, b), Clip(scaled, a, b));
            }
            double first = nodes[0], last = nodes[n - 1];
            if (first > start)
                leftOverhang = new(this.forces.Where(p => p.X < first), this.moments.Where(p => p.X < first),
                    Clip(distributed, start, first), multipoles.Where(p => p.Pole.Last < first),
                    ClipLinear(lines, start, first), Clip(scaled, start, first));
            if (last < end)
                rightOverhang = new(this.forces.Where(p => p.X > last), this.moments.Where(p => p.X > last),
                    Clip(distributed, last, end), multipoles.Where(p => p.Pole.First > last),
                    ClipLinear(lines, last, end), Clip(scaled, last, end));

            double overhangShear = rightOverhang is null ? 0 : -rightOverhang.Shear(end);
            double overhangMoment = rightOverhang is null ? 0 : -overhangShear * (end - last) - rightOverhang.Moment(end);

            rotation = SolveRotations(overhangMoment);
            shearRight = new double[n];
            momentRight = new double[n];
            for (int i = 0; i < n - 1; i++)
            {
                double l = nodes[i + 1] - nodes[i];
                double p = spans[i].Deflection(nodes[i + 1]), t = spans[i].Slope(nodes[i + 1]);
                double sum = rotation[i] + rotation[i + 1];
                shearRight[i] = 6 * ei * sum / l / l + (12 * p / l - 6 * t) / l / l;
                momentRight[i] = -ei * (4 * rotation[i] + 2 * rotation[i + 1]) / l + 2 * t / l - 6 * p / l / l;
            }
            shearRight[n - 1] = overhangShear;
            momentRight[n - 1] = overhangMoment;
        }

        private double[] SolveRotations(double overhangMoment)
        {
            int n = nodes.Length;
            int[] free = Enumerable.Range(0, n).Where(i => !clamped[i]).ToArray();
            var result = new double[n];
            if (free.Length == 0)
                return result;
            var index = new Dictionary<int, int>();
            for (int k = 0; k < free.Length; k++) index[free[k]] = k;

            var matrix = Matrix<double>.Build.Dense(free.Length, free.Length);
            var vector = Vector<double>.Build.Dense(free.Length);
            foreach (int i in free)
            {
                int k = index[i];
                // A rotational spring resists like the adjacent spans: M = -k*θ on the beam.
                matrix[k, k] += rotational[i];
                double right = moments.Where(c => c.X == nodes[i]).Sum(c => c.Value);
                if (i < n - 1)
                {
                    double l = nodes[i + 1] - nodes[i], x = nodes[i + 1];
                    matrix[k, k] += NumericalGuard.Positive(4 * (ei / l), "4EI/L");
                    if (index.TryGetValue(i + 1, out int next)) matrix[k, next] += 2 * (ei / l);
                    right += 2 * spans[i].Slope(x) / l - 6 * spans[i].Deflection(x) / l / l;
                }
                else right += overhangMoment;

                if (i > 0)
                {
                    double l = nodes[i] - nodes[i - 1], x = nodes[i];
                    matrix[k, k] += NumericalGuard.Positive(4 * (ei / l), "4EI/L");
                    if (index.TryGetValue(i - 1, out int previous)) matrix[k, previous] += 2 * (ei / l);
                    right -= -4 * spans[i - 1].Slope(x) / l + 6 * spans[i - 1].Deflection(x) / l / l + spans[i - 1].Moment(x);
                }
                else if (leftOverhang is not null) right -= leftOverhang.Moment(nodes[0]);
                vector[k] = right;
            }
            Vector<double> solution = ReducedSystemSolver.SolveSymmetric(matrix, vector);
            foreach (int i in free) result[i] = solution[index[i]];
            return result;
        }

        internal IReadOnlyList<double> Nodes => nodes;

        /// <summary>w'' at x: M/EI plus the imposed curvature, right of x.</summary>
        internal double Curvature(double x) => At(x).M / ei + imposed.Where(k => k.Start <= x && x < k.End).Sum(k => k.Intensity);

        /// <summary>Displacement, rotation and the right-side moment and shear at x.</summary>
        internal (double W, double Theta, double M, double V) At(double x)
        {
            int n = nodes.Length;
            if (x < nodes[0])
            {
                Actions o = leftOverhang!;
                double c1 = ei * rotation[0] - o.Slope(nodes[0]);
                return ((o.Deflection(x) - o.Deflection(nodes[0]) + c1 * (x - nodes[0])) / ei, (o.Slope(x) + c1) / ei, o.Moment(x), o.Shear(x));
            }
            int i = Array.BinarySearch(nodes, x);
            if (i < 0) i = ~i - 1;
            if (x == nodes[i])
                return (0, rotation[i], momentRight[i], shearRight[i]);
            Actions actions = i < n - 1 ? spans[i] : rightOverhang!;
            double s = x - nodes[i], v = shearRight[i], m = momentRight[i];
            return ((v * s * s * s / 6 + m * s * s / 2 + actions.Deflection(x)) / ei + rotation[i] * s,
                rotation[i] + (v * s * s / 2 + m * s + actions.Slope(x)) / ei,
                m + v * s + actions.Moment(x), v + actions.Shear(x));
        }

        internal (double Y, double Moment) Reaction(double x)
        {
            int i = Array.BinarySearch(nodes, x);
            double shearLeft, momentLeft;
            if (i > 0)
            {
                double l = x - nodes[i - 1];
                shearLeft = shearRight[i - 1] + spans[i - 1].Shear(x);
                momentLeft = momentRight[i - 1] + shearRight[i - 1] * l + spans[i - 1].Moment(x);
            }
            else
            {
                shearLeft = leftOverhang?.Shear(x) ?? 0;
                momentLeft = leftOverhang?.Moment(x) ?? 0;
            }
            return (shearRight[i] - shearLeft - forces.Where(p => p.X == x).Sum(p => p.Value),
                momentLeft - momentRight[i] - moments.Where(c => c.X == x).Sum(c => c.Value));
        }
    }

    /// <summary>
    /// Cuts the beam at every hinge. A hinge transmits only a shear force, so the unknowns are these
    /// shears plus the rigid motion of parts their supports cannot hold on their own. A hinge is then
    /// a point load on an overhang of each part, and its distance to a support enters no stiffness term.
    /// An elastic support is handled the same way: its force (and couple) is an unknown point action,
    /// and its compatibility w + F/k = 0 (θ + C/kθ = 0) is one more equation. No displacement is an unknown.
    /// Springs closer than <see cref="GroupFraction"/> of their part form groups of up to three. A group uses
    /// Newton multipoles as unknowns and divided differences of its equations, both evaluated without
    /// subtracting near-equal numbers, so stiff springs a few nanometres apart stay well conditioned.
    /// </summary>
    private sealed class HingedBending
    {
        internal const double GroupFraction = 0.05;
        private static readonly double[] GaussNodes = [-Math.Sqrt(0.6), 0, Math.Sqrt(0.6)];
        private static readonly double[] GaussWeights = [5.0 / 9, 8.0 / 9, 5.0 / 9];

        /// <summary>An unknown action on a part; Sign is +1 or -1 for the two faces of a cut.</summary>
        private readonly record struct Unknown(int Index, double Sign, Multipole Pole);

        /// <summary>A kinematic equation: Order-th divided difference of w (or θ for Couple) over Knots.</summary>
        private readonly record struct Row(int Index, int Part, Multipole Functional, double Sign);

        /// <summary>One group of springs; Indices[k] is the unknown and the equation of Newton order k+1.</summary>
        private sealed record Group(int Part, double[] Knots, double[] Flexibility, int[] Indices, bool Couple);

        private sealed class Part
        {
            internal double Start, End, Anchor, EI;
            internal int RigidModes, FirstRigid = -1;
            internal LinearAction[] Linear = [];
            internal UniformAction[] Curvature = [];
            internal readonly List<Unknown> Unknowns = [];
            internal PointAction[] Forces = [], Moments = [];
            internal UniformAction[] Uniform = [];
            internal RigidSupport[] RealSupports = [], CalculationSupports = [];
            internal double[] Stations = [];
            internal BendingField Field = null!;
        }

        private readonly Part[] parts;
        private readonly double[] cuts, solution = [];
        private readonly double[] hinges;
        private readonly double ei;
        private readonly List<Row> rows = [];
        private readonly List<Group> groups = [];

        internal static RigidSupport[] RigidSupports(BeamModel beam, SolverLoads loads) =>
            beam.Supports.Select(s => new RigidSupport(s.Position.Meters, s.Type == SupportType.Fixed))
                .Concat(loads.Springs.Where(s => s.RigidTranslation)
                    .Select(s => new RigidSupport(s.X, s.RigidRotation, s.RigidRotation ? 0 : s.Rotational)))
                .ToArray();

        /// <summary>EI right of x: the beam's own value, or the last change at or before x.</summary>
        private double RigidityOf(SolverLoads loads, double x) =>
            loads.Rigidity.Where(r => r.X <= x).OrderBy(r => r.X).Select(r => r.Value).DefaultIfEmpty(ei).Last();

        internal double RigidityAt(double x) => parts[PartAt(x)].EI;

        internal HingedBending(BeamModel beam, SolverLoads loads, double ei, double length)
        {
            this.ei = ei;
            hinges = loads.Hinges.Distinct().Order().ToArray();
            RigidSupport[] supports = RigidSupports(beam, loads);
            // A change of EI is a cut that passes shear and moment; a hinge at the same place releases the moment.
            double[] joints = loads.Rigidity.Select(r => r.X).Where(x => x > 0 && x < length).Except(hinges).ToArray();
            cuts = [0, .. hinges.Concat(joints).Distinct().Order(), length];
            parts = new Part[cuts.Length - 1];
            for (int i = 0; i < parts.Length; i++)
            {
                double a = cuts[i], b = cuts[i + 1];
                bool first = i == 0;
                var part = new Part
                {
                    Start = a,
                    End = b,
                    Forces = loads.Forces.Where(p => p.X > a && p.X <= b || first && p.X == a).ToArray(),
                    Moments = loads.Moments.Where(p => p.X > a && p.X <= b || first && p.X == a).ToArray(),
                    Uniform = Clip(loads.Uniform, a, b).ToArray(),
                    Linear = ClipLinear(loads.Linear, a, b).ToArray(),
                    Curvature = Clip(loads.Curvature, a, b).ToArray(),
                    EI = RigidityOf(loads, a + (b - a) / 2),
                    // A rotational spring on a support at a joint belongs to the left face only.
                    RealSupports = supports.Where(s => s.X >= a && s.X <= b)
                        .Select(s => s.X == a && joints.Contains(a) ? s with { Rotational = 0 } : s).ToArray()
                };
                bool holdsRotation = part.RealSupports.Any(s => s.Clamped || s.Rotational > 0);
                int distinct = part.RealSupports.Select(s => s.X).Distinct().Count();
                part.RigidModes = distinct >= 2 || distinct == 1 && holdsRotation ? 0 : 2 - distinct;
                part.Stations = new[] { a, b }.Concat(part.Forces.Select(p => p.X)).Concat(part.Moments.Select(p => p.X))
                    .Concat(part.Uniform.SelectMany(q => new[] { q.Start, q.End })).Concat(part.RealSupports.Select(s => s.X))
                    .Concat(part.Linear.SelectMany(q => new[] { q.Start, q.End })).Concat(part.Curvature.SelectMany(q => new[] { q.Start, q.End }))
                    .Concat(loads.Springs.Select(s => s.X)).Where(x => x >= a && x <= b).Distinct().Order().ToArray();
                // A free part is anchored in its widest gap, so no spring or cut sits on the calculation clamp.
                part.Anchor = part.RigidModes switch
                {
                    1 => part.RealSupports[0].X,
                    2 => part.Stations.Zip(part.Stations.Skip(1)).MaxBy(g => g.Second - g.First) is var (lo, hi) ? lo + (hi - lo) / 2 : a,
                    _ => a
                };
                part.CalculationSupports = part.RigidModes > 0 ? [new RigidSupport(part.Anchor, true)] : part.RealSupports;
                parts[i] = part;
            }

            int unknowns = 0;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                double x = cuts[i + 1];
                // Shear passes every cut unless a support holds both faces; a joint also passes the moment.
                var passed = new List<bool>();
                if (!supports.Any(s => s.X == x)) passed.Add(false);
                if (joints.Contains(x) && !supports.Any(s => s.X == x && s.Clamped)) passed.Add(true);
                foreach (bool couple in passed)
                {
                    int index = unknowns++;
                    var point = new Multipole([x], 1, couple);
                    parts[i].Unknowns.Add(new(index, 1, point));
                    parts[i + 1].Unknowns.Add(new(index, -1, point));
                    rows.Add(new(index, i, point, 1));
                    rows.Add(new(index, i + 1, point, -1));
                }
            }
            ElasticSupport[] elastic = loads.Springs.Where(s => !s.RigidTranslation).OrderBy(s => s.X).ToArray();
            int[] owners = elastic.Select(s => OwnerOf(s.X, elastic)).ToArray();
            for (int p = 0; p < parts.Length; p++)
            {
                Part part = parts[p];
                double[] nodes = part.CalculationSupports.Select(s => s.X).ToArray();
                ElasticSupport[] own = elastic.Where((s, j) => owners[j] == p).ToArray();
                foreach (bool couple in new[] { false, true })
                {
                    // Only springs stiff against the beam make neighbouring equations nearly equal; a soft
                    // spring's own flexibility dominates its equation, so it stays alone.
                    double span = part.End - part.Start;
                    ElasticSupport[] members = couple ? own.Where(s => s.Rotational > 0).ToArray() : own;
                    bool Stiff(ElasticSupport s) => couple ? s.Rotational * span / part.EI >= 1 : s.Translational * span * span * span / part.EI >= 1;
                    int limit = couple ? 2 : 3;
                    var current = new List<ElasticSupport>();
                    void Close()
                    {
                        if (current.Count == 0) return;
                        double[] knots = current.Select(s => s.X).ToArray();
                        double[] flexibility = current.Select(s => couple ? 1 / s.Rotational : 1 / s.Translational).ToArray();
                        int[] indices = new int[knots.Length];
                        for (int k = 0; k < knots.Length; k++)
                        {
                            indices[k] = unknowns++;
                            var pole = new Multipole(knots, k + 1, couple);
                            part.Unknowns.Add(new(indices[k], 1, pole));
                            rows.Add(new(indices[k], p, pole, 1));
                        }
                        groups.Add(new(p, knots, flexibility, indices, couple));
                        current.Clear();
                    }
                    foreach (ElasticSupport s in members)
                    {
                        bool atNode = nodes.Contains(s.X);
                        if (current.Count > 0)
                        {
                            double last = current[^1].X;
                            bool close = s.X - last <= GroupFraction * span && Stiff(s) && Stiff(current[^1]) &&
                                !nodes.Any(x => x > last && x < s.X) && !atNode && current.Count < limit;
                            if (!close) Close();
                        }
                        current.Add(s);
                        if (atNode) Close();
                    }
                    Close();
                }
            }
            foreach (Part part in parts)
                if (part.RigidModes > 0)
                {
                    part.FirstRigid = unknowns;
                    unknowns += part.RigidModes;
                }

            if (unknowns > 0)
                solution = SolveUnknowns(unknowns);
            foreach (Part part in parts)
                part.Field = Field(part, part.Forces, part.Moments, part.Uniform,
                    part.Unknowns.Select(u => new PoleAction(u.Pole, u.Sign * solution[u.Index])), loads: true);
        }

        /// <summary>A first-order pole is an ordinary point action, so one at a support node enters its reaction.</summary>
        private BendingField Field(Part p, IEnumerable<PointAction> forces, IEnumerable<PointAction> moments,
            IEnumerable<UniformAction> uniform, IEnumerable<PoleAction> poles, bool loads = false)
        {
            PoleAction[] all = poles.ToArray();
            var point = all.Where(a => a.Pole.Order == 1).ToArray();
            return new BendingField(p.CalculationSupports,
                forces.Concat(point.Where(a => !a.Pole.Couple).Select(a => new PointAction(a.Pole.First, a.Value))),
                moments.Concat(point.Where(a => a.Pole.Couple).Select(a => new PointAction(a.Pole.First, a.Value))),
                uniform, p.EI, p.Start, p.End, all.Where(a => a.Pole.Order > 1),
                loads ? p.Linear : null, loads ? p.Curvature : null);
        }

        /// <summary>Rejects positions that make the release or the spring ambiguous.</summary>
        internal static void CheckModel(BeamModel beam, SolverLoads loads)
        {
            double length = beam.Length.Meters;
            RigidSupport[] supports = RigidSupports(beam, loads);
            foreach (double h in loads.Hinges)
            {
                if (!(h > 0 && h < length))
                    throw new BeamSolverException(SolverErrorCode.InvalidModel, "A hinge must lie inside the beam.");
                if (supports.Any(s => s.X == h && s.Clamped))
                    throw new BeamSolverException(SolverErrorCode.InvalidModel, "A hinge cannot coincide with a fixed support.");
                if (loads.Springs.Any(s => s.X == h && s.Rotational > 0))
                    throw new BeamSolverException(SolverErrorCode.InvalidModel,
                        "A hinge cannot coincide with a rotational restraint, because its side would be undefined.");
                if (loads.Moments.Any(c => c.X == h))
                    throw new BeamSolverException(SolverErrorCode.InvalidModel, "A point moment cannot act exactly on a hinge.");
            }
            foreach (PointAction r in loads.Rigidity)
                if (!(r.X > 0 && r.X < length) || !(r.Value > 0) || !double.IsFinite(r.Value))
                    throw new BeamSolverException(SolverErrorCode.InvalidModel,
                        "A change of EI must lie inside the beam and have a positive, finite value.");
            foreach (LinearAction l in loads.Linear)
                if (!(l.Start >= 0 && l.End <= length && l.Start < l.End) || !double.IsFinite(l.StartIntensity) || !double.IsFinite(l.EndIntensity))
                    throw new BeamSolverException(SolverErrorCode.InvalidModel, "A linear load must lie on the beam with finite intensities.");
            foreach (UniformAction k in loads.Curvature)
                if (!(k.Start >= 0 && k.End <= length && k.Start < k.End) || !double.IsFinite(k.Intensity))
                    throw new BeamSolverException(SolverErrorCode.InvalidModel, "An imposed curvature must lie on the beam and be finite.");
            var seen = new HashSet<double>();
            foreach (ElasticSupport s in loads.Springs)
            {
                if (!(s.X >= 0 && s.X <= length))
                    throw new BeamSolverException(SolverErrorCode.InvalidModel, "An elastic support must lie on the beam.");
                if (!(s.Translational > 0) || double.IsNaN(s.Rotational) || s.Rotational < 0)
                    throw new BeamSolverException(SolverErrorCode.InvalidModel,
                        "A translational stiffness must be positive and a rotational stiffness must not be negative.");
                if (!seen.Add(s.X) || beam.Supports.Any(c => c.Position.Meters == s.X))
                    throw new BeamSolverException(SolverErrorCode.InvalidModel,
                        "An elastic support cannot coincide with another support.");
            }
        }

        /// <summary>
        /// The part a spring acts on. A spring exactly at a hinge may act on either face; it joins the face whose
        /// nearest other spring is closer, so a close group is not split by the cut.
        /// </summary>
        private int OwnerOf(double x, ElasticSupport[] elastic)
        {
            for (int i = 0; i < parts.Length - 1; i++)
            {
                if (x < cuts[i + 1]) return i;
                if (x > cuts[i + 1]) continue;
                double leftGap = elastic.Where(s => s.X < x && s.X >= cuts[i]).Select(s => x - s.X).DefaultIfEmpty(double.PositiveInfinity).Min();
                double rightGap = elastic.Where(s => s.X > x && s.X <= cuts[i + 2]).Select(s => s.X - x).DefaultIfEmpty(double.PositiveInfinity).Min();
                return rightGap < leftGap ? i + 1 : i;
            }
            return parts.Length - 1;
        }

        /// <summary>
        /// The functional of a row on one field. Order 1 is the value at the knot; order 2 is the mean of θ
        /// (of M/EI for couples) between the knots; order 3 integrates M/EI against the hat kernel of the
        /// second divided difference. Gauss points between stations make each integral exact.
        /// </summary>
        private double Apply(Multipole f, Part p, BendingField field)
        {
            Func<double, (double W, double Theta, double M, double V)> at = field.At;
            if (f.Order == 1)
                return f.Couple ? LocalRotation(field, p, f.First) : LocalDisplacement(field, p, f.First);
            double t1 = f.Knots[0], t2 = f.Knots[1];
            double total = 0;
            double[] stations = p.Stations.Where(x => x > t1 && x < f.Last).Prepend(t1).Append(f.Last).ToArray();
            for (int s = 0; s < stations.Length - 1; s++)
            {
                double a = stations[s], b = stations[s + 1], half = (b - a) / 2, mid = a + half;
                for (int g = 0; g < 3; g++)
                {
                    double x = mid + half * GaussNodes[g];
                    var v = at(x);
                    double integrand;
                    if (f.Order == 2) integrand = (f.Couple ? field.Curvature(x) : LocalRotation(field, p, x)) / (t2 - t1);
                    else
                    {
                        double t3 = f.Knots[2];
                        double kernel = x <= t2 ? (x - t1) / ((t2 - t1) * (t3 - t1)) : (t3 - x) / ((t3 - t2) * (t3 - t1));
                        integrand = field.Curvature(x) * kernel;
                    }
                    total += GaussWeights[g] * half * integrand;
                }
            }
            return total;
        }

        /// <summary>
        /// w and θ at x measured from the nearest node of the field, where w = 0 and θ is the solved rotation.
        /// Near a support the value is then accurate relative to its own small size, which a stiff spring needs:
        /// its force is k times this displacement. The Gauss points between stations make both integrals exact.
        /// </summary>
        private double LocalDisplacement(BendingField field, Part p, double x) => Local(field, p, x, displacement: true);

        private double LocalRotation(BendingField field, Part p, double x) => Local(field, p, x, displacement: false);

        private double Local(BendingField field, Part p, double x, bool displacement)
        {
            double node = field.Nodes.MinBy(n => Math.Abs(n - x));
            if (node == x) return displacement ? 0 : field.At(x).Theta;
            double lo = Math.Min(node, x), hi = Math.Max(node, x);
            double[] stations = p.Stations.Where(s => s > lo && s < hi).Prepend(lo).Append(hi).ToArray();
            double sum = 0;
            for (int s = 0; s < stations.Length - 1; s++)
            {
                double a = stations[s], b = stations[s + 1], half = (b - a) / 2, mid = a + half;
                for (int g = 0; g < 3; g++)
                {
                    double y = mid + half * GaussNodes[g];
                    double curvature = field.Curvature(y);
                    sum += GaussWeights[g] * half * (displacement ? (x - y) * curvature : curvature);
                }
            }
            // sum integrates over [lo, hi]; from node to x the orientation flips when x lies left of the node.
            double oriented = x > node ? sum : -sum;
            double theta = field.At(node).Theta;
            return displacement ? theta * (x - node) + oriented : theta + oriented;
        }

        /// <summary>Coefficient of each rigid unknown in a row: w = r0 + r1*(x - anchor), θ = r1.</summary>
        private static (double Translation, double Rotation) RigidCoefficients(Multipole f, Part p) => f.Couple
            ? (0, f.Order == 1 ? 1 : 0)
            : f.Order switch { 1 => (1, f.First - p.Anchor), 2 => (0, 1), _ => (0, 0) };

        private double[] SolveUnknowns(int unknowns)
        {
            var left = Matrix<double>.Build.Dense(unknowns, unknowns);
            var right = Vector<double>.Build.Dense(unknowns);
            // Each part: its own loads, then one unit action for each unknown acting on it.
            var own = parts.Select(p => Field(p, p.Forces, p.Moments, p.Uniform, [], loads: true)).ToArray();
            var unit = parts.Select(p => p.Unknowns.Select(u => Field(p, [], [], [], [new PoleAction(u.Pole, 1)])).ToArray()).ToArray();

            foreach (Row row in rows)
            {
                Part p = parts[row.Part];
                right[row.Index] -= row.Sign * Apply(row.Functional, p, own[row.Part]);
                for (int k = 0; k < p.Unknowns.Count; k++)
                {
                    Unknown u = p.Unknowns[k];
                    left[row.Index, u.Index] += row.Sign * u.Sign * Apply(row.Functional, p, unit[row.Part][k]);
                }
                (double translation, double rotation) = RigidCoefficients(row.Functional, p);
                if (p.RigidModes == 1)
                    left[row.Index, p.FirstRigid] += row.Sign * rotation;
                if (p.RigidModes == 2)
                {
                    left[row.Index, p.FirstRigid] += row.Sign * translation;
                    left[row.Index, p.FirstRigid + 1] += row.Sign * rotation;
                }
            }
            // The springs act on the beam with F = -k*w and C = -kθ*θ: in Newton form Σ_j T_kj T_lj / k_j.
            foreach (Group g in groups)
            {
                Multipole[] poles = Enumerable.Range(1, g.Knots.Length).Select(o => new Multipole(g.Knots, o, g.Couple)).ToArray();
                for (int k = 0; k < poles.Length; k++)
                    for (int l = 0; l < poles.Length; l++)
                        for (int j = 0; j < g.Knots.Length; j++)
                            left[g.Indices[k], g.Indices[l]] += poles[k].Weight(j) * poles[l].Weight(j) * g.Flexibility[j];
            }
            for (int i = 0; i < parts.Length; i++)
            {
                Part p = parts[i];
                if (p.RigidModes == 0) continue;
                (double Y, double Moment) own0 = own[i].Reaction(p.Anchor);
                int row = p.FirstRigid;
                right[row] -= own0.Moment;
                if (p.RigidModes == 2) right[row + 1] -= own0.Y;
                for (int k = 0; k < p.Unknowns.Count; k++)
                {
                    Unknown u = p.Unknowns[k];
                    (double Y, double Moment) r = unit[i][k].Reaction(p.Anchor);
                    left[row, u.Index] += u.Sign * r.Moment;
                    if (p.RigidModes == 2) left[row + 1, u.Index] += u.Sign * r.Y;
                }
            }
            return ReducedSystemSolver.SolveGeneral(left, right).ToArray();
        }

        private int PartAt(double x)
        {
            for (int i = parts.Length - 1; i > 0; i--)
                if (x >= cuts[i]) return i;
            return 0;
        }

        internal (double W, double Theta, double M, double V) At(double x)
        {
            Part p = parts[PartAt(x)];
            (double w, double theta, double m, double v) = p.Field.At(x);
            return (w + Rigid(p, x), theta + RigidSlope(p), m, v);
        }

        internal double RotationLeft(double x, double right)
        {
            if (Array.BinarySearch(hinges, x) < 0) return right;
            Part p = parts[Array.IndexOf(cuts, x) - 1];
            return p.Field.At(x).Theta + RigidSlope(p);
        }

        private double Rigid(Part p, double x) => p.RigidModes switch
        {
            1 => solution[p.FirstRigid] * (x - p.Anchor),
            2 => solution[p.FirstRigid] + solution[p.FirstRigid + 1] * (x - p.Anchor),
            _ => 0
        };

        private double RigidSlope(Part p) => p.RigidModes switch
        {
            1 => solution[p.FirstRigid],
            2 => solution[p.FirstRigid + 1],
            _ => 0
        };

        /// <summary>The spring action at knot j of a group: the sum of its multipole weights times their values.</summary>
        private double GroupAction(Group g, int j)
        {
            double sum = 0;
            for (int k = 0; k < g.Knots.Length; k++)
                sum += new Multipole(g.Knots, k + 1, g.Couple).Weight(j) * solution[g.Indices[k]];
            return sum;
        }

        internal bool TryReaction(double x, out double reactionY, out double? reactionMoment)
        {
            reactionY = 0;
            reactionMoment = null;
            bool spring = false;
            foreach (Group g in groups)
            {
                int j = Array.IndexOf(g.Knots, x);
                if (j < 0) continue;
                spring = true;
                if (g.Couple) reactionMoment = GroupAction(g, j);
                else reactionY = GroupAction(g, j);
            }
            if (spring) return true;
            bool found = false;
            foreach (Part p in parts)
            {
                if (!p.RealSupports.Any(s => s.X == x)) continue;
                found = true;
                (double y, double moment) = p.Field.Reaction(x);
                reactionY += y;
                // At a change of EI a clamp holds both faces; its moment is the sum of both.
                if (p.RealSupports.Any(s => s.X == x && (s.Clamped || s.Rotational > 0)))
                    reactionMoment = (reactionMoment ?? 0) + moment;
            }
            return found;
        }
    }

    /// <summary>Axial restraints have u = 0, so each interval follows from statics and u(end) = 0.</summary>
    private sealed class AxialField
    {
        private readonly double ea;
        private readonly double[] nodes;
        private readonly Actions[] spans;
        private readonly Actions? leftOverhang, rightOverhang;
        private readonly double[] normalRight;
        private readonly SolverLoads loads;

        internal AxialField(BeamModel beam, SolverLoads loads, double ea, double length)
        {
            this.loads = loads;
            this.ea = ea;
            nodes = beam.Supports.Where(s => s.Type is SupportType.Fixed or SupportType.Pinned)
                .Select(s => s.Position.Meters).Concat(loads.Springs.Where(s => s.AxialRestraint).Select(s => s.X))
                .Distinct().Order().ToArray();
            int n = nodes.Length;
            spans = new Actions[n - 1];
            normalRight = new double[n];
            for (int i = 0; i < n - 1; i++)
            {
                double a = nodes[i], b = nodes[i + 1];
                spans[i] = new(loads.AxialForces.Where(p => p.X > a && p.X < b), [], Clip(loads.AxialUniform, a, b));
                normalRight[i] = spans[i].Moment(b) / (b - a);
            }
            double first = nodes[0], last = nodes[n - 1];
            if (first > 0)
                leftOverhang = new(loads.AxialForces.Where(p => p.X < first), [], Clip(loads.AxialUniform, 0, first));
            if (last < length)
            {
                rightOverhang = new(loads.AxialForces.Where(p => p.X > last), [], Clip(loads.AxialUniform, last, length));
                normalRight[n - 1] = rightOverhang.Shear(length);
            }
        }

        internal (double U, double N) At(double x)
        {
            if (x < nodes[0])
            {
                Actions o = leftOverhang!;
                return ((o.Moment(nodes[0]) - o.Moment(x)) / ea, -o.Shear(x));
            }
            int i = Array.BinarySearch(nodes, x);
            if (i < 0) i = ~i - 1;
            if (x == nodes[i])
                return (0, normalRight[i]);
            Actions actions = i < nodes.Length - 1 ? spans[i] : rightOverhang!;
            return ((normalRight[i] * (x - nodes[i]) - actions.Moment(x)) / ea, normalRight[i] - actions.Shear(x));
        }

        internal bool TryReaction(double x, out double reaction)
        {
            reaction = 0;
            int i = Array.BinarySearch(nodes, x);
            if (i < 0) return false;
            double left = i > 0 ? normalRight[i - 1] - spans[i - 1].Shear(x) : -(leftOverhang?.Shear(x) ?? 0);
            reaction = left - normalRight[i] - loads.AxialForces.Where(p => p.X == x).Sum(p => p.Value);
            return true;
        }
    }
}
