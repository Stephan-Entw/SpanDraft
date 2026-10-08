using System.Globalization;
using System.Numerics;

namespace SpanDraft.Desktop.Presentation;

/// <summary>
/// An exact, normalized Mantissa · 10^Exponent presentation value. It also represents
/// finite decimal results and steps outside double's range, without replacing them by infinity or zero.
/// </summary>
public sealed record DecimalNumber
{
    public DecimalNumber(BigInteger mantissa, int exponent)
    {
        if (exponent is < -2048 or > 2048) throw new ArgumentOutOfRangeException(nameof(exponent));
        if (mantissa.IsZero) exponent = 0;
        else
        {
            while (mantissa % 10 == 0)
            {
                mantissa /= 10;
                exponent++;
            }
        }
        Mantissa = mantissa;
        Exponent = exponent;
    }

    public BigInteger Mantissa { get; }
    public int Exponent { get; }
    public bool IsZero => Mantissa.IsZero;
    internal int MagnitudeExponent => BigInteger.Abs(Mantissa).ToString(CultureInfo.InvariantCulture).Length - 1 + Exponent;
    internal PresentationNumber Number => new PresentationNumber(Mantissa, 1) * PresentationNumber.PowerOfTen(Exponent);

    /// <summary>Approximation for consumers that accept double's range limits; formatting does not use this.</summary>
    public double ToDouble() => Number.ToDouble();
}

/// <summary>The result of the one final precision rounding, before any notation or culture choice.</summary>
public sealed class RoundedNumber
{
    internal RoundedNumber(DecimalNumber value, DecimalNumber step, bool isApproximateZero = false,
        bool isPositiveInfinity = false)
    {
        Value = value;
        Step = step;
        IsApproximateZero = isApproximateZero;
        IsPositiveInfinity = isPositiveInfinity;
    }

    public DecimalNumber Value { get; }
    public DecimalNumber Step { get; }
    public bool IsApproximateZero { get; }
    public bool IsPositiveInfinity { get; }
}
