using System.Collections.Immutable;
using SpanDraft.Solver.Kernel;
using Xunit;
using static SpanDraft.Tests.SolverExactNumber;
using static SpanDraft.Tests.SolverExactLinearAlgebra;
using static SpanDraft.Tests.SolverOutputOracle;
using static SpanDraft.Tests.SolverBendingTestSupport;
using static SpanDraft.Solver.Kernel.RestrainedDirections;

namespace SpanDraft.Tests;

public sealed class SolverBendingOutputTests
{
    public static TheoryData<string> Cases => new()
    {
        "end-force", "end-moment", "udl", "pinned", "indeterminate", "coincident", "support-load", "mixed",
        "root-loads", "right-fixed", "near", "graded", "short-udl", "scaled", "compensated", "unloaded", "signed-zero"
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void StagesEAndFEncloseFixedIndependentExactMechanicsThroughoutEveryPiece(string name)
    {
        SolverOutputOracle oracle = Case(name);
        BendingResult result = SolverKernel.Solve(oracle.Input);
        Assert.True(result.Quality.IsVerified && result.Quality.GatePassed);
        double[] events = oracle.Input.Restraints.Select(s => s.Position)
            .Concat(oracle.Input.PointForces.Select(p => p.Position)).Concat(oracle.Input.PointMoments.Select(p => p.Position))
            .Concat(oracle.Input.LineLoads.SelectMany(q => new[] { q.Start, q.End })).Concat([0, oracle.Input.Length])
            .Distinct().Order().ToArray();
        Assert.Equal(events, result.Stations.Select(s => s.Position));
        Assert.Equal(events.Length - 1, result.Pieces.Length);
        Assert.Equal(events.Zip(events.Skip(1)), result.Pieces.Select(p => (p.Start, p.End)));
        SolverExactNumber[] scales = oracle.Scales(events);
        if (!result.Quality.IsUnloaded)
        {
            for (int q = 0; q < 4; q++)
            {
                AssertEncloses(result.Quality.ScalesLowSI[q], scales[q], result.Quality.ScalesHighSI[q]);
                Assert.InRange(result.Quality.ScalesLowSI[q], BendingOutputQuality.SiMin, BendingOutputQuality.SiMax);
                Assert.InRange(result.Quality.MaxRatios[q], 0, 1d / Binary64.TauDenominator);
            }
            Assert.Equal(result.Quality.PrimaryIterations + 1, result.Quality.LuSolveCalls);
            Assert.Equal(result.Quality.StateAdjoints + result.Quality.FunctionalAdjoints + result.Quality.AdjointIterations,
                result.Quality.LuTransposeSolveCalls);
            Assert.Equal(4 * (2 * events.Length - 2), result.Quality.StationChecks);
            Assert.Equal(oracle.Reactions.Length, result.Quality.ReactionChecks);
            Assert.Equal(4 * (events.Length - 1), result.Quality.PieceChecks);
        }
        foreach (BendingStation station in result.Stations)
        {
            Assert.Equal(station.Position == 0, station.Left is null);
            Assert.Equal(station.Position == oracle.Input.Length, station.Right is null);
            if (station.Left is { } left) Check(left, null);
            if (station.Right is { } right) Check(right, null);
        }
        foreach (BendingPiece piece in result.Pieces)
        {
            Check(result.EvaluateAt(piece.Start, FieldSide.Right), piece);
            Check(result.EvaluateAt(piece.End, FieldSide.Left), piece);
            double[] samples = [Math.BitIncrement(piece.Start), Math.BitDecrement(piece.End),
                piece.Start + (piece.End - piece.Start) / 4, piece.Start + (piece.End - piece.Start) / 2,
                piece.Start + (piece.End - piece.Start) * 0.75];
            foreach (double x in samples.Distinct().Where(x => x > piece.Start && x < piece.End))
                Check(result.EvaluateAt(x, FieldSide.Interior), piece);
        }
        foreach (BendingReaction reaction in result.Reactions)
        {
            if (reaction.Direction == U)
            {
                Assert.Equal(default, reaction.Output);
                continue;
            }
            var expected = Assert.Single(oracle.Reactions, r => r.X == reaction.Position && r.Direction == reaction.Direction);
            Bounded(From(reaction.Output.Value) - expected.Value, reaction.Output.VerifiedOutputBound);
            int q = reaction.Direction == W ? 3 : 2;
            if (!result.Quality.IsUnloaded)
                Assert.True(From(reaction.Output.VerifiedOutputBound).CompareTo(Max(scales[q], expected.Value.Abs()) / Q(Binary64.TauDenominator)) <= 0);
        }
        Assert.Equal(oracle.Reactions.Length, result.Reactions.Count(r => r.Direction != U));
        Assert.Equal(oracle.Input.Restraints.Where(s => s.Directions.HasFlag(U)).Select(s => s.Position).Distinct().Order(),
            result.Reactions.Where(r => r.Direction == U).Select(r => r.Position));
        foreach (var (range, q) in new[] { (result.Extrema.W, 0), (result.Extrema.M, 2), (result.Extrema.V, 3) })
            foreach (BendingExtremum extreme in new[] { range.Minimum, range.Maximum })
            {
                BendingField direct = result.EvaluateAt(extreme.Position, extreme.Side);
                Assert.Equal(direct.Bending[q], extreme.Output);
                Check(direct, result.Pieces.First(p => p.Start <= extreme.Position && p.End >= extreme.Position &&
                    !(p.Start == extreme.Position && extreme.Side == FieldSide.Left) &&
                    !(p.End == extreme.Position && extreme.Side == FieldSide.Right)));
            }

        void Check(BendingField field, BendingPiece? piece)
        {
            SolverExactNumber[] exact = oracle.Field(field.Position, field.Side);
            NormalizedBendingField normalized = result.Fields.EvaluateNormalized(field.Position, field.Side);
            Assert.Equal(default, field.U);
            Assert.Equal(default, field.N);
            for (int q = 0; q < 4; q++)
            {
                Bounded(From(field.Bending[q].Value) - exact[q], field.Bending[q].VerifiedOutputBound);
                Bounded(From(normalized.Values[q]) - exact[q] * PowerOfTwo(-result.Fields.Exponents[q]), normalized.Bounds[q]);
                AssertStored(field.Bending[q].VerifiedOutputBound);
                if (piece is not null)
                {
                    Assert.True(normalized.Bounds[q] <= piece.Quality.Bounds[q]);
                    Assert.True(exact[q].Abs().CompareTo(From(piece.Quality.UpperValues[q]) * PowerOfTwo(result.Fields.Exponents[q])) <= 0);
                    if (!result.Quality.IsUnloaded)
                    {
                        OutputGateCheck gate = BendingOutputQuality.Check(piece.Quality.Bounds[q], piece.Quality.Denominators[q], result.Fields.Exponents[q]);
                        Assert.True(gate.Passed);
                        Assert.True(field.Bending[q].VerifiedOutputBound <= gate.BoundSI);
                    }
                }
                if (!result.Quality.IsUnloaded)
                {
                    SolverExactNumber d = q < 2 ? scales[q] : Max(scales[q], exact[q].Abs());
                    Assert.True(From(field.Bending[q].VerifiedOutputBound).CompareTo(d / Q(Binary64.TauDenominator)) <= 0);
                }
                else
                {
                    Assert.Equal(0UL, BitConverter.DoubleToUInt64Bits(field.Bending[q].Value));
                    Assert.Equal(0, field.Bending[q].VerifiedOutputBound);
                }
            }
        }
    }

    [Fact]
    public void EventRefinementCannotChangeDirectFieldsOrTheirBounds()
    {
        var input = Case("mixed").Input;
        var refined = new MechanicalInput(input.Length, input.EI, input.Restraints,
            input.PointForces.Concat(Enumerable.Range(1, 999).Select(i => new TransversePointForce(i / 500d, -0d))),
            input.PointMoments, input.LineLoads);
        BendingResult a = SolverKernel.Solve(input), b = SolverKernel.Solve(refined);
        foreach (double x in new[] { 0.125, 0.755, 1.123, 1.99, 2 })
        {
            BendingField av = a.EvaluateAt(x, FieldSide.Left), bv = b.EvaluateAt(x, FieldSide.Left);
            Assert.Equal(av.Bending, bv.Bending);
        }
        Assert.Equal(a.Quality.StateAdjoints, b.Quality.StateAdjoints);
    }

    [Fact]
    public void ExactLoadSidesAndMechanicalSidesPreserveJumpsAndEndpointMapping()
    {
        BendingResult result = SolverKernel.Solve(Case("mixed").Input);
        BendingStation load = Assert.Single(result.Stations, s => s.Position == 1);
        Assert.Equal(BendingJump.PointForce | BendingJump.PointMoment, load.Jumps);
        Assert.Equal(-2, load.Right!.Bending[3].Value - load.Left!.Bending[3].Value);
        Assert.Equal(-3, load.Right.Bending[2].Value - load.Left.Bending[2].Value);
        foreach (double x in new[] { 0.25, 1.75 })
            Assert.Equal(result.EvaluateAt(x, FieldSide.Left).Bending, result.EvaluateAt(x, FieldSide.Right).Bending);
        foreach (double x in new[] { 0d, 2 })
        {
            BendingField left = result.EvaluateAt(x, FieldSide.Left), right = result.EvaluateAt(x, FieldSide.Right);
            Assert.Equal(left.Side, right.Side);
            Assert.Equal(left.Bending, right.Bending);
        }
        var mechanical = SolverKernel.Solve(Case("indeterminate").Input);
        BendingStation support = Assert.Single(mechanical.Stations, s => s.Position == 1);
        Assert.Equal(BendingJump.ReactionY, support.Jumps);
        Assert.NotNull(support.Left);
        Assert.NotNull(support.Right);
        Assert.Equal(0, support.Right.Bending[0].Value);
        Assert.NotEqual(support.Left!.Bending[3].Value, support.Right.Bending[3].Value);
    }

    [Fact]
    public void UnloadedSkipsNumericsAndUsesFirstPhysicalEndpointForAllExtrema()
    {
        BendingSystem system = SolverKernel.BuildSystem(Case("signed-zero").Input);
        VerifiedBendingState state = BendingStateSolver.Solve(system, out BendingSolveContext? context);
        Assert.Null(context);
        // No additional solve or evaluation for diagnostic purposes, even with poisoned skipped data.
        state = state with { D = [double.NaN], VerifiedStateBound = [double.NaN] };
        Assert.All(BendingReactions.Calculate(state, null), r => Assert.Equal(0UL, BitConverter.DoubleToUInt64Bits(r.Value)));
        BendingResult result = SolverKernel.Solve(Case("signed-zero").Input);
        Assert.Equal(0, result.Quality.LuSolveCalls);
        Assert.Equal(0, result.Quality.LuTransposeSolveCalls);
        Assert.Equal(0, result.Quality.StationChecks);
        Assert.Equal(0, result.Quality.ReactionChecks);
        Assert.Equal(0, result.Quality.PieceChecks);
        foreach (var range in new[] { result.Extrema.W, result.Extrema.V, result.Extrema.M })
        {
            Assert.Equal(new BendingExtremum(0, FieldSide.Right, default), range.Minimum);
            Assert.Equal(range.Minimum, range.Maximum);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InteriorRejectsEveryEventKindWhilePhysicalSidesRemainValid(bool unloaded)
    {
        BendingResult result = EventResult(unloaded);
        Assert.Equal(new[] { 0d, 0.5, 0.75, 1, 1.5, 1.75, 2 }, result.Fields.EventPositions);
        foreach (double x in result.Fields.EventPositions)
        {
            Assert.Equal("side", Assert.Throws<ArgumentException>(() => result.EvaluateAt(x, FieldSide.Interior)).ParamName);
            Assert.Throws<ArgumentException>(() => result.Fields.EvaluateNormalized(x, FieldSide.Interior));
            BendingField left = result.EvaluateAt(x, FieldSide.Left), right = result.EvaluateAt(x, FieldSide.Right);
            if (x == 0 || x == 2)
            {
                Assert.Equal(x == 0 ? FieldSide.Right : FieldSide.Left, left.Side);
                Assert.Equal(left.Side, right.Side);
                Assert.Equal(left.Bending, right.Bending);
            }
            if (x == 1 || x == 1.5)
                Assert.Equal(left.Bending, right.Bending);
        }
        if (!unloaded)
        {
            Assert.Equal(-2, result.EvaluateAt(0.5, FieldSide.Right).Bending[3].Value -
                result.EvaluateAt(0.5, FieldSide.Left).Bending[3].Value);
            Assert.Equal(-3, result.EvaluateAt(0.75, FieldSide.Right).Bending[2].Value -
                result.EvaluateAt(0.75, FieldSide.Left).Bending[2].Value);
        }
    }

    [Fact]
    public void InteriorWithinEveryOpenEventPieceMatchesBothPhysicalQueriesIncludingBounds()
    {
        BendingResult result = EventResult(unloaded: false);
        foreach (double x in new[] { 0.25, 0.625, 0.875, 1.25, 1.625, 1.875,
            Math.BitDecrement(0.5), Math.BitIncrement(0.5) })
        {
            BendingField interior = result.EvaluateAt(x, FieldSide.Interior);
            Assert.Equal(FieldSide.Interior, interior.Side);
            NormalizedBendingField normalized = result.Fields.EvaluateNormalized(x, FieldSide.Interior);
            foreach (FieldSide side in new[] { FieldSide.Left, FieldSide.Right })
            {
                Assert.Equal(interior.Bending, result.EvaluateAt(x, side).Bending);
                NormalizedBendingField other = result.Fields.EvaluateNormalized(x, side);
                Assert.Equal(normalized.Values, other.Values);
                Assert.Equal(normalized.Bounds, other.Bounds);
            }
        }
    }

    private static BendingResult EventResult(bool unloaded) => SolverKernel.Solve(Model(
        restraints: [new(0, U | W | Theta), new(1.75, U)], forces: [new(0.5, unloaded ? 0 : -2)],
        moments: [new(0.75, unloaded ? 0 : 3)], lines: [new(1, 1.5, unloaded ? 0 : 4)]));

    [Fact]
    public void ExactEventSearchFindsManyNonmechanicalEventsAndDistinguishesAdjacentDoubles()
    {
        var input = Model(forces: Enumerable.Range(1, 2047).Select(i => new TransversePointForce(i / 1024d, 0))
            .Append(new(Math.BitIncrement(1), 0)).ToArray());
        BendingFields fields = BendingFields.From(SolverKernel.SolveSystem(SolverKernel.BuildSystem(input)));
        Assert.Equal(2, fields.Stations.Length);
        Assert.Equal(2050, fields.EventPositions.Length);
        foreach (double x in fields.EventPositions)
        {
            Assert.True(fields.IsEventPosition(x));
            Assert.Throws<ArgumentException>(() => fields.EvaluateNormalized(x, FieldSide.Interior));
        }
        for (int i = 1; i < fields.EventPositions.Length; i++)
        {
            double left = fields.EventPositions[i - 1], right = fields.EventPositions[i];
            double middle = left + (right - left) / 2;
            if (!(middle > left && middle < right)) continue;
            Assert.False(fields.IsEventPosition(middle));
            Assert.Equal(new[] { 0d, 0, 0, 0 }, fields.EvaluateNormalized(middle, FieldSide.Interior).Values);
        }
        Assert.True(fields.IsEventPosition(-0d));
        Assert.True(fields.IsEventPosition(1));
        Assert.True(fields.IsEventPosition(Math.BitIncrement(1)));
        foreach (double x in new[] { Math.BitDecrement(0), double.Epsilon, Math.BitDecrement(1),
            Math.BitIncrement(Math.BitIncrement(1)), Math.BitDecrement(2), Math.BitIncrement(2) })
            Assert.False(fields.IsEventPosition(x));
    }
}
