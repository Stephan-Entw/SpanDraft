using SpanDraft.Core.Beams;
using SpanDraft.Core.Loads;
using SpanDraft.Core.Materials;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Solver;
using Xunit;

namespace SpanDraft.Tests;

internal static class SolverTestSupport
{
    internal const double L = 2;
    internal const double E = 210e9;
    internal static double I => 0.04 * Math.Pow(0.08, 3) / 12;
    internal static double EI => E * I;
    internal static Length M(double value) => Length.FromMeters(value);
    internal static Support SupportAt(double x, SupportType type) => new(M(x), type);
    internal static PointForce Point(double x, double magnitude) => new(M(x), Force.FromNewtons(magnitude));
    internal static PointMoment Couple(double x, double magnitude) => new(M(x), Moment.FromNewtonMeters(magnitude));
    internal static DistributedLoad Uniform(double start, double end, double intensity) =>
        new(M(start), M(end), ForcePerLength.FromNewtonsPerMeter(intensity));

    internal static DistributedLoad Linear(double start, double end, double startIntensity, double endIntensity) =>
        new(M(start), M(end), ForcePerLength.FromNewtonsPerMeter(startIntensity),
            ForcePerLength.FromNewtonsPerMeter(endIntensity));

    /// <summary>The resultant l (q1 + q2) / 2 of a trapezoid load.</summary>
    internal static double ResultantOf(DistributedLoad load) =>
        (load.EndPosition.Meters - load.StartPosition.Meters) *
        (load.StartIntensity.NewtonsPerMeter + load.EndIntensity.NewtonsPerMeter) / 2;

    /// <summary>The moment of a trapezoid load about x = 0: l (q1 (a + b) / 2 + (q2 - q1) (a / 2 + l / 3)).</summary>
    internal static double MomentAboutOriginOf(DistributedLoad load)
    {
        double a = load.StartPosition.Meters, b = load.EndPosition.Meters, width = b - a;
        double q1 = load.StartIntensity.NewtonsPerMeter, rise = load.EndIntensity.NewtonsPerMeter - q1;
        return width * (q1 * (a + b) / 2 + rise * (a / 2 + width / 3));
    }

    internal static BeamModel Beam(IEnumerable<Support>? supports = null, IEnumerable<BeamLoad>? loads = null,
        Material? material = null, Section? section = null) =>
        new(M(L), material ?? new Material("Test steel", Pressure.FromPascals(E), Pressure.FromMegapascals(235), MaterialStrengthType.Re),
            section ?? new RectangleSection(Length.FromMillimeters(40), Length.FromMillimeters(80)),
            supports ?? [SupportAt(0, SupportType.Pinned), SupportAt(L, SupportType.Roller)], loads ?? []);

    internal static BeamModel Cantilever(params BeamLoad[] loads) => Beam([SupportAt(0, SupportType.Fixed)], loads);
    /// <summary>
    /// w = sign·(t⁴ - 2t³ + 1.18t² - 0.18t) with t = x/L: a simply supported beam under a uniform load
    /// and equal end moments. θ has the roots 0.1, 0.5 and 0.9; w is -0.0081·sign at 0.1/0.9 and 0.0175·sign at 0.5.
    /// </summary>
    internal static BeamModel ThreeStationaryPointQuartic(double sign = 1)
    {
        double endMoment = sign * 2.36 * EI / (L * L);
        return Beam(loads: [Uniform(0, L, sign * 24 * EI / Math.Pow(L, 4)), Couple(0, -endMoment), Couple(L, endMoment)]);
    }

