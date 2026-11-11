using SpanDraft.Solver;
using Xunit;
using static SpanDraft.Tests.SolverTestSupport;

namespace SpanDraft.Tests.SolverReference;

internal static class ReferenceTestSupport
{
    internal static BeamNodeResult At(ReferenceBeamSolution solution, double x) =>
        Assert.Single(solution.Nodes, node => node.Position == M(x));
}
