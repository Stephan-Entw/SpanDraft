using SpanDraft.Core.Units;

namespace SpanDraft.Solver;

/// <summary>A one-sided limit at a node. Both sides agree in an element's interior.</summary>
public enum EvaluationSide { Left, Right }

/// <summary>
/// Analytical fields at x: u rightwards, w upwards, θ = dw/dx in radians.
/// N is positive in tension; M is positive in sagging; V = dM/dx.
/// These internal signs are distinct from the signs of applied forces and moments.
/// </summary>
public sealed class BeamSectionResult
{
    internal BeamSectionResult(Length position, double axialDisplacement, double transverseDisplacement,
        double rotationRadians, double axialForce, double shearForce, double bendingMoment)
    {
        NumericalGuard.AllFinite([axialDisplacement, transverseDisplacement, rotationRadians,
            axialForce, shearForce, bendingMoment], "Section result");
        Position = position;
        AxialDisplacement = Displacement.FromMeters(axialDisplacement);
        TransverseDisplacement = Displacement.FromMeters(transverseDisplacement);
        RotationRadians = rotationRadians;
        AxialForce = Force.FromNewtons(axialForce);
        ShearForce = Force.FromNewtons(shearForce);
        BendingMoment = Moment.FromNewtonMeters(bendingMoment);
    }

    public Length Position { get; }
    public Displacement AxialDisplacement { get; }
    public Displacement TransverseDisplacement { get; }
    public double RotationRadians { get; }
    public Force AxialForce { get; }
    public Force ShearForce { get; }
    public Moment BendingMoment { get; }
}
