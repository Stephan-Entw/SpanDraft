namespace SpanDraft.Desktop.Presentation;

/// <summary>
/// The one display rounding for given and calculated values: convert first, then round once in display units.
/// Never changes a model or input value.
/// </summary>
public static class NumericRounding
{
    public static RoundedNumber Round(double siValue, QuantityKind kind, UnitDefinition unit,
        int significantDigits = ResultPresentationOptions.DefaultSignificantDigits,
        EngineeringNotation notation = EngineeringNotation.Automatic)
    {
        UnitCatalog.Validate(kind, unit);
        if (kind == QuantityKind.SafetyFactor && double.IsPositiveInfinity(siValue))
        {
            ResultPresentationOptions.ValidateSignificantDigits(significantDigits);
            return new(new(0, 0), new(0, 0), isPositiveInfinity: true);
        }
        if (kind == QuantityKind.SafetyFactor && siValue < 0)
            throw new ArgumentOutOfRangeException(nameof(siValue));
        // A safety factor is never shown larger than calculated: it rounds down (toward zero).
        return Round(PresentationNumber.FromDouble(siValue) / unit.Scale, significantDigits, notation,
            towardZero: kind == QuantityKind.SafetyFactor);
    }

    /// <summary>
    /// n significant digits. In decimal notation the rounding place is 10^min(0, e − n + 1), so digits before the
    /// decimal separator are never lost. With automatic notation, a magnitude rounded to n digits below 10⁻³ or
    /// from 10⁶ uses engineering notation with an n-digit mantissa instead. Halves round away from zero.
    /// </summary>
    internal static RoundedNumber Round(PresentationNumber value, int significantDigits, EngineeringNotation notation,
        bool towardZero = false)
    {
        ResultPresentationOptions.ValidateSignificantDigits(significantDigits);
        if (!Enum.IsDefined(notation)) throw new ArgumentOutOfRangeException(nameof(notation));
        if (value.IsZero) return new(new(0, 0), new(0, 0));
        // The magnitude rounded to the precision alone decides the notation.
        int precisionExponent = value.DecimalExponent - significantDigits + 1;
        var byPrecision = RoundAt(value, precisionExponent, towardZero);
        int magnitude = byPrecision.MagnitudeExponent;
        if (notation == EngineeringNotation.Automatic && magnitude is < -3 or >= 6)
            // A carry into the next decade keeps the mantissa at the requested digits.
            return new(byPrecision, new(1, magnitude - significantDigits + 1), usesEngineeringNotation: true);
        var rounded = RoundWithinUnit(value, significantDigits, out int exponent, towardZero);
        return new(rounded, new(1, exponent));
    }

    /// <summary>
    /// Significant digits in decimal notation, but never coarser than one whole display unit:
    /// the rounding place is 10^min(0, e − n + 1).
    /// </summary>
    internal static DecimalNumber RoundWithinUnit(PresentationNumber value, int significantDigits, out int exponent,
        bool towardZero = false)
    {
        exponent = Math.Min(0, value.DecimalExponent - significantDigits + 1);
        return RoundAt(value, exponent, towardZero);
    }

    private static DecimalNumber RoundAt(PresentationNumber value, int exponent, bool towardZero)
    {
        var scaled = value / PresentationNumber.PowerOfTen(exponent);
        return new(towardZero ? scaled.TruncateToInteger() : scaled.RoundToInteger(), exponent);
    }
}
