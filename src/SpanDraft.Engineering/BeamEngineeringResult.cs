using SpanDraft.Core.Units;
using SpanDraft.Solver;

namespace SpanDraft.Engineering;

/// <summary>Immutable elastic bending indicators, without a normative safety claim or fiber assignment.</summary>
public sealed class BeamEngineeringResult
{
    internal BeamEngineeringResult(BeamExtremum<Displacement> displacement,
        BeamExtremum<Moment> moment, Pressure stress, double safetyFactor)
    {
        CriticalTransverseDisplacement = displacement;
        TransverseDisplacementMagnitude = Displacement.FromMeters(Math.Abs(displacement.Value.Meters));
        CriticalBendingMoment = moment;
        BendingMomentMagnitude = Moment.FromNewtonMeters(Math.Abs(moment.Value.NewtonMeters));
        MaximumBendingStress = stress;
        SafetyFactor = safetyFactor;
    }

    /// <summary>Original signed extremum; Side is null at an interior analytical extremum.</summary>
    public BeamExtremum<Displacement> CriticalTransverseDisplacement { get; }
    public Displacement TransverseDisplacementMagnitude { get; }
    /// <summary>Original signed extremum; positive means sagging, negative means hogging.</summary>
    public BeamExtremum<Moment> CriticalBendingMoment { get; }
    public Moment BendingMomentMagnitude { get; }
    /// <summary>Non-negative |M|/W in pascals; does not identify a tensile or compressive fiber.</summary>
    public Pressure MaximumBendingStress { get; }
    public Length BendingStressPosition => CriticalBendingMoment.Position;
    public EvaluationSide? BendingStressSide => CriticalBendingMoment.Side;
    /// <summary>Dimensionless Re/σ. Positive infinity denotes zero bending stress (or floating-point overflow).</summary>
    public double SafetyFactor { get; }
}
