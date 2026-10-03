using System.Reflection;
using SpanDraft.Core.Beams;
using SpanDraft.Core.Materials;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Units;
using SpanDraft.Engineering;
using SpanDraft.Solver;
using Xunit;
using static SpanDraft.Tests.SolverTestSupport;

namespace SpanDraft.Tests;

public class EngineeringTests
{
    private static CustomSection Section(double w = 0.001) => new(Area.FromSquareMeters(0.01),
        SecondMomentOfArea.FromMetersToTheFourth(1e-6), SectionModulus.FromCubicMeters(w));
    private static Material Steel(double yieldStrength = 250e6) =>
        new("Engineering test steel", Pressure.FromPascals(200e9), Pressure.FromPascals(yieldStrength));
    private static BeamModel CenterLoad(double force = -1000, double w = 0.001, double re = 250e6) =>
        Beam(loads: [Point(1, force)], material: Steel(re), section: Section(w));

    [Fact]
    public void CenterPointLoadUsesAnalyticalMomentStressAndSafetyFactor()
    {
        BeamSolution solution = Solve(CenterLoad());
        BeamEngineeringResult result = BeamEngineeringAnalysis.Analyze(solution);

        // L=2 m, P=1000 N: PL/4=500 Nm; W=.001 m³; Re=250 MPa.
        NumericAssert.Close(500, result.CriticalBendingMoment.Value.NewtonMeters);
        NumericAssert.Close(500, result.BendingMomentMagnitude.NewtonMeters);
        Assert.Equal(M(1), result.CriticalBendingMoment.Position);
        Assert.Equal(EvaluationSide.Left, result.CriticalBendingMoment.Side);
        NumericAssert.Close(500000, result.MaximumBendingStress.Pascals);
        NumericAssert.Close(500, result.SafetyFactor);
        Assert.Equal(M(1), result.BendingStressPosition);
        Assert.Equal(EvaluationSide.Left, result.BendingStressSide);
        Assert.Same(solution.Extrema.MaximumBendingMoment, result.CriticalBendingMoment);
        // w=-PL³/(48EI), with EI=200000 Nm².
        NumericAssert.Close(-1.0 / 1200, result.CriticalTransverseDisplacement.Value.Meters);
    }

    [Fact]
    public void CantileverRetainsNegativeMomentAndPositiveStress()
    {
        BeamSolution solution = Solve(Beam([SupportAt(0, Core.Supports.SupportType.Fixed)],
            [Point(2, -1000)], Steel(), Section()));
        BeamEngineeringResult result = BeamEngineeringAnalysis.Analyze(solution);

        NumericAssert.Close(-2000, result.CriticalBendingMoment.Value.NewtonMeters);
        NumericAssert.Close(2000, result.BendingMomentMagnitude.NewtonMeters);
        NumericAssert.Close(2000000, result.MaximumBendingStress.Pascals);
        NumericAssert.Close(125, result.SafetyFactor);
        Assert.Equal(M(0), result.BendingStressPosition);
        Assert.Equal(EvaluationSide.Right, result.BendingStressSide);
        Assert.Same(solution.Extrema.MinimumBendingMoment, result.CriticalBendingMoment);
        Assert.Equal(M(2), result.CriticalTransverseDisplacement.Position);
        Assert.Equal(EvaluationSide.Left, result.CriticalTransverseDisplacement.Side);
        Assert.True(result.CriticalTransverseDisplacement.Value.Meters < 0);
    }

