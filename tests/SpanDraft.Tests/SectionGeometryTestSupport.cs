using SpanDraft.Core.Sections.Geometry;
using Xunit;

namespace SpanDraft.Tests;

internal static class SectionGeometryTestSupport
{
    internal const double Epsilon = 2.2204460492503131e-16;

    // 256 ulps of the stated reference scale cover chained trig/contour operations,
    // including comparisons with zero. There is no absolute SI or engineering tolerance.
    internal static void Close(double expected, double actual, double? referenceScale = null, int ulps = 256)
    {
        var scale = referenceScale ?? Math.Abs(expected);
        var tolerance = Math.Max(ulps * Epsilon * scale, 4 * double.Epsilon);
        Assert.True(double.IsFinite(actual) && Math.Abs(expected - actual) <= tolerance,
            $"Expected {expected:R}; actual {actual:R}; rounding budget {tolerance:R}.");
    }

    internal static SectionPoint P(double y, double z) => SectionPoint.FromMeters(y, z);

    internal static SectionContour Polygon(params SectionPoint[] points) =>
        new(points.Select((point, i) => (SectionSegment)new SectionLine(point, points[(i + 1) % points.Length])));

    internal static SectionContour Rectangle(double width, double height, double y = 0, double z = 0) =>
        Polygon(P(y, z), P(y + width, z), P(y + width, z + height), P(y, z + height));

    internal static SectionContour Circle(double radius, double y = 0, double z = 0, bool fullTurn = false)
    {
        var center = P(y, z);
        var points = new[] { P(y + radius, z), P(y, z + radius), P(y - radius, z), P(y, z - radius) };
        return fullTurn
            ? new([new SectionArc(center, points[0], points[0], ArcDirection.Counterclockwise)])
            : new(points.Select((point, i) => (SectionSegment)new SectionArc(center, point,
                points[(i + 1) % points.Length], ArcDirection.Counterclockwise)));
    }

    internal static SectionContour L() => Polygon(P(0, 0), P(5, 0), P(5, 1), P(1, 1), P(1, 3), P(0, 3));

    internal static SectionContour Transform(SectionContour contour, Func<SectionPoint, SectionPoint> map,
        bool reflection = false) => new(contour.Segments.Select(segment => segment switch
        {
            SectionLine line => (SectionSegment)new SectionLine(map(line.Start), map(line.End)),
            SectionArc arc => new SectionArc(map(arc.Center), map(arc.Start), map(arc.End),
                reflection
                    ? arc.Direction == ArcDirection.Counterclockwise ? ArcDirection.Clockwise : ArcDirection.Counterclockwise
                    : arc.Direction),
            _ => throw new InvalidOperationException()
        }));

    internal static SectionGeometry Transform(SectionGeometry geometry, Func<SectionPoint, SectionPoint> map,
        bool reflection = false) => new(Transform(geometry.OuterContour, map, reflection),
            geometry.Holes.Select(hole => Transform(hole, map, reflection)));

    internal static SectionPoint Rotate(SectionPoint point, double angle)
    {
        var (s, c) = Math.SinCos(angle);
        return P(c * point.Y.Meters - s * point.Z.Meters, s * point.Y.Meters + c * point.Z.Meters);
    }

    internal static void SameIntrinsic(SectionGeometryProperties expected, SectionGeometryProperties actual)
    {
        Close(expected.Area.SquareMeters, actual.Area.SquareMeters);
        Close(expected.I1.MetersToTheFourth, actual.I1.MetersToTheFourth);
        Close(expected.I2.MetersToTheFourth, actual.I2.MetersToTheFourth);
    }

    internal static void SameProperties(SectionGeometryProperties expected, SectionGeometryProperties actual)
    {
        SameIntrinsic(expected, actual);
        var scale = expected.I1.MetersToTheFourth;
        Close(expected.Iy.MetersToTheFourth, actual.Iy.MetersToTheFourth, scale);
        Close(expected.Iz.MetersToTheFourth, actual.Iz.MetersToTheFourth, scale);
        Close(expected.Iyz.MetersToTheFourth, actual.Iyz.MetersToTheFourth, scale);
        Close(expected.PrincipalAxisAngleRadians, actual.PrincipalAxisAngleRadians, 1);
        Close(expected.Axis1PositiveDistance.Meters, actual.Axis1PositiveDistance.Meters);
        Close(expected.Axis1NegativeDistance.Meters, actual.Axis1NegativeDistance.Meters);
        Close(expected.Axis2PositiveDistance.Meters, actual.Axis2PositiveDistance.Meters);
        Close(expected.Axis2NegativeDistance.Meters, actual.Axis2NegativeDistance.Meters);
        Close(expected.W1Positive.CubicMeters, actual.W1Positive.CubicMeters);
        Close(expected.W1Negative.CubicMeters, actual.W1Negative.CubicMeters);
        Close(expected.W2Positive.CubicMeters, actual.W2Positive.CubicMeters);
        Close(expected.W2Negative.CubicMeters, actual.W2Negative.CubicMeters);
    }
}