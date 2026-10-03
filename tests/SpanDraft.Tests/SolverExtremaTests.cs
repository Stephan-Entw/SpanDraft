using SpanDraft.Core.Beams;
using SpanDraft.Core.Units;
using SpanDraft.Solver;
using Xunit;
using static SpanDraft.Tests.SolverTestSupport;

namespace SpanDraft.Tests;

public class SolverExtremaTests
{
    [Fact]
    public void SingleUdlFindsMidspanDeflectionAndMomentWithoutSolverNode()
    {
        BeamSolution solution = Solve(Beam(loads: [Uniform(0, L, -1000)]));
        Assert.Equal(2, solution.Nodes.Count);
        BeamExtrema extrema = solution.Extrema;
        Close(1, extrema.MinimumTransverseDisplacement.Position.Meters, 1e-12);
        DisplacementClose(-5 * 1000 * Math.Pow(L, 4) / (384 * EI), extrema.MinimumTransverseDisplacement.Value);
        Assert.Null(extrema.MinimumTransverseDisplacement.Side);
        Close(1, extrema.MaximumBendingMoment.Position.Meters, 1e-12);
        Close(1000 * L * L / 8, extrema.MaximumBendingMoment.Value.NewtonMeters, 1e-7);
        Assert.Null(extrema.MaximumBendingMoment.Side);
        Assert.Equal(M(0), extrema.MaximumTransverseDisplacement.Position);
        Assert.Equal(EvaluationSide.Right, extrema.MaximumTransverseDisplacement.Side);
        DisplacementClose(0, extrema.MaximumTransverseDisplacement.Value);
        Close(1000, extrema.MaximumShearForce.Value.Newtons, 1e-7);
        Close(-1000, extrema.MinimumShearForce.Value.Newtons, 1e-7);
        Assert.Equal(M(0), extrema.MaximumShearForce.Position);
        Assert.Equal(M(L), extrema.MinimumShearForce.Position);
        Assert.Equal(EvaluationSide.Right, extrema.MaximumShearForce.Side);
        Assert.Equal(EvaluationSide.Left, extrema.MinimumShearForce.Side);
        CheckReportedValues(solution);
    }

    [Fact]
    public void ReversingUdlExchangesSignedMinimaAndMaxima()
    {
        BeamSolution solution = Solve(Beam(loads: [Uniform(0, L, 1000)]));
        Close(1, solution.Extrema.MaximumTransverseDisplacement.Position.Meters, 1e-12);
        DisplacementClose(5 * 1000 * Math.Pow(L, 4) / (384 * EI), solution.Extrema.MaximumTransverseDisplacement.Value);
        Close(1, solution.Extrema.MinimumBendingMoment.Position.Meters, 1e-12);
        Close(-500, solution.Extrema.MinimumBendingMoment.Value.NewtonMeters, 1e-7);
        CheckReportedValues(solution);
    }

    [Fact]
    public void CantileverTipForceHasMinimumMomentAtRootAndDeflectionAtTip()
    {
        BeamSolution solution = Solve(Cantilever(Point(L, -1000)));
        Assert.Equal(M(0), solution.Extrema.MinimumBendingMoment.Position);
        Assert.Equal(EvaluationSide.Right, solution.Extrema.MinimumBendingMoment.Side);
        Close(-2000, solution.Extrema.MinimumBendingMoment.Value.NewtonMeters, 1e-7);
        Assert.Equal(M(L), solution.Extrema.MinimumTransverseDisplacement.Position);
        Assert.Equal(EvaluationSide.Left, solution.Extrema.MinimumTransverseDisplacement.Side);
        DisplacementClose(-1000 * Math.Pow(L, 3) / (3 * EI), solution.Extrema.MinimumTransverseDisplacement.Value);
        Assert.Equal(M(0), solution.Extrema.MinimumShearForce.Position); // constant field chooses first x
        Assert.Equal(M(0), solution.Extrema.MaximumShearForce.Position);
        CheckReportedValues(solution);
    }

    [Fact]
    public void PointForceExtremaRetainShearSidesAndTiePrefersLeftForMoment()
    {
        BeamSolution solution = Solve(Beam(loads: [Point(1, -1000)]));
        Assert.Equal(M(0), solution.Extrema.MaximumShearForce.Position);
        Assert.Equal(EvaluationSide.Right, solution.Extrema.MaximumShearForce.Side);
        Close(500, solution.Extrema.MaximumShearForce.Value.Newtons, 1e-7);
        Assert.Equal(M(1), solution.Extrema.MinimumShearForce.Position);
        Assert.Equal(EvaluationSide.Right, solution.Extrema.MinimumShearForce.Side);
        Close(-500, solution.Extrema.MinimumShearForce.Value.Newtons, 1e-7);
        Assert.Equal(M(1), solution.Extrema.MaximumBendingMoment.Position);
        Assert.Equal(EvaluationSide.Left, solution.Extrema.MaximumBendingMoment.Side);
        CheckReportedValues(solution);
    }

