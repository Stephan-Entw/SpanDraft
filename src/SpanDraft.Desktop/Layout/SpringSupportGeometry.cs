namespace SpanDraft.Desktop.Layout;

/// <summary>
/// Local DIP geometry of a spring support: a joint on the beam, a zigzag spring (translational), a spiral around the
/// joint (rotational, hanging from its outer end at 9 o'clock) and a rigid body below it where the translation is held. It shares the ground line, hatches,
/// extents and hit zone with the hinged supports so spring supports align with them.
/// </summary>
public readonly record struct SpringSupportGeometry(bool HasTranslationalSpring, bool HasRotationalSpring, bool RigidTranslation)
{
    public const double ZigzagHalfWidth = 5;
    public const int ZigzagSegments = 8;
    public const double SpiralInnerRadius = 5;
    public const double SpiralOuterRadius = 11;
    public const double SpiralTurns = 1.5;
    public const int SpiralSegments = 36;
    /// <summary>The innermost quarter turn is omitted: the spiral ends at 12 o'clock and a short stroke leads down to the joint.</summary>
    private const int SpiralFirstSegment = SpiralSegments / 6;

    private static readonly HingedSupportGeometry Ground_ = new(true);

    public (double X, double Y, double Radius) Joint => Ground_.Joint;
    public (double StartX, double StartY, double EndX, double EndY) Ground => Ground_.Ground;
    public IEnumerable<(double StartX, double StartY, double EndX, double EndY)> Hatches => Ground_.Hatches;

    /// <summary>The element between the joint and the ground: a zigzag spring, the roller triangle or a plain stem.</summary>
    public IEnumerable<(double StartX, double StartY, double EndX, double EndY)> Body
    {
        get
        {
            if (RigidTranslation)
            {
                foreach (var line in Ground_.TriangleLines) yield return line;
                yield break;
            }
            double top = Joint.Radius, bottom = SchematicMetrics.SupportGroundY;
            if (!HasTranslationalSpring)
            {
                // A rotational spring hangs from the outer end of its spiral (9 o'clock), straight down to the ground.
                if (HasRotationalSpring)
                {
                    var end = SpiralPoint(SpiralSegments);
                    yield return (end.X, end.Y, end.X, bottom);
                }
                else yield return (0, top, 0, bottom);
                yield break;
            }
            // Straight lead-in and lead-out, the coils between them alternate to both sides of the axis.
            double lead = 2, span = bottom - top - 2 * lead, step = span / ZigzagSegments;
            double x = 0, y = top;
            yield return (0, y, 0, y + lead);
            y += lead;
            for (int i = 0; i < ZigzagSegments; i++)
            {
                double nextX = i == ZigzagSegments - 1 ? 0 : (i % 2 == 0 ? -ZigzagHalfWidth : ZigzagHalfWidth);
                if (i == 0) nextX = -ZigzagHalfWidth;
                yield return (x, y, nextX, y + step);
                x = nextX;
                y += step;
            }
            yield return (x, y, 0, bottom);
        }
    }

    /// <summary>
    /// The rotational spring: a short stroke from the joint up to the inner end of the spiral at 12 o'clock, then the spiral as
    /// short segments around the joint, ending at its outer end at 9 o'clock.
    /// </summary>
    public IEnumerable<(double StartX, double StartY, double EndX, double EndY)> Spiral
    {
        get
        {
            if (!HasRotationalSpring) yield break;
            var previous = SpiralPoint(SpiralFirstSegment);
            yield return (previous.X, -Joint.Radius, previous.X, previous.Y);
            for (int i = SpiralFirstSegment + 1; i <= SpiralSegments; i++)
            {
                var next = SpiralPoint(i);
                yield return (previous.X, previous.Y, next.X, next.Y);
                previous = next;
            }
        }
    }

    private static (double X, double Y) SpiralPoint(int i)
    {
        double t = (double)i / SpiralSegments;
        double angle = t * SpiralTurns * 2 * Math.PI, radius = SpiralInnerRadius + t * (SpiralOuterRadius - SpiralInnerRadius);
        return (radius * Math.Cos(angle), -radius * Math.Sin(angle));
    }

    /// <summary>
    /// The distance from a point to the strokes that belong to this spring alone: the zigzag or roller body and the spiral with
    /// its stroke. The ring, the ground line and the hatches are shared with every support of the station and not counted.
    /// </summary>
    public double DistanceToSpring(double dx, double dy) => Body.Concat(Spiral)
        .Min(l => DistanceToSegment(dx, dy, l.StartX, l.StartY, l.EndX, l.EndY));

    private static double DistanceToSegment(double px, double py, double x1, double y1, double x2, double y2)
    {
        double sx = x2 - x1, sy = y2 - y1, length2 = sx * sx + sy * sy;
        double t = length2 == 0 ? 0 : Math.Clamp(((px - x1) * sx + (py - y1) * sy) / length2, 0, 1);
        return Math.Sqrt(Math.Pow(px - (x1 + t * sx), 2) + Math.Pow(py - (y1 + t * sy), 2));
    }

    private double TopRadius => HasRotationalSpring ? SpiralOuterRadius : Joint.Radius;
    public double LeftExtent => Ground_.LeftExtent;
    public double RightExtent => Ground_.RightExtent;
    public double Top => -TopRadius - SchematicMetrics.SymbolStrokeWidth / 2;
    public double Bottom => Ground_.Bottom;

    public bool Contains(double dx, double dy, double padding) =>
        dx >= -LeftExtent - padding && dx <= RightExtent + padding && dy >= Top - padding && dy <= Bottom + padding;
}
