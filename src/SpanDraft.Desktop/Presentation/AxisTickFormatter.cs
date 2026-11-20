using System.Globalization;

namespace SpanDraft.Desktop.Presentation;

/// <summary>Shared notation and precision for the labels of one nice-tick axis.</summary>
public sealed class AxisTickFormatter
{
    private readonly double _scaledStep;
    private readonly string _pattern;

    private AxisTickFormatter(double scaledStep, int decimals, int? engineeringExponent)
    {
        _scaledStep = scaledStep;
        string digits = decimals == 0 ? "0" : "0." + new string('#', decimals);
        _pattern = digits + ";" + QuantityFormatter.MinusSign + digits;
        EngineeringExponent = engineeringExponent;
    }

    public int? EngineeringExponent { get; }

    public static AxisTickFormatter Create(int lowerTickIndex, int upperTickIndex, double stepMantissa, int stepExponent)
    {
        if (lowerTickIndex > upperTickIndex) throw new ArgumentOutOfRangeException(nameof(upperTickIndex));
        long largestIndex = Math.Max(Math.Abs((long)lowerTickIndex), Math.Abs((long)upperTickIndex));
        if (largestIndex == 0) return new(0, 0, null);
        if (stepMantissa is not (1 or 2 or 2.5 or 5 or 10))
            throw new ArgumentOutOfRangeException(nameof(stepMantissa));
        if (stepExponent is < -324 or > 309) throw new ArgumentOutOfRangeException(nameof(stepExponent));

        long smallestIndex = lowerTickIndex > 0 ? lowerTickIndex : upperTickIndex < 0 ? -(long)upperTickIndex : 1;
        int largestOrder = Order(largestIndex);
        int extraDigit = stepMantissa == 2.5 ? 1 : 0;
        int? engineeringExponent = Order(smallestIndex) >= -2 && largestOrder < 4 && -stepExponent + extraDigit <= 4
            ? null : (int)Math.Floor(largestOrder / 3d) * 3;
        int scaledExponent = stepExponent - (engineeringExponent ?? 0);
        return new(stepMantissa * Math.Pow(10, scaledExponent), Math.Max(0, -scaledExponent + extraDigit),
            engineeringExponent);

        int Order(long index)
        {
            // Count coefficient digits before applying the step exponent, which may be outside double's range.
            long coefficient = (long)(index * stepMantissa);
            int order = stepExponent;
            for (; coefficient >= 10; coefficient /= 10) order++;
            return order;
        }
    }

    public string Format(int index, CultureInfo? culture = null) =>
        index == 0 ? "0" : (index * _scaledStep).ToString(_pattern, culture ?? CultureInfo.CurrentCulture);

    public string FormatTitleUnit(string symbol) => EngineeringExponent is { } exponent
        ? $" [10{Superscript(exponent)} {symbol}]" : $" [{symbol}]";

    private static string Superscript(int exponent) => string.Concat(exponent.ToString(CultureInfo.InvariantCulture)
        .Select(c => c == '-' ? '⁻' : "⁰¹²³⁴⁵⁶⁷⁸⁹"[c - '0']));
}
