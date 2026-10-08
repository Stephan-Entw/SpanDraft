using SpanDraft.Core.Sections.Geometry;
using SpanDraft.Core.Sections.Parametric;
using SpanDraft.Core.Units;
using Xunit;
using static SpanDraft.Tests.SectionGeometryTestSupport;

namespace SpanDraft.Tests;

internal static class ParametricSectionTestSupport
{
    internal static Length M(double value) => Length.FromMeters(value);

    internal static object Shape(int kind, double b = 6, double h = 10, double t = 1, double tf = 1, double r = 0) => kind switch
    {
        0 => new RectangleSectionGeometry(M(b), M(h)),
        1 => new RectangularHollowSectionGeometry(M(b), M(h), M(t), M(r)),
        2 => new CircleSectionGeometry(M(b)),
        3 => new CircularHollowSectionGeometry(M(b), M(t)),
        4 => new ISectionGeometry(M(h), M(b), M(t), M(tf), M(r)),
        5 => new USectionGeometry(M(h), M(b), M(t), M(tf), M(r)),
        6 => new TSectionGeometry(M(h), M(b), M(t), M(tf), M(r)),
        7 => new AngleSectionGeometry(M(b), M(h), M(t), M(r)),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    internal static SectionGeometry Geometry(object shape) =>
        (SectionGeometry)shape.GetType().GetProperty("Geometry")!.GetValue(shape)!;

    // Independent area integrals of rectangles and quarter disks, not boundary integration.
    internal readonly record struct Integrals(double A, double Qy, double Qz, double Jy, double Jz, double Jyz)
    {
        public static Integrals operator +(Integrals a, Integrals b) =>
            new(a.A + b.A, a.Qy + b.Qy, a.Qz + b.Qz, a.Jy + b.Jy, a.Jz + b.Jz, a.Jyz + b.Jyz);
        public static Integrals operator -(Integrals a, Integrals b) =>
            new(a.A - b.A, a.Qy - b.Qy, a.Qz - b.Qz, a.Jy - b.Jy, a.Jz - b.Jz, a.Jyz - b.Jyz);

        internal Integrals Place(double y, double z, int sy = 1, int sz = 1) => new(A,
            A * y + sy * Qy, A * z + sz * Qz,
            Jy + 2 * z * sz * Qz + A * z * z,
            Jz + 2 * y * sy * Qy + A * y * y,
            sy * sz * Jyz + y * sz * Qz + z * sy * Qy + A * y * z);
    }

    internal static Integrals Rect(double b, double h, double y = 0, double z = 0) =>
        new Integrals(b * h, b * b * h / 2, b * h * h / 2,
            b * h * h * h / 3, h * b * b * b / 3, b * b * h * h / 4).Place(y, z);

    private static Integrals Quarter(double radius, double y, double z, int sy, int sz)
    {
        var r2 = radius * radius;
        var r3 = r2 * radius;
        var r4 = r2 * r2;
        // Polar integrals over 0 <= theta <= pi/2: Qy=Qz=r^3/3, Jyz=r^4/8.
        return new Integrals(Math.PI * r2 / 4, r3 / 3, r3 / 3,
            Math.PI * r4 / 16, Math.PI * r4 / 16, r4 / 8).Place(y, z, sy, sz);
    }

    private static Integrals Corner(double y, double z, double r, int sy, int sz) => r == 0 ? default :
        Rect(r, r, Math.Min(y, y + sy * r), Math.Min(z, z + sz * r)) -
        Quarter(r, y + sy * r, z + sz * r, -sy, -sz);

    private static Integrals RoundedRect(double b, double h, double r, double y = 0, double z = 0) =>
        Rect(b, h, y, z) - Corner(y, z, r, 1, 1) - Corner(y + b, z, r, -1, 1) -
        Corner(y + b, z + h, r, -1, -1) - Corner(y, z + h, r, 1, -1);