    /// <summary>
    /// A simply supported beam under the linear load q = 24 EI (ξ - 1/2) / L⁴ and end couples whose deflection is
    /// the quintic w = p(ξ) with p' = ((ξ - 1/2)² - a²)((ξ - 1/2)² - b²). It has four interior stationary points
    /// at ξ = 1/2 ± a and 1/2 ± b (a = 0.2), and p is odd about the midspan with p(0) = p(1) = 0.
    /// </summary>
    internal static (BeamModel Beam, double[] StationaryXi, Func<double, double> Deflection) FourStationaryPointQuintic()
    {
        const double a2 = 0.04;
        double b2 = (a2 / 24 - 1.0 / 160) / (a2 / 2 - 1.0 / 24);
        double Deflection(double xi)
        {
            double s = xi - 0.5;
            return s * s * s * s * s / 5 - (a2 + b2) * s * s * s / 3 + a2 * b2 * s;
        }
        // p''(s) = 4 s³ - 2 (a² + b²) s; the bending moment is EI p'' / L² and the applied couples follow M(0+) = -C(0), M(L-) = C(L).
        double endMoment0 = EI / (L * L) * (4 * -0.125 - 2 * (a2 + b2) * -0.5);
        double endMoment1 = EI / (L * L) * (4 * 0.125 - 2 * (a2 + b2) * 0.5);
        double[] xi = [0.5 - Math.Sqrt(b2), 0.5 - Math.Sqrt(a2), 0.5 + Math.Sqrt(a2), 0.5 + Math.Sqrt(b2)];
        BeamModel beam = Beam(loads: [Linear(0, L, -12 * EI / Math.Pow(L, 4), 12 * EI / Math.Pow(L, 4)),
            Couple(0, -endMoment0), Couple(L, endMoment1)]);
        return (beam, xi, Deflection);
    }

    internal static BeamSolution Solve(BeamModel beam) => new EulerBernoulliBeamSolver().Solve(beam);
    internal static BeamNodeResult At(BeamSolution solution, double x) =>
        Assert.Single(solution.Nodes, node => node.Position == M(x));

    // Solver comparisons: absolute + relative tolerance, including meaningful zero checks.
    internal static void Close(double expected, double actual, double absoluteTolerance) =>
        Assert.True(double.IsFinite(actual) && Math.Abs(actual - expected) <= absoluteTolerance + 1e-9 * Math.Abs(expected),
            $"Expected {expected:R}, actual {actual:R}; absolute tolerance {absoluteTolerance:R}, relative tolerance 1e-9.");

    internal static void ForceClose(double expected, Force? actual)
    {
        Assert.True(actual.HasValue, "Expected a constrained force DOF.");
        Close(expected, actual.Value.Newtons, 1e-7);
    }

    internal static void MomentClose(double expected, Moment? actual)
    {
        Assert.True(actual.HasValue, "Expected a constrained rotation DOF.");
        Close(expected, actual.Value.NewtonMeters, 1e-7);
    }

    internal static void DisplacementClose(double expected, Displacement actual) => Close(expected, actual.Meters, 1e-12);
    internal static void RotationClose(double expected, double actual) => Close(expected, actual, 1e-12);

    internal static void SameResults(BeamSolution expected, BeamSolution actual) => SameNodes(expected.Nodes, actual.Nodes);

    internal static void SameNodes(IReadOnlyList<BeamNodeResult> expected, IReadOnlyList<BeamNodeResult> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        foreach (var pair in expected.Zip(actual))
        {
            Assert.Equal(pair.First.Position, pair.Second.Position);
            DisplacementClose(pair.First.AxialDisplacement.Meters, pair.Second.AxialDisplacement);
            DisplacementClose(pair.First.TransverseDisplacement.Meters, pair.Second.TransverseDisplacement);
            RotationClose(pair.First.RotationRadians, pair.Second.RotationRadians);
            CompareForce(pair.First.ReactionX, pair.Second.ReactionX);
            CompareForce(pair.First.ReactionY, pair.Second.ReactionY);
            if (pair.First.ReactionMoment.HasValue)
                MomentClose(pair.First.ReactionMoment.Value.NewtonMeters, pair.Second.ReactionMoment);
            else
                Assert.Null(pair.Second.ReactionMoment);
        }
    }

    private static void CompareForce(Force? expected, Force? actual)
    {
        if (expected.HasValue) ForceClose(expected.Value.Newtons, actual);
        else Assert.Null(actual);
    }
}
