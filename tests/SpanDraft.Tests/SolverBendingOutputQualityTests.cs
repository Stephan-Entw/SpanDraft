using System.Collections.Immutable;
using SpanDraft.Solver.Kernel;
using Xunit;
using static SpanDraft.Tests.SolverExactNumber;
using static SpanDraft.Tests.SolverExactLinearAlgebra;
using static SpanDraft.Tests.SolverBendingTestSupport;
using static SpanDraft.Tests.SolverOutputOracle;

namespace SpanDraft.Tests;

public sealed class SolverBendingOutputQualityTests
{
    public static TheoryData<double, double, int> SiCases => new()
    {
        { 0.75, Math.ScaleB(1, -50), 100 }, { -0.75, Math.ScaleB(1, -50), -100 },
        { double.Epsilon, 0, 1000 }, { -double.Epsilon, 0, 0 },
        { Binary64.Lambda, 0, 1000 }, { 1, 0, -1023 }, { -1, 0, -1075 },
        { 1, 0, -1022 }, { 0, double.Epsilon, 0 }, { 1, Binary64.Lambda, -500 },
        { 1, 0, -969 }, { 1, 0, 1000 }
    };

    [Theory]
    [MemberData(nameof(SiCases))]
    public void StageFBackScalingEnclosesExactBinaryValueAndBothUnderflowLosses(double v, double b, int exponent)
    {
        VerifiedOutput output = BendingFields.ToSi(v, b, exponent);
        SolverExactNumber exact = From(v) * PowerOfTwo(exponent);
        Bounded(From(output.Value) - exact, output.VerifiedOutputBound);
        AssertStored(output.VerifiedOutputBound);
        Assert.True(From(output.VerifiedOutputBound).CompareTo(From(b) * PowerOfTwo(exponent)) >= 0);
        if (double.IsSubnormal(v))
        {
            Assert.Equal(0UL, BitConverter.DoubleToUInt64Bits(output.Value));
            Assert.True(From(output.VerifiedOutputBound).CompareTo(From(Binary64.Lambda) * PowerOfTwo(exponent)) >= 0);
        }
        if (Math.Abs(output.Value) <= Binary64.Lambda)
            Assert.True(output.VerifiedOutputBound >= Binary64.Lambda);
        if (v == Binary64.Lambda && exponent == 1000)
        {
            Assert.Equal(Math.ScaleB(1, -22), output.Value);
            Assert.Equal(0, output.VerifiedOutputBound);
        }
    }

    [Fact]
    public void SiRangeBoundariesAreInclusiveAndNotClamped()
    {
        Assert.Equal((BendingOutputQuality.SiMin, BendingOutputQuality.SiMin), BendingOutputQuality.ScaleRange(1, 1, -969));
        Assert.Equal((BendingOutputQuality.SiMax, BendingOutputQuality.SiMax), BendingOutputQuality.ScaleRange(1, 1, 1000));
        foreach (var item in new[] { (1d, 1d, -970), (1d, Math.BitIncrement(1d), 1000), (1d, 1d, 1024) })
            Assert.Equal(KernelFailureCause.NumericalRange,
                Assert.Throws<NumericalRangeException>(() => BendingOutputQuality.ScaleRange(item.Item1, item.Item2, item.Item3)).Cause);
        BendingOutputQuality.CheckMagnitude(1, 1000);
        Assert.Throws<NumericalRangeException>(() => BendingOutputQuality.CheckMagnitude(Math.BitIncrement(1), 1000));
        Assert.Throws<NumericalRangeException>(() => BendingFields.ToSi(1, 0, 1024));
        Assert.Throws<NumericalRangeException>(() => BendingFields.ToSi(0, 1, 1024));
        foreach (double v in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            Assert.Equal(KernelFailureCause.NonFinite, Assert.Throws<KernelFailureException>(() => BendingFields.ToSi(v, 0, 0)).Cause);
            Assert.Equal(KernelFailureCause.NonFinite, Assert.Throws<KernelFailureException>(() => BendingFields.ToSi(1, v, 0)).Cause);
        }
        Assert.Equal(default, BendingFields.ToSi(double.NaN, double.NaN, 1024, unloaded: true));
    }

