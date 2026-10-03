using SpanDraft.Core.Supports;
using SpanDraft.Solver;
using Xunit;
using static SpanDraft.Tests.SolverTestSupport;

namespace SpanDraft.Tests;

public class SolverAnalyticalTests
{
    [Theory]
    [InlineData(-1000)]
    [InlineData(1000)]
    public void SimplySupportedMidspanForceMatchesAnalyticalSolution(double force)
    {
        BeamSolution solution = Solve(Beam(loads: [Point(L / 2, force)]));
        BeamNodeResult left = At(solution, 0), middle = At(solution, L / 2), right = At(solution, L);

        ForceClose(-force / 2, left.ReactionY);
        ForceClose(-force / 2, right.ReactionY);
        ForceClose(0, left.ReactionX);
        Assert.Null(right.ReactionX);
        Assert.Null(left.ReactionMoment);
        Assert.Null(right.ReactionMoment);
        DisplacementClose(force * Math.Pow(L, 3) / (48 * EI), middle.TransverseDisplacement);
        RotationClose(0, middle.RotationRadians);
        RotationClose(force * L * L / (16 * EI), left.RotationRadians);
        RotationClose(-force * L * L / (16 * EI), right.RotationRadians);
        DisplacementClose(0, left.TransverseDisplacement);
        DisplacementClose(0, right.TransverseDisplacement);
        foreach (BeamNodeResult node in solution.Nodes) DisplacementClose(0, node.AxialDisplacement);
    }

    [Theory]
    [InlineData(-1000)]
    [InlineData(1000)]
    public void CantileverTipForceMatchesAnalyticalSolution(double force)
    {
        BeamSolution solution = Solve(Cantilever(Point(L, force)));
        BeamNodeResult root = At(solution, 0), tip = At(solution, L);

        ForceClose(-force, root.ReactionY);
        ForceClose(0, root.ReactionX);
        MomentClose(-force * L, root.ReactionMoment);
        DisplacementClose(force * Math.Pow(L, 3) / (3 * EI), tip.TransverseDisplacement);
        RotationClose(force * L * L / (2 * EI), tip.RotationRadians);
        DisplacementClose(0, root.AxialDisplacement);
        DisplacementClose(0, root.TransverseDisplacement);
        RotationClose(0, root.RotationRadians);
        Assert.Null(tip.ReactionX);
        Assert.Null(tip.ReactionY);
        Assert.Null(tip.ReactionMoment);
    }

    [Theory]
    [InlineData(-1000)]
    [InlineData(1000)]
    public void CantileverFullUniformLoadMatchesAnalyticalSolution(double intensity)
    {
        BeamSolution solution = Solve(Cantilever(Uniform(0, L, intensity)));
        Assert.Equal(2, solution.Nodes.Count);
        BeamNodeResult root = At(solution, 0), tip = At(solution, L);

        ForceClose(-intensity * L, root.ReactionY);
        ForceClose(0, root.ReactionX);
        MomentClose(-intensity * L * L / 2, root.ReactionMoment);
        DisplacementClose(intensity * Math.Pow(L, 4) / (8 * EI), tip.TransverseDisplacement);
        RotationClose(intensity * Math.Pow(L, 3) / (6 * EI), tip.RotationRadians);
    }

    [Theory]
    [InlineData(-1000)]
    [InlineData(1000)]
    public void SimplySupportedUniformLoadMatchesAnalyticalMidspanSolution(double intensity)
    {
        BeamSolution solution = Solve(Beam(loads:
            [Uniform(0, L / 2, intensity), Uniform(L / 2, L, intensity)]));
        Assert.Equal(3, solution.Nodes.Count);

        ForceClose(-intensity * L / 2, At(solution, 0).ReactionY);
        ForceClose(-intensity * L / 2, At(solution, L).ReactionY);
        ForceClose(0, At(solution, 0).ReactionX);
        DisplacementClose(5 * intensity * Math.Pow(L, 4) / (384 * EI), At(solution, L / 2).TransverseDisplacement);
        RotationClose(0, At(solution, L / 2).RotationRadians);
        RotationClose(intensity * Math.Pow(L, 3) / (24 * EI), At(solution, 0).RotationRadians);
    }

    [Theory]
    [InlineData(-500)]
    [InlineData(500)]
    public void CantileverEndMomentMatchesAnalyticalSolution(double moment)
    {
        BeamSolution solution = Solve(Cantilever(Couple(L, moment)));
        ForceClose(0, At(solution, 0).ReactionY);
        MomentClose(-moment, At(solution, 0).ReactionMoment);
        DisplacementClose(moment * L * L / (2 * EI), At(solution, L).TransverseDisplacement);
        RotationClose(moment * L / EI, At(solution, L).RotationRadians);
    }

