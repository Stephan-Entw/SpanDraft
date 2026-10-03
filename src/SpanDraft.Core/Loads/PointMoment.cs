using SpanDraft.Core.Units;

namespace SpanDraft.Core.Loads;

/// <summary>A point moment at x from the left beam end. Positive moment acts counterclockwise.</summary>
public sealed class PointMoment : BeamLoad
{
    public PointMoment(Length position, Moment moment)
    {
        Position = position;
        Moment = moment;
    }

    public Length Position { get; }
    public Moment Moment { get; }
}
