using SpanDraft.Core.Units;

namespace SpanDraft.Core.Sections.Geometry;

/// <summary>
/// A circular arc specified by centre, endpoints and direction. Equal endpoints mean one full turn.
/// Endpoint radii must agree within floating-point roundoff; endpoints are never snapped.
/// </summary>
public sealed class SectionArc : SectionSegment
{
    public SectionArc(SectionPoint center, SectionPoint start, SectionPoint end, ArcDirection direction)
        : base(start, end)
    {
        if (!Enum.IsDefined(direction))
            throw new ArgumentOutOfRangeException(nameof(direction));
        Center = center;
        Direction = direction;
        var dy = DomainGuard.Finite(start.Y.Meters - center.Y.Meters, nameof(start));
        var dz = DomainGuard.Finite(start.Z.Meters - center.Z.Meters, nameof(start));
        var radius = DomainGuard.Positive(GeometryMath.Hypot(dy, dz), nameof(start));
        var endRadius = DomainGuard.Positive(GeometryMath.Hypot(
            DomainGuard.Finite(end.Y.Meters - center.Y.Meters, nameof(end)),
            DomainGuard.Finite(end.Z.Meters - center.Z.Meters, nameof(end))), nameof(end));
        if (Math.Abs(radius - endRadius) / Math.Max(radius, endRadius) > GeometryMath.Roundoff)
            throw new ArgumentException("The endpoints must lie on the same circle.", nameof(end));

        Radius = Length.FromMeters(radius);
        StartYDirection = dy / radius;
        StartZDirection = dz / radius;
        var endY = (end.Y.Meters - center.Y.Meters) / endRadius;
        var endZ = (end.Z.Meters - center.Z.Meters) / endRadius;
        EndYDirection = endY;
        EndZDirection = endZ;
        var angle = Math.Atan2(StartYDirection * endZ - StartZDirection * endY,
            StartYDirection * endY + StartZDirection * endZ);
        if (start != end && angle == 0)
            throw new ArgumentException("Distinct endpoints must define a non-zero representable angle.", nameof(end));
        var sign = direction == ArcDirection.Counterclockwise ? 1 : -1;
        var sweep = sign * angle;
        if (start == end)
            sweep = Math.Tau;
        else if (sweep <= 0)
            sweep += Math.Tau;
        if (sweep <= 0 || sweep > Math.Tau)
            throw new ArgumentException("The arc sweep must be representable and non-zero.", nameof(end));
        SweepRadians = sign * sweep;
        var (sine, cosine) = Math.SinCos(SweepRadians / 2);
        MidYDirection = StartYDirection * cosine - StartZDirection * sine;
        MidZDirection = StartZDirection * cosine + StartYDirection * sine;
    }

    private SectionArc(SectionArc source) : base(source.End, source.Start)
    {
        Center = source.Center;
        Direction = source.Direction == ArcDirection.Counterclockwise ? ArcDirection.Clockwise : ArcDirection.Counterclockwise;
        Radius = source.Radius;
        SweepRadians = -source.SweepRadians;
        StartYDirection = source.EndYDirection;
        StartZDirection = source.EndZDirection;
        EndYDirection = source.StartYDirection;
        EndZDirection = source.StartZDirection;
        MidYDirection = source.MidYDirection;
        MidZDirection = source.MidZDirection;
    }

    public SectionPoint Center { get; }
    public Length Radius { get; }
    public ArcDirection Direction { get; }
    public double SweepRadians { get; }

    internal double StartYDirection { get; }
    internal double StartZDirection { get; }

    private double EndYDirection { get; }
    private double EndZDirection { get; }
    internal double MidYDirection { get; }
    internal double MidZDirection { get; }

    // Preserve the same analytic circle and midpoint, rather than infer a new
    // radius/angle from endpoints whose coordinates can differ by rounding.
    public override SectionArc Reversed() => new(this);
}