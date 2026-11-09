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
    private readonly double ea;
    // w(t) = c0 + c1*t + c2*t² + c3*t³ + c4*t⁴.
    private readonly double c0, c1, c2, c3, c4, c5;
    // Equilibrium in s = t*length: M(s) = m + v*s + q*s²/2, V(s) = v + q*s, N(s) = n - nq*s.
    private readonly double m, v, q, qs, n, nq;
    // At a hinge the rotation of this field's right end differs from the node's right-side value.
    private readonly double rightRotation;

    internal ElementField(BeamModel beam, BeamElement element, BeamNodeResult left, BeamNodeResult right)
    {
        this.left = left;
        this.right = right;
        length = element.LengthMeters;
        ei = NumericalGuard.Positive(beam.Material.YoungsModulus.Pascals *
            beam.BendingAxisProperties.SecondMomentOfArea.MetersToTheFourth, "Bending rigidity EI");
        ea = NumericalGuard.Positive(beam.Material.YoungsModulus.Pascals *
            beam.Section.Area.SquareMeters, "Axial rigidity EA");
        n = NumericalGuard.Finite((ea / length) *
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
        m = 2 * c2 / length / length * ei;
        v = 6 * c3 / length / length / length * ei;
        q = 24 * c4 / length / length / length / length * ei;
        rightRotation = right.RotationRadians;
        c5 = qs = 0;
    }

    /// <summary>Uses the segment's equilibrium state directly, so a short segment amplifies nothing.</summary>
    internal ElementField(BeamModel beam, BeamElement element, BeamNodeResult left, BeamNodeResult right, SegmentState state,
        double rightRotation)
    {
        this.left = left;
        this.right = right;
        this.rightRotation = rightRotation;
        length = element.LengthMeters;
        ei = state.BendingRigidity > 0 ? state.BendingRigidity : NumericalGuard.Positive(beam.Material.YoungsModulus.Pascals *
            beam.BendingAxisProperties.SecondMomentOfArea.MetersToTheFourth, "Bending rigidity EI");
        ea = NumericalGuard.Positive(beam.Material.YoungsModulus.Pascals *
            beam.Section.Area.SquareMeters, "Axial rigidity EA");
        m = state.Moment;
        qs = state.IntensitySlope;
        v = state.Shear;
        q = state.Intensity;
        n = state.AxialForce;
        nq = state.AxialIntensity;
        c0 = state.Displacement;
        c1 = length * state.Rotation;
        // EI*w'' = M + EI*κ with an imposed curvature κ (temperature); q varies linearly with slope qs.
        c2 = (m / ei + state.ImposedCurvature) * length * length / 2;
        c3 = v / ei * length * length * length / 6;
        c4 = q / ei * length * length * length * length / 24;
        c5 = qs / ei * length * length * length * length * length / 120;
        NumericalGuard.AllFinite([c0, c1, c2, c3, c4, c5, m, v, q, qs, n, nq], "Segment state");
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
        double s = t * length;
        double u = node?.AxialDisplacement.Meters ??
            left.AxialDisplacement.Meters + (n * s - nq * s * s / 2) / ea;
        double w = node?.TransverseDisplacement.Meters ?? ((((c5 * t + c4) * t + c3) * t + c2) * t + c1) * t + c0;
        double theta = node is null ? ((((5 * c5 * t + 4 * c4) * t + 3 * c3) * t + 2 * c2) * t + c1) / length :
            node == right ? rightRotation : node.RotationRadians;
        double moment = m + v * s + q * s * s / 2 + qs * s * s * s / 6;
        double shear = v + q * s + qs * s * s / 2;
        return new BeamSectionResult(position, u, w, theta, n - nq * s, shear, moment);
    }

    internal IReadOnlyList<double> StationaryDisplacements() =>
        PolynomialRoots.InUnitInterval(c1, 2 * c2, 3 * c3, 4 * c4, 5 * c5);

    /// <summary>V is stationary where q = 0; only a linearly varying load can put that inside a segment.</summary>
    internal IReadOnlyList<double> StationaryShears() =>
        PolynomialRoots.InUnitInterval(q, qs * length);

    internal IReadOnlyList<double> StationaryMoments() =>
        PolynomialRoots.InUnitInterval(v, q * length, qs * length * length / 2);
}
