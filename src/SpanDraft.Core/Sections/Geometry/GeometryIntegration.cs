using SpanDraft.Core.Units;

namespace SpanDraft.Core.Sections.Geometry;

internal static class GeometryIntegration
{
    internal static SectionGeometryProperties Calculate(SectionGeometry geometry)
    {
        var contours = new[] { geometry.OuterContour }.Concat(geometry.Holes).ToArray();
        var frame = GeometryFrame.Create(contours);
        var first = Sum(contours, frame, 0, 0);
        var area = DomainGuard.Positive(first.Area.Value, nameof(geometry));
        var cy = first.FirstY / first.Area;
        var cz = first.FirstZ / first.Area;

        // Reintegrating about the centroid is the parallel-axis transformation evaluated
        // without subtracting large, nearly equal origin moments.
        var centered = Sum(contours, frame, cy, cz);
        var iy = centered.Iy - centered.FirstZ * centered.FirstZ / centered.Area;
        var iz = centered.Iz - centered.FirstY * centered.FirstY / centered.Area;
        var iyz = centered.Iyz - centered.FirstY * centered.FirstZ / centered.Area;
        DomainGuard.Positive(iy.Value, nameof(geometry));
        DomainGuard.Positive(iz.Value, nameof(geometry));
        DomainGuard.Finite(iyz.Value, nameof(geometry));

        var scale = Math.Max(iy.Value, iz.Value);
        var difference = (iy - iz).Value / scale;
        var product = iyz.Value / scale;
        var gap = GeometryMath.Hypot(difference, 2 * product);
        var angle = gap <= GeometryMath.Roundoff ? 0 : Math.Atan2(-2 * product, difference) / 2;
        if (angle >= Math.PI / 2)
            angle -= Math.PI;
        // Remove negative zero from the deterministic orientation contract.
        if (angle == 0)
            angle = 0;
        var i1 = ((iy + iz) / 2).Value + scale * gap / 2;
        // det/I1 avoids cancellation of mean-radius for a very small second eigenvalue.
        var i2 = ((iy * iz - iyz * iyz) / i1).Value;
        DomainGuard.Positive(i1, nameof(geometry));
        DomainGuard.Positive(i2, nameof(geometry));
        i2 = Math.Min(i1, i2);

        var (sine, cosine) = Math.SinCos(angle);
        var distances1 = Extrema(contours, frame, cy, cz, -sine, cosine);
        var distances2 = Extrema(contours, frame, cy, cz, cosine, sine);
        var centroid = SectionPoint.FromMeters(
            DomainGuard.Finite(frame.Origin.Y.Meters + Math.ScaleB(cy.Value, frame.Exponent), nameof(geometry)),
            DomainGuard.Finite(frame.Origin.Z.Meters + Math.ScaleB(cz.Value, frame.Exponent), nameof(geometry)));

        return new(
            Area.FromSquareMeters(Rescale(area, 2)),
            centroid,
            SecondMomentOfArea.FromMetersToTheFourth(Rescale(iy.Value, 4)),
            SecondMomentOfArea.FromMetersToTheFourth(Rescale(iz.Value, 4)),
            ProductMomentOfArea.FromMetersToTheFourth(DomainGuard.Finite(Math.ScaleB(iyz.Value, 4 * frame.Exponent), nameof(geometry))),
            SecondMomentOfArea.FromMetersToTheFourth(Rescale(i1, 4)),
            SecondMomentOfArea.FromMetersToTheFourth(Rescale(i2, 4)),
            angle,
            Length.FromMeters(Rescale(distances1.Positive, 1)),
            Length.FromMeters(Rescale(distances1.Negative, 1)),
            Length.FromMeters(Rescale(distances2.Positive, 1)),
            Length.FromMeters(Rescale(distances2.Negative, 1)),
            SectionModulus.FromCubicMeters(Rescale(i1 / distances1.Positive, 3)),
            SectionModulus.FromCubicMeters(Rescale(i1 / distances1.Negative, 3)),
            SectionModulus.FromCubicMeters(Rescale(i2 / distances2.Positive, 3)),
            SectionModulus.FromCubicMeters(Rescale(i2 / distances2.Negative, 3)));

        double Rescale(double value, int power) =>
            DomainGuard.Positive(Math.ScaleB(value, power * frame.Exponent), nameof(geometry));
    }

    private static Moments Sum(SectionContour[] contours, GeometryFrame frame, Extended cy, Extended cz)
    {
        var result = new Moments();
        for (var i = 0; i < contours.Length; i++)
        {
            var moments = Integrate(contours[i], frame, cy, cz);
            var sign = Math.Sign(moments.Area.Value) * (i == 0 ? 1 : -1);
            if (sign == 0)
                throw new ArgumentException("A contour must enclose a non-zero area.", nameof(contours));
            result.Add(moments, sign);
        }
        return result;
    }

