using System.Reflection;
using SpanDraft.Solver.Kernel;
using Xunit;
using static SpanDraft.Tests.SolverExactNumber;

namespace SpanDraft.Tests;

public sealed class SolverDenseLuTests
{
    public static TheoryData<double[], double[]> Matrices => new()
    {
        { [3], [7] },
        { [0, 2, 1, 3], [1, -7] },
        { [0, 2, 3, 4, 5, 6, 7, 8, 10], [1, -3, 7] },
        { [1, 2, 3, -1, 7, 2, 1, 3, 9], [2, -1, 5] },
        { [1, Math.ScaleB(1, -40), 0, 3], [1, -2] }
    };

    [Theory]
    [MemberData(nameof(Matrices))]
    public void BothSolvesAgreeWithIndependentRationalSolutions(double[] matrix, double[] rhs)
    {
        var lu = new DenseLu(rhs.Length, matrix);
        foreach (bool transpose in new[] { false, true })
        {
            double[] solution = transpose ? lu.SolveTranspose(rhs) : lu.Solve(rhs);
            SolverExactNumber[] exact = SolverExactLinearAlgebra.Solve(SolverExactLinearAlgebra.Matrix(matrix, transpose),
                rhs.Select(From).ToArray());
            for (int i = 0; i < rhs.Length; i++)
            {
                // Small fixed systems: a conservative operation-count tolerance for the candidate alone.
                SolverExactNumber tolerance = PowerOfTwo(-45) * (Integer(1) + exact[i].Abs());
                Assert.True((From(solution[i]) - exact[i]).Abs().CompareTo(tolerance) <= 0);
            }
        }
    }

    [Fact]
    public void PivotTiesKeepTheFirstMaximumAndTransposeUsesInversePermutation()
    {
        var tied = new DenseLu(3, [1, 2, 0, -1, 3, 1, 1, 1, 4]);
        int[] permutation = (int[])typeof(DenseLu).GetField("piv", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(tied)!;
        Assert.Equal(0, permutation[0]);
        double[] matrix = [0, 0, 2, 3, 0, 0, 0, 5, 0];
        var cycled = new DenseLu(3, matrix);
        Assert.Equal(new double[] { 3, 1, 2 }, cycled.SolveTranspose([3, 10, 6]));
        Assert.Equal(new double[] { 1, 2, 3 }, cycled.Solve([6, 3, 10]));
    }

    [Fact]
    public void ZeroTinyAndNonfinitePivotsHaveDifferentCausesWithoutFallback()
    {
        Assert.Equal(KernelFailureCause.ZeroPivot, Assert.Throws<KernelFailureException>(() =>
            new DenseLu(2, [1, 2, 2, 4])).Cause);
        foreach (double pivot in new[] { Binary64.Lambda, -Binary64.Lambda, double.Epsilon })
            Assert.Equal(KernelFailureCause.NumericalRange, Assert.Throws<NumericalRangeException>(() => new DenseLu(1, [pivot])).Cause);
        Assert.Equal(1d, new DenseLu(1, [Math.BitIncrement(Binary64.Lambda)]).Solve([Math.BitIncrement(Binary64.Lambda)])[0]);
        foreach (double value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            Assert.Equal(KernelFailureCause.NonFinite, Assert.Throws<KernelFailureException>(() => new DenseLu(1, [value])).Cause);
            var lu = new DenseLu(1, [1]);
            Assert.Equal(KernelFailureCause.NonFinite, Assert.Throws<KernelFailureException>(() => lu.Solve([value])).Cause);
            Assert.Equal(KernelFailureCause.NonFinite, Assert.Throws<KernelFailureException>(() => lu.SolveTranspose([value])).Cause);
        }
        Assert.Equal(KernelFailureCause.NonFinite, Assert.Throws<KernelFailureException>(() =>
            new DenseLu(2, [1, double.MaxValue, -1, double.MaxValue])).Cause);
        Assert.Equal(KernelFailureCause.NonFinite, Assert.Throws<KernelFailureException>(() =>
            new DenseLu(1, [0.5]).Solve([double.MaxValue])).Cause);
    }

    [Fact]
    public void MandatoryRefinementAndExactStopRulesArePreserved()
    {
        var scaled = new EquilibratedSystem(1, [0], [0], [1], [0], [3], [0]);
        var (y, iterations) = BendingStateSolver.Primary(scaled, new DenseLu(1, scaled.S));
        Assert.Equal(new[] { 3d }, y);
        Assert.Equal(1, iterations);
        Assert.True(BendingStateSolver.StopPrimary(double.NaN, 1, double.PositiveInfinity));
        Assert.False(BendingStateSolver.StopPrimary(double.PositiveInfinity, 1, double.PositiveInfinity));
        Assert.True(BendingStateSolver.StopPrimary(Math.ScaleB(1, -52), 1, 1));
        Assert.False(BendingStateSolver.StopPrimary(Math.BitIncrement(Math.ScaleB(1, -52)), 1, 1));
        Assert.False(BendingStateSolver.StopPrimary(0.5, 0, 1));
        Assert.True(BendingStateSolver.StopPrimary(Math.BitIncrement(0.5), 0, 1));
    }

    [Fact]
    public void StoredSolutionsFlushStrictlyBelowLambdaToPositiveZero()
    {
        double[] values = [double.Epsilon, -double.Epsilon, Math.BitDecrement(Binary64.Lambda),
            -Math.BitDecrement(Binary64.Lambda), -0d, Binary64.Lambda, -Binary64.Lambda];
        BendingStateSolver.FlushStored(values);
        Assert.All(values.Take(5), v => Assert.Equal(0UL, BitConverter.DoubleToUInt64Bits(v)));
        Assert.Equal(Binary64.Lambda, values[5]);
        Assert.Equal(-Binary64.Lambda, values[6]);
        var scaled = new EquilibratedSystem(1, [0], [0], [1], [0], [double.Epsilon], [Binary64.Lambda]);
        Assert.Equal(0UL, BitConverter.DoubleToUInt64Bits(BendingStateSolver.Primary(scaled, new DenseLu(1, [1])).Y[0]));
    }

    [Fact]
    public void SlowlyHalvingCorrectionsCannotExceedFiveSteps()
    {
        // Fixed cancellation case whose corrections keep halving beyond step five.
        // Only the contract limit is asserted; candidate roundings may vary by platform.
        double[] matrix = [9.434922932169331e-05, 4.976918209021285e-05, 136.20342500914796,
            255.22354281491846, -292.13033284778885, 29.397254713149493,
            255.2236447704021, -292.1305616758022, 165.60067972315304];
        var scaled = new EquilibratedSystem(3, [0, 0, 0], [0, 0, 0], matrix, new double[9],
            [-1.409608771163581, -0.5410813570412238, -1.713311550640784], [0, 0, 0]);
        var (_, iterations) = BendingStateSolver.Primary(scaled, new DenseLu(3, matrix));
        Assert.InRange(iterations, 1, 5);
    }
}
