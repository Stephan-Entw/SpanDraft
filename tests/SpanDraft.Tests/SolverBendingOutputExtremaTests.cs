using SpanDraft.Solver;
using SpanDraft.Solver.Kernel;
using Xunit;
using static SpanDraft.Solver.Kernel.RestrainedDirections;
using static SpanDraft.Tests.SolverBendingTestSupport;
using static SpanDraft.Tests.SolverExactNumber;
using static SpanDraft.Tests.SolverExactLinearAlgebra;
using static SpanDraft.Tests.SolverOutputOracle;

namespace SpanDraft.Tests;

public sealed class SolverBendingOutputExtremaTests
{
    [Fact]
    public void SymmetricUdlHasExactInteriorWAndMExtremaAndMonotoneShear()
    {
        var input = Model(restraints: [new(0, U | W), new(2, W)], lines: [new(0, 2, -3)]);
        BendingResult result = SolverKernel.Solve(input);
        Assert.Equal(1, result.Extrema.W.Minimum.Position, 12);
        Assert.Equal(FieldSide.Interior, result.Extrema.W.Minimum.Side);
        Bounded(From(result.Extrema.W.Minimum.Output.Value) - Q(-5, 8), result.Extrema.W.Minimum.Output.VerifiedOutputBound);
        Assert.Equal(1, result.Extrema.M.Maximum.Position, 12);
        Bounded(From(result.Extrema.M.Maximum.Output.Value) - Q(3, 2), result.Extrema.M.Maximum.Output.VerifiedOutputBound);
        Assert.Equal(0, result.Extrema.V.Maximum.Position);
        Assert.Equal(FieldSide.Right, result.Extrema.V.Maximum.Side);
        Assert.Equal(2, result.Extrema.V.Minimum.Position);
        Assert.Equal(FieldSide.Left, result.Extrema.V.Minimum.Side);
        Check(result, new(input, Q(0), Q(-1), [(0, W, Q(3)), (2, W, Q(3))]));
    }

    [Fact]
    public void IrrationalDisplacementRootUsesDirectValueAtRoundedGlobalPosition()
    {
        var input = Model(restraints: [new(0, U | W), new(2, W)], forces: [new(0.5, -3)]);
        var oracle = new SolverOutputOracle(input, Q(0), Q(-21, 32), [(0, W, Q(9, 4)), (2, W, Q(3, 4))]);
        BendingResult result = SolverKernel.Solve(input);
        BendingExtremum minimum = result.Extrema.W.Minimum;
        Assert.Equal(FieldSide.Interior, minimum.Side);
        // Independent algebraic root: (2-x)^2=5/4, x in (1/2,1).
        Assert.InRange(minimum.Position, 0.5, 1);
        SolverExactNumber y = Q(2) - From(minimum.Position);
        Bounded(y * y - Q(5, 4), Math.ScaleB(1, -40));
        Check(result, oracle);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(0.000000001)]
    [InlineData(0.000000000001)]
    public void RootAtOrNextToEventKeepsExactPositionAndPhysicalSide(double offset)
    {
        var input = Model(restraints: [new(0, U | W), new(2, W)], forces: [new(1 + offset, 0)], lines: [new(0, 2, -3)]);
        BendingResult result = SolverKernel.Solve(input);
        Assert.Equal(1, result.Extrema.W.Minimum.Position, 12);
        Assert.Equal(offset == 0 ? FieldSide.Left : FieldSide.Interior, result.Extrema.W.Minimum.Side);
        Assert.Equal(offset == 0 ? FieldSide.Left : FieldSide.Interior, result.Extrema.M.Maximum.Side);
        Check(result, new(input, Q(0), Q(-1), [(0, W, Q(3)), (2, W, Q(3))]));
    }

    [Theory]
    [InlineData(-20)]
    [InlineData(-36)]
    public void VeryShortPieceAroundExtremumDoesNotLoseRequiredCandidate(int exponent)
    {
        double gap = Math.ScaleB(1, exponent);
        var input = Model(restraints: [new(0, U | W), new(2, W)],
            forces: [new(1 - gap, 0), new(1 + gap, 0)], lines: [new(0, 2, -3)]);
        BendingResult result = SolverKernel.Solve(input);
        BendingPiece piece = Assert.Single(result.Pieces, p => p.Start == 1 - gap);
        Assert.Contains(piece.Candidates(BendingComponent.W), t => t > 0 && t < 1);
        Assert.Contains(piece.Candidates(BendingComponent.M), t => t > 0 && t < 1);
        Assert.InRange(result.Extrema.W.Minimum.Position, 1 - gap, 1 + gap);
        Check(result, new(input, Q(0), Q(-1), [(0, W, Q(3)), (2, W, Q(3))]));
    }

    [Theory]
    [InlineData(0.5, 0d)]
    [InlineData(0.5, 0.00000095367431640625)]
    [InlineData(0.00000095367431640625, 0d)]
    [InlineData(0.9999990463256836, 0d)]
    public void ConstructedCubicHasDoubleOrTwoNearbyRootsAgainstExactFactoredOracle(double a, double delta)
    {
        double m0 = a * a - delta * delta;
        // theta=x*((x-a)^2-delta^2), so q=6, V0=-4a, M0=a²-delta².
        var input = Model(length: 1, forces: [new(1, 4 * a - 6)], moments: [new(1, 3 - 4 * a + m0)], lines: [new(0, 1, 6)]);
        BendingResult result = SolverKernel.Solve(input);
        BendingPiece piece = Assert.Single(result.Pieces);
        var roots = piece.Candidates(BendingComponent.W);
        Assert.Contains(roots, t => t == 0);
        foreach (double root in delta == 0 ? new[] { a } : new[] { a - delta, a + delta })
            Assert.Contains(roots, t => Math.Abs(t - root) <= Math.ScaleB(1, -30));
        var oracle = new SolverOutputOracle(input, Q(0), Q(0), [(0, W, From(-4 * a)), (0, Theta, From(-m0))]);
        Check(result, oracle);
    }

