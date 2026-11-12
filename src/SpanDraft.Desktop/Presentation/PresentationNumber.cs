using System.Globalization;
using System.Numerics;

namespace SpanDraft.Desktop.Presentation;

/// <summary>
/// Private exact decimal/rational arithmetic for presentation only. Neither decimal's
/// exponent range nor double intermediates can truncate a step or a converted value.
/// </summary>
internal readonly struct PresentationNumber : IComparable<PresentationNumber>
{
    internal PresentationNumber(BigInteger numerator, BigInteger denominator)
    {
        if (denominator <= 0) throw new ArgumentOutOfRangeException(nameof(denominator));
        BigInteger divisor = BigInteger.GreatestCommonDivisor(numerator, denominator);
        Numerator = numerator / divisor;
        Denominator = denominator / divisor;
    }

    internal BigInteger Numerator { get; }
    internal BigInteger Denominator { get; }
    internal bool IsZero => Numerator.IsZero;

    internal static PresentationNumber FromDouble(double value)
    {
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
        // Round-trip decimals retain distinct adjacent normal doubles, while avoiding
        // binary representation noise at intended decimal steps and halfway values.
        // Subnormal round-trip strings can contain just one significant digit; retain
        // their relative precision explicitly before the requested result rounding.
        string format = value != 0 && Math.Abs(value) < 2.2250738585072014e-308 ? "E16" : "R";
        return Parse(value.ToString(format, CultureInfo.InvariantCulture));
    }

    internal static PresentationNumber Parse(string text) => DecimalNumber.Parse(text).Number;

    internal static PresentationNumber PowerOfTen(int exponent) => exponent >= 0
        ? new(BigInteger.Pow(10, exponent), 1)
        : new(1, BigInteger.Pow(10, -exponent));

    internal PresentationNumber Abs() => new(BigInteger.Abs(Numerator), Denominator);

    internal int DecimalExponent
    {
        get
        {
            if (IsZero) throw new InvalidOperationException("Zero has no decimal exponent.");
            PresentationNumber magnitude = Abs();
            int exponent = BigInteger.Abs(Numerator).ToString(CultureInfo.InvariantCulture).Length
                - Denominator.ToString(CultureInfo.InvariantCulture).Length;
            return magnitude.CompareTo(PowerOfTen(exponent)) < 0 ? exponent - 1 : exponent;
        }
    }

    internal BigInteger RoundToInteger()
    {
        BigInteger integral = BigInteger.DivRem(BigInteger.Abs(Numerator), Denominator, out BigInteger remainder);
        if (2 * remainder >= Denominator) integral++;
        return Numerator.Sign < 0 ? -integral : integral;
    }

    internal BigInteger TruncateToInteger() => BigInteger.Divide(Numerator, Denominator);

    internal double ToDouble()
    {
        if (IsZero) return 0;
        int exponent = DecimalExponent - 16;
        BigInteger digits = (this / PowerOfTen(exponent)).RoundToInteger();
        return double.Parse(digits.ToString(CultureInfo.InvariantCulture) + "E" + exponent,
            CultureInfo.InvariantCulture);
    }

    public int CompareTo(PresentationNumber other) =>
        (Numerator * other.Denominator).CompareTo(other.Numerator * Denominator);

    public static PresentationNumber operator *(PresentationNumber left, PresentationNumber right) =>
        new(left.Numerator * right.Numerator, left.Denominator * right.Denominator);
    public static PresentationNumber operator /(PresentationNumber left, PresentationNumber right)
    {
        if (right.IsZero) throw new DivideByZeroException();
        return new(left.Numerator * right.Denominator * right.Numerator.Sign,
            left.Denominator * BigInteger.Abs(right.Numerator));
    }
}
