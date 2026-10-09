using SpanDraft.Analysis;
using SpanDraft.Core.Sections;
using SpanDraft.Engineering;
using Xunit;
using static SpanDraft.Tests.SectionAxisTestSupport;
using static SpanDraft.Tests.SolverTestSupport;

namespace SpanDraft.Tests;

public sealed class EngineeringSectionAxisTests
{
    [Theory]
    [InlineData(.0002, .0001, -1000)]
    [InlineData(.0001, .0002, -1000)]
    [InlineData(.0002, .0001, 1000)]
    [InlineData(.0001, .0002, 1000)]
    public void EitherGeometricSideCanGovernForEitherMomentSign(double positiveW, double negativeW, double force)
    {
        var beam = AxisBeam(new TabulatedSection(positiveW: positiveW, negativeW: negativeW),
            SectionAxisDesignation.Y, [Point(L, force)]);
        var result = BeamEngineeringAnalysis.Analyze(Solve(beam));
        NumericAssert.Close(Math.Abs(force) * L / Math.Min(positiveW, negativeW), result.MaximumBendingStress.Pascals);
        NumericAssert.Close(235e6 / result.MaximumBendingStress.Pascals, result.SafetyFactor);
        Assert.Equal(M(0), result.BendingStressPosition);
        Assert.Equal(Math.Sign(force), Math.Sign(result.CriticalBendingMoment.Value.NewtonMeters));
    }

    [Theory]
    [InlineData(-500)]
    [InlineData(500)]
    public void ParametricTProfileUsesAsymmetricModuliAtTheAnalyticalMomentExtremum(double q)
    {
        var section = Profile("T");
        var axis = section.GetAxis(SectionAxisDesignation.Y);
        Assert.NotEqual(axis.PositiveSectionModulus, axis.NegativeSectionModulus);
        var beam = AxisBeam(section, SectionAxisDesignation.Y, [Uniform(0, L, q)], cantilever: false);
        var solution = Solve(beam);
        var result = BeamEngineeringAnalysis.Analyze(solution);
        double expected = Math.Abs(q) * L * L / 8 /
            Math.Min(axis.PositiveSectionModulus.CubicMeters, axis.NegativeSectionModulus.CubicMeters);
        NumericAssert.Close(expected, result.MaximumBendingStress.Pascals);
        NumericAssert.Close(235e6 / expected, result.SafetyFactor);
        NumericAssert.Close(L / 2, result.BendingStressPosition.Meters);
        Assert.Null(result.BendingStressSide);
        Assert.Same(q < 0 ? solution.Extrema.MaximumBendingMoment : solution.Extrema.MinimumBendingMoment,
            result.CriticalBendingMoment);
    }

    [Theory]
    [InlineData(SectionAxisDesignation.Y)]
    [InlineData(SectionAxisDesignation.Z)]
    public void EngineeringUsesTheSelectedAxisModuli(SectionAxisDesignation axis)
    {
        var section = new TabulatedSection();
        var properties = section.GetAxis(axis);
        var result = BeamEngineeringAnalysis.Analyze(Solve(AxisBeam(section, axis, [Point(L, -1000)])));
        NumericAssert.Close(2000 / Math.Min(properties.PositiveSectionModulus.CubicMeters,
            properties.NegativeSectionModulus.CubicMeters), result.MaximumBendingStress.Pascals);
    }

    [Fact]
    public void SymmetricManualSectionPreservesSingleModulusAndZeroLoadBehavior()
    {
        var manual = Manual(SectionAxisDesignation.Y);
        var result = BeamEngineeringAnalysis.Analyze(Solve(AxisBeam(manual, SectionAxisDesignation.Y, [Point(L, -1000)])));
        NumericAssert.Close(2000 / .0002, result.MaximumBendingStress.Pascals);
        var unloaded = BeamEngineeringAnalysis.Analyze(Solve(AxisBeam(manual, SectionAxisDesignation.Y)));
        Assert.Equal(0, unloaded.MaximumBendingStress.Pascals);
        Assert.Equal(double.PositiveInfinity, unloaded.SafetyFactor);
    }

    [Theory]
    [InlineData("T", SectionAxisDesignation.Y)]
    [InlineData("rectangle", SectionAxisDesignation.Z)]
    [InlineData("angle", SectionAxisDesignation.U)]
    [InlineData("angle", SectionAxisDesignation.V)]
    public void AnalysisFacadeRetainsTheNewDomainModelAndConsistentEngineering(string shape, SectionAxisDesignation axis)
    {
        var section = Profile(shape);
        var beam = AxisBeam(section, axis, [Point(L, -1000)]);
        var outcome = BeamAnalysis.Analyze(beam);
        Assert.True(outcome.IsSuccess);
        Assert.Null(outcome.Failure);
        var result = Assert.IsType<BeamAnalysisResult>(outcome.Result);
        Assert.Same(beam, result.Beam);
        Assert.Same(beam, result.Solution.Beam);
        Assert.Same(section, result.Beam.Section);
        Assert.Equal(axis, result.Beam.BendingAxis);
        Assert.Same(section.GetAxis(axis), result.Beam.BendingAxisProperties);
        Assert.Equal(BeamEngineeringAnalysis.Analyze(result.Solution).MaximumBendingStress,
            result.Engineering.MaximumBendingStress);
    }
}
