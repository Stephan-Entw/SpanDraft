using System.Collections.Immutable;
using SpanDraft.Solver.Kernel;
using Xunit;
using static SpanDraft.Solver.Kernel.RestrainedDirections;
using static SpanDraft.Tests.SolverBendingTestSupport;
using static SpanDraft.Tests.SolverOutputOracle;
using static SpanDraft.Tests.SolverExactNumber;
using static SpanDraft.Tests.SolverExactLinearAlgebra;

namespace SpanDraft.Tests;

public sealed class SolverM2AcceptanceTests
{
    [Fact]
    public void HistoricalTinyBeamIsVerifiedButRejectedBySiRangeBeforeOutputGate()
    {
        // fpc_chain.py:underflow(), U4: normalization is ordinary; only SI w underflows.
        const double length = 1e-60, ei = 1e10;
        double force = 1e-318 * 48 * ei / Math.Pow(length, 3);
        var input = Model(length, ei, [new(0, U | W), new(length, W)], [new(length / 2, -force)]);
        VerifiedBendingState state = SolverKernel.SolveSystem(SolverKernel.BuildSystem(input));
        Assert.True(state.IsVerified && !state.IsUnloaded);
        Assert.Equal(KernelFailureCause.NumericalRange,
            Assert.Throws<NumericalRangeException>(() => SolverKernel.Solve(input)).Cause);
    }

    [Theory]
    [InlineData(10, 1d, true)]
    [InlineData(24, 1.3125, true)]
    [InlineData(24, 1.25, false)]
    [InlineData(28, 1d, false)]
    [InlineData(52, 1d, false)]
    public void CompleteRigidSupportCasesEncloseExactErrorsAndRespectMathematicalTau(int exponent, double factor, bool delivered)
    {
        double gap = Math.ScaleB(factor, -exponent);
        var input = Model(restraints: [new(1, U | W), new(1 + gap, W)], lines: [new(0.25, 1.75, 3)]);
        var oracle = FromCompatibility(input);
        BendingSystem system = SolverKernel.BuildSystem(input);
        VerifiedBendingState state = BendingStateSolver.Solve(system, out var context);
        Assert.True(state.IsVerified && state.SelectedMu < 1);
        var fields = BendingFields.From(state);
        var reactions = BendingReactions.Calculate(state, context);
        double[] events = system.EventStations.Select(s => s.Position).ToArray();
        var sides = events.SelectMany(x => BendingOutputQuality.PhysicalSides(x, input.Length)
            .Select(side => (X: x, Side: side, Field: fields.EvaluateNormalized(x, side)))).ToArray();
        var (low, _) = BendingOutputQuality.Scales(fields, system.Excitation, sides.Select(s => s.Field));
        SolverExactNumber[] exactScales = oracle.Scales(events);
        SolverExactNumber tau = Q(1, 100_000_000), worstGate = Q(0), worstError = Q(0);
        var pieces = ImmutableArray.CreateBuilder<BendingPiece>();
        foreach (var s in sides)
        {
            CheckField(s.X, s.Side);
            for (int q = 0; q < 4; q++)
                CheckGate(s.Field.Bounds[q], q < 2 ? low[q] : Math.Max(low[q],
                    Directed.Subtract(Math.Abs(s.Field.Values[q]), s.Field.Bounds[q], false)), q);
        }
        foreach (var r in reactions.Where(r => r.Direction != U))
        {
            var expected = Assert.Single(oracle.Reactions, e => e.X == r.Position && e.Direction == r.Direction).Value;
            var output = BendingFields.ToSi(r.Value, r.Bound, fields.Exponents[r.Component]);
            CheckOutput(output, expected, r.Component);
            CheckGate(r.Bound, Math.Max(low[r.Component], Directed.Subtract(Math.Abs(r.Value), r.Bound, false)), r.Component);
        }
        foreach (var (a, b) in events.Zip(events.Skip(1)))
        {
            var piece = BendingOutputQuality.Piece(fields, a, b, fields.EvaluateNormalized(a, FieldSide.Right),
                fields.EvaluateNormalized(b, FieldSide.Left), low);
            pieces.Add(BendingPiece.Create(fields, piece));
            for (int q = 0; q < 4; q++) CheckGate(piece.Bounds[q], piece.Denominators[q], q);
            foreach (double x in new[] { Math.BitIncrement(a), a + (b - a) / 4, a + (b - a) / 2,
                a + 3 * (b - a) / 4, Math.BitDecrement(b) }.Distinct().Where(x => x > a && x < b))
                CheckField(x, FieldSide.Interior);
        }
        var extrema = BendingExtrema.Find(fields, pieces.ToImmutable());
        foreach (var range in new[] { extrema.W, extrema.V, extrema.M })
            foreach (var extreme in new[] { range.Minimum, range.Maximum }) CheckField(extreme.Position, extreme.Side);
        Assert.Equal(delivered, worstGate.CompareTo(tau) <= 0);
        // The rejected fixtures demonstrate conservative rejection, not unsafe delivery.
        if (exponent <= 28) Assert.True(worstError.CompareTo(tau) < 0);
        if (exponent == 10) Assert.True(worstGate.CompareTo(tau / Q(100)) < 0);
        if (exponent == 24)
        {
            Assert.True(worstGate.CompareTo(tau * Q(9, 10)) > 0);
            Assert.True(worstGate.CompareTo(tau * Q(11, 10)) < 0);
        }
        if (delivered) Assert.True(SolverKernel.Solve(input).Quality.GatePassed);
        else Assert.Equal(KernelFailureCause.Gate, Assert.Throws<KernelFailureException>(() => SolverKernel.Solve(input)).Cause);

        void CheckGate(double bound, double denominator, int q)
        {
            var check = BendingOutputQuality.Check(bound, denominator, fields.Exponents[q]);
            var ratio = From(check.BoundSI) / From(check.DenominatorSI);
            Assert.Equal(ratio.CompareTo(tau) <= 0, check.Passed);
            worstGate = Max(worstGate, ratio);
        }
        void CheckField(double x, FieldSide side)
        {
            var actual = fields.EvaluateAt(x, side);
            var exact = oracle.Field(x, side);
            for (int q = 0; q < 4; q++) CheckOutput(actual.Bending[q], exact[q], q);
        }
        void CheckOutput(VerifiedOutput output, SolverExactNumber exact, int q)
        {
            var error = (From(output.Value) - exact).Abs();
            Bounded(error, output.VerifiedOutputBound);
            var scale = q < 2 ? exactScales[q] : Max(exactScales[q], exact.Abs());
            worstError = Max(worstError, error / scale);
            if (delivered) Assert.True(From(output.VerifiedOutputBound).CompareTo(tau * scale) <= 0);
        }
    }

