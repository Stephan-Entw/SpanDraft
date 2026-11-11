using System.Reflection;
using System.Runtime.CompilerServices;
using SpanDraft.Solver.Kernel;
using Xunit;
using static SpanDraft.Tests.SolverExactNumber;
using static SpanDraft.Tests.SolverExactLinearAlgebra;

namespace SpanDraft.Tests;

public sealed class SolverCertifiedResidualTests
{
    public static TheoryData<double, (double, double)[]> BoundaryCases => new()
    {
        { -0d, [] },
        { Math.BitDecrement(Binary64.Lambda), [] },
        { Binary64.Lambda, [] },
        { 0, [(Binary64.Lambda, 0.75)] },
        { Binary64.Lambda, [(Binary64.Lambda, 0.75)] },
        { Binary64.Lambda, Enumerable.Repeat((Binary64.Lambda, 0.75), 4).ToArray() },
        { 1, [(1, 1)] },
        { 1, [(1, 1), (Math.ScaleB(1, -54), 1)] },
        { 2, Enumerable.Repeat((Math.BitIncrement(1), Math.BitIncrement(1)), 2).ToArray() },
        { Math.BitIncrement(Binary64.Lambda), [(Binary64.Lambda, 1)] },
        { -Math.BitDecrement(Binary64.Lambda), [] },
        { 0, [(0, double.MaxValue), (double.MaxValue, 0)] }
    };

    [Theory]
    [MemberData(nameof(BoundaryCases))]
    public void BoundaryOraclesEncloseExactResidualAndAllEmulatedUnderflowModes(double rhs, (double, double)[] products)
    {
        var (value, error) = CertifiedResidual.Evaluate(rhs, products);
        SolverExactNumber exact = Reduce(From(rhs) - Dot(products.Select(p => (From(p.Item1), From(p.Item2)))));
        AssertStored(error);
        Assert.False(double.IsSubnormal(value));
        Bounded(From(value) - exact, error);

        foreach (bool ftz in new[] { false, true })
            foreach (bool daz in new[] { false, true })
            {
                var (emulated, budget) = Emulate(rhs, products, ftz, daz);
                Assert.True((From(emulated) - exact).Abs().CompareTo(budget) <= 0);
                if (!ftz && !daz)
                {
                    Assert.Equal(BitConverter.DoubleToUInt64Bits(emulated), BitConverter.DoubleToUInt64Bits(value));
                    Assert.True(budget.CompareTo(From(error)) <= 0);
                }
            }
    }

    [Fact]
    public void FlushedProductSkipsSubtractionAndAccumulatorRounding()
    {
        var (value, error) = CertifiedResidual.Evaluate(Binary64.Lambda, [(Binary64.Lambda, 0.75)]);
        Assert.Equal(Binary64.Lambda, value);
        Assert.Equal(Math.BitIncrement(Binary64.Lambda), error);
        Assert.Equal((1d, 0d), CertifiedResidual.Evaluate(1, [(0, 2), (2, 0)]));
        Assert.Equal(Binary64.Lambda, CertifiedResidual.LocalRounding(Binary64.Lambda));
        Assert.Equal(Binary64.Lambda, CertifiedResidual.LocalRounding(0));
    }

    [Fact]
    public void ProductAndSubtractionMustRemainTwoMaterializedRoundings()
    {
        double operand = Math.BitIncrement(1);
        double rhs = 1 + Math.ScaleB(1, -51);
        var (value, error) = CertifiedResidual.Evaluate(rhs, [(operand, operand)]);
        Assert.Equal(0UL, BitConverter.DoubleToUInt64Bits(value));
        Assert.NotEqual(0d, Math.FusedMultiplyAdd(-operand, operand, rhs));
        Bounded(From(rhs) - From(operand) * From(operand), error);
        foreach (string name in new[] { "Product", "Difference" })
        {
            MethodInfo method = typeof(CertifiedResidual).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!;
            Assert.True(method.GetMethodImplementationFlags().HasFlag(MethodImplAttributes.NoInlining));
        }
    }

    [Fact]
    public void FixedA6MatrixCoversCancellationAndNormalToUnderflowProducts()
    {
        var random = new Random(6055);
        for (int test = 0; test < 128; test++)
        {
            var products = new (double, double)[1 + test % 8];
            for (int j = 0; j < products.Length; j++)
            {
                double a = Math.ScaleB((random.NextDouble() + 1) * (j % 2 == 0 ? -1 : 1), -random.Next(0, 61));
                double b = j % 5 == 0 ? 0 : Math.ScaleB(random.NextDouble() + 1,
                    j % 3 == 0 ? -random.Next(900, 1016) : -random.Next(0, 81));
                products[j] = (a, b);
            }
            double sum = products.Sum(p => p.Item1 * p.Item2);
            double rhs = test % 3 == 0 ? sum : test % 3 == 1 ? Math.BitIncrement(sum) : 1;
            var (value, error) = CertifiedResidual.Evaluate(rhs, products);
            Bounded(From(value) - (From(rhs) - Dot(products.Select(p => (From(p.Item1), From(p.Item2))))), error);
        }
    }

