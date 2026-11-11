using System.Collections.Immutable;
using SpanDraft.Solver.Kernel;
using Xunit;
using static SpanDraft.Solver.Kernel.RestrainedDirections;
using static SpanDraft.Tests.SolverBendingTestSupport;
using static SpanDraft.Tests.SolverExactNumber;
using static SpanDraft.Tests.SolverExactLinearAlgebra;

namespace SpanDraft.Tests;

public sealed class SolverVerifiedBendingStateTests
{
    public static TheoryData<string> Cases => new(SolverBendingSystemTests.SystemCases.Select(row => row.Data)
        .Concat(["graded", "compensated", "scale-extreme"]));

    private static MechanicalInput Input(string name) => name switch
    {
        "graded" => Model(restraints: [new(0, U | W), new(Math.ScaleB(1, -200), W), new(2, W)], forces: [new(1, -3)]),
        "compensated" => Model(forces: [new(1, 3), new(1, -3)]),
        "scale-extreme" => Model(length: Math.ScaleB(1, 200), ei: Math.ScaleB(1.25, -200),
            forces: [new(Math.ScaleB(1, 199), Math.ScaleB(1, -300))]),
        _ => SolverBendingSystemTests.Case(name)
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void StagesBThroughDEncloseExactMechanicsAndFullNormalizedState(string name)
    {
        MechanicalInput input = Input(name);
        BendingSystem system = SolverKernel.BuildSystem(input);
        VerifiedBendingState result = SolverKernel.SolveSystem(system);
        Assert.True(result.IsVerified);
        Assert.Same(system, result.System);
        Assert.Equal(system.IsUnloaded, result.IsUnloaded);
        Assert.Equal(system.Dimension, result.Y.Length);
        Assert.Equal(system.Stations.Length * 4, result.FullState.Length);
        if (system.IsUnloaded)
        {
            CheckUnloaded(result);
            return;
        }

        int n = system.Dimension;
        var (a, b) = SolverBendingSystemTests.CheckExactMechanics(input, system);
        var s = new SolverExactNumber[n, n];
        var sb = new SolverExactNumber[n];
        for (int i = 0; i < n; i++)
        {
            sb[i] = Reduce(b[i] * PowerOfTwo(result.RS[i]));
            Bounded(From(result.SB[i]) - sb[i], result.DSB[i]);
            for (int j = 0; j < n; j++)
            {
                s[i, j] = Reduce(a[i, j] * PowerOfTwo(result.RS[i] + result.CS[j]));
                Bounded(From(result.S[i * n + j]) - s[i, j], result.DS[i * n + j]);
            }
            SolverExactNumber residual = sb[i] - Dot(Enumerable.Range(0, n).Select(j => (s[i, j], From(result.Y[j]))));
            Bounded(residual, result.D[i]);
            Bounded(From(result.Residual[i]) - (From(result.SB[i]) - Dot(Enumerable.Range(0, n)
                .Select(j => (From(result.S[i * n + j]), From(result.Y[j]))))), result.ResidualRoundingBound[i]);
        }
        SolverExactNumber[] exactY = Solve(s, sb);
        for (int i = 0; i < n; i++)
        {
            Bounded(From(result.Y[i]) - exactY[i], result.VerifiedStateBound[i]);
            Assert.True(result.VerifiedStateBound[i] >= result.RawBound[i]);
            FreeBendingComponent c = system.FreeComponents[i];
            int k = c.StationIndex * 4 + (int)c.Component;
            Bounded(From(result.FullState[k]) - exactY[i] * PowerOfTwo(result.CS[i]), result.FullStateBound[k]);
        }
        for (int k = 0; k < system.ReducedColumns.Length; k++)
            if (system.ReducedColumns[k] < 0)
            {
                Assert.Equal(0UL, BitConverter.DoubleToUInt64Bits(result.FullState[k]));
                Assert.Equal(0d, result.FullStateBound[k]);
                Assert.Equal(0d, result.StateScaleError[k]);
            }
        Assert.InRange(result.PrimaryIterations, 1, 5);
        Assert.Equal(n, result.AdjointSolves);
        Assert.InRange(result.AdjointIterations, n, 5 * n);
        Assert.InRange(result.MaxAdjointIterations, 1, 5);
        Assert.Equal(4, result.TestedMus.Length);
        Assert.True(result.SelectedMu is >= 0 and < 1);
        Assert.All(result.Y.Concat(result.FullState), value => Assert.True(value == 0 || double.IsNormal(value)));
        Assert.All(result.VerifiedStateBound.Concat(result.FullStateBound).Concat(result.RawBound), value =>
        {
            Assert.True(double.IsFinite(value) && value >= 0);
            AssertStored(value);
        });
    }

    [Fact]
    public void CantileverTipForceMatchesIndependentClosedFormWithoutSiOutputPath()
    {
        BendingSystem system = SolverKernel.BuildSystem(Model(length: 1, ei: 1, forces: [new(1, 1)]));
        VerifiedBendingState result = SolverKernel.SolveSystem(system);
        SolverExactNumber[] exactFull = [Integer(0), Integer(0), Integer(1), Integer(-1),
            Integer(1) / Integer(3), Integer(1) / Integer(2), Integer(0), Integer(0)];
        for (int i = 0; i < exactFull.Length; i++)
            Bounded(From(result.FullState[i]) - exactFull[i], result.FullStateBound[i]);
    }

    [Fact]
    public void LoadedZeroRhsStillExecutesFullVerification()
    {
        foreach (MechanicalInput input in new[]
        {
            Input("compensated"),
            Model(restraints: [new(0, U | W), new(1, W), new(2, W)], forces: [new(1, 3)])
        })
        {
            BendingSystem system = SolverKernel.BuildSystem(input);
            Assert.False(system.IsUnloaded);
            Assert.All(system.B, value => Assert.Equal(0d, value));
            VerifiedBendingState state = SolverKernel.SolveSystem(system);
            Assert.False(state.IsUnloaded);
            Assert.InRange(state.PrimaryIterations, 1, 5);
            Assert.Equal(system.Dimension, state.AdjointSolves);
            Assert.NotEmpty(state.TestedMus);
        }
    }

    [Fact]
    public void UnloadedDoesNotReadSkippedNumericalStageData()
    {
        BendingSystem system = SolverKernel.BuildSystem(Input("signed-zero"));
        // A poisoned numerical payload makes accidental execution of Ruiz/LU observable.
        system = system with { A = Enumerable.Repeat(double.NaN, system.A.Length).ToImmutableArray() };
        CheckUnloaded(SolverKernel.SolveSystem(system));
    }

    [Fact]
    public void WeightsFollowFpcReferenceScalesAndUseUnflushedRightSideStates()
    {
        BendingSystem system = SolverKernel.BuildSystem(Model(ei: 1.25,
            restraints: [new(0, U | W | Theta), new(1, W)], forces: [new(2, 1)]));
        double[] x = [2, 3, 5, 7, 11, 13, 17, 19, 1e6, 1e6, 1e6, 1e6];
        int[] cs = Enumerable.Range(0, system.Dimension).Select(i => i - 3).ToArray();
        double[] expected = [17 / 1.25, 17 / 1.25, 17, 17];
        double[] weights = BendingStateSolver.Weights(system, x, cs);
        for (int i = 0; i < weights.Length; i++)
            Assert.Equal(Math.ScaleB(expected[(int)system.FreeComponents[i].Component], -cs[i]), weights[i]);

        x[0] = 31;
        x[5] = 29;
        weights = BendingStateSolver.Weights(system, x, cs);
        expected = [31, 29, 17, 17];
        for (int i = 0; i < weights.Length; i++)
            Assert.Equal(Math.ScaleB(expected[(int)system.FreeComponents[i].Component], -cs[i]), weights[i]);
        x[0] = 2;
        x[5] = 13;

        system = system with { Excitation = new(0, 31, 31) };
        weights = BendingStateSolver.Weights(system, x, cs);
        expected = [31 / 1.25, 31 / 1.25, 31, 31];
        for (int i = 0; i < weights.Length; i++)
            Assert.Equal(Math.ScaleB(expected[(int)system.FreeComponents[i].Component], -cs[i]), weights[i]);

        system = system with { Excitation = default, NormalizedEI = new(1.25) };
        x = new double[system.ReducedColumns.Length];
        x[0] = Math.BitDecrement(Binary64.Lambda);
        cs = Enumerable.Repeat(-100, system.Dimension).ToArray();
        weights = BendingStateSolver.Weights(system, x, cs);
        int w = system.FreeComponents.IndexOf(new(2, BendingComponent.W));
        Assert.Equal(Math.ScaleB(x[0], 100), weights[w]);
        Assert.Equal(BitConverter.DoubleToUInt64Bits(Math.BitDecrement(Binary64.Lambda)), BitConverter.DoubleToUInt64Bits(x[0]));
    }

    [Fact]
    public void StateBackScalingBudgetsLossAtAndBelowLambdaThenFlushes()
    {
        BendingSystem system = SolverKernel.BuildSystem(Model());
        double[] y = Enumerable.Repeat(1d, system.Dimension).ToArray();
        int[] cs = Enumerable.Repeat(-1023, system.Dimension).ToArray();
        cs[0] = -1022;
        double[] x = BendingStateSolver.UnscaleState(system, cs, y);
        Assert.Contains(x, double.IsSubnormal);
        double[] stateBound = Enumerable.Repeat(Math.ScaleB(1, -52), system.Dimension).ToArray();
        var (bound, loss) = BendingStateSolver.UnscaleBounds(system, cs, y, x, stateBound);
        for (int i = 0; i < system.Dimension; i++)
        {
            FreeBendingComponent c = system.FreeComponents[i];
            int k = 4 * c.StationIndex + (int)c.Component;
            Assert.Equal(Binary64.Lambda, loss[k]);
            Assert.Equal(i == 0 ? Binary64.Lambda : 0, x[k]);
            Bounded(PowerOfTwo(cs[i]) - From(x[k]), bound[k]);
            Assert.True(bound[k] >= 2 * Binary64.Lambda);
        }
        Assert.Throws<NumericalRangeException>(() => BendingStateSolver.UnscaleState(system,
            Enumerable.Repeat(1024, system.Dimension).ToArray(), y));
        Assert.Throws<NumericalRangeException>(() => BendingStateSolver.UnscaleBounds(system,
            Enumerable.Repeat(1024, system.Dimension).ToArray(), y, new double[x.Length], y));
    }

    [Fact]
    public void InconsistentSystemDimensionsHaveStructuredInternalCause()
    {
        BendingSystem system = SolverKernel.BuildSystem(Model());
        Assert.Equal(KernelFailureCause.InternalInconsistency, Assert.Throws<KernelFailureException>(() =>
            SolverKernel.SolveSystem(system with { Dimension = system.Dimension + 1 })).Cause);
    }

    private static void CheckUnloaded(VerifiedBendingState state)
    {
        Assert.True(state.IsUnloaded && state.IsVerified);
        Assert.Equal(state.System.Dimension, state.Y.Length);
        Assert.Equal(state.System.Dimension, state.RawBound.Length);
        Assert.Equal(state.System.Dimension, state.VerifiedStateBound.Length);
        Assert.Equal(state.System.ReducedColumns.Length, state.FullStateBound.Length);
        Assert.Equal(state.System.ReducedColumns.Length, state.StateScaleError.Length);
        Assert.All(state.Y.Concat(state.RawBound).Concat(state.VerifiedStateBound).Concat(state.FullState)
            .Concat(state.FullStateBound).Concat(state.StateScaleError), v => Assert.Equal(0UL, BitConverter.DoubleToUInt64Bits(v)));
        Assert.Empty(state.RS);
        Assert.Empty(state.CS);
        Assert.Empty(state.S);
        Assert.Empty(state.DS);
        Assert.Empty(state.SB);
        Assert.Empty(state.DSB);
        Assert.Empty(state.Residual);
        Assert.Empty(state.ResidualRoundingBound);
        Assert.Empty(state.D);
        Assert.Empty(state.TestedMus);
        Assert.Null(state.SelectedMu);
        Assert.Equal(0, state.PrimaryIterations);
        Assert.Equal(0, state.AdjointSolves);
        Assert.Equal(0, state.AdjointIterations);
        Assert.Equal(0, state.MaxAdjointIterations);
    }
}
