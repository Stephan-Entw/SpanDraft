namespace SpanDraft.Desktop.Layout;

/// <summary>
/// Local DIP geometry of a spring support: a joint on the beam, a zigzag spring (translational), a spiral around the
/// joint (rotational) and a rigid body below it where the translation is held. It shares the ground line, hatches,
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
                yield return (0, top, 0, bottom);
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

    /// <summary>The spiral of the rotational spring as short segments around the joint.</summary>
    public IEnumerable<(double StartX, double StartY, double EndX, double EndY)> Spiral
    {
        get
        {
            if (!HasRotationalSpring) yield break;
            (double X, double Y) At(int i)
            {
                double t = (double)i / SpiralSegments;
                double angle = t * SpiralTurns * 2 * Math.PI, radius = SpiralInnerRadius + t * (SpiralOuterRadius - SpiralInnerRadius);
                return (radius * Math.Cos(angle), -radius * Math.Sin(angle));
            }
            var previous = At(0);
            for (int i = 1; i <= SpiralSegments; i++)
            {
                var next = At(i);
                yield return (previous.X, previous.Y, next.X, next.Y);
                previous = next;
            }
        }
    }

    private double TopRadius => HasRotationalSpring ? SpiralOuterRadius : Joint.Radius;
    public double LeftExtent => Ground_.LeftExtent;
    public double RightExtent => Ground_.RightExtent;
    public double Top => -TopRadius - SchematicMetrics.SymbolStrokeWidth / 2;
    public double Bottom => Ground_.Bottom;

    public bool Contains(double dx, double dy, double padding) =>
        dx >= -LeftExtent - padding && dx <= RightExtent + padding && dy >= Top - padding && dy <= Bottom + padding;
}