    [Fact]
    public void NonfiniteCertifiedIntermediatesAreNonfiniteFailures()
    {
        foreach (double value in new[] { double.NaN, double.PositiveInfinity })
            Assert.Equal(KernelFailureCause.NonFinite, Assert.Throws<KernelFailureException>(() =>
                CertifiedResidual.Evaluate(value, [])).Cause);
        Assert.Equal(KernelFailureCause.NonFinite, Assert.Throws<KernelFailureException>(() =>
            CertifiedResidual.Evaluate(0, [(double.MaxValue, 2)])).Cause);
        Assert.Equal(KernelFailureCause.NonFinite, Assert.Throws<KernelFailureException>(() =>
            CertifiedResidual.Evaluate(double.MaxValue, [(-double.MaxValue, 1)])).Cause);
    }

    [Fact]
    public void StageBIncludesMatrixDataRhsDataAndScaledSubnormalRhsLoss()
    {
        double a = 1d / 3;
        EquilibratedSystem scaled = EquilibratedSystem.Create(1, [a], [1], [1], [Math.ScaleB(1, -50)]);
        SolverExactNumber exactA = Integer(1) / Integer(3) * PowerOfTwo(scaled.RS[0] + scaled.CS[0]);
        SolverExactNumber exactB = (Integer(1) + PowerOfTwo(-51)) * PowerOfTwo(scaled.RS[0]);
        Bounded(From(scaled.S[0]) - exactA, scaled.DS[0]);
        Bounded(From(scaled.SB[0]) - exactB, scaled.DSB[0]);
        double[] y = [3];
        var (_, _, d) = BendingStateSolver.ResidualAndD(scaled, y);
        Bounded(exactB - exactA * From(y[0]), d[0]);
        Assert.True(d[0] > scaled.DSB[0]);

        double tiny = Math.BitDecrement(Binary64.Lambda);
        var subnormal = new EquilibratedSystem(1, [0], [0], [1], [0], [tiny], [Binary64.Lambda]);
        var (_, dr, tinyD) = BendingStateSolver.ResidualAndD(subnormal, [0]);
        Assert.Equal(Binary64.Lambda, dr[0]);
        Bounded(From(tiny), tinyD[0]);
        Assert.True(tinyD[0] >= 2 * Binary64.Lambda);
    }

    [Fact]
    public void ExactZeroStoredResidualStillRequiresBothMatrixAndRhsDataTerms()
    {
        var scaled = new EquilibratedSystem(1, [0], [0], [1], [0.25], [1], [0.25]);
        var (residual, rounding, d) = BendingStateSolver.ResidualAndD(scaled, [1]);
        Assert.Equal(0d, residual[0]);
        Assert.True(rounding[0] < 0.25);
        // Exact data S*=3/4, sb*=5/4 are both within their given data bounds.
        SolverExactNumber exactResidual = Integer(5) / Integer(4) - Integer(3) / Integer(4);
        Bounded(exactResidual, d[0]);
        Assert.True(d[0] >= 0.5);
    }

    // Independent exact budget; FTZ/DAZ are emulated on test operations only.
    private static (double Value, SolverExactNumber Budget) Emulate(double rhs, (double, double)[] products, bool ftz, bool daz)
    {
        static double Flush(double x) => x == 0 || double.IsSubnormal(x) ? 0 : x;
        SolverExactNumber lambda = PowerOfTwo(-1022), u = PowerOfTwo(-53);
        SolverExactNumber budget = double.IsSubnormal(rhs) ? lambda : Integer(0);
        double acc = Flush(rhs);
        foreach (var (a, b) in products)
        {
            if (a == 0 || b == 0) continue;
            double p = Product(daz ? Flush(a) : a, daz ? Flush(b) : b);
            if (ftz) p = Flush(p);
            budget = Reduce(budget + (Math.Abs(p) <= Math.ScaleB(1, -1022) ? lambda : u * From(p).Abs()));
            p = Flush(p);
            if (p == 0) continue;
            acc = Difference(daz ? Flush(acc) : acc, daz ? Flush(p) : p);
            if (ftz) acc = Flush(acc);
            budget = Reduce(budget + (Math.Abs(acc) <= Math.ScaleB(1, -1022) ? lambda : u * From(acc).Abs()));
            acc = Flush(acc);
        }
        return (acc, budget);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static double Product(double a, double b) => a * b;
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static double Difference(double a, double b) => a - b;
}
