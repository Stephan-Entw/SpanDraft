using SpanDraft.Core.Units;

namespace SpanDraft.Core.Loads;

/// <summary>
/// A transverse line load between two x positions whose intensity varies linearly from
/// <see cref="StartIntensity"/> at the start to <see cref="EndIntensity"/> at the end.
/// Positive intensity acts upwards. Intensities may be negative or zero, also both at once.
/// </summary>
public sealed class DistributedLoad : BeamLoad
{
    public DistributedLoad(Length startPosition, Length endPosition, ForcePerLength startIntensity,
        ForcePerLength endIntensity)
    {
        if (startPosition.Meters >= endPosition.Meters)
            throw new ArgumentOutOfRangeException(nameof(endPosition), "End position must be greater than start position.");

        StartPosition = startPosition;
        EndPosition = endPosition;
        StartIntensity = startIntensity;
        EndIntensity = endIntensity;
    }

    /// <summary>Creates a constant load.</summary>
    public DistributedLoad(Length startPosition, Length endPosition, ForcePerLength intensity)
        : this(startPosition, endPosition, intensity, intensity)
    {
    }

    public Length StartPosition { get; }
    public Length EndPosition { get; }
    public ForcePerLength StartIntensity { get; }
    public ForcePerLength EndIntensity { get; }

    /// <summary>True when both end intensities are equal, so the load is a constant line load.</summary>
    public bool IsConstant => StartIntensity.NewtonsPerMeter == EndIntensity.NewtonsPerMeter;

    /// <summary>Creates a downward full-span constant load using standard gravity (9.80665 m/s²).</summary>
    public static DistributedLoad FromSelfWeight(Length length, MassDensity density, Area area)
    {
        DomainGuard.Positive(length.Meters, nameof(length));
        return FromSelfWeight(default, length, density, area);
    }

    /// <summary>Creates the downward constant self-weight load of one beam segment between two positions.</summary>
    public static DistributedLoad FromSelfWeight(Length start, Length end, MassDensity density, Area area)
    {
        DomainGuard.Positive(density.KilogramsPerCubicMeter, nameof(density));
        DomainGuard.Positive(area.SquareMeters, nameof(area));
        return new(start, end, ForcePerLength.FromNewtonsPerMeter(
            -density.KilogramsPerCubicMeter * area.SquareMeters * 9.80665));
    }
}
