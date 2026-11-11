using SpanDraft.Core.Units;
using SpanDraft.Solver.Kernel;

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
    internal BeamExtrema(BendingExtrema extrema)
    {
        MinimumTransverseDisplacement = Map(extrema.W.Minimum, Displacement.FromMeters);
        MaximumTransverseDisplacement = Map(extrema.W.Maximum, Displacement.FromMeters);
        MinimumShearForce = Map(extrema.V.Minimum, Force.FromNewtons);
        MaximumShearForce = Map(extrema.V.Maximum, Force.FromNewtons);
        MinimumBendingMoment = Map(extrema.M.Minimum, Moment.FromNewtonMeters);
        MaximumBendingMoment = Map(extrema.M.Maximum, Moment.FromNewtonMeters);

        static BeamExtremum<T> Map<T>(BendingExtremum value, Func<double, T> factory) =>
            new(factory(value.Output.Value), Length.FromMeters(value.Position), value.Side switch
            {
                FieldSide.Left => EvaluationSide.Left,
                FieldSide.Right => EvaluationSide.Right,
                FieldSide.Interior => null,
                _ => throw new InvalidOperationException("Unknown kernel extremum side.")
            });
    }

    public BeamExtremum<Displacement> MinimumTransverseDisplacement { get; }
    public BeamExtremum<Displacement> MaximumTransverseDisplacement { get; }
    public BeamExtremum<Force> MinimumShearForce { get; }
    public BeamExtremum<Force> MaximumShearForce { get; }
    public BeamExtremum<Moment> MinimumBendingMoment { get; }
    public BeamExtremum<Moment> MaximumBendingMoment { get; }
}