    internal static Moments Integrate(SectionContour contour, GeometryFrame frame, Extended cy, Extended cz)
    {
        var result = new Moments();
        foreach (var segment in contour.Segments)
        {
            var a = frame.Point(segment.Start);
            var b = frame.Point(segment.End);
            Extended ay = (Extended)a.Y - cy, az = (Extended)a.Z - cz;
            Extended by = (Extended)b.Y - cy, bz = (Extended)b.Z - cz;
            var cross = ay * bz - by * az;
            // Green's theorem on the straight chord (triangle with the integration origin).
            result.Area += cross / 2;
            result.FirstY += cross * (ay + by) / 6;
            result.FirstZ += cross * (az + bz) / 6;
            result.Iy += cross * (az * az + az * bz + bz * bz) / 12;
            result.Iz += cross * (ay * ay + ay * by + by * by) / 12;
            result.Iyz += cross * (2 * ay * az + ay * bz + by * az + 2 * by * bz) / 24;

            if (segment is SectionArc arc)
            {
                var half = Math.Abs(arc.SweepRadians) / 2;
                var sign = Math.Sign(arc.SweepRadians);
                var uy = arc.MidYDirection;
                var uz = arc.MidZDirection;
                var cap = CircularSegment(frame.Radius(arc), half);
                // Moments of the exact circular segment, referred to its chord midpoint.
                var my = (ay + by) / 2;
                var mz = (az + bz) / 2;
                result.Area += sign * cap.Area;
                result.FirstY += sign * (cap.Area * my + cap.First * uy);
                result.FirstZ += sign * (cap.Area * mz + cap.First * uz);
                result.Iy += sign * (cap.Area * mz * mz + 2 * cap.First * mz * uz +
                    cap.Uu * uz * uz + cap.Vv * uy * uy);
                result.Iz += sign * (cap.Area * my * my + 2 * cap.First * my * uy +
                    cap.Uu * uy * uy + cap.Vv * uz * uz);
                result.Iyz += sign * (cap.Area * my * mz + cap.First * (my * uz + mz * uy) +
                    (cap.Uu - cap.Vv) * uy * uz);
            }
        }
        return result;
    }

    private static (Extended Area, Extended First, Extended Uu, Extended Vv) CircularSegment(double radius, double half)
    {
        if (half <= 1)
        {
            // Factored Taylor expansions of the analytic trigonometric primitives.
            // Leading powers are h^3, h^5, h^7 and h^5 respectively. Factoring r*h
            // also prevents intermediate overflow for shallow arcs with large radii.
            var h2 = half * half;
            var a = Series(half, 1, k => -Math.Pow(4, k));
            var b = Series(half, 2, k => (Math.Pow(9, k) - 8 * k - 1) / 4);
            var uu = Series(half, 3, k => ((12 * k - 8) * Math.Pow(4, k) - Math.Pow(16, k)) / 12);
            var vv = Series(half, 2, k => (Math.Pow(16, k) - 4 * Math.Pow(4, k)) / 12);
            var t = Extended.Product(radius, half);
            var t2 = t * t;
            var t3 = t2 * t;
            var t4 = t2 * t2;
            return (t2 * half * a, t3 * h2 * b, t4 * (h2 * half) * uu, t4 * half * vv);
        }

        var (s, c) = Math.SinCos(half);
        var area = half - s * c;
        var first = 2 * s * s * s / 3 - c * area;
        var uuFactor = half * (0.75 + 0.5 * Math.Cos(2 * half)) -
            7 * Math.Sin(2 * half) / 12 - Math.Sin(4 * half) / 48;
        var vvFactor = half / 4 - Math.Sin(2 * half) / 6 + Math.Sin(4 * half) / 48;
        var r2 = Extended.Product(radius, radius);
        var r3 = r2 * radius;
        var r4 = r2 * r2;
        return (r2 * area, r3 * first, r4 * uuFactor, r4 * vvFactor);
    }

    private static double Series(double half, int firstIndex, Func<int, double> coefficient)
    {
        // 32 terms at |h| <= 1 exceed double precision even for the sin(4h) terms.
        var sum = (Extended)0;
        var power = 1.0;
        var factorial = 1.0;
        for (var k = 0; k < 32; k++)
        {
            if (k >= firstIndex)
                sum += (k % 2 == 0 ? 1 : -1) * coefficient(k) * power / factorial;
            if (k >= firstIndex)
                power *= half * half;
            factorial *= (2.0 * k + 2) * (2.0 * k + 3);
        }
        return sum.Value;
    }

    private static (double Positive, double Negative) Extrema(SectionContour[] contours,
        GeometryFrame frame, Extended cy, Extended cz, double dy, double dz)
    {
        var min = double.PositiveInfinity;
        var max = double.NegativeInfinity;
        foreach (var contour in contours)
        foreach (var segment in contour.Segments)
        {
            Include(frame.Point(segment.Start));
            Include(frame.Point(segment.End));
            if (segment is SectionArc arc)
            {
                var parameter = GeometryMath.ArcParameter(arc, dy, dz);
                if (GeometryMath.OnArc(arc, parameter))
                    Include(frame.ArcPoint(arc, parameter));
                parameter = GeometryMath.ArcParameter(arc, -dy, -dz);
                if (GeometryMath.OnArc(arc, parameter))
                    Include(frame.ArcPoint(arc, parameter));
            }
        }
        return (DomainGuard.Positive(max, nameof(contours)), DomainGuard.Positive(-min, nameof(contours)));

        void Include(Point2 p)
        {
            var distance = (((Extended)p.Y - cy) * dy + ((Extended)p.Z - cz) * dz).Value;
            min = Math.Min(min, distance);
            max = Math.Max(max, distance);
        }
    }

    internal struct Moments
    {
        internal Extended Area, FirstY, FirstZ, Iy, Iz, Iyz;

        internal void Add(Moments other, double sign)
        {
            Area += sign * other.Area;
            FirstY += sign * other.FirstY;
            FirstZ += sign * other.FirstZ;
            Iy += sign * other.Iy;
            Iz += sign * other.Iz;
            Iyz += sign * other.Iyz;
        }
    }
}