    [Fact]
    public void LoadsAppliedAtFixedSupportProduceReactionsWithoutDeformation()
    {
        BeamSolution solution = Solve(Cantilever(Point(0, -700), Couple(0, 300)));
        ForceClose(700, At(solution, 0).ReactionY);
        MomentClose(-300, At(solution, 0).ReactionMoment);
        foreach (BeamNodeResult node in solution.Nodes)
        {
            DisplacementClose(0, node.TransverseDisplacement);
            RotationClose(0, node.RotationRadians);
        }
    }

    [Fact]
    public void PartialUniformLoadMatchesIntegratedCantileverSolution()
    {
        const double start = 0.5, end = 1.5, intensity = -1000;
        BeamSolution solution = Solve(Cantilever(Uniform(start, end, intensity)));
        ForceClose(-intensity * (end - start), At(solution, 0).ReactionY);
        MomentClose(-intensity * (end * end - start * start) / 2, At(solution, 0).ReactionMoment);
        // Integrate the tip response to a point force at s: s²(3L-s)/(6EI).
        double expectedTip = intensity * (L * (Math.Pow(end, 3) - Math.Pow(start, 3)) -
            (Math.Pow(end, 4) - Math.Pow(start, 4)) / 4) / (6 * EI);
        double expectedRotation = intensity * (Math.Pow(end, 3) - Math.Pow(start, 3)) / (6 * EI);
        DisplacementClose(expectedTip, At(solution, L).TransverseDisplacement);
        RotationClose(expectedRotation, At(solution, L).RotationRadians);
    }

    [Fact]
    public void FixedFixedUniformLoadWorksWithNoFreeDofs()
    {
        const double intensity = -1000;
        BeamSolution solution = Solve(Beam([SupportAt(0, SupportType.Fixed), SupportAt(L, SupportType.Fixed)],
            [Uniform(0, L, intensity)]));
        ForceClose(-intensity * L / 2, At(solution, 0).ReactionY);
        ForceClose(-intensity * L / 2, At(solution, L).ReactionY);
        MomentClose(-intensity * L * L / 12, At(solution, 0).ReactionMoment);
        MomentClose(intensity * L * L / 12, At(solution, L).ReactionMoment);
        foreach (BeamNodeResult node in solution.Nodes)
        {
            DisplacementClose(0, node.TransverseDisplacement);
            RotationClose(0, node.RotationRadians);
        }
    }

    [Fact]
    public void ThreeSupportContinuousBeamMatchesIndeterminateReference()
    {
        // Two equal spans s=1, full UDL: outer reactions 3|p|s/8, middle 5|p|s/4.
        BeamSolution solution = Solve(Beam(
            [SupportAt(0, SupportType.Pinned), SupportAt(1, SupportType.Roller), SupportAt(2, SupportType.Roller)],
            [Uniform(0, 2, -1000)]));
        ForceClose(375, At(solution, 0).ReactionY);
        ForceClose(1250, At(solution, 1).ReactionY);
        ForceClose(375, At(solution, 2).ReactionY);
        RotationClose(0, At(solution, 1).RotationRadians);
    }

    [Fact]
    public void OverlappingUniformLoadsAreEquivalentToSummedPiecewiseIntensity()
    {
        BeamSolution overlap = Solve(Cantilever(Uniform(0, 2, -400), Uniform(0.5, 1.5, -600), Uniform(0.5, 1.5, 100)));
        BeamSolution summed = Solve(Cantilever(Uniform(0, 0.5, -400), Uniform(0.5, 1.5, -900), Uniform(1.5, 2, -400)));
        SameResults(summed, overlap);
        ForceClose(1300, At(overlap, 0).ReactionY);
        MomentClose(1300, At(overlap, 0).ReactionMoment);
    }

    [Fact]
    public void CoincidentPointLoadsAndMomentsAddAtOneNode()
    {
        BeamSolution multiple = Solve(Cantilever(Point(1, -1000), Point(1, 250), Couple(1, 100), Couple(1, -25)));
        BeamSolution summed = Solve(Cantilever(Point(1, -750), Couple(1, 75)));
        Assert.Equal(3, multiple.Nodes.Count);
        SameResults(summed, multiple);
        ForceClose(750, At(multiple, 0).ReactionY);
        MomentClose(675, At(multiple, 0).ReactionMoment);
    }

    [Fact]
    public void BeamWithBothOverhangsBalancesEndpointForces()
    {
        BeamSolution solution = Solve(Beam([SupportAt(0.5, SupportType.Pinned), SupportAt(1.5, SupportType.Roller)],
            [Point(0, -100), Point(2, -300)]));
        ForceClose(0, At(solution, 0.5).ReactionY);
        ForceClose(400, At(solution, 1.5).ReactionY);
        ForceClose(0, At(solution, 0.5).ReactionX);
    }
}
