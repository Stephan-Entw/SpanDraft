using System.Numerics;
using static SpanDraft.Solver.Kernel.Binary64;

namespace SpanDraft.Solver.Kernel;

internal readonly record struct RoundCounted(double Value, long Count = 0, bool Underflow = false)
{
    public static RoundCounted operator -(RoundCounted value) =>
        new(Negate(value.Value), value.Count, value.Underflow);

    public static RoundCounted operator *(RoundCounted a, RoundCounted b) =>
        Result(a.Value * b.Value, checked(a.Count + b.Count + 1), a.Underflow || b.Underflow,
            !IsZero(a.Value) && !IsZero(b.Value));

    public static RoundCounted operator /(RoundCounted a, RoundCounted b) =>
        Result(a.Value / b.Value, checked(a.Count + b.Count + 1), a.Underflow || b.Underflow,
            !IsZero(a.Value) && !IsZero(b.Value));

    public static RoundCounted operator *(RoundCounted a, int factor) =>
        Result(a.Value * factor, checked(a.Count + IntegerRounding(factor)), a.Underflow,
            !IsZero(a.Value) && factor != 0);

    public static RoundCounted operator /(RoundCounted a, int divisor) =>
        Result(a.Value / divisor, checked(a.Count + IntegerRounding(divisor)), a.Underflow,
            !IsZero(a.Value));

    private static int IntegerRounding(int value) => value >= 0 && (value & (value - 1)) == 0 ? 0 : 1;

    private static RoundCounted Result(double value, long count, bool underflow, bool requiredNonzero) =>
        new(value, count, underflow || (!IsZero(value) && Abs(value) <= Lambda) ||
            (requiredNonzero && IsZero(value)));

    internal void RequireNormal()
    {
        if (Underflow || !IsFinite(Value) || !(Abs(Value) > Lambda))
            throw new NumericalRangeException("A required transfer coefficient or intermediate is outside its range.");
    }

    internal double Bound() => ComputedBound(Count, Value);

    internal static double ComputedBound(long count, double value)
    {
        if (count == 0)
            return 0;
        double gamma = Directed.GammaUpper(count);
        if (!(gamma < 1))
            throw new NumericalRangeException("The relative coefficient bound must be below one.");
        return Directed.Multiply(Directed.Divide(gamma,
            Directed.Subtract(1, gamma, upper: false), upper: true), Abs(value), upper: true);
    }

    internal static RoundCounted Distance(double x, double origin, int lengthExponent)
    {
        double raw = x - origin;
        double value = ScaleByPowerOfTwo(raw, checked(-lengthExponent));
        return new(value, 1, !SameValue(x, origin) && (Abs(raw) <= Lambda || Abs(value) <= Lambda));
    }

    internal static Bounded BoundedDistance(double x, double origin, int lengthExponent)
    {
        if (SameValue(x, origin))
            return default;
        // Protected gradual subtraction, including coordinates that would otherwise be lost to DAZ.
        double raw = RoundExact(ExactUnits(x) - ExactUnits(origin), BigInteger.One, UnitExponent);
        double value = ScaleByPowerOfTwo(raw, checked(-lengthExponent));
        double error = Directed.Add(Directed.Multiply(U, Abs(value), upper: true),
            Directed.Add(Directed.DirectedScale(Lambda, checked(-lengthExponent), upper: true), Lambda,
                upper: true), upper: true);
        return new(value, error);
    }
}
