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
        // Decimal notation never rounds coarser than one whole display unit.
        int exponent = Math.Min(0, precisionExponent);
        return new(exponent == precisionExponent ? byPrecision : RoundAt(value, exponent), new(1, exponent));
    }

    private static DecimalNumber RoundAt(PresentationNumber value, int exponent) =>
        new((value / PresentationNumber.PowerOfTen(exponent)).RoundToInteger(), exponent);
}
