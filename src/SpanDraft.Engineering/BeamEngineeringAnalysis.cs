using SpanDraft.Core.Units;
using SpanDraft.Solver;

namespace SpanDraft.Engineering;

/// <summary>Elastic bending postprocessing of an existing solution; not a normative strength assessment.</summary>
public static class BeamEngineeringAnalysis
{
    /// <summary>Uses only analytical extrema and the original solution's section and material.</summary>
    public static BeamEngineeringResult Analyze(BeamSolution solution)
    {
        ArgumentNullException.ThrowIfNull(solution);

        BeamExtrema extrema = solution.Extrema;
        var displacement = SelectMaximumAbsolute(extrema.MinimumTransverseDisplacement,
            extrema.MaximumTransverseDisplacement, value => value.Meters);
        var moment = SelectMaximumAbsolute(extrema.MinimumBendingMoment,
            extrema.MaximumBendingMoment, value => value.NewtonMeters);
        var axis = solution.Beam.BendingAxisProperties;
        double governingModulus = Math.Min(axis.PositiveSectionModulus.CubicMeters,
            axis.NegativeSectionModulus.CubicMeters);
        var stress = Pressure.FromPascals(Math.Abs(moment.Value.NewtonMeters) / governingModulus);
        double safetyFactor = stress.Pascals == 0 ? double.PositiveInfinity :
            solution.Beam.Material.YieldStrength.Pascals / stress.Pascals;

        return new BeamEngineeringResult(displacement, moment, stress, safetyFactor);
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
