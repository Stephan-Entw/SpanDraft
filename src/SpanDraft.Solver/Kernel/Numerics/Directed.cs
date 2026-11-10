using System.Numerics;
using static System.BitConverter;
using static SpanDraft.Solver.Kernel.Binary64;

namespace SpanDraft.Solver.Kernel;

internal static class Directed
{
    internal static double Outward(double value, bool upper)
    {
        RejectNaN(value);
        if (IsZero(value))
            return upper ? Lambda : -Lambda;
        if (IsSubnormal(value))
            return Underflow(IsNegative(value), upper);

        bool negative = IsNegative(value);
        if (IsInfinity(value) && negative != upper)
            return value;

        ulong bits = DoubleToUInt64Bits(value);
        double neighbor = UInt64BitsToDouble(negative == upper ? bits - 1 : bits + 1);
        return IsSubnormal(neighbor) ? Underflow(negative, upper) : neighbor;
    }

    internal static double Add(double left, double right, bool upper)
    {
        if (IsFinite(left) && SameValue(left, Negate(right)))
            return 0;

        double result = left + right;
        if (NeedsExactRounding(left, right, result))
            result = RoundExact(ExactUnits(left) + ExactUnits(right), BigInteger.One, UnitExponent);
        return Outward(result, upper);
    }

    internal static double Subtract(double left, double right, bool upper) =>
        Add(left, Negate(right), upper);

    internal static double Multiply(double left, double right, bool upper)
    {
        RejectNaN(left);
        RejectNaN(right);
        if (IsZero(left) || IsZero(right))
            return 0;
        if (IsInfinity(left) || IsInfinity(right))
            return Outward(SignedInfinity(left, right), upper);

        double result = left * right;
        if (NeedsExactRounding(left, right, result))
            result = RoundExact(ExactUnits(left) * ExactUnits(right), BigInteger.One, 2 * UnitExponent);
        return Outward(result, upper);
    }

    internal static double Divide(double numerator, double denominator, bool upper)
    {
        RejectNaN(numerator);
        RejectNaN(denominator);
        if (IsZero(numerator) && IsPositive(denominator))
            return 0;
        if (IsZero(denominator) && !IsZero(numerator))
            return Outward(SignedInfinity(numerator, denominator), upper);

        double result = numerator / denominator;
        if (!IsZero(denominator) && NeedsExactRounding(numerator, denominator, result))
        {
            BigInteger exactNumerator = ExactUnits(numerator);
            BigInteger exactDenominator = ExactUnits(denominator);
            if (exactDenominator.Sign < 0)
                exactNumerator = -exactNumerator;
            result = RoundExact(exactNumerator, BigInteger.Abs(exactDenominator), 0);
        }
        return Outward(result, upper);
    }

    internal static double DirectedScale(double value, int exponent, bool upper)
    {
        RejectNaN(value);
        if (IsZero(value) || IsInfinity(value))
            return value;

        double result = ScaleByPowerOfTwo(value, exponent);
        if (IsZero(result))
            return Underflow(IsNegative(value), upper);
        if (IsNormal(result) && (Abs(result) > Lambda ||
            (long)PowerOfTwoExponent(Abs(value)) + exponent == -1022))
            return result;
        return Outward(result, upper);
    }

    internal static double GammaUpper(long count)
    {
        if (count == 0)
            return 0;
        if (count < 0 || count >= 1L << 53)
            throw new NumericalRangeException("The rounding count must be in [0, 2^53).");

        double ku = ScaleByPowerOfTwo(count, -53);
        return Divide(ku, Subtract(1, ku, upper: false), upper: true);
    }

    // Use gradual rounding in the exact path when DAZ or FTZ can affect the native operation.
    private static bool NeedsExactRounding(double left, double right, double result) =>
        IsFinite(left) && IsFinite(right) &&
        (IsSubnormal(left) || IsSubnormal(right) || IsSubnormal(result) || IsZero(result));

    private static double Underflow(bool negative, bool upper) =>
        negative ? (upper ? 0 : -Lambda) : (upper ? Lambda : 0);

    private static double SignedInfinity(double left, double right) =>
        IsNegative(left) == IsNegative(right) ? double.PositiveInfinity : double.NegativeInfinity;

    private static void RejectNaN(double value)
    {
        if (IsNaN(value))
            throw new NumericalRangeException("NaN cannot enter directed arithmetic.");
    }
}
