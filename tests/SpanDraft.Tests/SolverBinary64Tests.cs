using SpanDraft.Solver.Kernel;
using Xunit;
using static SpanDraft.Tests.SolverExactNumber;

namespace SpanDraft.Tests;

public sealed class SolverBinary64Tests
{
    private const double Normal = 2.2250738585072014e-308;
    private const double Max = double.MaxValue;
    private static readonly double Tiny = Bits(1);

    public static TheoryData<double, string> Classes => new()
    {
        { 0, "zero" }, { Bits(1UL << 63), "zero" },
        { Tiny, "subnormal" }, { -Math.BitDecrement(Normal), "subnormal" },
        { Normal, "normal" }, { -Max, "normal" },
        { double.PositiveInfinity, "infinity" }, { double.NegativeInfinity, "infinity" },
        { double.NaN, "nan" }, { Bits(0x7ff0000000000001), "nan" }
    };

    [Theory]
    [MemberData(nameof(Classes))]
    public void ClassifiesStoredBits(double value, string category)
    {
        Assert.Equal(category == "zero", Binary64.IsZero(value));
        Assert.Equal(category == "subnormal", Binary64.IsSubnormal(value));
        Assert.Equal(category == "normal", Binary64.IsNormal(value));
        Assert.Equal(category == "infinity", Binary64.IsInfinity(value));
        Assert.Equal(category == "nan", Binary64.IsNaN(value));
        Assert.Equal(category is not ("infinity" or "nan"), Binary64.IsFinite(value));
    }

    [Fact]
    public void ExponentEnclosesEveryBoundaryInItsExactBinaryInterval()
    {
        double[] values = [Tiny, Math.BitDecrement(Normal), Normal, Math.BitIncrement(Normal),
            Math.BitDecrement(1), 1, Math.BitIncrement(1), Math.BitDecrement(2), 2, Math.BitIncrement(2), Max];
        foreach (double value in values)
            From(value).AssertExponent(Binary64.PowerOfTwoExponent(value));

        foreach (double value in new[] { 0, -0d, -Tiny, -1, double.PositiveInfinity, double.NegativeInfinity, double.NaN })
            Assert.Throws<NumericalRangeException>(() => Binary64.PowerOfTwoExponent(value));
    }

    public static TheoryData<double, int, double> Scaling => new()
    {
        { Tiny, 1074, 1 }, { -Tiny, 1074, -1 },
        { Math.BitDecrement(Normal), 1, Bits(0x001ffffffffffffe) },
        { Max, -1023, Math.BitDecrement(2) }, { 1.5, 6, 96 }, { -1.5, -3, -0.1875 },
        { 1, -1075, 0 }, { -1, -1075, Bits(1UL << 63) },
        { 1, -1074, Tiny }, { 1.5, -1074, Bits(2) }, { 2.5, -1074, Bits(2) },
        { 1, 1024, double.PositiveInfinity }, { -1, 1024, double.NegativeInfinity },
        { 1, -1022, Normal }, { Math.BitDecrement(1), -1022, Normal },
        { Math.BitIncrement(1), -1022, Math.BitIncrement(Normal) },
        { -1, int.MinValue, Bits(1UL << 63) }, { 1, int.MaxValue, double.PositiveInfinity }
    };

    [Theory]
    [MemberData(nameof(Scaling))]
    public void ScalesWithOneCorrectRounding(double value, int exponent, double expected)
    {
        Assert.Equal(BitConverter.DoubleToUInt64Bits(expected),
            BitConverter.DoubleToUInt64Bits(Binary64.ScaleByPowerOfTwo(value, exponent)));

        // The large int cases would need multi-gigabit fractions, so their bounds are fixed directly.
        if (exponent == int.MinValue)
            AssertDirectedScale(value, exponent, -Normal, 0);
        else if (exponent == int.MaxValue)
            AssertDirectedScale(value, exponent, Max, double.PositiveInfinity);
        else
            AssertEncloses(Directed.DirectedScale(value, exponent, upper: false),
                From(value) * PowerOfTwo(exponent), Directed.DirectedScale(value, exponent, upper: true));
    }