    [Fact]
    public void PowerOfTwoUnitChangePreservesNormalizedSystemFieldsBoundsAndGateBits()
    {
        var input = Case("mixed").Input;
        var changed = new MechanicalInput(Math.ScaleB(input.Length, 20), Math.ScaleB(input.EI, -10),
            input.Restraints.Select(r => r with { Position = Math.ScaleB(r.Position, 20) }),
            input.PointForces.Select(p => new TransversePointForce(Math.ScaleB(p.Position, 20), Math.ScaleB(p.Newtons, 5))),
            input.PointMoments.Select(p => new AppliedPointMoment(Math.ScaleB(p.Position, 20), Math.ScaleB(p.NewtonMeters, 25))),
            input.LineLoads.Select(q => new ConstantLineLoad(Math.ScaleB(q.Start, 20), Math.ScaleB(q.End, 20), Math.ScaleB(q.NewtonsPerMeter, -15))));
        var a = SolverKernel.Solve(input);
        var b = SolverKernel.Solve(changed);
        Assert.Equal(a.Quality.MaxRatios, b.Quality.MaxRatios);
        Assert.Equal(a.Quality.Mu, b.Quality.Mu);
        Assert.Equal(a.Fields.State, b.Fields.State);
        Assert.Equal(a.Fields.StateBound, b.Fields.StateBound);
        foreach (double x in new[] { 0, 0.125, 0.5, 1, 1.5, 2 })
            foreach (var side in new[] { FieldSide.Left, FieldSide.Right })
            {
                var original = a.EvaluateAt(x, side);
                var scaled = b.EvaluateAt(Math.ScaleB(x, 20), side);
                var originalNormalized = a.Fields.EvaluateNormalized(x, side);
                var scaledNormalized = b.Fields.EvaluateNormalized(Math.ScaleB(x, 20), side);
                Assert.Equal(originalNormalized.Values, scaledNormalized.Values);
                Assert.Equal(originalNormalized.Bounds, scaledNormalized.Bounds);
                int[] shifts = [75, 55, 25, 5];
                for (int q = 0; q < 4; q++)
                {
                    Assert.Equal(BitConverter.DoubleToUInt64Bits(Math.ScaleB(original.Bending[q].Value, shifts[q])),
                        BitConverter.DoubleToUInt64Bits(scaled.Bending[q].Value));
                }
            }
        SolverBendingOutputTests.CheckExactOutputs(FromCompatibility(changed));
    }

