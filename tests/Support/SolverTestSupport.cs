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
    internal static UniformDistributedLoad Uniform(double start, double end, double intensity) =>
        new(M(start), M(end), ForcePerLength.FromNewtonsPerMeter(intensity));

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
