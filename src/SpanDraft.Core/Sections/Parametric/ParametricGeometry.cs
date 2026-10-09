using SpanDraft.Core.Sections.Geometry;
using SpanDraft.Core.Units;

namespace SpanDraft.Core.Sections.Parametric;

/// <summary>Construction only: all section properties belong to the geometry kernel.</summary>
internal static class ParametricGeometry
{
    internal static double Positive(Length value, string name) => DomainGuard.Positive(value.Meters, name);
    internal static double Radius(Length value, string name) => DomainGuard.NonNegative(value.Meters, name);

    internal static void Require(bool condition, string name, string message)
    {
        if (!condition)
            throw new ArgumentOutOfRangeException(name, message);
    }

    internal static ParametricSectionData Validated(SectionShapeKind shapeKind, Func<SectionGeometry> construct)
    {
        try
        {
            return new(construct(), shapeKind);
        }
        catch (ArgumentException exception)
        {
            throw new ArgumentException(
                "The parameter combination must define a representable section geometry with positive finite properties.",
                exception);
        }
    }

    internal static SectionPoint P(double y, double z) => SectionPoint.FromMeters(y, z);

    internal static SectionContour Rectangle(double left, double bottom, double right, double top, double radius = 0) =>
        Contour([P(left, bottom), P(right, bottom), P(right, top), P(left, top)],
            [radius, radius, radius, radius]);

    internal static SectionContour Circle(double centerY, double centerZ, double radius)
    {
        var center = P(centerY, centerZ);
        var points = new[] { Offset(center, radius, 0), Offset(center, 0, radius),
            Offset(center, -radius, 0), Offset(center, 0, -radius) };
        return new(points.Select((point, i) => (SectionSegment)new SectionArc(
            center, point, points[(i + 1) % points.Length], ArcDirection.Counterclockwise)));
    }

    /// <summary>
    /// Fillets an orthogonal CCW polygon. Radii are tangent setbacks at each vertex.
    /// A completely consumed edge has one shared tangent point, with no zero-length line.
    /// Positive setbacks and residual edges must survive coordinate rounding.
    /// </summary>
    internal static SectionContour Contour(SectionPoint[] vertices, double[] radii)
    {
        var count = vertices.Length;
        var entries = new SectionPoint[count];
        var exits = new SectionPoint[count];
        var directions = new Point2[count];
        var hasLine = new bool[count];
        for (var i = 0; i < count; i++)
        {
            var next = (i + 1) % count;
            var dy = vertices[next].Y.Meters - vertices[i].Y.Meters;
            var dz = vertices[next].Z.Meters - vertices[i].Z.Meters;
            Require((dy == 0) != (dz == 0), "dimensions", "Every polygon edge must be orthogonal and non-zero.");
            var length = DomainGuard.Positive(Math.Abs(dy) + Math.Abs(dz), "dimensions");
            var direction = new Point2(Math.Sign(dy), Math.Sign(dz));
            directions[i] = direction;
            // Keep the low subtraction bits to distinguish an exact limit from a lost positive edge.
            var remaining = ((Extended)length - radii[i] - radii[next]).Value;
            Require(remaining >= 0, "radius", "Fillets must not overlap along an edge.");
            exits[i] = Offset(vertices[i], direction.Y * radii[i], direction.Z * radii[i]);
            entries[next] = remaining == 0 ? exits[i] :
                Offset(vertices[next], -direction.Y * radii[next], -direction.Z * radii[next]);
            hasLine[i] = remaining > 0;
            if (hasLine[i])
            {
                var representedLength = direction.Y * (entries[next].Y.Meters - exits[i].Y.Meters) +
                    direction.Z * (entries[next].Z.Meters - exits[i].Z.Meters);
                Require(representedLength > 0, "radius", "A positive residual edge must remain representable.");
            }
        }

        var segments = new List<SectionSegment>();
        for (var i = 0; i < count; i++)
        {
            if (radii[i] > 0)
            {
                var incoming = directions[(i + count - 1) % count];
                var outgoing = directions[i];
                var center = Offset(vertices[i], (outgoing.Y - incoming.Y) * radii[i],
                    (outgoing.Z - incoming.Z) * radii[i]);
                var cross = incoming.Y * outgoing.Z - incoming.Z * outgoing.Y;
                segments.Add(new SectionArc(center, entries[i], exits[i],
                    cross > 0 ? ArcDirection.Counterclockwise : ArcDirection.Clockwise));
            }
            if (hasLine[i])
                segments.Add(new SectionLine(exits[i], entries[(i + 1) % count]));
        }
        return new(segments);
    }

    private static SectionPoint Offset(SectionPoint point, double dy, double dz)
    {
        var y = DomainGuard.Finite(point.Y.Meters + dy, "dimensions");
        var z = DomainGuard.Finite(point.Z.Meters + dz, "dimensions");
        Require(dy == 0 || y != point.Y.Meters, "radius", "A positive coordinate setback must remain representable.");
        Require(dz == 0 || z != point.Z.Meters, "radius", "A positive coordinate setback must remain representable.");
        return P(y, z);
    }
}
