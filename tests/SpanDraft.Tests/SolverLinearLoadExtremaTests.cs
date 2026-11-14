using SpanDraft.Core.Beams;
using SpanDraft.Core.Units;
using SpanDraft.Solver;
using Xunit;
using static SpanDraft.Tests.SolverTestSupport;

namespace SpanDraft.Tests;

/// <summary>
/// Global extrema under linearly varying loads: V has interior extrema at the zero of q, M follows from the
/// quadratic V and w from the quartic θ. Expected values are closed-form beam theory in ξ = x / L.
/// </summary>
public sealed class SolverLinearLoadExtremaTests
{
    private const double PositionTolerance = 1e-9;

    [Theory]
    [InlineData(3000)]
    [InlineData(-3000)]
    public void TriangleLoadHasItsExtremaAtTheClosedFormPositions(double q0)
    {
        // q(ξ) = -q0 ξ; positive q0 loads downwards. The closed-form extremes below are for the load as given.
        BeamSolution solution = Solve(Beam(loads: [Linear(0, L, 0, -q0)]));
        BeamExtrema extrema = solution.Extrema;
        double momentPeak = q0 * L * L / (9 * Math.Sqrt(3));
        double xiStar = Math.Sqrt(1 - 2 * Math.Sqrt(30) / 15);
        double deflection = -q0 * Math.Pow(L, 4) * (7 * xiStar - 10 * Math.Pow(xiStar, 3) + 3 * Math.Pow(xiStar, 5)) / (360 * EI);
        BeamExtremum<Moment> moment = q0 > 0 ? extrema.MaximumBendingMoment : extrema.MinimumBendingMoment;
        Close(Math.Abs(momentPeak), Math.Abs(moment.Value.NewtonMeters), 1e-7);
        Close(L / Math.Sqrt(3), moment.Position.Meters, PositionTolerance);
        Assert.Null(moment.Side);
        BeamExtremum<Displacement> w = q0 > 0 ? extrema.MinimumTransverseDisplacement : extrema.MaximumTransverseDisplacement;
        DisplacementClose(deflection, w.Value);
        Close(xiStar * L, w.Position.Meters, PositionTolerance);
        // V = q0 L / 6 at x = 0+ and -q0 L / 3 at x = L-; the extreme shears are the end values.
        BeamExtremum<Force> upper = q0 > 0 ? extrema.MaximumShearForce : extrema.MinimumShearForce;
        BeamExtremum<Force> lower = q0 > 0 ? extrema.MinimumShearForce : extrema.MaximumShearForce;
        Close(q0 * L / 6, upper.Value.Newtons, 1e-7);
        Assert.Equal(M(0), upper.Position);
        Assert.Equal(EvaluationSide.Right, upper.Side);
        Close(-q0 * L / 3, lower.Value.Newtons, 1e-7);
        Assert.Equal(M(L), lower.Position);
        Assert.Equal(EvaluationSide.Left, lower.Side);
    }

    [Theory]
    [InlineData(4000)]
    [InlineData(-4000)]
    public void SignChangingLoadHasAnInteriorShearExtremumAtTheZeroOfTheLoad(double q0)
    {
        // q(ξ) = q0 (2ξ - 1): V = q0 L (ξ² - ξ + 1/6) has its vertex V = -q0 L / 12 at midspan, where q = 0.
        BeamSolution solution = Solve(Beam(loads: [Linear(0, L, -q0, q0)]));
        BeamExtrema extrema = solution.Extrema;
        BeamExtremum<Force> vertex = q0 > 0 ? extrema.MinimumShearForce : extrema.MaximumShearForce;
        BeamExtremum<Force> ends = q0 > 0 ? extrema.MaximumShearForce : extrema.MinimumShearForce;
        Close(-q0 * L / 12, vertex.Value.Newtons, 1e-7);
        Close(L / 2, vertex.Position.Meters, PositionTolerance);
        Assert.Null(vertex.Side);
        // Both end shears are q0 L / 6; the tie goes to the smaller position.
        Close(q0 * L / 6, ends.Value.Newtons, 1e-7);
        Assert.Equal(M(0), ends.Position);
        Assert.Equal(EvaluationSide.Right, ends.Side);
        // M = q0 L² (ξ³/3 - ξ²/2 + ξ/6) has the extremes ± √3 q0 L² / 108 at ξ = (3 ∓ √3) / 6, where V = 0.
        double peak = Math.Sqrt(3) * q0 * L * L / 108;
        BeamExtremum<Moment> maximum = q0 > 0 ? extrema.MaximumBendingMoment : extrema.MinimumBendingMoment;
        BeamExtremum<Moment> minimum = q0 > 0 ? extrema.MinimumBendingMoment : extrema.MaximumBendingMoment;
        Close(peak, maximum.Value.NewtonMeters, 1e-7);
        Close((3 - Math.Sqrt(3)) / 6 * L, maximum.Position.Meters, PositionTolerance);
        Close(-peak, minimum.Value.NewtonMeters, 1e-7);
        Close((3 + Math.Sqrt(3)) / 6 * L, minimum.Position.Meters, PositionTolerance);
    }

    [Fact]
    public void AntisymmetricCantileverLoadHasItsShearExtremumAtTheMiddleOfTheLoad()
    {
        // V = q u (u / l - 1) for u = x - a inside the load, so the minimum -q l / 4 lies at u = l / 2.
        const double q = 600, a = 0.5, b = 1.75, width = b - a;
        BeamSolution solution = Solve(Cantilever(Linear(a, b, -q, q)));
        Close(-q * width / 4, solution.Extrema.MinimumShearForce.Value.Newtons, 1e-7);
        Close(a + width / 2, solution.Extrema.MinimumShearForce.Position.Meters, PositionTolerance);
        Close(0, solution.Extrema.MaximumShearForce.Value.Newtons, 1e-6);
        // M = ∫V on [a, b] has a vertex-free cubic; its extremes are the fixed end and the load ends.
        Close(q * width * width / 6, solution.Extrema.MaximumBendingMoment.Value.NewtonMeters, 1e-6);
    }

    [Fact]
    public void FourStationaryPointsOfAQuinticDeflectionAreAllFoundAndTheGlobalExtremaAreTheLargest()
    {
        var (beam, stationary, deflection) = FourStationaryPointQuintic();
        BeamSolution solution = Solve(beam);
        double[] values = stationary.Select(deflection).ToArray();
        int maximum = Array.IndexOf(values, values.Max()), minimum = Array.IndexOf(values, values.Min());
        DisplacementClose(values[maximum], solution.Extrema.MaximumTransverseDisplacement.Value);
        Close(stationary[maximum] * L, solution.Extrema.MaximumTransverseDisplacement.Position.Meters, PositionTolerance);
        DisplacementClose(values[minimum], solution.Extrema.MinimumTransverseDisplacement.Value);
        Close(stationary[minimum] * L, solution.Extrema.MinimumTransverseDisplacement.Position.Meters, PositionTolerance);
        // Every stationary point is a candidate: the exact Sturm isolation finds exactly four roots of θ.
        SolverLinearLoadTests.CheckCandidatesAgainstExactRoots("quintic", BeamModelAdapter.Adapt(beam));
    }
}