    public static TheoryData<double, int> CadCases => new(
        from length in new[] { 0.2, 0.3, 0.5, 1d, 2, 5, 10 }
        from kind in Enumerable.Range(0, 5)
        select (length, kind));

    [Theory]
    [MemberData(nameof(CadCases))]
    public void HistoricalCadSixNanometerCasesMeetExactOutputContract(double length, int kind)
    {
        // compare.py:cad(), on the original stored coordinates and stiffness scale.
        const double x1 = 0.10054, x2 = 0.100540006;
        RigidRestraint[] supports = kind switch
        {
            0 => [new(0, U | W), new(length, W)],
            1 => [new(0, U | W | Theta)],
            2 => [new(0, U | W | Theta), new(length, U | W | Theta)],
            _ => [new(x1, U | W), new(length, W)]
        };
        var input = Model(length, 210e9 * (8e-6 * Math.Pow(length, 4) / 256), supports,
            kind < 3 ? [new(x1, -5000), new(x2, -3000)] :
                kind == 3 ? [new(x2, -5000), new((x1 + length) / 2, -3000)] : [new(x2, -5000)],
            lines: kind == 4 ? [new(x1, length, -2000)] : []);
        SolverBendingOutputTests.CheckExactOutputs(FromCompatibility(input));
    }

    [Theory]
    [InlineData("closure-six-nm")]
    [InlineData("global-distance")]
    [InlineData("adjacent-loaded-events")]
    [InlineData("overlap")]
    public void MigrationRegressionsMeetExactOutputContract(string name)
    {
        MechanicalInput input = name switch
        {
            "closure-six-nm" => Model(4, 210e9 * 8e-6,
                [new(0, U | W), new(2, W), new(2 + 6e-9, W), new(4, W)],
                [new(2 + 3e-9, -1e4), new(3.5, -1)], lines: [new(0, 4, -1)]),
            "global-distance" => Model(0.02, 210e9 * (8e-6 * 1e-8),
                [new(0, U | W), new(0.00384, W), new(0.01345, W), new(0.02, W)],
                [new(0.0132539, -1e3)], lines: [new(0, 0.02, -1e2)]),
            "adjacent-loaded-events" => Model(forces: [new(1, -3), new(Math.BitIncrement(1), 5)],
                moments: [new(Math.BitIncrement(Math.BitIncrement(1)), 7)]),
            _ => Model(lines: [new(0.25, 1.25, -4), new(0.75, 1.75, 3)])
        };
        SolverBendingOutputTests.CheckExactOutputs(FromCompatibility(input));
    }

    [Theory]
    [InlineData("end-force")]
    [InlineData("indeterminate")]
    [InlineData("coincident")]
    [InlineData("right-fixed")]
    public void GlobalCompatibilityOracleAgreesExactlyWithFrozenStatics(string name)
    {
        var frozen = Case(name);
        var solved = FromCompatibility(frozen.Input);
        Assert.Equal(0, solved.W0.CompareTo(frozen.W0));
        Assert.Equal(0, solved.Theta0.CompareTo(frozen.Theta0));
        foreach (var reaction in frozen.Reactions)
            Assert.Equal(0, reaction.Value.CompareTo(Assert.Single(solved.Reactions,
                r => r.X == reaction.X && r.Direction == reaction.Direction).Value));
    }
}
