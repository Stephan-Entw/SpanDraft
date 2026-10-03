namespace SpanDraft.Core;

internal static class DomainGuard
{
    internal static double Finite(double value, string parameterName)
    {
        if (!double.IsFinite(value))
            throw new ArgumentOutOfRangeException(parameterName, value, "The value must be finite.");
        return value;
    }

    internal static double NonNegative(double value, string parameterName)
    {
        Finite(value, parameterName);
        if (value < 0)
            throw new ArgumentOutOfRangeException(parameterName, value, "The value must be non-negative.");
        return value;
    }

    internal static double Positive(double value, string parameterName)
    {
        Finite(value, parameterName);
        if (value <= 0)
            throw new ArgumentOutOfRangeException(parameterName, value, "The value must be positive.");
        return value;
    }
}
