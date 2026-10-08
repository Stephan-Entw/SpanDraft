namespace SpanDraft.Core.Sections.Geometry;

/// <summary>Analytic intersections and ray crossings; no boundary tessellation or repair.</summary>
internal static class GeometryTopology
{
    internal static void ValidateContour(SectionContour contour)
    {
        var frame = GeometryFrame.Create([contour]);
        var segments = contour.Segments;
        for (var i = 0; i < segments.Count; i++)
        for (var j = i + 1; j < segments.Count; j++)
        {
            var adjacent = j == i + 1 || i == 0 && j == segments.Count - 1;
            if (Conflict(segments[i], segments[j], frame, adjacent))
                throw new ArgumentException("A contour must be simple, without self-intersections or overlapping segments.", nameof(contour));
        }
        var area = GeometryIntegration.Integrate(contour, frame, 0, 0).Area.Value;
        DomainGuard.Positive(Math.Abs(area), nameof(contour));
    }

    internal static void ValidateHoles(SectionGeometry geometry)
    {
        if (geometry.Holes.Count == 0)
            return;
        var frame = GeometryFrame.Create(new[] { geometry.OuterContour }.Concat(geometry.Holes));
        foreach (var hole in geometry.Holes)
        {
            if (BoundariesConflict(geometry.OuterContour, hole, frame) ||
                !Contains(geometry.OuterContour, frame.Point(hole.Segments[0].Start), frame))
                throw new ArgumentException("A hole must be strictly inside the outer contour, without touching its boundary.", nameof(geometry));
        }
        for (var i = 0; i < geometry.Holes.Count; i++)
        for (var j = i + 1; j < geometry.Holes.Count; j++)
        {
            var a = geometry.Holes[i];
            var b = geometry.Holes[j];
            if (BoundariesConflict(a, b, frame) ||
                Contains(a, frame.Point(b.Segments[0].Start), frame) ||
                Contains(b, frame.Point(a.Segments[0].Start), frame))
                throw new ArgumentException("Holes must be disjoint and cannot touch or contain one another.", nameof(geometry));
        }
    }

    private static bool BoundariesConflict(SectionContour a, SectionContour b, GeometryFrame frame) =>
        a.Segments.Any(first => b.Segments.Any(second => Conflict(first, second, frame, false)));

    private static bool Conflict(SectionSegment a, SectionSegment b, GeometryFrame frame, bool allowSharedEndpoints)
    {
        if (a is SectionLine lineA && b is SectionLine lineB)
            return LinesConflict(lineA, lineB, frame, allowSharedEndpoints);

        if (a is SectionLine line && b is SectionArc arc)
            return LineArcIntersections(line, arc, frame).Any(point => !Allowed(point));
        if (a is SectionArc arcA && b is SectionLine otherLine)
            return LineArcIntersections(otherLine, arcA, frame).Any(point => !Allowed(point));

        var first = (SectionArc)a;
        var second = (SectionArc)b;
        var centerA = frame.Point(first.Center);
        var centerB = frame.Point(second.Center);
        var radiusA = frame.Radius(first);
        var radiusB = frame.Radius(second);
        var commonEndpoints = first.Start == second.Start || first.Start == second.End ||
            first.End == second.Start || first.End == second.End;
        if (centerA == centerB && (radiusA == radiusB ||
            commonEndpoints && Math.Abs(radiusA - radiusB) <= GeometryMath.Roundoff * Math.Max(radiusA, radiusB)))
        {
            if (Math.Abs(first.SweepRadians) == Math.Tau || Math.Abs(second.SweepRadians) == Math.Tau ||
                Interior(first, second.Start) || Interior(first, second.End) ||
                Interior(second, first.Start) || Interior(second, first.End) ||
                InteriorPoint(first, second) || InteriorPoint(second, first))
                return true;
            return new[] { first.Start, first.End }.Any(point =>
                OnCircleArc(second, frame.Point(point), frame) && !Allowed(frame.Point(point)));
        }
        return ArcIntersections(first, second, frame).Any(point => !Allowed(point));

        bool Allowed(Point2 point) => SharedEndpointAllowed(a, b, point, frame, allowSharedEndpoints);

        bool Interior(SectionArc target, SectionPoint point)
        {
            var relative = frame.Point(point) - frame.Point(target.Center);
            return GeometryMath.OnArc(target, GeometryMath.ArcParameter(target, relative.Y, relative.Z), true);
        }

        bool InteriorPoint(SectionArc source, SectionArc target)
        {
            var point = frame.ArcPoint(source, Math.Abs(source.SweepRadians) / 2);
            var relative = point - frame.Point(target.Center);
            return GeometryMath.OnArc(target, GeometryMath.ArcParameter(target, relative.Y, relative.Z), true);
        }
    }

