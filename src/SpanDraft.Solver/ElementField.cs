using SpanDraft.Core.Beams;
using SpanDraft.Core.Loads;
using SpanDraft.Core.Units;

namespace SpanDraft.Solver;

/// <summary>One quartic displacement polynomial in t = (x-x1)/Le; no sampling or extra solve.</summary>
internal sealed class ElementField
{
    private readonly BeamNodeResult left;
    private readonly BeamNodeResult right;
    private readonly double length;
    private readonly double ei;
    private readonly double axialForce;
    // w(t) = c0 + c1*t + c2*t² + c3*t³ + c4*t⁴.
    private readonly double c0, c1, c2, c3, c4;

    internal ElementField(BeamModel beam, BeamElement element, BeamNodeResult left, BeamNodeResult right)
    {
        this.left = left;
        this.right = right;
        length = element.LengthMeters;
        ei = NumericalGuard.Positive(beam.Material.YoungsModulus.Pascals *
            beam.Section.SecondMomentOfArea.MetersToTheFourth, "Bending rigidity EI");
        double ea = NumericalGuard.Positive(beam.Material.YoungsModulus.Pascals *
            beam.Section.Area.SquareMeters, "Axial rigidity EA");
        axialForce = NumericalGuard.Finite((ea / length) *
            (right.AxialDisplacement.Meters - left.AxialDisplacement.Meters), "Axial force");

        double intensity = 0;
        foreach (UniformDistributedLoad load in beam.Loads.OfType<UniformDistributedLoad>())
            if (element.IsCoveredBy(load))
                intensity = NumericalGuard.Finite(intensity + load.Intensity.NewtonsPerMeter, "Resultant UDL");

        // The bubble q*s²*(Le-s)²/(24EI) satisfies EI*w''''=q and leaves
        // both nodal displacements and both nodal slopes unchanged.
        double bubble = intensity == 0 ? 0 : NumericalGuard.Finite(
            (((intensity / ei) * length) * length / 24 * length) * length, "UDL displacement coefficient");
        double delta = right.TransverseDisplacement.Meters - left.TransverseDisplacement.Meters;
        double slope1 = length * left.RotationRadians, slope2 = length * right.RotationRadians;
        c0 = left.TransverseDisplacement.Meters;
        c1 = slope1;
        c2 = 3 * delta - 2 * slope1 - slope2 + bubble;
        c3 = -2 * delta + slope1 + slope2 - 2 * bubble;
        c4 = bubble;
        NumericalGuard.AllFinite([c0, c1, c2, c3, c4], "Displacement polynomial");
    }

    internal Length LeftPosition => left.Position;
    internal Length RightPosition => right.Position;

    internal BeamSectionResult EvaluateAt(Length position)
    {
        // Exact node values avoid cancellation in the polynomial at its endpoints.
        if (position == left.Position) return Evaluate(0, position, left);
        if (position == right.Position) return Evaluate(1, position, right);
        return Evaluate((position.Meters - left.Position.Meters) / length, position, null);
    }

    internal BeamSectionResult EvaluateNormalized(double t)
    {
        if (t == 0) return EvaluateAt(left.Position);
        if (t == 1) return EvaluateAt(right.Position);
        var position = Length.FromMeters(Math.Clamp(left.Position.Meters + t * length,
            left.Position.Meters, right.Position.Meters));
        // Use the same representable global position that EvaluateAt will later receive.
        return EvaluateAt(position);
    }

    private BeamSectionResult Evaluate(double t, Length position, BeamNodeResult? node)
    {
        double u = node?.AxialDisplacement.Meters ??
            left.AxialDisplacement.Meters + (right.AxialDisplacement.Meters - left.AxialDisplacement.Meters) * t;
        double w = node?.TransverseDisplacement.Meters ?? (((c4 * t + c3) * t + c2) * t + c1) * t + c0;
        double theta = node?.RotationRadians ?? (((4 * c4 * t + 3 * c3) * t + 2 * c2) * t + c1) / length;
        double moment = ((12 * c4 * t + 6 * c3) * t + 2 * c2) / length / length * ei;
        double shear = (24 * c4 * t + 6 * c3) / length / length / length * ei;
        return new BeamSectionResult(position, u, w, theta, axialForce, shear, moment);
    }

    internal IReadOnlyList<double> StationaryDisplacements() =>
        PolynomialRoots.InUnitInterval(c1, 2 * c2, 3 * c3, 4 * c4);

    internal IReadOnlyList<double> StationaryMoments() =>
        PolynomialRoots.InUnitInterval(6 * c3, 24 * c4);
}
