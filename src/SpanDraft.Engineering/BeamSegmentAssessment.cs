using SpanDraft.Core.Units;
using SpanDraft.Solver;

namespace SpanDraft.Engineering;

/// <summary>The elastic bending assessment of one beam segment with its own section and material.</summary>
public sealed class BeamSegmentAssessment
{
    internal BeamSegmentAssessment(int segmentIndex, BeamExtremum<Moment> moment, Pressure stress, double safetyFactor)
    {
        SegmentIndex = segmentIndex;
        CriticalBendingMoment = moment;
        MaximumBendingStress = stress;
        SafetyFactor = safetyFactor;
    }

    /// <summary>Zero-based index into the beam's segments.</summary>
    public int SegmentIndex { get; }

    /// <summary>The signed moment extremum of largest magnitude within the segment; a boundary uses the inner side.</summary>
    public BeamExtremum<Moment> CriticalBendingMoment { get; }

    /// <summary>Non-negative |M|/W of the segment in pascals, with the smaller of its two section moduli.</summary>
    public Pressure MaximumBendingStress { get; }

    /// <summary>The segment material's strength divided by <see cref="MaximumBendingStress"/>; infinite at zero stress.</summary>
    public double SafetyFactor { get; }
}