    private static bool LinesConflict(SectionLine a, SectionLine b, GeometryFrame frame, bool allowSharedEndpoints)
    {
        var start = frame.Point(a.Start);
        var direction = frame.Point(a.End) - start;
        var other = frame.Point(b.Start);
        var otherDirection = frame.Point(b.End) - other;
        var separation = other - start;
        var determinant = GeometryMath.Cross(direction, otherDirection);
        if (determinant.Value == 0)
        {
            if (GeometryMath.Cross(separation, direction).Value != 0)
                return false;
            var useY = Math.Abs(direction.Y) >= Math.Abs(direction.Z);
            var t0 = useY ? separation.Y / direction.Y : separation.Z / direction.Z;
            var difference = frame.Point(b.End) - start;
            var t1 = useY ? difference.Y / direction.Y : difference.Z / direction.Z;
            var low = Math.Max(0, Math.Min(t0, t1));
            var high = Math.Min(1, Math.Max(t0, t1));
            if (low > high)
                return false;
            if (low < high)
                return true;
            return !SharedEndpointAllowed(a, b, start + direction * low, frame, allowSharedEndpoints);
        }

        var t = (GeometryMath.Cross(separation, otherDirection) / determinant).Value;
        var u = (GeometryMath.Cross(separation, direction) / determinant).Value;
        if (!InSegment(t) || !InSegment(u))
            return false;
        return !SharedEndpointAllowed(a, b, start + direction * t, frame, allowSharedEndpoints);
    }

    private static IEnumerable<Point2> LineArcIntersections(SectionLine line, SectionArc arc, GeometryFrame frame)
    {
        var start = frame.Point(line.Start);
        var direction = frame.Point(line.End) - start;
        var relative = start - frame.Point(arc.Center);
        var radius = frame.Radius(arc);
        var a = GeometryMath.Dot(direction, direction);
        var b = 2 * GeometryMath.Dot(relative, direction);
        var c = GeometryMath.Dot(relative, relative) - Extended.Product(radius, radius);
        // Declared shared endpoints are known roots. Factoring them avoids an
        // ill-conditioned discriminant at shallow, almost tangent arc/chord joins.
        var startShared = line.Start == arc.Start || line.Start == arc.End;
        var endShared = line.End == arc.Start || line.End == arc.End;
        if (startShared || endShared)
        {
            if (startShared)
                yield return start;
            if (endShared)
                yield return frame.Point(line.End);
            if (!(startShared && endShared))
            {
                var other = (-b / a).Value - (startShared ? 0 : 1);
                if (InSegment(other))
                {
                    var point = start + direction * other;
                    if (OnCircleArc(arc, point, frame))
                        yield return point;
                }
            }
            yield break;
        }
        var discriminant = b * b - 4 * a * c;
        if (discriminant.Value < 0)
            yield break;
        var root = Math.Sqrt(discriminant.Value);
        var q = -0.5 * (b.Value + Math.CopySign(root, b.Value));
        if (q == 0)
        {
            var t = (-b / (2 * a)).Value;
            if (InSegment(t))
            {
                var point = start + direction * t;
                if (OnCircleArc(arc, point, frame))
                    yield return point;
            }
            yield break;
        }
        foreach (var t in new[] { ((Extended)q / a).Value, (c / q).Value })
        {
            if (InSegment(t))
            {
                var point = start + direction * t;
                if (OnCircleArc(arc, point, frame))
                    yield return point;
            }
        }
    }

