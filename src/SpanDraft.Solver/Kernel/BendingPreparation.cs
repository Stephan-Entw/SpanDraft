using System.Collections.Immutable;
using static SpanDraft.Solver.Kernel.Binary64;

namespace SpanDraft.Solver.Kernel;

internal sealed record BendingPreparation(int EL, int EEI, int EF, ImmutableArray<int> StateExponents,
    int ForceExponent, int MomentExponent, int LineLoadExponent, bool IsUnloaded, ExcitationBounds Excitation,
    RoundCounted NormalizedEI)
{
    internal static BendingPreparation Create(MechanicalInput input, StationModel stations)
    {
        CheckInput(input.Length);
        CheckInput(input.EI);
        if (!IsPositive(input.Length) || !IsNormal(input.Length) ||
            !IsPositive(input.EI) || !IsNormal(input.EI))
            throw new NumericalRangeException("Length and EI must be positive normal reference scales.");
        foreach (RigidRestraint restraint in input.Restraints)
            CheckInput(restraint.Position);
        foreach (ElasticRestraint elastic in input.ElasticRestraints)
        {
            CheckInput(elastic.Position);
            CheckInput(elastic.TranslationalStiffness);
            CheckInput(elastic.RotationalStiffness);
        }
        foreach (TransversePointForce force in input.PointForces)
        {
            CheckInput(force.Position);
            CheckInput(force.Newtons);
        }
        foreach (AppliedPointMoment moment in input.PointMoments)
        {
            CheckInput(moment.Position);
            CheckInput(moment.NewtonMeters);
        }
        foreach (TransverseLineLoad load in input.LineLoads)
        {
            CheckInput(load.Start);
            CheckInput(load.End);
            CheckInput(load.StartNewtonsPerMeter);
            CheckInput(load.EndNewtonsPerMeter);
        }

        int eL = PowerOfTwoExponent(input.Length);
        int eEI = PowerOfTwoExponent(input.EI);
        var e = new RoundCounted(ScaleByPowerOfTwo(input.EI, -eEI), 1);
        e.RequireNormal();
        if (input.Length < ScaleByPowerOfTwo(1, -968))
            throw new NumericalRangeException("Beam length is below the contract range.");

        bool unloaded = BendingExcitation.IsUnloaded(input);
        ExcitationBounds excitation = unloaded ? default : BendingExcitation.Excitation(input);
        BendingPreparation preparation;
        try
        {
            checked
            {
                int eF = unloaded ? 0 : excitation.Exponent + 2 * eL - eEI;
                preparation = new(eL, eEI, eF, [eL + eF, eF, eEI - eL + eF, eEI - 2 * eL + eF],
                    2 * eL - eEI - eF, eL - eEI - eF, 3 * eL - eEI - eF, unloaded, excitation, e);
            }
        }
        catch (OverflowException)
        {
            throw new NumericalRangeException("The normalization exponents exceed the integer range.");
        }

        foreach (TransversePointForce force in input.PointForces)
            CheckScaled(force.Newtons, preparation.ForceExponent);
        foreach (AppliedPointMoment moment in input.PointMoments)
            CheckScaled(moment.NewtonMeters, preparation.MomentExponent);
        foreach (TransverseLineLoad load in input.LineLoads)
        {
            CheckScaled(load.StartNewtonsPerMeter, preparation.LineLoadExponent);
            CheckScaled(load.EndNewtonsPerMeter, preparation.LineLoadExponent);
        }
        foreach (TransverseLineLoad load in input.LineLoads)
        {
            double distance = ScaleByPowerOfTwo(load.End - load.Start, -eL);
            if (distance < ScaleByPowerOfTwo(1, -969))
                throw new NumericalRangeException("The normalized distributed load length is too small.");
        }
        for (int i = 1; i < stations.MechanicalStations.Length; i++)
            if (stations.MechanicalStations[i].Position - stations.MechanicalStations[i - 1].Position <= Lambda)
                throw new NumericalRangeException("The mechanical station distance is outside its range.");
        return preparation;
    }

    /// <summary>
    /// The transverse spring stiffness k in the units of the normalized state, k·2^(3·e_L − e_EI): an exact power-of-two
    /// scaling that must stay normal. It has no rounding, so it enters the matrix without a data error.
    /// </summary>
    internal RoundCounted ScaledTranslationalStiffness(double stiffness) =>
        ScaleTranslationalStiffness(stiffness, EL, EEI);

    internal RoundCounted ScaledRotationalStiffness(double stiffness) =>
        ScaleRotationalStiffness(stiffness, EL, EEI);

    internal static RoundCounted ScaleTranslationalStiffness(double stiffness, int lengthExponent, int stiffnessExponent) =>
        ScaleStiffness(stiffness, () => checked(3 * lengthExponent - stiffnessExponent));

    internal static RoundCounted ScaleRotationalStiffness(double stiffness, int lengthExponent, int stiffnessExponent) =>
        ScaleStiffness(stiffness, () => checked(lengthExponent - stiffnessExponent));

    private static RoundCounted ScaleStiffness(double stiffness, Func<int> exponent)
    {
        int shift;
        try
        {
            shift = exponent();
        }
        catch (OverflowException)
        {
            throw new NumericalRangeException("The spring stiffness exponent exceeds the integer range.");
        }
        var scaled = new RoundCounted(ScaleByPowerOfTwo(stiffness, shift));
        scaled.RequireNormal();
        return scaled;
    }

    private static void CheckInput(double value)
    {
        if (!IsZero(value) && !IsNormal(value))
            throw new NumericalRangeException("Every required nonzero input must be finite and normal.");
    }

    private static void CheckScaled(double value, int exponent)
    {
        if (IsZero(value))
            return;
        double scaled = ScaleByPowerOfTwo(value, exponent);
        if (!IsFinite(scaled) || !(Abs(scaled) > Lambda))
            throw new NumericalRangeException("A normalized load primitive is outside its range.");
    }
}

internal readonly record struct TransferCoefficients(RoundCounted A, RoundCounted AE,
    RoundCounted A2, RoundCounted A3)
{
    internal static TransferCoefficients Create(double start, double end, BendingPreparation preparation)
    {
        RoundCounted a = RoundCounted.Distance(end, start, preparation.EL);
        RoundCounted e = preparation.NormalizedEI;
        RoundCounted square = a * a;
        var coefficients = new TransferCoefficients(a, a / e, (square / 2) / e, ((square * a) / 6) / e);
        coefficients.A.RequireNormal();
        coefficients.AE.RequireNormal();
        coefficients.A2.RequireNormal();
        coefficients.A3.RequireNormal();
        return coefficients;
    }
}