    [Fact]
    public void PointMomentExtremaSelectOppositeSidesOfItsJump()
    {
        BeamSolution solution = Solve(Beam(loads: [Couple(0.75, 400)]));
        Assert.Equal(M(0.75), solution.Extrema.MaximumBendingMoment.Position);
        Assert.Equal(EvaluationSide.Left, solution.Extrema.MaximumBendingMoment.Side);
        Close(150, solution.Extrema.MaximumBendingMoment.Value.NewtonMeters, 1e-7);
        Assert.Equal(M(0.75), solution.Extrema.MinimumBendingMoment.Position);
        Assert.Equal(EvaluationSide.Right, solution.Extrema.MinimumBendingMoment.Side);
        Close(-250, solution.Extrema.MinimumBendingMoment.Value.NewtonMeters, 1e-7);
        CheckReportedValues(solution);
    }

    [Fact]
    public void OffCenterPointForceDeflectionExtremumIsNotAtLoadNode()
    {
        const double a = 0.6, force = -1000;
        BeamSolution solution = Solve(Beam(loads: [Point(a, force)]));
        double x = L - Math.Sqrt((L * L - a * a) / 3);
        double y = L - x;
        Close(x, solution.Extrema.MinimumTransverseDisplacement.Position.Meters, 1e-12);
        DisplacementClose(force * a * y * (L * L - a * a - y * y) / (6 * L * EI),
            solution.Extrema.MinimumTransverseDisplacement.Value);
        Assert.Null(solution.Extrema.MinimumTransverseDisplacement.Side);
        Assert.DoesNotContain(solution.Nodes, node => node.Position == solution.Extrema.MinimumTransverseDisplacement.Position);
        CheckReportedValues(solution);
    }

    [Fact]
    public void UnloadedFieldsChooseFirstPhysicalEndpointForEveryExtremum()
    {
        BeamSolution solution = Solve(Cantilever(Point(0.5, 0), Couple(1.5, 0)));
        foreach (var item in new[] {
            (solution.Extrema.MinimumTransverseDisplacement.Position, solution.Extrema.MinimumTransverseDisplacement.Side),
            (solution.Extrema.MaximumTransverseDisplacement.Position, solution.Extrema.MaximumTransverseDisplacement.Side),
            (solution.Extrema.MinimumShearForce.Position, solution.Extrema.MinimumShearForce.Side),
            (solution.Extrema.MaximumShearForce.Position, solution.Extrema.MaximumShearForce.Side),
            (solution.Extrema.MinimumBendingMoment.Position, solution.Extrema.MinimumBendingMoment.Side),
            (solution.Extrema.MaximumBendingMoment.Position, solution.Extrema.MaximumBendingMoment.Side) })
        {
            Assert.Equal(M(0), item.Position);
            Assert.Equal(EvaluationSide.Right, item.Side);
        }
        CheckReportedValues(solution);
    }

    [Fact]
    public void QuarticWithThreeStationaryPointsConsidersAllInteriorCandidates()
    {
        // Manufacture an element field w=t^4-2t^3+1.18t²-0.18t.
        // θ has roots .1, .5, .9; minima tie at .1/.9, maximum at .5.
        BeamModel beam = Cantilever(Uniform(0, L, 24 * EI / Math.Pow(L, 4)));
        SolverModel model = SolverModel.Create(beam);
        BeamNodeResult[] nodes = [
            new(0, M(0), default, default, -0.18 / L, null, null, null),
            new(1, M(L), default, default, 0.18 / L, null, null, null) ];
        var solution = new BeamSolution(beam, nodes, model);
        Close(0.1 * L, solution.Extrema.MinimumTransverseDisplacement.Position.Meters, 1e-12);
        DisplacementClose(-0.0081, solution.Extrema.MinimumTransverseDisplacement.Value);
        Close(0.5 * L, solution.Extrema.MaximumTransverseDisplacement.Position.Meters, 1e-12);
        DisplacementClose(0.0175, solution.Extrema.MaximumTransverseDisplacement.Value);
        CheckReportedValues(solution);
    }

    [Theory]
    [InlineData(1e-100)]
    [InlineData(1e100)]
    public void LoadScaleDoesNotChangeExtremumPosition(double scale)
    {
        BeamSolution solution = Solve(Beam(loads: [Uniform(0, L, -1000 * scale)]));
        Close(1, solution.Extrema.MinimumTransverseDisplacement.Position.Meters, 1e-12);
        Close(1, solution.Extrema.MaximumBendingMoment.Position.Meters, 1e-12);
        Close(-5 * 1000 * Math.Pow(L, 4) / (384 * EI),
            solution.Extrema.MinimumTransverseDisplacement.Value.Meters / scale, 1e-12);
        Close(500, solution.Extrema.MaximumBendingMoment.Value.NewtonMeters / scale, 1e-7);
    }

    private static void CheckReportedValues(BeamSolution solution)
    {
        foreach (BeamExtremum<Displacement> item in new[] { solution.Extrema.MinimumTransverseDisplacement, solution.Extrema.MaximumTransverseDisplacement })
            Assert.Equal(item.Value, solution.EvaluateAt(item.Position, item.Side ?? EvaluationSide.Right).TransverseDisplacement);
        foreach (BeamExtremum<Force> item in new[] { solution.Extrema.MinimumShearForce, solution.Extrema.MaximumShearForce })
            Assert.Equal(item.Value, solution.EvaluateAt(item.Position, item.Side ?? EvaluationSide.Right).ShearForce);
        foreach (BeamExtremum<Moment> item in new[] { solution.Extrema.MinimumBendingMoment, solution.Extrema.MaximumBendingMoment })
            Assert.Equal(item.Value, solution.EvaluateAt(item.Position, item.Side ?? EvaluationSide.Right).BendingMoment);
    }
}
