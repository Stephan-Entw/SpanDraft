using SpanDraft.Core.Units;

namespace SpanDraft.Core.Loads;

/// <summary>A constant transverse line load between two x positions. Positive intensity acts upwards.</summary>
public sealed class UniformDistributedLoad : BeamLoad
{
    public UniformDistributedLoad(Length startPosition, Length endPosition, ForcePerLength intensity)
    {
        if (startPosition.Meters >= endPosition.Meters)
            throw new ArgumentOutOfRangeException(nameof(endPosition), "End position must be greater than start position.");

        StartPosition = startPosition;
        EndPosition = endPosition;
        Intensity = intensity;
    }

    public Length StartPosition { get; }
    public Length EndPosition { get; }
    public ForcePerLength Intensity { get; }
}