    [Fact]
    public void RoundedInteriorRootUsesExistingEndpointSideWithoutSnapping()
    {
        var quality = new PieceQuality(1, Math.BitIncrement(1), [0, 0, 0, 0], [1, 1, 1, 1], [1, 1, 1, 1]);
        var piece = new BendingPiece(quality.Start, quality.End, [-0.5, 1], [0], 0, quality);
        Assert.Equal((1d, FieldSide.Right), piece.CandidatePosition(0.5));
        Assert.Equal((Math.BitIncrement(1), FieldSide.Left), piece.CandidatePosition(0.75));
        Assert.Equal((1d, FieldSide.Right), piece.CandidatePosition(0));
        Assert.Equal((Math.BitIncrement(1), FieldSide.Left), piece.CandidatePosition(1));
    }

    [Fact]
    public void EqualGlobalExtremaChooseSmallestPositionThenLeftBeforeRight()
    {
        var input = Model(restraints: [new(1, U | W | Theta)], forces: [new(0, -1), new(2, -1)]);
        BendingResult result = SolverKernel.Solve(input);
        Assert.Equal(0, result.Extrema.W.Minimum.Position);
        Assert.Equal(FieldSide.Right, result.Extrema.W.Minimum.Side);
        Assert.Equal(1, result.Extrema.M.Minimum.Position);
        Assert.Equal(FieldSide.Left, result.Extrema.M.Minimum.Side);
        Check(result, new(input, Q(-1, 3), Q(1, 2), [(1, W, Q(2)), (1, Theta, Q(0))]));
    }

    [Fact]
    public void ConstantFieldsAndLoadJumpRetainCurrentSignedSelection()
    {
        BendingResult constant = SolverKernel.Solve(Case("end-moment").Input);
        Assert.Equal(0, constant.Extrema.M.Minimum.Position);
        Assert.Equal(0, constant.Extrema.M.Maximum.Position);
        Assert.Equal(FieldSide.Right, constant.Extrema.M.Maximum.Side);
        var input = Model(restraints: [new(0, U | W), new(2, W)], moments: [new(0.75, 4)]);
        BendingResult jump = SolverKernel.Solve(input);
        Assert.Equal(0.75, jump.Extrema.M.Maximum.Position);
        Assert.Equal(FieldSide.Left, jump.Extrema.M.Maximum.Side);
        Assert.Equal(0.75, jump.Extrema.M.Minimum.Position);
        Assert.Equal(FieldSide.Right, jump.Extrema.M.Minimum.Side);
        Assert.True(jump.Extrema.M.Maximum.Output.Value > 0);
        Assert.True(jump.Extrema.M.Minimum.Output.Value < 0);
    }

    [Fact]
    public void RelativeTieRuleHasNoAbsoluteSiFloorAndRetainsBoundOfSelectedCandidate()
    {
        foreach (double magnitude in new[] { Math.ScaleB(1, -900), 1d, Math.ScaleB(1, 900) })
        {
            var left = new BendingExtremum(1, FieldSide.Left, new(magnitude, 0.125));
            var right = new BendingExtremum(1, FieldSide.Right, new(Math.BitIncrement(magnitude), 0.25));
            Assert.True(BendingExtrema.Better(left, right, false));
            Assert.False(BendingExtrema.Better(right, left, false));
            var earlier = left with { Position = 0 };
            Assert.True(BendingExtrema.Better(earlier, left, true));
            var distinct = left with { Output = new(magnitude * (1 + 128 * PolynomialRoots.MachinePrecision), 1) };
            Assert.True(BendingExtrema.Better(distinct, earlier, false));
        }
    }

    private static void Check(BendingResult result, SolverOutputOracle oracle)
    {
        foreach (var (range, q) in new[] { (result.Extrema.W, 0), (result.Extrema.M, 2), (result.Extrema.V, 3) })
            foreach (var extreme in new[] { range.Minimum, range.Maximum })
            {
                VerifiedOutput direct = result.EvaluateAt(extreme.Position, extreme.Side).Bending[q];
                Assert.Equal(direct, extreme.Output);
                Bounded(From(direct.Value) - oracle.Field(extreme.Position, extreme.Side)[q], direct.VerifiedOutputBound);
                BendingPiece piece = result.Pieces.First(p => p.Start <= extreme.Position && p.End >= extreme.Position &&
                    !(p.Start == extreme.Position && extreme.Side == FieldSide.Left) &&
                    !(p.End == extreme.Position && extreme.Side == FieldSide.Right));
                OutputGateCheck gate = BendingOutputQuality.Check(piece.Quality.Bounds[q], piece.Quality.Denominators[q], result.Fields.Exponents[q]);
                Assert.True(gate.Passed);
                Assert.True(direct.VerifiedOutputBound <= gate.BoundSI);
            }
    }
}