    [Fact]
    public void GateUsesDirectedSiBudgetAndExactThresholdInsteadOfRoundedTauProduct()
    {
        // Find a fixed binary boundary whose rounded division lies above exact D/10^8.
        double denominator = 1;
        while (From(denominator / Binary64.TauDenominator).CompareTo(From(denominator) / Q(Binary64.TauDenominator)) <= 0)
            denominator = Math.BitIncrement(denominator);
        double rounded = denominator / Binary64.TauDenominator;
        Assert.True(Binary64.ThresholdDown(denominator) < rounded);
        foreach (double bound in new[] { 0d, rounded, Math.BitDecrement(rounded),
            Math.BitDecrement(Math.BitDecrement(rounded)), Math.BitDecrement(Math.BitDecrement(Math.BitDecrement(rounded))), Math.ScaleB(1, -30) })
            foreach (int exponent in new[] { -900, 0, 900 })
            {
                OutputGateCheck check = BendingOutputQuality.Check(bound, denominator, exponent);
                SolverExactNumber d = From(denominator) * PowerOfTwo(exponent);
                SolverExactNumber requiredBudget = (From(bound) + From(Binary64.Lambda)) * PowerOfTwo(exponent) + From(Binary64.Lambda);
                Assert.True(From(check.BoundSI).CompareTo(requiredBudget) >= 0);
                Assert.True(From(check.DenominatorSI).CompareTo(d) <= 0);
                Assert.Equal(check.BoundSI <= Binary64.ThresholdDown(check.DenominatorSI), check.Passed);
                if (check.Passed)
                    Assert.True(From(check.BoundSI).CompareTo(d / Q(Binary64.TauDenominator)) <= 0);
            }
        Assert.False(BendingOutputQuality.Check(rounded, denominator, 0).Passed);
    }

    [Fact]
    public void FixedVerifiedLowLevelSystemFailsOnlyTheOutputGate()
    {
        BendingSystem system = SolverKernel.BuildSystem(Model(length: 1, forces: [new(1, 1)]));
        // A fixed conservative data-count fixture: verification succeeds, but its certified
        // output accuracy is insufficient. No production switch changes any bound.
        system = system with { K = system.K.Select(k => k == 0 ? 0 : 1_000_000_000L).ToImmutableArray() };
        VerifiedBendingState state = BendingStateSolver.Solve(system, out var context);
        Assert.True(state.IsVerified && state.SelectedMu < 1);
        BendingFields fields = BendingFields.From(state);
        var reactions = BendingReactions.Calculate(state, context);
        Assert.Equal(KernelFailureCause.Gate, Assert.Throws<KernelFailureException>(() =>
            BendingOutputQuality.Evaluate(state, fields, system.EventStations, reactions, context)).Cause);
    }

    [Fact]
    public void ReferenceScalesUseEveryPhysicalEventSideAndActualNormalizedLength()
    {
        var oracle = Case("mixed");
        BendingSystem system = SolverKernel.BuildSystem(oracle.Input);
        VerifiedBendingState state = BendingStateSolver.Solve(system);
        BendingFields fields = BendingFields.From(state);
        var stations = system.EventStations.SelectMany(e => BendingOutputQuality.PhysicalSides(e.Position, oracle.Input.Length)
            .Select(side => fields.EvaluateNormalized(e.Position, side))).ToArray();
        var (low, high) = BendingOutputQuality.Scales(fields, system.Excitation, stations);
        SolverExactNumber[] exact = oracle.Scales(system.EventStations.Select(e => e.Position));
        for (int q = 0; q < 4; q++)
            AssertEncloses(low[q], exact[q] * PowerOfTwo(-fields.Exponents[q]), high[q]);
        // Sentinel values deliberately occur only on a nonmechanical event's left side.
        var sentinel = new NormalizedBendingField([31, 29, 23, 19], [0, 0, 0, 0]);
        (low, high) = BendingOutputQuality.Scales(fields, system.Excitation, [sentinel]);
        Assert.True(low[0] > 30 && low[1] > 28 && low[2] > 22);
        var longer = fields with { Stations = [new(0, 0), new(1.5, 0)], EL = 0, EI = new(1) };
        (low, high) = BendingOutputQuality.Scales(longer, new(0, 1, 1), []);
        AssertEncloses(low[2], Q(3, 2), high[2]);
        AssertEncloses(low[1], Q(9, 4), high[1]);
        AssertEncloses(low[0], Q(27, 8), high[0]);
    }

