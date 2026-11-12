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
    internal int SignificantDigits => IsZero ? 1 : BigInteger.Abs(Mantissa).ToString(CultureInfo.InvariantCulture).Length;

    /// <summary>Parses an invariant decimal or E-notation number without changing its digits.</summary>
    internal static DecimalNumber Parse(string text)
    {
        int separator = text.IndexOfAny(['e', 'E']);
        int exponent = separator < 0 ? 0 : int.Parse(text.AsSpan(separator + 1), CultureInfo.InvariantCulture);
        string mantissa = separator < 0 ? text : text[..separator];
        int point = mantissa.IndexOf('.');
        if (point >= 0)
        {
            exponent -= mantissa.Length - point - 1;
            mantissa = mantissa.Remove(point, 1);
        }
        return new(BigInteger.Parse(mantissa, CultureInfo.InvariantCulture), exponent);
    }

    internal PresentationNumber Number =>new PresentationNumber(Mantissa, 1) * PresentationNumber.PowerOfTen(Exponent);

    /// <summary>Approximation for consumers that accept double's range limits; formatting does not use this.</summary>
    public double ToDouble() => Number.ToDouble();
}

/// <summary>
/// The result of the one final precision rounding and its notation, before any culture choice.
/// <see cref="Step"/> is the place of the last displayed digit, so trailing zeros up to it are kept.
/// </summary>
public sealed class RoundedNumber
{
    internal RoundedNumber(DecimalNumber value, DecimalNumber step, bool isPositiveInfinity = false, bool usesEngineeringNotation = false)
    {
        Value = value;
        Step = step;
        IsPositiveInfinity = isPositiveInfinity;
        UsesEngineeringNotation = usesEngineeringNotation;
    }

    public DecimalNumber Value { get; }
    public DecimalNumber Step { get; }
    public bool IsPositiveInfinity { get; }
    public bool UsesEngineeringNotation { get; }
}
