using SpanDraft.Core.Beams;
using SpanDraft.Core.Materials;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Engineering;
using SpanDraft.Solver;
using Xunit;
using static SpanDraft.Tests.SolverTestSupport;

namespace SpanDraft.Tests;

/// <summary>The stress assessment per beam segment; the governing segment has the smallest safety factor.</summary>
public sealed class EngineeringSegmentTests
{
    private static CustomSection Section(double w, double i = 1e-6) => new(Area.FromSquareMeters(0.01),
        SecondMomentOfArea.FromMetersToTheFourth(i), SectionModulus.FromCubicMeters(w));
    private static Material Steel(double yieldStrength) =>
        new("Segment steel", Pressure.FromPascals(200e9), Pressure.FromPascals(yieldStrength), MaterialStrengthType.Re);
    private static BeamSegment Segment(double start, double end, double w, double re, double i = 1e-6) =>
        new(M(start), M(end), Steel(re), Section(w, i), SectionAxisDesignation.Y);

    private static BeamEngineeringResult Analyze(BeamModel beam) => BeamEngineeringAnalysis.Analyze(Solve(beam));

    private static BeamModel Cantilever(BeamSegment[] segments, double tip = -1000) =>
        new(M(2), segments, [SupportAt(0, SupportType.Fixed)], [Point(2, tip)]);

    [Fact]
    public void ASingleSegmentGivesTheUnchangedBeamIndicators()
    {
        BeamSolution solution = Solve(Cantilever([Segment(0, 2, 0.001, 250e6)]));
        BeamEngineeringResult result = BeamEngineeringAnalysis.Analyze(solution);

        BeamSegmentAssessment only = Assert.Single(result.SegmentAssessments);
        Assert.Equal(0, result.GoverningSegmentIndex);
        Assert.Same(only, result.GoverningSegment);
        Assert.Equal(2000000, result.MaximumBendingStress.Pascals, 1e-6);
        Assert.Equal(125, result.SafetyFactor, 1e-9);
        Assert.Equal(M(0), result.BendingStressPosition);
        Assert.Equal(EvaluationSide.Right, result.BendingStressSide);
        Assert.Same(solution.Extrema.MinimumBendingMoment, result.CriticalBendingMoment);
        BeamExtrema segmentExtrema = Assert.Single(solution.SegmentExtrema);
        Assert.Equal(solution.Extrema.MinimumBendingMoment.Value, segmentExtrema.MinimumBendingMoment.Value);
        Assert.Equal(solution.Extrema.MaximumBendingMoment.Position, segmentExtrema.MaximumBendingMoment.Position);
        Assert.Equal(solution.Extrema.MinimumTransverseDisplacement.Value, segmentExtrema.MinimumTransverseDisplacement.Value);
        Assert.Equal(solution.Extrema.MaximumShearForce.Side, segmentExtrema.MaximumShearForce.Side);
    }

    [Fact]
    public void EachSegmentIsAssessedWithItsOwnMomentModulusAndStrength()
    {
        // M(x) = -P (L - x): 2000 Nm at the support, 1200 Nm at the boundary x = 0.8 (inner side).
        BeamEngineeringResult result = Analyze(Cantilever([Segment(0, 0.8, 0.002, 235e6), Segment(0.8, 2, 0.0005, 355e6)]));

        BeamSegmentAssessment first = result.SegmentAssessments[0], second = result.SegmentAssessments[1];
        Assert.Equal(2000 / 0.002, first.MaximumBendingStress.Pascals, 1e-3);
        Assert.Equal(235e6 / (2000 / 0.002), first.SafetyFactor, 1e-9);
        Assert.Equal(M(0), first.CriticalBendingMoment.Position);
        Assert.Equal(1200 / 0.0005, second.MaximumBendingStress.Pascals, 1e-3);
        Assert.Equal(355e6 / (1200 / 0.0005), second.SafetyFactor, 1e-9);
        Assert.Equal(M(0.8), second.CriticalBendingMoment.Position);
        Assert.Equal(EvaluationSide.Right, second.CriticalBendingMoment.Side);
        // 235 / 1.0 = 235 versus 355 / 2.4 = 147.9: the second segment governs although its moment is smaller.
        Assert.Equal(1, result.GoverningSegmentIndex);
        Assert.Equal(second.SafetyFactor, result.SafetyFactor);
        Assert.Equal(second.MaximumBendingStress, result.MaximumBendingStress);
        Assert.Equal(M(0.8), result.BendingStressPosition);
        // The beam's critical moment stays the global extremum.
        Assert.Equal(-2000, result.CriticalBendingMoment.Value.NewtonMeters, 1e-6);
        Assert.Equal(2000, result.BendingMomentMagnitude.NewtonMeters, 1e-6);
    }

