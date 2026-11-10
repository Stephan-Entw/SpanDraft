using System.Numerics;
using Xunit;

namespace SpanDraft.Tests;

// Test oracle. Compare cross-products of exact fractions, independently of the kernel helpers.
internal readonly record struct SolverExactNumber(BigInteger Numerator, BigInteger Denominator)
    : IComparable<SolverExactNumber>
{
    internal static SolverExactNumber Integer(long value) => new(value, 1);
    internal static SolverExactNumber PowerOfTwo(int exponent) => exponent >= 0
        ? new(BigInteger.One << exponent, 1)
        : new(1, BigInteger.One << -exponent);

    internal static SolverExactNumber From(double value)
    {
        Assert.True(double.IsFinite(value));
        ulong bits = BitConverter.DoubleToUInt64Bits(value);
        int exponentField = (int)((bits >> 52) & 0x7ff);
        BigInteger fraction = bits & 0x000fffffffffffffUL;
        if (exponentField != 0)
            fraction += BigInteger.One << 52;
        if ((bits >> 63) != 0)
            fraction = -fraction;
        return new SolverExactNumber(fraction, 1) * PowerOfTwo(Math.Max(exponentField, 1) - 1023 - 52);
    }

    internal SolverExactNumber Abs() => new(BigInteger.Abs(Numerator), Denominator);

    public int CompareTo(SolverExactNumber other) =>
        (Numerator * other.Denominator).CompareTo(other.Numerator * Denominator);

    public static SolverExactNumber operator +(SolverExactNumber a, SolverExactNumber b) =>
        new(a.Numerator * b.Denominator + b.Numerator * a.Denominator, a.Denominator * b.Denominator);

    public static SolverExactNumber operator -(SolverExactNumber a, SolverExactNumber b) =>
        a + new SolverExactNumber(-b.Numerator, b.Denominator);

    public static SolverExactNumber operator *(SolverExactNumber a, SolverExactNumber b) =>
        new(a.Numerator * b.Numerator, a.Denominator * b.Denominator);

    public static SolverExactNumber operator /(SolverExactNumber a, SolverExactNumber b) =>
        new(a.Numerator * b.Denominator * b.Numerator.Sign, a.Denominator * BigInteger.Abs(b.Numerator));

    internal static void AssertStored(double value)
    {
        bool zero = (BitConverter.DoubleToUInt64Bits(value) << 1) == 0;
        Assert.True(zero || double.IsNormal(value) || double.IsInfinity(value), $"Invalid bound {value:R}");
    }

    internal static void AssertEncloses(double lower, SolverExactNumber exact, double upper)
    {
        AssertStored(lower);
        AssertStored(upper);
        Assert.True(double.IsNegativeInfinity(lower) || From(lower).CompareTo(exact) <= 0,
            $"Lower bound {lower:R} exceeds the exact result");
        Assert.True(double.IsPositiveInfinity(upper) || From(upper).CompareTo(exact) >= 0,
            $"Upper bound {upper:R} falls below the exact result");
    }

    internal void AssertExponent(int exponent)
    {
        Assert.True(CompareTo(PowerOfTwo(exponent)) >= 0, $"Exponent {exponent} is too large");
        Assert.True(CompareTo(PowerOfTwo(exponent + 1)) < 0, $"Exponent {exponent} is too small");
    }
}
