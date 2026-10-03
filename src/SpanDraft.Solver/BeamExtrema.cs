using SpanDraft.Core.Units;

namespace SpanDraft.Solver;

/// <summary>One representative global extremum; Side is null in an element's interior.</summary>
public sealed class BeamExtremum<T>
{
    internal BeamExtremum(T value, Length position, EvaluationSide? side)
    {
        Value = value;
        Position = position;
        Side = side;
    }

    public T Value { get; }
    public Length Position { get; }
    public EvaluationSide? Side { get; }
}

/// <summary>
/// Global signed minima/maxima from piecewise polynomials, independent of plotting resolution.
/// Ties select the smallest x, then Left before Right. Constant intervals use an endpoint.
/// </summary>
public sealed class BeamExtrema
{
    private BeamExtrema(Range w, Range v, Range m)
    {
        MinimumTransverseDisplacement = w.Minimum.ToExtremum(Displacement.FromMeters);
        MaximumTransverseDisplacement = w.Maximum.ToExtremum(Displacement.FromMeters);
        MinimumShearForce = v.Minimum.ToExtremum(Force.FromNewtons);
        MaximumShearForce = v.Maximum.ToExtremum(Force.FromNewtons);
        MinimumBendingMoment = m.Minimum.ToExtremum(Moment.FromNewtonMeters);
        MaximumBendingMoment = m.Maximum.ToExtremum(Moment.FromNewtonMeters);
    }

    public BeamExtremum<Displacement> MinimumTransverseDisplacement { get; }
    public BeamExtremum<Displacement> MaximumTransverseDisplacement { get; }
    public BeamExtremum<Force> MinimumShearForce { get; }
    public BeamExtremum<Force> MaximumShearForce { get; }
    public BeamExtremum<Moment> MinimumBendingMoment { get; }
    public BeamExtremum<Moment> MaximumBendingMoment { get; }

    internal static BeamExtrema Find(IEnumerable<ElementField> fields)
    {
        var w = new Range();
        var v = new Range();
        var m = new Range();
        foreach (ElementField field in fields)
        {
            AddEndpoint(field.EvaluateAt(field.LeftPosition), EvaluationSide.Right);
            AddEndpoint(field.EvaluateAt(field.RightPosition), EvaluationSide.Left);
            foreach (double t in field.StationaryDisplacements())
                if (t > 0 && t < 1)
                {
                    BeamSectionResult result = field.EvaluateNormalized(t);
                    w.Add(new(result.TransverseDisplacement.Meters, result.Position, SideAt(field, result.Position)));
                }
            foreach (double t in field.StationaryMoments())
                if (t > 0 && t < 1)
                {
                    BeamSectionResult result = field.EvaluateNormalized(t);
                    m.Add(new(result.BendingMoment.NewtonMeters, result.Position, SideAt(field, result.Position)));
                }
        }
        return new BeamExtrema(w, v, m);

        void AddEndpoint(BeamSectionResult result, EvaluationSide side)
        {
            w.Add(new(result.TransverseDisplacement.Meters, result.Position, side));
            v.Add(new(result.ShearForce.Newtons, result.Position, side));
            m.Add(new(result.BendingMoment.NewtonMeters, result.Position, side));
        }
    }

    private static EvaluationSide? SideAt(ElementField field, Length position) =>
        position == field.LeftPosition ? EvaluationSide.Right :
        position == field.RightPosition ? EvaluationSide.Left : null;

    private sealed record Candidate(double Value, Length Position, EvaluationSide? Side)
    {
        internal BeamExtremum<T> ToExtremum<T>(Func<double, T> factory) => new(factory(Value), Position, Side);

        internal bool Precedes(Candidate other) => Position.Meters < other.Position.Meters ||
            (Position == other.Position && Side.GetValueOrDefault() < other.Side.GetValueOrDefault());
    }

    private sealed class Range
    {
        private Candidate? minimum, maximum;
        internal Candidate Minimum => minimum!;
        internal Candidate Maximum => maximum!;

        internal void Add(Candidate candidate)
        {
            if (minimum is null || Better(candidate, minimum, minimize: true)) minimum = candidate;
            if (maximum is null || Better(candidate, maximum, minimize: false)) maximum = candidate;
        }

        private static bool Better(Candidate candidate, Candidate current, bool minimize)
        {
            // Relative roundoff comparison only; no position epsilon or absolute SI floor.
            double scale = Math.Max(Math.Abs(candidate.Value), Math.Abs(current.Value));
            bool tied = scale == 0 || Math.Abs(candidate.Value / scale - current.Value / scale) <=
                64 * PolynomialRoots.MachinePrecision;
            return tied ? candidate.Precedes(current) :
                minimize ? candidate.Value < current.Value : candidate.Value > current.Value;
        }
    }
}
