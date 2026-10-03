using SpanDraft.Core.Units;

namespace SpanDraft.Core.Loads;

/// <summary>A transverse point force at x from the left beam end. Positive force acts upwards.</summary>
public sealed class PointForce : BeamLoad
{
    public PointForce(Length position, Force force)
    {
        Position = position;
        Force = force;
    }

    public Length Position { get; }
    public Force Force { get; }
}
