using static SpanDraft.Solver.Kernel.Binary64;
using static SpanDraft.Solver.Kernel.Directed;

namespace SpanDraft.Solver.Kernel;

internal readonly struct Bounded
{
    internal Bounded(double value, double error = 0, double? magnitude = null)
    {
        if (!IsFinite(value))
            throw new NumericalRangeException("The bounded value must be finite.");

        Value = value;
        Error = StoreUpperBound(error);
        Magnitude = StoreUpperBound(magnitude ?? Abs(value));
        if (MagnitudeBits(Magnitude) < MagnitudeBits(value))
            throw new NumericalRangeException("The magnitude bound must enclose the stored value.");
    }

    internal double Value { get; }
    internal double Error { get; }
    internal double Magnitude { get; }

    private bool IsExactZero => IsZero(Magnitude) && IsZero(Error);
    private double ErrorWithDaz => Add(Error, Math.Min(Magnitude, Lambda), upper: true);

    public static Bounded operator +(Bounded left, Bounded right)
    {
        if (left.IsExactZero)
            return right;
        if (right.IsExactZero)
            return left;

        double error = Add(left.ErrorWithDaz, right.ErrorWithDaz, upper: true);
        double magnitude = Add(left.Magnitude, right.Magnitude, upper: true);
        return Result(left.Value + right.Value, error, magnitude);
    }

    public static Bounded operator -(Bounded value) =>
        new(Negate(value.Value), value.Error, value.Magnitude);

    public static Bounded operator -(Bounded left, Bounded right) => left + -right;

    public static Bounded operator *(Bounded left, Bounded right)
    {
        if (left.IsExactZero || right.IsExactZero)
            return default;

        double a = left.ErrorWithDaz;
        double b = right.ErrorWithDaz;
        double first = Multiply(a, Add(right.Magnitude, b, upper: true), upper: true);
        double second = Multiply(left.Magnitude, b, upper: true);
        double error = Add(first, second, upper: true);
        double magnitude = Multiply(left.Magnitude, right.Magnitude, upper: true);
        return Result(left.Value * right.Value, error, magnitude);
    }

    public static Bounded operator /(Bounded left, Bounded right)
    {
        double divisor = Abs(right.Value);
        if (!IsNormal(divisor))
            throw new NumericalRangeException("The stored divisor must be normal.");

        double lower = Subtract(divisor, right.Error, upper: false);
        if (!IsPositive(lower))
            throw new NumericalRangeException("The divisor must have a positive lower magnitude bound.");
        if (left.IsExactZero)
            return default;

        double magnitude = Divide(left.Magnitude, divisor, upper: true);
        double divisorError = Multiply(magnitude, right.Error, upper: true);
        double error = Divide(Add(left.ErrorWithDaz, divisorError, upper: true), lower, upper: true);
        return Result(left.Value / right.Value, error, magnitude);
    }

    private static Bounded Result(double value, double error, double magnitude)
    {
        double rounding = Add(Multiply(U, magnitude, upper: true), Lambda, upper: true);
        return new Bounded(value, Add(error, rounding, upper: true), Add(magnitude, rounding, upper: true));
    }

    private static double StoreUpperBound(double bound)
    {
        if (IsZero(bound))
            return 0;
        if (!IsPositive(bound))
            throw new NumericalRangeException("Error and magnitude bounds must be nonnegative.");
        return IsSubnormal(bound) ? Lambda : bound;
    }
}
