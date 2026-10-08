using System.Numerics;

namespace SpanDraft.Desktop.Presentation;

/// <summary>Result precision only; every final rounding uses the unrounded converted value.</summary>
public static class NumericRounding
{
    public static RoundedNumber Round(double siValue, QuantityKind kind, UnitDefinition unit,
        PresentationMode mode = PresentationMode.Standard, ModelReferenceValues? references = null)
    {
        UnitCatalog.Validate(kind, unit);
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (kind == QuantityKind.SafetyFactor && double.IsPositiveInfinity(siValue))
            return new(new(0, 0), new(0, 0), isPositiveInfinity: true);
        if (!double.IsFinite(siValue) || (kind == QuantityKind.SafetyFactor && siValue < 0))
            throw new ArgumentOutOfRangeException(nameof(siValue));

        var value = PresentationNumber.FromDouble(siValue) / unit.Scale;
        DecimalNumber step;
        if (mode == PresentationMode.Detailed)
            step = SignificantStep(value, 3);
        else if (kind == QuantityKind.SafetyFactor)
            step = siValue switch
            {
                < 3 => new(1, -2),
                < 10 => new(1, -1),
                < 100 => new(1, 0),
                _ => SignificantStep(value, 2)
            };
        else
        {
            var modelMinimum = ModelMinimum(kind, references) / unit.Scale;
            int digits = kind == QuantityKind.TransverseDisplacement ? 2 : 3;
            if (kind == QuantityKind.Stress)
            {
                var threshold = references!.YieldStrength / unit.Scale * PresentationNumber.Parse("0.5");
                var preliminary = Round(value.Abs(), SignificantStep(value, 2)).Value.Number;
                digits = value.Abs().CompareTo(threshold) >= 0 || preliminary.CompareTo(threshold) >= 0 ? 3 : 2;
            }
            var minimum = PresentationNumber.Max(SignificantStep(value, digits).Number, modelMinimum);
            step = Ceiling125(minimum);
        }
        return Round(value, step);
    }

    /// <summary>Smallest 1/2/5 · 10^k step greater than or equal to a positive minimum.</summary>
    public static DecimalNumber Ceiling125(double minimum)
    {
        if (!double.IsFinite(minimum) || minimum <= 0) throw new ArgumentOutOfRangeException(nameof(minimum));
        return Ceiling125(PresentationNumber.FromDouble(minimum));
    }

    /// <summary>Isolated one-step rounding in display units, including exact half values away from zero.</summary>
    public static RoundedNumber RoundToStep(double displayValue, DecimalNumber step)
    {
        ArgumentNullException.ThrowIfNull(step);
        if (step.Mantissa <= 0) throw new ArgumentOutOfRangeException(nameof(step));
        return Round(PresentationNumber.FromDouble(displayValue), step);
    }

    private static PresentationNumber ModelMinimum(QuantityKind kind, ModelReferenceValues? references)
    {
        if (kind is QuantityKind.TransverseDisplacement or QuantityKind.TransverseForce or QuantityKind.Moment or QuantityKind.Stress)
            ArgumentNullException.ThrowIfNull(references);
        return kind switch
        {
            QuantityKind.TransverseDisplacement => references!.Length * PresentationNumber.PowerOfTen(-5),
            QuantityKind.TransverseForce => PresentationNumber.Min(references!.Force * PresentationNumber.PowerOfTen(-3), PresentationNumber.One),
            QuantityKind.Moment => PresentationNumber.Min(references!.Moment * PresentationNumber.PowerOfTen(-3), PresentationNumber.PowerOfTen(-1)),
            QuantityKind.Stress => PresentationNumber.Min(references!.YieldStrength * PresentationNumber.PowerOfTen(-3), PresentationNumber.PowerOfTen(5)),
            _ => PresentationNumber.Zero
        };
    }

    private static DecimalNumber SignificantStep(PresentationNumber value, int digits) =>
        value.IsZero ? new(0, 0) : new(1, value.DecimalExponent - digits + 1);

    private static DecimalNumber Ceiling125(PresentationNumber minimum)
    {
        if (minimum.IsZero) return new(0, 0);
        int exponent = minimum.DecimalExponent;
        foreach (int mantissa in new[] { 1, 2, 5 })
        {
            var step = new DecimalNumber(mantissa, exponent);
            if (step.Number.CompareTo(minimum) >= 0) return step;
        }
        return new(1, exponent + 1);
    }

    private static RoundedNumber Round(PresentationNumber value, DecimalNumber step)
    {
        if (value.IsZero) return new(new(0, 0), step);
        BigInteger multiples = (value / step.Number).RoundToInteger();
        var rounded = new DecimalNumber(multiples * step.Mantissa, step.Exponent);
        return new(rounded, step, isApproximateZero: rounded.IsZero);
    }
}