    private static IEnumerable<Point2> ArcIntersections(SectionArc a, SectionArc b, GeometryFrame frame)
    {
        var centerA = frame.Point(a.Center);
        var centerB = frame.Point(b.Center);
        var separation = centerB - centerA;
        var distance = GeometryMath.Hypot(separation.Y, separation.Z);
        if (distance == 0)
            yield break;

        // Shared endpoints are known intersections. Reflecting one across the
        // centre line finds the other without an ill-conditioned square root
        // at tangency. A full circle has only one distinct endpoint.
        var startShared = a.Start == b.Start || a.Start == b.End;
        var endShared = a.End == b.Start || a.End == b.End;
        if (startShared || endShared)
        {
            var shared = frame.Point(startShared ? a.Start : a.End);
            yield return shared;
            if (startShared && endShared && a.Start != a.End)
            {
                yield return frame.Point(a.End);
                yield break;
            }

            var reflectionNormal = new Point2(-separation.Z / distance, separation.Y / distance);
            var offset = GeometryMath.Dot(shared - centerA, reflectionNormal);
            var reflected = shared - reflectionNormal * (2 * offset).Value;
            if (OnCircleArc(a, reflected, frame) && OnCircleArc(b, reflected, frame))
                yield return reflected;
            yield break;
        }

        var ra = frame.Radius(a);
        var rb = frame.Radius(b);
        if (distance > (ra + rb) * (1 + GeometryMath.Roundoff) ||
            distance < Math.Abs(ra - rb) * (1 - GeometryMath.Roundoff))
            yield break;
        var along = ((Extended.Product(ra, ra) - Extended.Product(rb, rb) +
            GeometryMath.Dot(separation, separation)) / (2 * distance)).Value;
        var heightSquared = Extended.Product(ra - along, ra + along).Value;
        if (heightSquared < -GeometryMath.Roundoff * ra * ra)
            yield break;
        var height = Math.Sqrt(Math.Max(0, heightSquared));
        var direction = separation * (1 / distance);
        var midpoint = centerA + direction * along;
        var normal = new Point2(-direction.Z, direction.Y);
        foreach (var point in new[] { midpoint + normal * height, midpoint - normal * height })
        {
            if (OnCircleArc(a, point, frame) && OnCircleArc(b, point, frame))
                yield return point;
        }
    }

    private static bool InSegment(double parameter) =>
        parameter >= -GeometryMath.Roundoff && parameter <= 1 + GeometryMath.Roundoff;

    private static bool OnCircleArc(SectionArc arc, Point2 point, GeometryFrame frame)
    {
        var relative = point - frame.Point(arc.Center);
        return GeometryMath.OnArc(arc, GeometryMath.ArcParameter(arc, relative.Y, relative.Z));
    }

    private static bool SharedEndpointAllowed(SectionSegment a, SectionSegment b, Point2 point,
        GeometryFrame frame, bool allow)
    {
        if (!allow)
            return false;
        foreach (var endpoint in new[] { a.Start, a.End })
        {
            if (endpoint != b.Start && endpoint != b.End)
                continue;
            var shared = frame.Point(endpoint);
            var error = GeometryMath.Hypot(point.Y - shared.Y, point.Z - shared.Z);
            if (error <= GeometryMath.Roundoff * Math.Max(1, GeometryMath.Hypot(shared.Y, shared.Z)))
                return true;
        }
        return false;
    }

    private static bool Contains(SectionContour contour, Point2 point, GeometryFrame frame)
    {
        var crossings = 0;
        foreach (var segment in contour.Segments)
        {
            if (segment is SectionLine)
            {
                var a = frame.Point(segment.Start);
                var b = frame.Point(segment.End);
                if ((a.Z > point.Z) != (b.Z > point.Z))
                {
                    var y = a.Y + (point.Z - a.Z) * (b.Y - a.Y) / (b.Z - a.Z);
                    if (y > point.Y)
                        crossings++;
                }
                continue;
            }

            var arc = (SectionArc)segment;
            var parameters = new List<double> { 0, Math.Abs(arc.SweepRadians) };
            foreach (var direction in new[] { new Point2(0, 1), new Point2(0, -1) })
            {
                var t = GeometryMath.ArcParameter(arc, direction.Y, direction.Z);
                if (GeometryMath.OnArc(arc, t, true))
                    parameters.Add(t);
            }
            parameters.Sort();
            var center = frame.Point(arc.Center);
            var radius = frame.Radius(arc);
            for (var i = 0; i + 1 < parameters.Count; i++)
            {
                var a = frame.ArcPoint(arc, parameters[i]);
                var b = frame.ArcPoint(arc, parameters[i + 1]);
                if ((a.Z > point.Z) == (b.Z > point.Z))
                    continue;
                var middle = frame.ArcPoint(arc, (parameters[i] + parameters[i + 1]) / 2);
                var dz = Math.Abs(point.Z - center.Z);
                var squared = (radius - dz) * (radius + dz);
                var root = Math.Sqrt(Math.Max(0, squared));
                var y = center.Y + Math.CopySign(root, middle.Y - center.Y);
                if (y > point.Y)
                    crossings++;
            }
        }
        return crossings % 2 == 1;
    }
}
