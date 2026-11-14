using System.Numerics;
using static SpanDraft.Solver.Kernel.Binary64;
using static SpanDraft.Solver.Kernel.Directed;

namespace SpanDraft.Solver.Kernel;

internal readonly record struct ExcitationBounds(int Exponent, double Lower, double Upper);

internal static class BendingExcitation
{
    internal static bool IsUnloaded(MechanicalInput input) =>
        input.PointForces.All(force => IsZero(force.Newtons)) &&
        input.PointMoments.All(moment => IsZero(moment.NewtonMeters)) &&
        input.LineLoads.All(load => IsZero(load.StartNewtonsPerMeter) && IsZero(load.EndNewtonsPerMeter));

    internal static ExcitationBounds Excitation(MechanicalInput input)
    {
        var terms = new List<ExcitationBounds>();
        foreach (TransversePointForce force in input.PointForces)
            if (!IsZero(force.Newtons))
                terms.Add(Mantissa(force.Newtons));

        ExcitationBounds length = default;
        foreach (AppliedPointMoment moment in input.PointMoments)
        {
            if (IsZero(moment.NewtonMeters))
                continue;
            if (length.Lower == 0)
            {
                if (!IsPositive(input.Length))
                    throw new NumericalRangeException("Beam length must be positive for moment excitation.");
                length = Mantissa(input.Length);
            }

            ExcitationBounds term = Mantissa(moment.NewtonMeters);
            terms.Add(new(term.Exponent - length.Exponent,
                Divide(term.Lower, length.Upper, upper: false),
                Divide(term.Upper, length.Lower, upper: true)));
        }

        foreach (TransverseLineLoad load in input.LineLoads)
        {
            if (IsUnloadedLine(load))
                continue;

            BigInteger width = WidthInUnits(load);
            int widthExponent = (int)width.GetBitLength() + UnitExponent;
            double widthMantissa = RoundExact(width, BigInteger.One, UnitExponent - widthExponent);
            // The trapezoid norm ((|q1| + |q2|) / 2) * l has one term for equal end intensities
            // and one half term per nonzero end otherwise.
            if (load.IsConstant)
                AddLineTerm(load.StartNewtonsPerMeter, 0);
            else
            {
                AddLineTerm(load.StartNewtonsPerMeter, -1);
                AddLineTerm(load.EndNewtonsPerMeter, -1);
            }

            void AddLineTerm(double newtonsPerMeter, int halvings)
            {
                if (IsZero(newtonsPerMeter))
                    return;
                ExcitationBounds intensity = Mantissa(newtonsPerMeter);
                terms.Add(new(intensity.Exponent + widthExponent + halvings,
                    Multiply(intensity.Lower, Outward(widthMantissa, upper: false), upper: false),
                    Multiply(intensity.Upper, Outward(widthMantissa, upper: true), upper: true)));
            }
        }

        if (terms.Count == 0)
            return default;

        int exponent = terms.Max(term => term.Exponent);
        double lower = 0;
        double upper = 0;
        foreach (ExcitationBounds term in terms)
        {
            int shift = term.Exponent - exponent;
            lower = Math.Max(lower, Add(lower, DirectedScale(term.Lower, shift, upper: false), upper: false));
            upper = Add(upper, DirectedScale(term.Upper, shift, upper: true), upper: true);
        }

        int offset = PowerOfTwoExponent(lower);
        if (offset != PowerOfTwoExponent(upper))
            offset = ExactExcitationExponent(input) - exponent;
        return new(exponent + offset,
            DirectedScale(lower, -offset, upper: false), DirectedScale(upper, -offset, upper: true));
    }

    private static ExcitationBounds Mantissa(double value)
    {
        double magnitude = Abs(value);
        int exponent = PowerOfTwoExponent(magnitude) + 1;
        double mantissa = ScaleByPowerOfTwo(magnitude, -exponent);
        return new(exponent, mantissa, mantissa);
    }

    private static bool IsUnloadedLine(TransverseLineLoad load) =>
        IsZero(load.StartNewtonsPerMeter) && IsZero(load.EndNewtonsPerMeter);

    private static BigInteger WidthInUnits(TransverseLineLoad load)
    {
        BigInteger width = ExactUnits(load.End) - ExactUnits(load.Start);
        if (width.Sign <= 0)
            throw new NumericalRangeException("A distributed load must have positive exact width.");
        return width;
    }

    private static int ExactExcitationExponent(MechanicalInput input)
    {
        BigInteger forces = BigInteger.Zero;
        foreach (TransversePointForce force in input.PointForces)
            forces += BigInteger.Abs(ExactUnits(force.Newtons));

        BigInteger moments = BigInteger.Zero;
        foreach (AppliedPointMoment moment in input.PointMoments)
            moments += BigInteger.Abs(ExactUnits(moment.NewtonMeters));

        // Everything is doubled so that the half of the trapezoid norm stays an integer; the final exponent
        // is reduced by one. Align force and line terms to squared subnormal units, then add moments over
        // the common length.
        BigInteger sum = forces << (1 - UnitExponent);
        foreach (TransverseLineLoad load in input.LineLoads)
            if (!IsUnloadedLine(load))
                sum += (BigInteger.Abs(ExactUnits(load.StartNewtonsPerMeter)) +
                    BigInteger.Abs(ExactUnits(load.EndNewtonsPerMeter))) * WidthInUnits(load);
        if (moments.IsZero)
            return (int)sum.GetBitLength() - 1 + 2 * UnitExponent - 1;

        BigInteger length = ExactUnits(input.Length);
        BigInteger numerator = sum * length + (moments << (1 - 2 * UnitExponent));
        return ExactExponent(numerator, length) + 2 * UnitExponent - 1;
    }
}
