namespace SpanDraft.Desktop.Presentation;

/// <summary>
/// Rounding of calculated values: convert first, then round once in display units. Never changes a
/// model or input value. Given values are not rounded here; see <see cref="QuantityFormatter.Input"/>.
/// </summary>
public static class NumericRounding
{
    public static RoundedNumber Round(double siValue, QuantityKind kind, UnitDefinition unit,
        int significantDigits = ResultPresentationOptions.DefaultSignificantDigits,
        EngineeringNotation notation = EngineeringNotation.Automatic)
    {
        UnitCatalog.Validate(kind, unit);
        ResultPresentationOptions.ValidateSignificantDigits(significantDigits);
        if (!Enum.IsDefined(notation)) throw new ArgumentOutOfRangeException(nameof(notation));
        if (kind == QuantityKind.SafetyFactor && double.IsPositiveInfinity(siValue))
            return new(new(0, 0), new(0, 0), isPositiveInfinity: true);
        if (kind == QuantityKind.SafetyFactor && siValue < 0)
            throw new ArgumentOutOfRangeException(nameof(siValue));
        var value = PresentationNumber.FromDouble(siValue) / unit.Scale;
        if (value.IsZero) return new(new(0, 0), new(0, 0));
        // The magnitude rounded to the precision alone decides the notation.
        int precisionExponent = value.DecimalExponent - significantDigits + 1;
        var byPrecision = RoundAt(value, precisionExponent);
        int magnitude = byPrecision.MagnitudeExponent;
        if (notation == EngineeringNotation.Automatic && magnitude is < -3 or >= 6)
            // A carry into the next decade keeps the mantissa at exactly the requested digits.
            return new(byPrecision, new(1, magnitude - significantDigits + 1), usesEngineeringNotation: true);
        var rounded = RoundWithinUnit(value, significantDigits, out int exponent);
        return new(rounded, new(1, exponent));
    }

    /// <summary>
    /// Significant digits in decimal notation, but never coarser than one whole display unit:
    /// the rounding place is 10^min(0, e − n + 1). Shared by results and approximated given values.
    /// </summary>
    internal static DecimalNumber RoundWithinUnit(PresentationNumber value, int significantDigits, out int exponent)
    {
        exponent = Math.Min(0, value.DecimalExponent - significantDigits + 1);
        return RoundAt(value, exponent);
    }

    private static DecimalNumber RoundAt(PresentationNumber value, int exponent) =>
        new((value / PresentationNumber.PowerOfTen(exponent)).RoundToInteger(), exponent);
}