    [Fact]
    public void TheGoverningSegmentFollowsTheSafetyFactorAndNotTheStress()
    {
        // The first segment has the larger stress but the stronger material.
        BeamEngineeringResult strong = Analyze(Cantilever([Segment(0, 0.8, 0.0008, 900e6), Segment(0.8, 2, 0.001, 235e6)]));
        Assert.True(strong.SegmentAssessments[0].MaximumBendingStress.Pascals >
            strong.SegmentAssessments[1].MaximumBendingStress.Pascals);
        Assert.Equal(1, strong.GoverningSegmentIndex);

        BeamEngineeringResult weak = Analyze(Cantilever([Segment(0, 0.8, 0.0008, 235e6), Segment(0.8, 2, 0.001, 900e6)]));
        Assert.Equal(0, weak.GoverningSegmentIndex);
        Assert.Equal(Math.Min(weak.SegmentAssessments[0].SafetyFactor, weak.SegmentAssessments[1].SafetyFactor),
            weak.SafetyFactor);
    }

    [Fact]
    public void ABoundaryExtremumBelongsToBothNeighboursWithTheirOwnSide()
    {
        // Simply supported, P at midspan: M = P L / 4 at the boundary x = 1 for both segments.
        BeamModel beam = new(M(2), [Segment(0, 1, 0.001, 250e6), Segment(1, 2, 0.001, 250e6)],
            [SupportAt(0, SupportType.Pinned), SupportAt(2, SupportType.Roller)], [Point(1, -1000)]);
        BeamSolution solution = Solve(beam);
        BeamEngineeringResult result = BeamEngineeringAnalysis.Analyze(solution);

        Assert.Equal(EvaluationSide.Left, solution.SegmentExtrema[0].MaximumBendingMoment.Side);
        Assert.Equal(EvaluationSide.Right, solution.SegmentExtrema[1].MaximumBendingMoment.Side);
        Assert.Equal(M(1), solution.SegmentExtrema[0].MaximumBendingMoment.Position);
        Assert.Equal(M(1), solution.SegmentExtrema[1].MaximumBendingMoment.Position);
        Assert.Equal(500, solution.SegmentExtrema[0].MaximumBendingMoment.Value.NewtonMeters, 1e-9);
        Assert.Equal(500, solution.SegmentExtrema[1].MaximumBendingMoment.Value.NewtonMeters, 1e-9);
        // Equal safety factors at the same position: the lower index governs.
        Assert.Equal(0, result.GoverningSegmentIndex);
        Assert.Equal(EvaluationSide.Left, result.BendingStressSide);
    }

