using SpanDraft.Solver.Kernel;
using Xunit;
using static SpanDraft.Tests.SolverExactNumber;

namespace SpanDraft.Tests;

public sealed class SolverBoundedTests
{
    private const double Normal = 2.2250738585072014e-308;
    private static readonly double Tiny = BitConverter.UInt64BitsToDouble(1);

    [Fact]
    public void OnlyZeroMagnitudeAndErrorPermitExactZeroShortcuts()
    {
        var zero = new Bounded(-0d);
        var operand = new Bounded(-1.25, 0.125);
        AssertSame(operand, zero + operand);
        AssertSame(operand, operand - zero);
        AssertSame(-operand, zero - operand);
        AssertSame(default, zero * operand);
        AssertSame(default, operand * zero);
        AssertSame(default, zero / operand);

        var uncertainZero = new Bounded(0, 0.125);
        AssertBounded(uncertainZero * operand, Integer(1) / Integer(8));
        AssertBounded(uncertainZero / operand, Integer(1) / Integer(10));
    }

    [Fact]
    public void AStoredSubnormalCannotHaveZeroMagnitude()
    {
        Assert.Throws<NumericalRangeException>(() => new Bounded(Tiny, magnitude: 0));
    }

    [Theory]
    [InlineData(1.25, 2.5)]
    [InlineData(1.25, -1.25)]
    [InlineData(Normal, Normal)]
    [InlineData(Normal, -Normal)]
    public void AdditionAndSubtractionEncloseNormalAndCancelledResults(double left, double right)
    {
        var a = new Bounded(left);
        var b = new Bounded(right);
        AssertBounded(a + b, From(left) + From(right));
        AssertBounded(a - b, From(left) - From(right));
    }

    [Fact]
    public void AdditionAndSubtractionIncludeInputErrors()
    {
        var a = new Bounded(1.25, 0.125);
        var b = new Bounded(1.25, 0.25);
        AssertBounded(a + b, From(1.125) + From(1));
        AssertBounded(a - b, From(1.375) - From(1));
    }

    [Fact]
    public void NegationChangesOnlyTheStoredSign()
    {
        var value = new Bounded(-Tiny, Tiny);
        Bounded negated = -value;
        Assert.Equal(1UL, BitConverter.DoubleToUInt64Bits(negated.Value));
        Assert.Equal(Normal, negated.Error);
        Assert.Equal(Normal, negated.Magnitude);
        AssertBounded(negated, From(Tiny));
    }

    [Fact]
    public void MultiplicationEnclosesTheExactProductWithInputErrors()
    {
        var a = new Bounded(-1.5, 0.125);
        var b = new Bounded(4, 0.25);
        AssertBounded(a * b, From(-1.625) * From(4.25));
        AssertBounded(a * b, From(-1.375) * From(3.75));
    }

    [Fact]
    public void DivisionEnclosesTheExactQuotientWithInputErrors()
    {
        var a = new Bounded(2, 0.125);
        var b = new Bounded(-4, 0.25);
        AssertBounded(a / b, From(2.125) / From(-3.75));
        AssertBounded(a / b, From(1.875) / From(-4.25));
        AssertBounded(new Bounded(Tiny) / new Bounded(2), From(Tiny) / Integer(2));
        double divisor = Math.BitIncrement(Normal);
        AssertBounded(new Bounded(1) / new Bounded(divisor), Integer(1) / From(divisor));
    }

    [Fact]
    public void DivisionRequiresBothANormalStoredDivisorAndAPositiveDirectedLowerBound()
    {
        Assert.Throws<NumericalRangeException>(() => new Bounded(1) / new Bounded(Tiny));
        Assert.Throws<NumericalRangeException>(() => new Bounded(1) / new Bounded(Normal));
        Assert.Throws<NumericalRangeException>(() => new Bounded(1) / new Bounded(1, 1));
        Assert.Throws<NumericalRangeException>(() => default(Bounded) / new Bounded(1, 2));
        Assert.Throws<NumericalRangeException>(() => default(Bounded) / default(Bounded));
    }

    [Fact]
    public void NearUnderflowMagnitudeIncludesTheAbsoluteRoundingTerm()
    {
        Bounded product = new Bounded(Normal) * new Bounded(0.5);
        AssertBounded(product, PowerOfTwo(-1023));
        Assert.True(From(product.Magnitude).CompareTo(Integer(3) * PowerOfTwo(-1022)) >= 0);
    }

    [Fact]
    public void DazAllowanceIsBudgetedForSubnormalAndNormalOperands()
    {
        double large = BitConverter.UInt64BitsToDouble(0x7fe0000000000000);
        foreach (double value in new[] { Tiny, Normal })
        {
            Bounded product = new Bounded(value) * new Bounded(large);
            SolverExactNumber exact = From(value) * From(large);
            AssertBounded(product, exact);
            // The same error budget must enclose the result if the stored tiny operand reads as zero.
            Assert.True(From(product.Error).CompareTo(exact) >= 0);
        }
    }

    [Fact]
    public void MagnitudeRoundsOutwardAtEveryStep()
    {
        Bounded sum = new Bounded(1) + new Bounded(1);
        // 2 advances once for the input sum, once when adding the local error, and once outward.
        Assert.Equal(0x4000000000000003UL, BitConverter.DoubleToUInt64Bits(sum.Magnitude));
        AssertBounded(sum, Integer(2));
    }

    private static void AssertSame(Bounded expected, Bounded actual)
    {
        Assert.Equal(expected.Value, actual.Value);
        Assert.Equal(expected.Error, actual.Error);
        Assert.Equal(expected.Magnitude, actual.Magnitude);
    }

    private static void AssertBounded(Bounded bound, SolverExactNumber exact)
    {
        AssertStored(bound.Error);
        AssertStored(bound.Magnitude);
        Assert.True((From(bound.Value) - exact).Abs().CompareTo(From(bound.Error)) <= 0);
        Assert.True(From(bound.Value).Abs().CompareTo(From(bound.Magnitude)) <= 0);
    }
}
