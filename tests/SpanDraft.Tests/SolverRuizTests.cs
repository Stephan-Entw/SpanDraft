using SpanDraft.Solver.Kernel;
using Xunit;
using static SpanDraft.Tests.SolverExactNumber;

namespace SpanDraft.Tests;

public sealed class SolverRuizTests
{
    [Fact]
    public void CorrectionMatchesIndependentIntegerPowerThresholdsIncludingNegativeTies()
    {
        foreach (int exponent in Enumerable.Range(-16, 33).Concat([-1000, 1000]))
            foreach (double mantissa in new[] { 1d, Math.BitIncrement(1), 1.5, Math.BitDecrement(2) })
                foreach (int shift in new[] { -9, 0, 8 })
                {
                    double a = Math.ScaleB(mantissa, exponent);
                    SolverExactNumber maximum = From(a) * PowerOfTwo(shift);
                    int expected = Enumerable.Range(-1100, 2201).First(c =>
                    {
                        int comparison = maximum.CompareTo(PowerOfTwo(2 * c + 1));
                        return comparison < 0 || comparison == 0 && (c & 1) == 0;
                    });
                    Assert.Equal(expected, EquilibratedSystem.Correction([(a, shift), (-a, shift), (0, int.MaxValue)]));
                }
        Assert.Equal(0, EquilibratedSystem.Correction([]));
        Assert.Equal(0, EquilibratedSystem.Correction([(0, 0)]));
        Assert.Equal(1, EquilibratedSystem.Correction([(2, 0), (3, 0)]));
    }

    [Fact]
    public void ExactlyEightRowThenColumnPassesMatchFixedReferenceAndNotSevenOrNine()
    {
        int[] exponents = [287, -339, -181, 1, -225, -452, 26, -533,
            262, 552, 665, 260, -533, 254, -454, 93];
        double[] a = exponents.Select(e => Math.ScaleB(1, e)).ToArray();
        EquilibratedSystem scaled = EquilibratedSystem.Create(4, a, new long[16], new double[4], new double[4]);
        Assert.Equal(new[] { -190, 191, -444, -133 }, scaled.RS);
        Assert.Equal(new[] { -96, -122, -220, 39 }, scaled.CS);
        Assert.NotEqual(189, scaled.RS[1]); // Seven passes.
        Assert.NotEqual(193, scaled.RS[1]); // Nine passes.
        for (int i = 0; i < 4; i++)
            for (int j = 0; j < 4; j++)
                Assert.Equal(Math.ScaleB(1, exponents[4 * i + j] + scaled.RS[i] + scaled.CS[j]), scaled.S[4 * i + j]);
        Assert.All(scaled.Rows, indices => Assert.Equal(new[] { 0, 1, 2, 3 }, indices));
        Assert.All(scaled.Columns, indices => Assert.Equal(new[] { 0, 1, 2, 3 }, indices));
    }

    [Fact]
    public void ZeroRowsAndSparseIndicesAreDeterministic()
    {
        EquilibratedSystem scaled = EquilibratedSystem.Create(3, [0, 0, 0, 0, 8, 0, 2, 0, 4],
            new long[9], [0, 1, 0], [0, 0, 0]);
        Assert.Empty(scaled.Rows[0]);
        Assert.Equal(0, scaled.RS[0]);
        Assert.Equal(new[] { 0, 2 }, scaled.Rows[2]);
        Assert.Equal(new[] { 2 }, scaled.Columns[0]);
        Assert.Equal(0UL, BitConverter.DoubleToUInt64Bits(scaled.S[0]));
    }

    [Fact]
    public void SubnormalRhsIsAllowedAndItsLossIsBudgeted()
    {
        double tiny = Math.BitDecrement(Binary64.Lambda);
        EquilibratedSystem scaled = EquilibratedSystem.Create(1, [1], [0], [tiny], [0]);
        Assert.Equal(BitConverter.DoubleToUInt64Bits(tiny), BitConverter.DoubleToUInt64Bits(scaled.SB[0]));
        Assert.True(scaled.DSB[0] >= Binary64.Lambda);
        SolverExactLinearAlgebra.Bounded(From(tiny), scaled.DSB[0]);
    }

    [Fact]
    public void RhsAtLambdaAndRhsRoundedToZeroBothReceiveTheAdditionalLossBudget()
    {
        EquilibratedSystem boundary = EquilibratedSystem.Create(1, [1], [0], [Binary64.Lambda], [0]);
        Assert.Equal(Binary64.Lambda, boundary.SB[0]);
        Assert.Equal(Math.BitIncrement(Binary64.Lambda), boundary.DSB[0]);
        EquilibratedSystem underflow = EquilibratedSystem.Create(1, [Math.ScaleB(1, 1000)], [0], [Binary64.Lambda], [0]);
        Assert.Equal(0d, underflow.SB[0]);
        Assert.Equal(Math.BitIncrement(Binary64.Lambda), underflow.DSB[0]);
        EquilibratedSystem exactZero = EquilibratedSystem.Create(1, [Math.ScaleB(1, 1000)], [0], [0], [0]);
        Assert.Equal(0d, exactZero.DSB[0]);
    }

    [Fact]
    public void MatrixUnderflowAndExponentOverflowAreRangeFailures()
    {
        Assert.Throws<NumericalRangeException>(() => EquilibratedSystem.Create(2,
            [1, Binary64.Lambda, 0, 1], [0, 0, 0, 0], [0, 0], [0, 0]));
        Assert.Throws<NumericalRangeException>(() => EquilibratedSystem.Create(2,
            [double.MaxValue, Binary64.Lambda, 0, double.MaxValue], [0, 0, 0, 0], [0, 0], [0, 0]));
        Assert.Throws<NumericalRangeException>(() => EquilibratedSystem.Create(1,
            [Math.ScaleB(1, -1000)], [0], [double.MaxValue], [0]));
        Assert.Throws<OverflowException>(() => EquilibratedSystem.Correction([(2, int.MaxValue)]));
    }
}