    [Fact]
    public void SegmentExtremaAreTheExtremaOfTheirOwnRangeAndTheGlobalOnesAreTheirBest()
    {
        BeamModel beam = new(M(3), [Segment(0, 1, 0.001, 250e6, 4e-6), Segment(1, 2.2, 0.001, 250e6, 1e-6),
                Segment(2.2, 3, 0.001, 250e6, 2e-6)],
            [SupportAt(0, SupportType.Pinned), SupportAt(1.7, SupportType.Roller), SupportAt(3, SupportType.Roller)],
            [Uniform(0, 3, -300), Point(2.5, -900), Couple(0.4, 120)]);
        BeamSolution solution = Solve(beam);

        double[] bounds = [0, 1, 2.2, 3];
        for (int i = 0; i < 3; i++)
            foreach (var extremum in new[] { solution.SegmentExtrema[i].MaximumBendingMoment,
                solution.SegmentExtrema[i].MinimumBendingMoment })
                Assert.InRange(extremum.Position.Meters, bounds[i], bounds[i + 1]);
        Assert.Equal(solution.Extrema.MaximumBendingMoment.Value.NewtonMeters,
            solution.SegmentExtrema.Max(e => e.MaximumBendingMoment.Value.NewtonMeters));
        Assert.Equal(solution.Extrema.MinimumBendingMoment.Value.NewtonMeters,
            solution.SegmentExtrema.Min(e => e.MinimumBendingMoment.Value.NewtonMeters));
        Assert.Equal(solution.Extrema.MinimumTransverseDisplacement.Value.Meters,
            solution.SegmentExtrema.Min(e => e.MinimumTransverseDisplacement.Value.Meters));
        Assert.Equal(solution.Extrema.MaximumShearForce.Value.Newtons,
            solution.SegmentExtrema.Max(e => e.MaximumShearForce.Value.Newtons));
    }

    [Fact]
    public void ZeroBendingStressEverywhereHasInfiniteSafetyAndTheFirstSegmentGoverns()
    {
        BeamModel beam = new(M(2), [Segment(0, 1, 0.001, 250e6), Segment(1, 2, 0.002, 400e6)],
            [SupportAt(0, SupportType.Pinned), SupportAt(2, SupportType.Roller)], []);
        BeamEngineeringResult result = Analyze(beam);

        Assert.All(result.SegmentAssessments, a => Assert.True(double.IsPositiveInfinity(a.SafetyFactor)));
        Assert.Equal(0, result.GoverningSegmentIndex);
        Assert.Equal(0, result.MaximumBendingStress.Pascals);
    }

    [Fact]
    public void ASegmentWithoutBendingMomentIsNotGoverningOverAStressedOne()
    {
        // The overhang right of the support carries no moment: its safety is infinite.
        BeamModel beam = new(M(3), [Segment(0, 2, 0.001, 250e6), Segment(2, 3, 0.001, 100e6)],
            [SupportAt(0, SupportType.Fixed)], [Point(1, -500)]);
        BeamEngineeringResult result = Analyze(beam);

        Assert.True(double.IsPositiveInfinity(result.SegmentAssessments[1].SafetyFactor));
        Assert.Equal(0, result.GoverningSegmentIndex);
    }

    [Fact]
    public void ABendingStressBeyondTheDoubleRangeIsRejectedAndNeverPublishedAsInfinite()
    {
        // 2000 Nm / 1e-306 m³ exceeds the largest double; Pressure refuses a non-finite value.
        BeamSolution solution = Solve(Cantilever([Segment(0, 2, 1e-306, 250e6)]));

        Assert.Throws<ArgumentOutOfRangeException>(() => BeamEngineeringAnalysis.Analyze(solution));
    }

    [Fact]
    public void EverySegmentHasExactlyOneExtremaSetInSegmentOrder()
    {
        BeamSolution solution = Solve(Cantilever([Segment(0, 0.8, 0.002, 235e6), Segment(0.8, 2, 0.0005, 355e6)]));

        Assert.Equal(solution.Beam.Segments.Count, solution.SegmentExtrema.Count);
        Assert.Equal(M(0), solution.SegmentExtrema[0].MinimumBendingMoment.Position);
        Assert.Equal(M(0.8), solution.SegmentExtrema[1].MinimumBendingMoment.Position);
    }

    [Fact]
    public void TheSegmentAssessmentsCannotBeChangedThroughTheResult()
    {
        BeamEngineeringResult result = Analyze(Cantilever([Segment(0, 0.8, 0.002, 235e6), Segment(0.8, 2, 0.0005, 355e6)]));
        BeamSegmentAssessment governing = result.GoverningSegment;

        Assert.IsNotType<BeamSegmentAssessment[]>(result.SegmentAssessments);
        var list = Assert.IsAssignableFrom<IList<BeamSegmentAssessment>>(result.SegmentAssessments);
        Assert.True(list.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => list[0] = governing);
        Assert.Same(governing, result.GoverningSegment);
    }
}
