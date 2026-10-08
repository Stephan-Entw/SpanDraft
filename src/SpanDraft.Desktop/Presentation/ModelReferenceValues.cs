using SpanDraft.Analysis;
using SpanDraft.Core.Loads;

namespace SpanDraft.Desktop.Presentation;

/// <summary>
/// Immutable presentation references in canonical SI, extracted from one completed
/// analysis. No fields are evaluated and no solver or engineering calculation is started.
/// </summary>
public sealed class ModelReferenceValues
{
    public ModelReferenceValues(double beamLengthMeters, double forceReferenceNewtons,
        double momentReferenceNewtonMeters, double yieldStrengthPascals)
        : this(NonNegative(beamLengthMeters), NonNegative(forceReferenceNewtons),
            NonNegative(momentReferenceNewtonMeters), NonNegative(yieldStrengthPascals)) { }

    private ModelReferenceValues(PresentationNumber length, PresentationNumber force,
        PresentationNumber moment, PresentationNumber yieldStrength)
    {
        Length = length;
        Force = force;
        Moment = moment;
        YieldStrength = yieldStrength;
    }

    // The factory retains extended-range reference arithmetic internally. A double
    // accessor can be infinite if a load resultant exceeds double's finite range.
    public double BeamLengthMeters => Length.ToDouble();
    public double ForceReferenceNewtons => Force.ToDouble();
    public double MomentReferenceNewtonMeters => Moment.ToDouble();
    public double YieldStrengthPascals => YieldStrength.ToDouble();
    internal PresentationNumber Length { get; }
    internal PresentationNumber Force { get; }
    internal PresentationNumber Moment { get; }
    internal PresentationNumber YieldStrength { get; }

    public static ModelReferenceValues FromAnalysis(BeamAnalysisResult analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        var beam = analysis.Beam;
        var solution = analysis.Solution;
        var length = PresentationNumber.FromDouble(beam.Length.Meters);
        var force = PresentationNumber.Zero;
        foreach (BeamLoad load in beam.Loads)
        {
            PresentationNumber candidate = load switch
            {
                PointForce point => PresentationNumber.FromDouble(point.Force.Newtons).Abs(),
                PointMoment couple => PresentationNumber.FromDouble(couple.Moment.NewtonMeters).Abs() / length,
                UniformDistributedLoad uniform => PresentationNumber.FromDouble(uniform.Intensity.NewtonsPerMeter).Abs()
                    * (PresentationNumber.FromDouble(uniform.EndPosition.Meters)
                        - PresentationNumber.FromDouble(uniform.StartPosition.Meters)),
                _ => throw new ArgumentException("Unsupported load kind.", nameof(analysis))
            };
            force = PresentationNumber.Max(force, candidate);
        }
        force = PresentationNumber.Max(force, PresentationNumber.FromDouble(solution.Extrema.MinimumShearForce.Value.Newtons).Abs());
        force = PresentationNumber.Max(force, PresentationNumber.FromDouble(solution.Extrema.MaximumShearForce.Value.Newtons).Abs());
        foreach (var node in solution.Nodes)
        {
            if (node.ReactionY is { } reaction)
                force = PresentationNumber.Max(force, PresentationNumber.FromDouble(reaction.Newtons).Abs());
        }
        var moment = PresentationNumber.Max(
            PresentationNumber.FromDouble(solution.Extrema.MinimumBendingMoment.Value.NewtonMeters).Abs(),
            PresentationNumber.FromDouble(solution.Extrema.MaximumBendingMoment.Value.NewtonMeters).Abs());
        return new(length, force, moment, PresentationNumber.FromDouble(beam.Material.YieldStrength.Pascals));
    }

    private static PresentationNumber NonNegative(double value)
    {
        if (!double.IsFinite(value) || value < 0) throw new ArgumentOutOfRangeException(nameof(value));
        return PresentationNumber.FromDouble(value);
    }
}
