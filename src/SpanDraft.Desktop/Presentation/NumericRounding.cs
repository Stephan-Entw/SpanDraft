namespace SpanDraft.Desktop.Presentation;

/// <summary>Convert first, then round once in display units. Never changes a model or input value.</summary>
public static class NumericRounding
{
    public static RoundedNumber Round(double siValue, QuantityKind kind, UnitDefinition unit,
        int significantDigits = ResultPresentationOptions.DefaultSignificantDigits)
    {
        UnitCatalog.Validate(kind, unit);
        ResultPresentationOptions.ValidateSignificantDigits(significantDigits);
        if (kind == QuantityKind.SafetyFactor && double.IsPositiveInfinity(siValue))
            return new(new(0, 0), new(0, 0), isPositiveInfinity: true);
        if (kind == QuantityKind.SafetyFactor && siValue < 0)
            throw new ArgumentOutOfRangeException(nameof(siValue));
        return Round(siValue, unit, significantDigits);
    }

    internal static RoundedNumber Round(double siValue, UnitDefinition unit, int significantDigits)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ResultPresentationOptions.ValidateSignificantDigits(significantDigits);
        var value = PresentationNumber.FromDouble(siValue) / unit.Scale;
        if (value.IsZero) return new(new(0, 0), new(0, 0));
        int exponent = value.DecimalExponent - significantDigits + 1;
        var step = new DecimalNumber(1, exponent);
        return new(new DecimalNumber((value / step.Number).RoundToInteger(), exponent), step);
    }
}
