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

    /// <summary>Creates a downward full-span load using standard gravity (9.80665 m/s²).</summary>
    public static UniformDistributedLoad FromSelfWeight(Length length, MassDensity density, Area area)
    {
        DomainGuard.Positive(length.Meters, nameof(length));
        DomainGuard.Positive(density.KilogramsPerCubicMeter, nameof(density));
        DomainGuard.Positive(area.SquareMeters, nameof(area));
        return new(default, length, ForcePerLength.FromNewtonsPerMeter(
            -density.KilogramsPerCubicMeter * area.SquareMeters * 9.80665));
    }
}
