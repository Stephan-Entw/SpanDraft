namespace SpanDraft.Core.Sections.Geometry;

internal static class GeometryMath
{
    internal const double Epsilon = 2.2204460492503131e-16;
    // A rounding budget, never a physical length or area tolerance.
    internal const double Roundoff = 64 * Epsilon;

    internal static double Hypot(double y, double z)
    {
        var scale = Math.Max(Math.Abs(y), Math.Abs(z));
        return scale == 0 ? 0 : scale * Math.Sqrt((y / scale) * (y / scale) + (z / scale) * (z / scale));
    }

    internal static double ArcParameter(SectionArc arc, double yDirection, double zDirection)
    {
        var angle = Math.Atan2(arc.StartYDirection * zDirection - arc.StartZDirection * yDirection,
            arc.StartYDirection * yDirection + arc.StartZDirection * zDirection);
        if (arc.Direction == ArcDirection.Clockwise)
            angle = -angle;
        return angle < 0 ? angle + Math.Tau : angle;
    }

    internal static bool OnArc(SectionArc arc, double parameter, bool strictlyInside = false)
    {
        var sweep = Math.Abs(arc.SweepRadians);
        if (strictlyInside)
            return parameter > Roundoff && parameter < sweep - Roundoff;
        return parameter <= sweep + Roundoff || Math.Tau - parameter <= Roundoff;
    }

    internal static Extended Cross(Point2 a, Point2 b) =>
        Extended.Product(a.Y, b.Z) - Extended.Product(a.Z, b.Y);

    internal static Extended Dot(Point2 a, Point2 b) =>
        Extended.Product(a.Y, b.Y) + Extended.Product(a.Z, b.Z);
}

internal readonly record struct Point2(double Y, double Z)
{
    public static Point2 operator +(Point2 a, Point2 b) => new(a.Y + b.Y, a.Z + b.Z);
    public static Point2 operator -(Point2 a, Point2 b) => new(a.Y - b.Y, a.Z - b.Z);
    public static Point2 operator *(Point2 a, double factor) => new(a.Y * factor, a.Z * factor);
}

/// <summary>
/// Two-component arithmetic retains low product/sum bits during contour cancellation.
/// It uses only BCL doubles and FMA, not an external numerical package.
/// </summary>
internal readonly struct Extended
{
    private readonly double high;
    private readonly double low;

    private Extended(double high, double low)
    {
        var sum = high + low;
        var carry = sum - high;
        this.low = (high - (sum - carry)) + (low - carry);
        this.high = sum;
    }

    public double Value => high + low;
    public static implicit operator Extended(double value) => new(value, 0);

    internal static Extended Product(double a, double b)
    {
        var product = a * b;
        return new(product, Math.FusedMultiplyAdd(a, b, -product));
    }

    public static Extended operator +(Extended a, Extended b)
    {
        var sum = a.high + b.high;
        var carry = sum - a.high;
        var error = (a.high - (sum - carry)) + (b.high - carry);
        return new(sum, error + a.low + b.low);
    }

    public static Extended operator -(Extended a) => new(-a.high, -a.low);
    public static Extended operator -(Extended a, Extended b) => a + -b;

    public static Extended operator *(Extended a, Extended b)
    {
        var product = a.high * b.high;
        var error = Math.FusedMultiplyAdd(a.high, b.high, -product) +
            a.high * b.low + a.low * b.high + a.low * b.low;
        return new(product, error);
    }

    public static Extended operator /(Extended a, Extended b)
    {
        var quotient = a.high / b.high;
        var remainder = a - b * quotient;
        return new(quotient, remainder.Value / b.high);
    }
}

/// <summary>One translated frame with an exact power-of-two scale for all contours.</summary>
internal sealed class GeometryFrame
{
    private GeometryFrame(SectionPoint origin, int exponent)
    {
        Origin = origin;
        Exponent = exponent;
    }

    internal SectionPoint Origin { get; }
    internal int Exponent { get; }

    internal static GeometryFrame Create(IEnumerable<SectionContour> contours)
    {
        var copy = contours.ToArray();
        var origin = copy[0].Segments[0].Start;
        var unscaled = new GeometryFrame(origin, 0);
        var size = 0.0;
        foreach (var contour in copy)
        foreach (var segment in contour.Segments)
        {
            Include(unscaled.Point(segment.Start));
            Include(unscaled.Point(segment.End));
            if (segment is SectionArc arc)
            {
                foreach (var direction in new[] { new Point2(1, 0), new Point2(-1, 0), new Point2(0, 1), new Point2(0, -1) })
                {
                    var parameter = GeometryMath.ArcParameter(arc, direction.Y, direction.Z);
                    if (GeometryMath.OnArc(arc, parameter))
                        Include(unscaled.ArcPoint(arc, parameter));
                }
            }
        }
        DomainGuard.Positive(size, nameof(contours));
        return new(origin, Math.ILogB(size));

        void Include(Point2 point)
        {
            DomainGuard.Finite(point.Y, nameof(contours));
            DomainGuard.Finite(point.Z, nameof(contours));
            size = Math.Max(size, Math.Max(Math.Abs(point.Y), Math.Abs(point.Z)));
        }
    }

    internal Point2 Point(SectionPoint point) => new(
        Math.ScaleB(DomainGuard.Finite(point.Y.Meters - Origin.Y.Meters, nameof(point)), -Exponent),
        Math.ScaleB(DomainGuard.Finite(point.Z.Meters - Origin.Z.Meters, nameof(point)), -Exponent));

    internal double Radius(SectionArc arc) => DomainGuard.Positive(
        Math.ScaleB(arc.Radius.Meters, -Exponent), nameof(arc));

    internal Point2 ArcPoint(SectionArc arc, double parameter)
    {
        if (parameter == 0)
            return Point(arc.Start);
        if (parameter == Math.Abs(arc.SweepRadians))
            return Point(arc.End);
        // cos(a+d)-cos(a) and sin(a+d)-sin(a) without subtracting nearly equal cosines.
        var delta = arc.Direction == ArcDirection.Counterclockwise ? parameter : -parameter;
        var (sine, cosine) = Math.SinCos(delta / 2);
        var displacement = 2 * (Radius(arc) * sine);
        var start = Point(arc.Start);
        return new(start.Y + displacement * (-arc.StartYDirection * sine - arc.StartZDirection * cosine),
            start.Z + displacement * (-arc.StartZDirection * sine + arc.StartYDirection * cosine));
    }
}