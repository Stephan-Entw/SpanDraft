using SpanDraft.Core.Beams;
using SpanDraft.Core.Units;
using SpanDraft.Solver;

namespace SpanDraft.Engineering;

/// <summary>Elastic bending postprocessing of an existing solution; not a normative strength assessment.</summary>
public static class BeamEngineeringAnalysis
{
    /// <summary>
    /// Uses only analytical extrema and the section and material of each segment. The governing segment has the
    /// smallest safety factor; a tie goes to the smaller position of its critical moment, then to the smaller
    /// index. Stress and safety factor of the result are those of that segment. Notch effects and local stresses
    /// at a change of section are not assessed.
    /// </summary>
    public static BeamEngineeringResult Analyze(BeamSolution solution)
    {
        ArgumentNullException.ThrowIfNull(solution);

        BeamExtrema extrema = solution.Extrema;
        var displacement = SelectMaximumAbsolute(extrema.MinimumTransverseDisplacement,
            extrema.MaximumTransverseDisplacement, value => value.Meters);
        var moment = SelectMaximumAbsolute(extrema.MinimumBendingMoment,
            extrema.MaximumBendingMoment, value => value.NewtonMeters);
        var assessments = new BeamSegmentAssessment[solution.Beam.Segments.Count];
        for (int i = 0; i < assessments.Length; i++)
        {
            BeamSegment segment = solution.Beam.Segments[i];
            BeamExtrema own = solution.SegmentExtrema[i];
            var segmentMoment = SelectMaximumAbsolute(own.MinimumBendingMoment, own.MaximumBendingMoment,
                value => value.NewtonMeters);
            var axis = segment.BendingAxisProperties;
            double governingModulus = Math.Min(axis.PositiveSectionModulus.CubicMeters,
                axis.NegativeSectionModulus.CubicMeters);
            var stress = Pressure.FromPascals(Math.Abs(segmentMoment.Value.NewtonMeters) / governingModulus);
            double safetyFactor = stress.Pascals == 0 ? double.PositiveInfinity :
                segment.Material.YieldStrength.Pascals / stress.Pascals;
            assessments[i] = new BeamSegmentAssessment(i, segmentMoment, stress, safetyFactor);
        }

        return new BeamEngineeringResult(displacement, moment, assessments, Governing(assessments));
    }

    private static int Governing(BeamSegmentAssessment[] assessments)
    {
        int governing = 0;
        for (int i = 1; i < assessments.Length; i++)
            if (SmallerSafety(assessments[i], assessments[governing]))
                governing = i;
        return governing;
    }

    private static bool SmallerSafety(BeamSegmentAssessment candidate, BeamSegmentAssessment current)
    {
        double a = candidate.SafetyFactor, b = current.SafetyFactor;
        bool tied = a == b || double.IsFinite(a) && double.IsFinite(b) &&
            Math.Abs(a - b) <= 64 * 2.2204460492503131e-16 * Math.Max(a, b);
        if (!tied) return a < b;
        // Equal safety: the earlier critical position wins; the earlier index keeps a full tie.
        return candidate.CriticalBendingMoment.Position.Meters < current.CriticalBendingMoment.Position.Meters;
    }

    internal static BeamExtremum<T> SelectMaximumAbsolute<T>(BeamExtremum<T> minimum,
        BeamExtremum<T> maximum, Func<T, double> scalar)
    {
        double a = Math.Abs(scalar(minimum.Value)), b = Math.Abs(scalar(maximum.Value));
        double scale = Math.Max(a, b);
        // Same relative roundoff order as the frozen solver, without an absolute SI floor.
        // double.Epsilon is not the relative precision of double.
        const double roundoff = 64 * 2.2204460492503131e-16;
        bool tied = scale == 0 || Math.Abs(a / scale - b / scale) <= roundoff;
        if (!tied) return a > b ? minimum : maximum;

        bool maximumPrecedes = maximum.Position.Meters < minimum.Position.Meters ||
            (maximum.Position == minimum.Position &&
                maximum.Side.GetValueOrDefault() < minimum.Side.GetValueOrDefault());
        return maximumPrecedes ? maximum : minimum;
    }
}
