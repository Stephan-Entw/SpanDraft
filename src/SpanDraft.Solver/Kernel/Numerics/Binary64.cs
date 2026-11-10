using System.Numerics;
using static System.BitConverter;

namespace SpanDraft.Solver.Kernel;

internal static class Binary64
{
    private const ulong SignBit = 1UL << 63;
    private const ulong ImplicitBit = 1UL << 52;
    private const ulong FractionMask = ImplicitBit - 1;
    private const ulong InfinityBits = 0x7FF0000000000000;
    internal const int UnitExponent = -1074;
    internal const double U = 1.0 / (1L << 53);
    internal const int TauDenominator = 100_000_000;
    internal static readonly double Lambda = UInt64BitsToDouble(ImplicitBit);

    internal static ulong MagnitudeBits(double value) => DoubleToUInt64Bits(value) & ~SignBit;
    internal static bool IsNegative(double value) => (DoubleToUInt64Bits(value) & SignBit) != 0;
    internal static bool IsZero(double value) => MagnitudeBits(value) == 0;
    internal static bool IsSubnormal(double value) => MagnitudeBits(value) is > 0 and < ImplicitBit;
    internal static bool IsNormal(double value) => MagnitudeBits(value) is >= ImplicitBit and < InfinityBits;
    internal static bool IsFinite(double value) => MagnitudeBits(value) < InfinityBits;
    internal static bool IsInfinity(double value) => MagnitudeBits(value) == InfinityBits;
    internal static bool IsNaN(double value) => MagnitudeBits(value) > InfinityBits;
    internal static bool IsPositive(double value) => !IsNegative(value) && !IsZero(value) && !IsNaN(value);
    internal static double Abs(double value) => UInt64BitsToDouble(MagnitudeBits(value));
    internal static double Negate(double value) => UInt64BitsToDouble(DoubleToUInt64Bits(value) ^ SignBit);

    internal static bool SameValue(double left, double right) =>
        DoubleToUInt64Bits(left) == DoubleToUInt64Bits(right) || (IsZero(left) && IsZero(right));

    internal static int PowerOfTwoExponent(double value)
    {
        if (!IsPositive(value) || !IsFinite(value))
            throw new NumericalRangeException("Binary exponent requires a positive finite value.");

        ulong bits = MagnitudeBits(value);
        int biasedExponent = (int)(bits >> 52);
        return biasedExponent == 0 ? BitOperations.Log2(bits) + UnitExponent : biasedExponent - 1023;
    }

    internal static double ScaleByPowerOfTwo(double value, int exponent)
    {
        if (!IsFinite(value) || IsZero(value))
            return value;

        var (significand, valueExponent) = Decompose(MagnitudeBits(value));
        double scaled = RoundExact(significand, BigInteger.One, (long)valueExponent + exponent);
        return IsNegative(value) ? Negate(scaled) : scaled;
    }

    private static (ulong Significand, int Exponent) Decompose(ulong magnitude)
    {
        int biasedExponent = (int)(magnitude >> 52);
        return biasedExponent == 0
            ? (magnitude, UnitExponent)
            : ((magnitude & FractionMask) | ImplicitBit, biasedExponent - 1075);
    }

    // All finite stored values are integer multiples of the smallest subnormal.
    internal static BigInteger ExactUnits(double value)
    {
        if (!IsFinite(value))
            throw new NumericalRangeException("Exact binary decomposition requires a finite value.");

        var (significand, exponent) = Decompose(MagnitudeBits(value));
        BigInteger units = (BigInteger)significand << (exponent - UnitExponent);
        return IsNegative(value) ? -units : units;
    }

    internal static int ExactExponent(BigInteger numerator, BigInteger denominator)
    {
        int exponent = (int)(numerator.GetBitLength() - denominator.GetBitLength());
        bool belowPower = exponent < 0 ? (numerator << -exponent) < denominator
            : numerator < (denominator << exponent);
        return belowPower ? exponent - 1 : exponent;
    }

    // Round numerator / positive denominator * 2^exponent, with a single ties-to-even step.
    internal static double RoundExact(BigInteger numerator, BigInteger denominator, long exponent)
    {
        if (numerator.IsZero)
            return 0;

        ulong sign = numerator.Sign < 0 ? SignBit : 0;
        numerator = BigInteger.Abs(numerator);
        long resultExponent = ExactExponent(numerator, denominator) + exponent;
        if (resultExponent > 1023)
            return UInt64BitsToDouble(sign | InfinityBits);
        if (resultExponent < UnitExponent - 1)
            return UInt64BitsToDouble(sign);

        long quantum = Math.Max(resultExponent - 52, UnitExponent);
        int shift = (int)(exponent - quantum);
        if (shift >= 0)
            numerator <<= shift;
        else
            denominator <<= -shift;

        BigInteger significand = BigInteger.DivRem(numerator, denominator, out BigInteger remainder);
        int half = (remainder << 1).CompareTo(denominator);
        if (half > 0 || (half == 0 && !significand.IsEven))
            significand++;

        // Adding the implicit bit also carries into the next exponent at a rounding boundary.
        ulong exponentBits = (ulong)(quantum - UnitExponent) << 52;
        return UInt64BitsToDouble(sign | (exponentBits + (ulong)significand));
    }

    internal static double StiffnessProduct(double elasticModulus, double secondMoment)
    {
        if (IsNegative(elasticModulus) || !IsNormal(elasticModulus) ||
            IsNegative(secondMoment) || !IsNormal(secondMoment))
            throw new NumericalRangeException("EI requires positive normal operands.");

        BigInteger product = ExactUnits(elasticModulus) * ExactUnits(secondMoment);
        if (product < (ExactUnits(Lambda) << -UnitExponent) ||
            product > (ExactUnits(double.MaxValue) << -UnitExponent))
            throw new NumericalRangeException("The exact EI product is outside the normal range.");

        return elasticModulus * secondMoment;
    }

    internal static double ThresholdDown(double denominator)
    {
        if (!IsPositive(denominator) || !IsFinite(denominator))
            throw new NumericalRangeException("The threshold requires a positive finite denominator.");

        double candidate = denominator / TauDenominator;
        if (ExactUnits(candidate) * TauDenominator > ExactUnits(denominator))
            candidate = UInt64BitsToDouble(DoubleToUInt64Bits(candidate) - 1);

        return IsNormal(candidate) ? candidate : 0;
    }
}

internal sealed class NumericalRangeException(string message) : ArithmeticException(message);
