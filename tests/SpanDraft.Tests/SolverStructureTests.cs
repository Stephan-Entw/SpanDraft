using SpanDraft.Core.Beams;
using SpanDraft.Core.Supports;
using SpanDraft.Solver;
using Xunit;
using static SpanDraft.Tests.SolverTestSupport;

namespace SpanDraft.Tests;

public class SolverStructureTests
{
    [Fact]
    public void StationsSortAndExactlyDeduplicateEveryRelevantPosition()
    {
        BeamModel beam = Beam([SupportAt(1.5, SupportType.Roller), SupportAt(0.5, SupportType.Pinned)],
            [Point(1, -1), Couple(1, 2), Uniform(0.25, 1.75, -3), Point(0.5, 4), Couple(2, 5)]);
        BeamSolution solution = Solve(beam);
        Assert.Equal(new double[] { 0, 0.25, 0.5, 1, 1.5, 1.75, 2 }, solution.Nodes.Select(n => n.Position.Meters));
        Assert.Equal(Enumerable.Range(0, solution.Nodes.Count), solution.Nodes.Select(n => n.NodeIndex));
    }

    [Fact]
    public void DistinctNearbyPositionsAreNotMerged()
    {
        BeamSolution solution = Solve(Cantilever(Point(1, 0), Point(Math.BitIncrement(1), 0)));
        Assert.Equal(new[] { 0, 1, Math.BitIncrement(1), L }, solution.Nodes.Select(n => n.Position.Meters));
    }

    [Fact]
    public void SingleFullLengthUniformLoadDoesNotIntroduceMidpoint()
    {
        BeamSolution solution = Solve(Beam(loads: [Uniform(0, L, -1000)]));
        Assert.Equal(new double[] { 0, L }, solution.Nodes.Select(n => n.Position.Meters));
    }

    [Fact]
    public void CoincidentDifferentSupportsMergeRestraintsWithoutDuplicatingNodes()
    {
        BeamModel beam = Beam([SupportAt(0, SupportType.Fixed), SupportAt(0, SupportType.Pinned), SupportAt(0, SupportType.Roller)]);
        BeamSolution solution = Solve(beam);
        Assert.Equal(2, solution.Nodes.Count);
        Assert.NotNull(solution.Nodes[0].ReactionX);
        Assert.NotNull(solution.Nodes[0].ReactionY);
        Assert.NotNull(solution.Nodes[0].ReactionMoment);
        SameResults(Solve(Cantilever()), solution);
    }

    [Fact]
    public void SolverPreservesInputAndReturnsReadOnlyPositionMappedResults()
    {
        BeamModel beam = Cantilever(Point(1, -100), Uniform(0, 2, -500));
        var supports = beam.Supports.ToArray();
        var loads = beam.Loads.ToArray();
        var material = beam.Material;
        var section = beam.Section;
        BeamSolution result = Solve(beam);
        Assert.Same(beam, result.Beam);
        Assert.Equal(M(2), beam.Length);
        Assert.Same(material, beam.Material);
        Assert.Same(section, beam.Section);
        Assert.Equal(supports, beam.Supports);
        Assert.Equal(loads, beam.Loads);
        Assert.All(supports.Zip(beam.Supports), pair => Assert.Same(pair.First, pair.Second));
        Assert.All(loads.Zip(beam.Loads), pair => Assert.Same(pair.First, pair.Second));
        Assert.Equal(new double[] { 0, 1, 2 }, result.Nodes.Select(n => n.Position.Meters));
        Assert.Equal(new[] { 0, 1, 2 }, result.Nodes.Select(n => n.NodeIndex));
        Assert.Throws<NotSupportedException>(() => ((IList<BeamNodeResult>)result.Nodes).Clear());
        Assert.All(result.Nodes.Skip(1), node =>
        {
            Assert.Null(node.ReactionX);
            Assert.Null(node.ReactionY);
            Assert.Null(node.ReactionMoment);
        });
    }

    [Fact]
    public void SolverInstanceCanBeReusedWithoutRetainingPreviousModelState()
    {
        var solver = new EulerBernoulliBeamSolver();
        BeamModel first = Cantilever(Point(2, -1000));
        BeamModel second = Beam(loads: [Point(1, -1000)]);
        BeamSolution before = solver.Solve(first);
        SameResults(Solve(second), solver.Solve(second));
        SameResults(before, solver.Solve(first));
    }
}