    [Fact]
    public void PieceGateLowerBoundsAndMovementBoundsEncloseIndependentQuadratic()
    {
        var input = Model(length: 1, lines: [new(0, 1, 2), new(0, 1, -1)]);
        BendingFields fields = BendingFields.From(BendingStateSolver.Solve(SolverKernel.BuildSystem(input)));
        fields = fields with { Loads = new([new([], [], [new(0, 1, 2), new(0, 1, -1)], 0)], []) };
        // Exact fixture V=10+x, M=20+10x+x²/2, theta=30+20x+5x²+x³/6.
        var a = new NormalizedBendingField([40, 30, 20, 10], [0.01, 0.02, 0.03, 0.04]);
        var b = new NormalizedBendingField([1961d / 24, 331d / 6, 30.5, 11], [0.02, 0.01, 0.04, 0.03]);
        PieceQuality piece = BendingOutputQuality.Piece(fields, 0, 1, a, b, [1, 1, 1, 1]);
        Assert.Equal(new[] { 0.02, 0.02, 0.04, 0.04 }, piece.Bounds);
        Assert.True(BendingOutputQuality.QSum(fields, 0, 1) >= 3);
        Assert.InRange(piece.Denominators[3], 7, 10);
        SolverExactNumber exactVLower = Q(11) - Q(3) - Q(2) * From(0.04);
        Assert.True(From(piece.Denominators[3]).CompareTo(exactVLower) <= 0);
        Assert.InRange(piece.Denominators[2], 17, 20);
        SolverExactNumber exactLower = From(30.5) - (Q(11) + From(0.04)) - Q(3, 2) - Q(2) * From(0.04);
        Assert.True(From(piece.Denominators[2]).CompareTo(exactLower) <= 0);
        // Exact Bernstein coefficients: min|V|>=10, min|M|>=20 on [0,1].
        SolverExactNumber[] bernsteinV = [Q(10), Q(11)], bernsteinM = [Q(20), Q(25), Q(61, 2)];
        Assert.All(bernsteinV, c => Assert.True(From(piece.Denominators[3]).CompareTo(c) <= 0));
        Assert.All(bernsteinM, c => Assert.True(From(piece.Denominators[2]).CompareTo(c) <= 0));
        Assert.True(piece.UpperValues[3] >= 13);
        Assert.True(piece.UpperValues[2] >= 33);
        Assert.True(piece.UpperValues[1] >= 63);
        Assert.True(piece.UpperValues[0] >= 103);
    }

    [Fact]
    public void ZeroMovementLowerBoundsRetainEveryRequiredDownwardRounding()
    {
        BendingFields fields = BendingFields.From(BendingStateSolver.Solve(SolverKernel.BuildSystem(
            Model(length: 1, forces: [new(1, 1)]))));
        var shear = new NormalizedBendingField([0, 0, 0, 1], [0, 0, 0, 0]);
        PieceQuality v = BendingOutputQuality.Piece(fields, 0, 1, shear, shear, [0.5, 0.5, 0.5, 0.5]);
        // Two directed subtractions from 1, each with a proven zero movement/error term.
        Assert.Equal(Math.BitDecrement(Math.BitDecrement(1)), v.Denominators[3]);
        var moment = new NormalizedBendingField([0, 0, 1, 0], [0, 0, 0, 0]);
        PieceQuality m = BendingOutputQuality.Piece(fields, 0, 1, moment, moment, [0.5, 0.5, 0.5, 0.5]);
        // Two edge subtractions and the final 2B subtraction, all directed down.
        Assert.Equal(Math.BitDecrement(Math.BitDecrement(Math.BitDecrement(1))), m.Denominators[2]);
    }
}
