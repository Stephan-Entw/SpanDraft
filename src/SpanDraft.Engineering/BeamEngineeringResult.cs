using SpanDraft.Core.Units;
using SpanDraft.Solver;

namespace SpanDraft.Engineering;

/// <summary>Immutable elastic bending indicators, without a normative safety claim or fiber assignment.</summary>
public sealed class BeamEngineeringResult
{
    internal BeamEngineeringResult(BeamExtremum<Displacement> displacement,
        BeamExtremum<Moment> moment, IReadOnlyList<BeamSegmentAssessment> segments, int governingSegmentIndex)
    {
        CriticalTransverseDisplacement = displacement;
        TransverseDisplacementMagnitude = Displacement.FromMeters(Math.Abs(displacement.Value.Meters));
        CriticalBendingMoment = moment;
        BendingMomentMagnitude = Moment.FromNewtonMeters(Math.Abs(moment.Value.NewtonMeters));
        // A copy behind a read-only wrapper: neither the caller's array nor a cast of this list can change the result.
        SegmentAssessments = Array.AsReadOnly(segments.ToArray());
        GoverningSegmentIndex = governingSegmentIndex;
    }

    /// <summary>Original signed extremum; Side is null at an interior analytical extremum.</summary>
    public BeamExtremum<Displacement> CriticalTransverseDisplacement { get; }
    public Displacement TransverseDisplacementMagnitude { get; }
    /// <summary>Original signed extremum; positive means sagging, negative means hogging.</summary>
    public BeamExtremum<Moment> CriticalBendingMoment { get; }
    public Moment BendingMomentMagnitude { get; }
    /// <summary>The assessment of every beam segment, in segment order.</summary>
    public IReadOnlyList<BeamSegmentAssessment> SegmentAssessments { get; }
    /// <summary>The segment with the smallest safety factor; its stress and safety factor are the beam's.</summary>
    public int GoverningSegmentIndex { get; }
    public BeamSegmentAssessment GoverningSegment => SegmentAssessments[GoverningSegmentIndex];
    /// <summary>Non-negative |M|/W in pascals of the governing segment; does not identify a tensile or compressive fiber.</summary>
    public Pressure MaximumBendingStress => GoverningSegment.MaximumBendingStress;
    public Length BendingStressPosition => GoverningSegment.CriticalBendingMoment.Position;
    public EvaluationSide? BendingStressSide => GoverningSegment.CriticalBendingMoment.Side;
    /// <summary>
    /// Dimensionless material strength/σ of the governing segment. Positive infinity denotes zero bending stress
    /// (or floating-point overflow).
    /// </summary>
    public double SafetyFactor => GoverningSegment.SafetyFactor;
}