    internal static Integrals Reference(int kind, double b = 6, double h = 10, double t = 1, double tf = 1, double r = 0)
    {
        var left = (b - t) / 2;
        var right = (b + t) / 2;
        return kind switch
        {
            0 => Rect(b, h),
            1 => RoundedRect(b, h, r) - RoundedRect(b - 2 * t, h - 2 * t, Math.Max(0, r - t), t, t),
            2 or 3 => DiskDifference(b / 2, kind == 2 ? 0 : b / 2 - t),
            4 => Rect(b, tf) + Rect(b, tf, 0, h - tf) + Rect(t, h - 2 * tf, left, tf) +
                Corner(right, tf, r, 1, 1) + Corner(right, h - tf, r, 1, -1) +
                Corner(left, h - tf, r, -1, -1) + Corner(left, tf, r, -1, 1),
            5 => Rect(b, tf) + Rect(b, tf, 0, h - tf) + Rect(t, h - 2 * tf, 0, tf) +
                Corner(t, tf, r, 1, 1) + Corner(t, h - tf, r, 1, -1),
            6 => Rect(b, tf, 0, h - tf) + Rect(t, h - tf, left) +
                Corner(right, h - tf, r, 1, -1) + Corner(left, h - tf, r, -1, -1),
            7 => Rect(b, t) + Rect(t, h - t, 0, t) + Corner(t, t, r, 1, 1),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    private static Integrals DiskDifference(double ro, double ri)
    {
        var a = Math.PI * (ro * ro - ri * ri);
        var inertia = Math.PI * (Math.Pow(ro, 4) - Math.Pow(ri, 4)) / 4;
        return new Integrals(a, 0, 0, inertia, inertia, 0).Place(ro, ro);
    }

    internal static void AssertTensor(Integrals expected, SectionGeometryProperties actual)
    {
        var cy = expected.Qy / expected.A;
        var cz = expected.Qz / expected.A;
        var iy = expected.Jy - expected.A * cz * cz;
        var iz = expected.Jz - expected.A * cy * cy;
        var iyz = expected.Jyz - expected.A * cy * cz;
        var mean = (iy + iz) / 2;
        var gap = Math.Sqrt(Math.Pow((iy - iz) / 2, 2) + iyz * iyz);
        var scale = Math.Max(iy, iz);
        Close(expected.A, actual.Area.SquareMeters);
        Close(cy, actual.Centroid.Y.Meters);
        Close(cz, actual.Centroid.Z.Meters);
        Close(iy, actual.Iy.MetersToTheFourth, scale);
        Close(iz, actual.Iz.MetersToTheFourth, scale);
        Close(iyz, actual.Iyz.MetersToTheFourth, scale);
        Close(mean + gap, actual.I1.MetersToTheFourth, scale);
        Close(mean - gap, actual.I2.MetersToTheFourth, scale);
    }

    internal static void AssertModuli(int kind, Integrals expected, SectionGeometryProperties actual,
        double b = 6, double h = 10, double t = 1, double tf = 1)
    {
        var cy = expected.Qy / expected.A;
        var cz = expected.Qz / expected.A;
        var iy = expected.Jy - expected.A * cz * cz;
        var iz = expected.Jz - expected.A * cy * cy;
        var iyz = expected.Jyz - expected.A * cy * cz;
        var angle = Math.Atan2(-2 * iyz, iy - iz) / 2;
        if (angle >= Math.PI / 2) angle -= Math.PI;
        if (Math.Abs(iy - iz) + Math.Abs(iyz) < 64 * Epsilon * Math.Max(iy, iz)) angle = 0;
        Close(angle, actual.PrincipalAxisAngleRadians, 1);
        var gap = Math.Sqrt(Math.Pow((iy - iz) / 2, 2) + iyz * iyz);
        var i1 = (iy + iz) / 2 + gap;
        var i2 = (iy + iz) / 2 - gap;
        var (s, c) = Math.SinCos(angle);
        // Extreme projections of the independently decomposed material rectangles.
        // Circular sections have the same outer support radius in every direction.
        var points = ExtremePoints(kind, b, h, t, tf);
        var p1 = kind is 2 or 3 ? [b / 2, -b / 2] : points.Select(p => -(p.Y - cy) * s + (p.Z - cz) * c).ToArray();
        var p2 = kind is 2 or 3 ? [b / 2, -b / 2] : points.Select(p => (p.Y - cy) * c + (p.Z - cz) * s).ToArray();
        Close(p1.Max(), actual.Axis1PositiveDistance.Meters);
        Close(-p1.Min(), actual.Axis1NegativeDistance.Meters);
        Close(p2.Max(), actual.Axis2PositiveDistance.Meters);
        Close(-p2.Min(), actual.Axis2NegativeDistance.Meters);
        Close(i1 / p1.Max(), actual.W1Positive.CubicMeters);
        Close(i1 / -p1.Min(), actual.W1Negative.CubicMeters);
        Close(i2 / p2.Max(), actual.W2Positive.CubicMeters);
        Close(i2 / -p2.Min(), actual.W2Negative.CubicMeters);
    }

    private static (double Y, double Z)[] ExtremePoints(int kind, double b, double h, double t, double tf)
    {
        var rectangles = kind switch
        {
            6 => new[] { ((b - t) / 2, 0.0, t, h - tf), (0.0, h - tf, b, tf) },
            7 => new[] { (0.0, 0.0, b, t), (0.0, t, t, h - t) },
            _ => new[] { (0.0, 0.0, b, h) }
        };
        return rectangles.SelectMany(v => new[] { (v.Item1, v.Item2), (v.Item1 + v.Item3, v.Item2),
            (v.Item1 + v.Item3, v.Item2 + v.Item4), (v.Item1, v.Item2 + v.Item4) }).ToArray();
    }
}
