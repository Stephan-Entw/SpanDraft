using SpanDraft.Core.Beams;
using SpanDraft.Core.Units;
using SpanDraft.Solver;

namespace SpanDraft.Tests.SolverReference;

/// <summary>
/// Frozen result of the former finite-element and prototype solvers. It mirrors the members of
/// <see cref="BeamSolution"/> that tests compare, but shares no evaluation code with the product path.
/// </summary>
internal sealed class ReferenceBeamSolution
{
    private readonly ElementField[] fields;

    internal ReferenceBeamSolution(BeamModel beam, IEnumerable<BeamNodeResult> nodes, SolverModel model)
    {
        Beam = beam;
        Nodes = Array.AsReadOnly(nodes.ToArray());
        fields = model.Elements.Select(element => new ElementField(beam, element,
            Nodes[element.Left.Index], Nodes[element.Right.Index])).ToArray();
        Extrema = ReferenceBeamExtrema.Find(fields);
    }

    internal ReferenceBeamSolution(BeamModel beam, IEnumerable<BeamNodeResult> nodes, SolverModel model,
        IReadOnlyList<SegmentState> segments, IReadOnlyList<double> rotationsLeft)
    {
        Beam = beam;
        Nodes = Array.AsReadOnly(nodes.ToArray());
        fields = model.Elements.Select(element => new ElementField(beam, element, Nodes[element.Left.Index],
            Nodes[element.Right.Index], segments[element.Left.Index], rotationsLeft[element.Right.Index])).ToArray();
        Extrema = ReferenceBeamExtrema.Find(fields);
    }

    internal BeamModel Beam { get; }
    internal IReadOnlyList<BeamNodeResult> Nodes { get; }
    internal ReferenceBeamExtrema Extrema { get; }

    internal BeamSectionResult EvaluateAt(Length position, EvaluationSide side)
    {
        if (!Enum.IsDefined(side))
            throw new ArgumentOutOfRangeException(nameof(side), side, "Unknown evaluation side.");
        if (position.Meters > Beam.Length.Meters)
            throw new ArgumentOutOfRangeException(nameof(position), position, "Position is outside the beam.");

        int low = 0, high = Nodes.Count - 1;
        while (low <= high)
        {
            int middle = low + (high - low) / 2;
            double x = Nodes[middle].Position.Meters;
            if (position.Meters < x) high = middle - 1;
            else if (position.Meters > x) low = middle + 1;
            else
            {
                int index = side == EvaluationSide.Left ? middle - 1 : middle;
                return fields[Math.Clamp(index, 0, fields.Length - 1)].EvaluateAt(position);
            }
        }
        return fields[high].EvaluateAt(position);
    }
}

/// <summary>Frozen extremum search over the reference element fields.</summary>
internal sealed class ReferenceBeamExtrema
{
    private ReferenceBeamExtrema(Range w, Range v, Range m)
    {
        MinimumTransverseDisplacement = w.Minimum.ToExtremum(Displacement.FromMeters);
        MaximumTransverseDisplacement = w.Maximum.ToExtremum(Displacement.FromMeters);
        MinimumShearForce = v.Minimum.ToExtremum(Force.FromNewtons);
        MaximumShearForce = v.Maximum.ToExtremum(Force.FromNewtons);
        MinimumBendingMoment = m.Minimum.ToExtremum(Moment.FromNewtonMeters);
        MaximumBendingMoment = m.Maximum.ToExtremum(Moment.FromNewtonMeters);
    }

    internal BeamExtremum<Displacement> MinimumTransverseDisplacement { get; }
    internal BeamExtremum<Displacement> MaximumTransverseDisplacement { get; }
    internal BeamExtremum<Force> MinimumShearForce { get; }
    internal BeamExtremum<Force> MaximumShearForce { get; }
    internal BeamExtremum<Moment> MinimumBendingMoment { get; }
    internal BeamExtremum<Moment> MaximumBendingMoment { get; }

    internal static ReferenceBeamExtrema Find(IEnumerable<ElementField> fields)
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
            foreach (double t in field.StationaryShears())
                if (t > 0 && t < 1)
                {
                    BeamSectionResult result = field.EvaluateNormalized(t);
                    v.Add(new(result.ShearForce.Newtons, result.Position, SideAt(field, result.Position)));
                }
            foreach (double t in field.StationaryMoments())
                if (t > 0 && t < 1)
                {
                    BeamSectionResult result = field.EvaluateNormalized(t);
                    m.Add(new(result.BendingMoment.NewtonMeters, result.Position, SideAt(field, result.Position)));
                }
        }
        return new ReferenceBeamExtrema(w, v, m);

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