    [Theory]
    [InlineData(-1000)]
    [InlineData(1000)]
    public void DeflectionSelectsMagnitudeAndPreservesSignedSolverExtremum(double load)
    {
        BeamSolution solution = Solve(CenterLoad(load));
        BeamEngineeringResult result = BeamEngineeringAnalysis.Analyze(solution);
        var expected = load < 0 ? solution.Extrema.MinimumTransverseDisplacement :
            solution.Extrema.MaximumTransverseDisplacement;

        Assert.Same(expected, result.CriticalTransverseDisplacement);
        Assert.Equal(expected.Position, result.CriticalTransverseDisplacement.Position);
        Assert.Equal(expected.Side, result.CriticalTransverseDisplacement.Side);
        NumericAssert.Close(load / 1200000, result.CriticalTransverseDisplacement.Value.Meters);
        NumericAssert.Close(Math.Abs(load) / 1200000, result.TransverseDisplacementMagnitude.Meters);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void CompetingNonzeroDeflectionsChooseGreaterMagnitude(double sign)
    {
        // Same analytical test field as SolverExtremaTests:
        // w=sign*(t⁴-2t³+1.18t²-.18t), with min=-.0081 and max=.0175.
        // Construct the already-computed solution directly; Engineering never solves it.
        BeamModel beam = Cantilever(Uniform(0, L, sign * 24 * EI / Math.Pow(L, 4)));
        SolverModel model = SolverModel.Create(beam);
        BeamNodeResult[] nodes = [
            new(0, M(0), default, default, -sign * 0.18 / L, null, null, null),
            new(1, M(L), default, default, sign * 0.18 / L, null, null, null) ];
        var solution = new BeamSolution(beam, nodes, model);
        var result = BeamEngineeringAnalysis.Analyze(solution);

        Assert.True(solution.Extrema.MinimumTransverseDisplacement.Value.Meters < 0);
        Assert.True(solution.Extrema.MaximumTransverseDisplacement.Value.Meters > 0);
        NumericAssert.Close(sign * 0.0175, result.CriticalTransverseDisplacement.Value.Meters);
        NumericAssert.Close(0.0175, result.TransverseDisplacementMagnitude.Meters);
        NumericAssert.Close(1, result.CriticalTransverseDisplacement.Position.Meters);
        Assert.Null(result.CriticalTransverseDisplacement.Side);
        Assert.Same(sign > 0 ? solution.Extrema.MaximumTransverseDisplacement :
            solution.Extrema.MinimumTransverseDisplacement, result.CriticalTransverseDisplacement);
    }

    [Fact]
    public void DoublingSectionModulusHalvesStressAndDoublesSafetyFactor()
    {
        var original = BeamEngineeringAnalysis.Analyze(Solve(CenterLoad()));
        var doubled = BeamEngineeringAnalysis.Analyze(Solve(CenterLoad(w: 0.002)));

        NumericAssert.Close(250000, doubled.MaximumBendingStress.Pascals);
        NumericAssert.Close(1000, doubled.SafetyFactor);
        NumericAssert.Close(original.MaximumBendingStress.Pascals / 2, doubled.MaximumBendingStress.Pascals);
        NumericAssert.Close(original.SafetyFactor * 2, doubled.SafetyFactor);
        SameMechanicalIndicators(original, doubled);
    }

    [Fact]
    public void DoublingYieldStrengthChangesOnlySafetyFactor()
    {
        var original = BeamEngineeringAnalysis.Analyze(Solve(CenterLoad()));
        var doubled = BeamEngineeringAnalysis.Analyze(Solve(CenterLoad(re: 500e6)));

        NumericAssert.Close(1000, doubled.SafetyFactor);
        NumericAssert.Close(original.SafetyFactor * 2, doubled.SafetyFactor);
        Assert.Equal(original.MaximumBendingStress, doubled.MaximumBendingStress);
        SameMechanicalIndicators(original, doubled);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnloadedBeamHasZeroStressAndPositiveInfiniteSafetyFactor(bool zeroLoads)
    {
        BeamSolution solution = Solve(zeroLoads ? CenterLoad(0) : Beam());
        BeamEngineeringResult result = BeamEngineeringAnalysis.Analyze(solution);

        Assert.Equal(0, result.CriticalBendingMoment.Value.NewtonMeters);
        Assert.Equal(0, result.BendingMomentMagnitude.NewtonMeters);
        Assert.Equal(0, result.MaximumBendingStress.Pascals);
        Assert.Equal(0, result.TransverseDisplacementMagnitude.Meters);
        Assert.Equal(double.PositiveInfinity, result.SafetyFactor);
        Assert.False(double.IsNaN(result.SafetyFactor));
        Assert.Equal(M(0), result.BendingStressPosition);
        Assert.Equal(EvaluationSide.Right, result.BendingStressSide);
        Assert.Same(solution.Extrema.MinimumBendingMoment, result.CriticalBendingMoment);
    }

    [Fact]
    public void ReversingLoadReversesSignedValuesButPreservesStressAndSafetyFactor()
    {
        var downward = BeamEngineeringAnalysis.Analyze(Solve(CenterLoad()));
        var upward = BeamEngineeringAnalysis.Analyze(Solve(CenterLoad(1000)));

        NumericAssert.Close(-downward.CriticalBendingMoment.Value.NewtonMeters, upward.CriticalBendingMoment.Value.NewtonMeters);
        NumericAssert.Close(-downward.CriticalTransverseDisplacement.Value.Meters, upward.CriticalTransverseDisplacement.Value.Meters);
        NumericAssert.Close(downward.MaximumBendingStress.Pascals, upward.MaximumBendingStress.Pascals);
        NumericAssert.Close(downward.SafetyFactor, upward.SafetyFactor);
        Assert.Equal(downward.BendingStressPosition, upward.BendingStressPosition);
        Assert.Equal(downward.BendingStressSide, upward.BendingStressSide);
    }

    [Fact]
    public void MidspanPointMomentTieSelectsLeftSideWithoutAveragingJump()
    {
        BeamSolution solution = Solve(Beam(loads: [Couple(1, 400)], material: Steel(), section: Section()));
        var result = BeamEngineeringAnalysis.Analyze(solution);

        NumericAssert.Close(200, solution.Extrema.MaximumBendingMoment.Value.NewtonMeters);
        NumericAssert.Close(-200, solution.Extrema.MinimumBendingMoment.Value.NewtonMeters);
        Assert.Same(solution.Extrema.MaximumBendingMoment, result.CriticalBendingMoment);
        Assert.Equal(M(1), result.BendingStressPosition);
        Assert.Equal(EvaluationSide.Left, result.BendingStressSide);
        NumericAssert.Close(200000, result.MaximumBendingStress.Pascals);
        NumericAssert.Close(1250, result.SafetyFactor);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EqualOppositeMagnitudesSelectSmallerPositionRegardlessOfSign(bool negativeFirst)
    {
        var minimum = new BeamExtremum<Moment>(Moment.FromNewtonMeters(-200), M(negativeFirst ? 0 : 2), EvaluationSide.Right);
        var maximum = new BeamExtremum<Moment>(Moment.FromNewtonMeters(200), M(negativeFirst ? 2 : 0), EvaluationSide.Left);
        var expected = negativeFirst ? minimum : maximum;

        Assert.Same(expected, Select(minimum, maximum));
        Assert.Same(expected, Select(maximum, minimum));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EqualDeflectionMagnitudesSelectLeftAtSamePosition(bool negativeLeft)
    {
        var minimum = new BeamExtremum<Displacement>(Displacement.FromMeters(-0.001), M(1),
            negativeLeft ? EvaluationSide.Left : EvaluationSide.Right);
        var maximum = new BeamExtremum<Displacement>(Displacement.FromMeters(0.001), M(1),
            negativeLeft ? EvaluationSide.Right : EvaluationSide.Left);
        var expected = negativeLeft ? minimum : maximum;

        Assert.Same(expected, BeamEngineeringAnalysis.SelectMaximumAbsolute(minimum, maximum, v => v.Meters));
        Assert.Same(expected, BeamEngineeringAnalysis.SelectMaximumAbsolute(maximum, minimum, v => v.Meters));
    }

    [Theory]
    [InlineData(1e-100, 32, true)]
    [InlineData(1, 32, true)]
    [InlineData(1e100, 32, true)]
    [InlineData(1e-100, 128, false)]
    [InlineData(1, 128, false)]
    [InlineData(1e100, 128, false)]
    public void MagnitudeTiesUseRelativeRoundoffWithoutAbsoluteFloor(double scale, int ulps, bool tied)
    {
        var earlier = new BeamExtremum<Moment>(Moment.FromNewtonMeters(-scale), M(0), EvaluationSide.Right);
        var greater = new BeamExtremum<Moment>(Moment.FromNewtonMeters(scale * (1 + ulps * 2.2204460492503131e-16)),
            M(2), EvaluationSide.Left);
        var expected = tied ? earlier : greater;

        Assert.Same(expected, Select(earlier, greater));
        Assert.Same(expected, Select(greater, earlier));
    }

    [Fact]
    public void IdenticalTieKeysPreferFirstCandidateAndPreserveNullSide()
    {
        var minimum = new BeamExtremum<Moment>(Moment.FromNewtonMeters(-200), M(1), null);
        var maximum = new BeamExtremum<Moment>(Moment.FromNewtonMeters(200), M(1), null);
        Assert.Same(minimum, Select(minimum, maximum));
    }

    [Fact]
    public void InteriorAnalyticalExtremaAreUsedWithoutNodesOrSampling()
    {
        BeamSolution solution = Solve(Beam(loads: [Uniform(0, 2, -1000)], material: Steel(), section: Section()));
        var nodes = solution.Nodes;
        var extrema = solution.Extrema;
        var result = BeamEngineeringAnalysis.Analyze(solution);

        Assert.Equal(2, solution.Nodes.Count);
        Assert.DoesNotContain(solution.Nodes, n => n.Position == result.CriticalTransverseDisplacement.Position);
        Assert.DoesNotContain(solution.Nodes, n => n.Position == result.CriticalBendingMoment.Position);
        Assert.Same(extrema.MinimumTransverseDisplacement, result.CriticalTransverseDisplacement);
        Assert.Same(extrema.MaximumBendingMoment, result.CriticalBendingMoment);
        Assert.Null(result.CriticalTransverseDisplacement.Side);
        Assert.Null(result.BendingStressSide);
        NumericAssert.Close(1, result.BendingStressPosition.Meters);
        NumericAssert.Close(500, result.BendingMomentMagnitude.NewtonMeters);
        NumericAssert.Close(500000, result.MaximumBendingStress.Pascals);
        // 5qL⁴/(384EI), EI=200000 Nm².
        NumericAssert.Close(1.0 / 960, result.TransverseDisplacementMagnitude.Meters);
        Assert.Same(nodes, solution.Nodes);
        Assert.Same(extrema, solution.Extrema);
    }

    [Fact]
    public void NullSolutionIsRejectedWithParameterName()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => BeamEngineeringAnalysis.Analyze(null!));
        Assert.Equal("solution", exception.ParamName);
    }

    [Fact]
    public void PublicResultAndExtremaHaveOnlyGettersAndNoPublicConstructors()
    {
        foreach (Type type in new[] { typeof(BeamEngineeringResult), typeof(BeamExtremum<Moment>), typeof(BeamExtremum<Displacement>) })
        {
            Assert.True(type.IsSealed);
            Assert.Empty(type.GetConstructors());
            Assert.All(type.GetProperties(), property => Assert.Null(property.SetMethod));
            Assert.Empty(type.GetFields(BindingFlags.Public | BindingFlags.Instance));
        }
        var method = Assert.Single(typeof(BeamEngineeringAnalysis).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly));
        Assert.Equal(typeof(BeamSolution), Assert.Single(method.GetParameters()).ParameterType);
        Assert.Equal(typeof(BeamEngineeringResult), method.ReturnType);
    }

    private static BeamExtremum<Moment> Select(BeamExtremum<Moment> first, BeamExtremum<Moment> second) =>
        BeamEngineeringAnalysis.SelectMaximumAbsolute(first, second, v => v.NewtonMeters);

    private static void SameMechanicalIndicators(BeamEngineeringResult first, BeamEngineeringResult second)
    {
        Assert.Equal(first.CriticalBendingMoment.Value, second.CriticalBendingMoment.Value);
        Assert.Equal(first.CriticalBendingMoment.Position, second.CriticalBendingMoment.Position);
        Assert.Equal(first.CriticalBendingMoment.Side, second.CriticalBendingMoment.Side);
        Assert.Equal(first.BendingStressPosition, second.BendingStressPosition);
        Assert.Equal(first.BendingStressSide, second.BendingStressSide);
        Assert.Equal(first.CriticalTransverseDisplacement.Value, second.CriticalTransverseDisplacement.Value);
        Assert.Equal(first.CriticalTransverseDisplacement.Position, second.CriticalTransverseDisplacement.Position);
        Assert.Equal(first.CriticalTransverseDisplacement.Side, second.CriticalTransverseDisplacement.Side);
    }
}