    [Fact]
    public void DirectedScalingPreservesExactNormalResultsAndUsesTheUnderflowSign()
    {
        AssertDirectedScale(1, -1022, Normal, Normal);
        AssertDirectedScale(-1, -1022, -Normal, -Normal);
        AssertDirectedScale(Math.BitDecrement(1), -1022, 0, Math.BitIncrement(Normal));
        AssertDirectedScale(1, -1075, 0, Normal);
        AssertDirectedScale(-1, -1075, -Normal, 0);
        AssertDirectedScale(-0d, 8, -0d, -0d);
        AssertDirectedScale(double.PositiveInfinity, -8, double.PositiveInfinity, double.PositiveInfinity);
        Assert.Throws<NumericalRangeException>(() => Directed.DirectedScale(double.NaN, 1, upper: true));
    }

    [Fact]
    public void OutwardImplementsTheFullBoundaryTable()
    {
        AssertOutward(0, -Normal, Normal);
        AssertOutward(-0d, -Normal, Normal);
        AssertOutward(Tiny, 0, Normal);
        AssertOutward(-Tiny, -Normal, 0);
        AssertOutward(Math.BitDecrement(Normal), 0, Normal);
        AssertOutward(-Math.BitDecrement(Normal), -Normal, 0);
        AssertOutward(Normal, 0, Math.BitIncrement(Normal));
        AssertOutward(-Normal, -Math.BitIncrement(Normal), 0);
        AssertOutward(Math.BitIncrement(Normal), Normal, Math.BitIncrement(Math.BitIncrement(Normal)));
        AssertOutward(1, Math.BitDecrement(1), Math.BitIncrement(1));
        AssertOutward(-1, Math.BitDecrement(-1), Math.BitIncrement(-1));
        AssertOutward(Max, Math.BitDecrement(Max), double.PositiveInfinity);
        AssertOutward(-Max, double.NegativeInfinity, Math.BitIncrement(-Max));
        AssertOutward(double.PositiveInfinity, Max, double.PositiveInfinity);
        AssertOutward(double.NegativeInfinity, double.NegativeInfinity, -Max);
        Assert.Throws<NumericalRangeException>(() => Directed.Outward(double.NaN, upper: false));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectedOperationsDistinguishProvenZerosFromUnderflow(bool upper)
    {
        Assert.Equal(0, Directed.Add(1, -1, upper));
        Assert.Equal(0, Directed.Add(Tiny, -Tiny, upper));
        Assert.Equal(0, Directed.Subtract(-Tiny, -Tiny, upper));
        Assert.Equal(0, Directed.Multiply(-0d, Tiny, upper));
        Assert.Equal(0, Directed.Divide(-0d, Tiny, upper));
        Assert.Equal(upper ? Normal : 0, Directed.Add(Tiny, Tiny, upper));
        Assert.Equal(upper ? 0 : -Normal, Directed.Subtract(0, Tiny, upper));
        Assert.Equal(upper ? Normal : 0, Directed.Multiply(Normal, 0.5, upper));
        Assert.Equal(upper ? Normal : 0, Directed.Divide(Normal, 2, upper));
        Assert.Equal(upper ? Normal : -Normal, Directed.Multiply(Tiny, Tiny, upper));
        Assert.Equal(upper ? Math.BitIncrement(1) : Math.BitDecrement(1), Directed.Divide(Tiny, Tiny, upper));
        double scaledTiny = 1.0 / (1L << 51);
        Assert.Equal(upper ? Math.BitIncrement(scaledTiny) : Math.BitDecrement(scaledTiny),
            Directed.Multiply(Tiny, Bits(0x7fe0000000000000), upper));
        Assert.Equal(upper ? double.PositiveInfinity : Max, Directed.Multiply(Tiny, double.PositiveInfinity, upper));
        Assert.Throws<NumericalRangeException>(() => Directed.Subtract(double.PositiveInfinity, double.PositiveInfinity, upper));
        Assert.Throws<NumericalRangeException>(() => Directed.Multiply(0, double.NaN, upper));
        Assert.Throws<NumericalRangeException>(() => Directed.Divide(0, 0, upper));
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(4L)]
    [InlineData(1L << 27)]
    [InlineData(1L << 52)]
    [InlineData((1L << 53) - 1)]
    public void GammaUpperUsesDownwardDenominatorAndUpperQuotient(long count)
    {
        double gamma = Directed.GammaUpper(count);
        AssertStored(gamma);
        Assert.True(From(gamma).CompareTo(Integer(count) / Integer((1L << 53) - count)) >= 0);
        if (count == 0)
            Assert.Equal(0, gamma);
        if (count == 1L << 52)
            Assert.Equal(Bits(0x3ff0000000000002), gamma);
    }

    [Theory]
    [InlineData(-1L)]
    [InlineData(1L << 53)]
    public void GammaRejectsInvalidCounts(long count) =>
        Assert.Throws<NumericalRangeException>(() => Directed.GammaUpper(count));

    [Fact]
    public void StiffnessChecksExactProductsBeforeRounding()
    {
        (double E, double I)[] pairs = [(Normal, Math.BitDecrement(1)), (Normal, 1),
            (Normal, Math.BitIncrement(1)), (Normal, Normal), (Max, Math.BitDecrement(1)), (Max, 1),
            (Max, Math.BitIncrement(1)), (Max, 2), (Bits(0x7fe9999999999999), 1.25)];
        foreach ((double e, double i) in pairs)
        {
            SolverExactNumber product = From(e) * From(i);
            if (product.CompareTo(From(Normal)) < 0 || product.CompareTo(From(Max)) > 0)
            {
                Assert.Throws<NumericalRangeException>(() => Binary64.StiffnessProduct(e, i));
                continue;
            }

            double result = Binary64.StiffnessProduct(e, i);
            Assert.True(double.IsNormal(result));
            AssertCloserThanNeighbor(result, Math.BitDecrement(result), product);
            if (result < Max)
                AssertCloserThanNeighbor(result, Math.BitIncrement(result), product);
        }
    }

    [Fact]
    public void StiffnessRejectsNonnormalOrNonpositiveOperandsInEitherPosition()
    {
        foreach (double value in new[] { -1, 0, -0d, Tiny, double.PositiveInfinity, double.NegativeInfinity, double.NaN })
        {
            Assert.Throws<NumericalRangeException>(() => Binary64.StiffnessProduct(value, 1));
            Assert.Throws<NumericalRangeException>(() => Binary64.StiffnessProduct(1, value));
        }
    }

    public static TheoryData<double> NormalThresholdBoundary => new()
    {
        Math.BitDecrement(Normal * 100_000_000),
        Normal * 100_000_000,
        Math.BitIncrement(Normal * 100_000_000)
    };

    [Theory]
    [InlineData(Normal)]
    [InlineData(1)]
    [InlineData(100_000_000)]
    [InlineData(Max)]
    [MemberData(nameof(NormalThresholdBoundary))]
    public void ThresholdIsTheLargestAllowedBoundBelowTheExactQuotient(double denominator)
    {
        SolverExactNumber limit = From(denominator) / Integer(100_000_000);
        double threshold = Binary64.ThresholdDown(denominator);
        AssertStored(threshold);
        Assert.True(From(threshold).CompareTo(limit) <= 0);
        double successor = threshold == 0 ? Normal : Math.BitIncrement(threshold);
        Assert.True(From(successor).CompareTo(limit) > 0);
    }

    [Fact]
    public void ThresholdHandlesPositiveSubnormalsAndRejectsInvalidDenominators()
    {
        Assert.Equal(0, Binary64.ThresholdDown(Tiny));
        foreach (double value in new[] { 0, -0d, -Tiny, -1, double.PositiveInfinity, double.NaN })
            Assert.Throws<NumericalRangeException>(() => Binary64.ThresholdDown(value));
    }

    private static void AssertCloserThanNeighbor(double result, double neighbor, SolverExactNumber exact)
    {
        int difference = (From(result) - exact).Abs().CompareTo((From(neighbor) - exact).Abs());
        Assert.True(difference < 0 || (difference == 0 && (BitConverter.DoubleToUInt64Bits(result) & 1) == 0));
    }

    private static double Bits(ulong bits) => BitConverter.UInt64BitsToDouble(bits);

    private static void AssertOutward(double value, double lower, double upper)
    {
        Assert.Equal(lower, Directed.Outward(value, upper: false));
        Assert.Equal(upper, Directed.Outward(value, upper: true));
    }

    private static void AssertDirectedScale(double value, int exponent, double lower, double upper)
    {
        Assert.Equal(BitConverter.DoubleToUInt64Bits(lower),
            BitConverter.DoubleToUInt64Bits(Directed.DirectedScale(value, exponent, upper: false)));
        Assert.Equal(BitConverter.DoubleToUInt64Bits(upper),
            BitConverter.DoubleToUInt64Bits(Directed.DirectedScale(value, exponent, upper: true)));
    }
}
