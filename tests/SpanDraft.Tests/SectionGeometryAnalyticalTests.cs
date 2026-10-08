using SpanDraft.Core.Sections.Geometry;
using Xunit;
using static SpanDraft.Tests.SectionGeometryTestSupport;

namespace SpanDraft.Tests;

public class SectionGeometryAnalyticalTests
{
    [Theory]
    [InlineData(4, 8, 0)]
    [InlineData(8, 4, -1.5707963267948966)]
    public void RectangleUsesCentroidalMomentsAndBothPrincipalAxes(double width, double height, double angle)
    {
        var result = new SectionGeometry(Rectangle(width, height)).CalculateProperties();
        var iy = width * Math.Pow(height, 3) / 12;
        var iz = height * Math.Pow(width, 3) / 12;
        Close(width * height, result.Area.SquareMeters);
        Close(width / 2, result.Centroid.Y.Meters);
        Close(height / 2, result.Centroid.Z.Meters);
        Close(iy, result.Iy.MetersToTheFourth);
        Close(iz, result.Iz.MetersToTheFourth);
        Close(0, result.Iyz.MetersToTheFourth, Math.Max(iy, iz));
        Close(Math.Max(iy, iz), result.I1.MetersToTheFourth);
        Close(Math.Min(iy, iz), result.I2.MetersToTheFourth);
        Close(angle, result.PrincipalAxisAngleRadians, 1);
        var d1 = Math.Max(width, height) / 2;
        var d2 = Math.Min(width, height) / 2;
        Close(d1, result.Axis1PositiveDistance.Meters);
        Close(d1, result.Axis1NegativeDistance.Meters);
        Close(d2, result.Axis2PositiveDistance.Meters);
        Close(d2, result.Axis2NegativeDistance.Meters);
        Close(Math.Max(iy, iz) / d1, result.W1Positive.CubicMeters);
        Close(Math.Max(iy, iz) / d1, result.W1Negative.CubicMeters);
        Close(Math.Min(iy, iz) / d2, result.W2Positive.CubicMeters);
        Close(Math.Min(iy, iz) / d2, result.W2Negative.CubicMeters);
    }

    [Fact]
    public void ShiftedRectangleHasSignedCentroidAndUnchangedCentroidalProperties()
    {
        var expected = new SectionGeometry(Rectangle(4, 8)).CalculateProperties();
        var actual = new SectionGeometry(Rectangle(4, 8, -17, 23)).CalculateProperties();
        SameProperties(expected, actual);
        Close(-15, actual.Centroid.Y.Meters);
        Close(27, actual.Centroid.Z.Meters);
    }

    [Theory]
    [InlineData(0.37)]
    [InlineData(-0.81)]
    [InlineData(2.1)]
    [InlineData(-2.4)]
    public void RotatedRectangleRetainsPrincipalPropertiesAndHasSignedProductMoment(double rotation)
    {
        const double width = 4, height = 8;
        var contour = Transform(Rectangle(width, height, -width / 2, -height / 2), p => Rotate(p, rotation));
        var result = new SectionGeometry(contour).CalculateProperties();
        var originalIy = width * Math.Pow(height, 3) / 12;
        var originalIz = height * Math.Pow(width, 3) / 12;
        var (s, c) = Math.SinCos(rotation);
        Close(originalIy * c * c + originalIz * s * s, result.Iy.MetersToTheFourth);
        Close(originalIy * s * s + originalIz * c * c, result.Iz.MetersToTheFourth);
        Close((originalIz - originalIy) * s * c, result.Iyz.MetersToTheFourth);
        Close(originalIy, result.I1.MetersToTheFourth);
        Close(originalIz, result.I2.MetersToTheFourth);
        Close(0, Math.Sin(result.PrincipalAxisAngleRadians - rotation), 1);
        Close(height / 2, result.Axis1PositiveDistance.Meters);
        Close(height / 2, result.Axis1NegativeDistance.Meters);
        Close(width / 2, result.Axis2PositiveDistance.Meters);
        Close(width / 2, result.Axis2NegativeDistance.Meters);
        Close(originalIy / (height / 2), result.W1Positive.CubicMeters);
        Close(originalIz / (width / 2), result.W2Negative.CubicMeters);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void HollowRectangleSubtractsHolesRegardlessOfWinding(bool reverseOuter, bool reverseInner)
    {
        var outer = Rectangle(6, 10, -3, -5);
        var inner = Rectangle(5, 9, -2.5, -4.5);
        var result = new SectionGeometry(reverseOuter ? outer.Reversed() : outer,
            [reverseInner ? inner.Reversed() : inner]).CalculateProperties();
        const double area = 60 - 45;
        var iy = (6 * Math.Pow(10, 3) - 5 * Math.Pow(9, 3)) / 12;
        var iz = (10 * Math.Pow(6, 3) - 9 * Math.Pow(5, 3)) / 12;
        Close(area, result.Area.SquareMeters);
        Close(0, result.Centroid.Y.Meters, 10);
        Close(0, result.Centroid.Z.Meters, 10);
        Close(iy, result.Iy.MetersToTheFourth);
        Close(iz, result.Iz.MetersToTheFourth);
        Close(0, result.Iyz.MetersToTheFourth, iy);
        Close(iy, result.I1.MetersToTheFourth);
        Close(iz, result.I2.MetersToTheFourth);
        Close(5, result.Axis1PositiveDistance.Meters);
        Close(5, result.Axis1NegativeDistance.Meters);
        Close(3, result.Axis2PositiveDistance.Meters);
        Close(3, result.Axis2NegativeDistance.Meters);
        Close(iy / 5, result.W1Positive.CubicMeters);
        Close(iy / 5, result.W1Negative.CubicMeters);
        Close(iz / 3, result.W2Positive.CubicMeters);
        Close(iz / 3, result.W2Negative.CubicMeters);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ExactCircleUsesAnalyticalAreaInertiaAndInteriorArcExtrema(bool fullTurn, bool reverse)
    {
        const double radius = 2;
        var circle = Circle(radius, -5, 7, fullTurn);
        var result = new SectionGeometry(reverse ? circle.Reversed() : circle).CalculateProperties();
        var inertia = Math.PI * Math.Pow(radius, 4) / 4;
        Close(Math.PI * radius * radius, result.Area.SquareMeters);
        Close(-5, result.Centroid.Y.Meters);
        Close(7, result.Centroid.Z.Meters);
        Close(inertia, result.Iy.MetersToTheFourth);
        Close(inertia, result.Iz.MetersToTheFourth);
        Close(0, result.Iyz.MetersToTheFourth, inertia);
        Close(inertia, result.I1.MetersToTheFourth);
        Close(inertia, result.I2.MetersToTheFourth);
        Assert.Equal(0, result.PrincipalAxisAngleRadians);
        Close(radius, result.Axis1PositiveDistance.Meters);
        Close(radius, result.Axis1NegativeDistance.Meters);
        Close(radius, result.Axis2PositiveDistance.Meters);
        Close(radius, result.Axis2NegativeDistance.Meters);
        foreach (var w in new[] { result.W1Positive, result.W1Negative, result.W2Positive, result.W2Negative })
            Close(inertia / radius, w.CubicMeters);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ExactAnnulusMatchesDifferenceOfCircleFormulas(bool fullTurn, bool reverseHole)
    {
        const double ro = 3, ri = 2;
        var hole = Circle(ri, 4, -7, fullTurn);
        var result = new SectionGeometry(Circle(ro, 4, -7, fullTurn),
            [reverseHole ? hole.Reversed() : hole]).CalculateProperties();
        var inertia = Math.PI * (Math.Pow(ro, 4) - Math.Pow(ri, 4)) / 4;
        Close(Math.PI * (ro * ro - ri * ri), result.Area.SquareMeters);
        Close(4, result.Centroid.Y.Meters);
        Close(-7, result.Centroid.Z.Meters);
        Close(inertia, result.Iy.MetersToTheFourth);
        Close(inertia, result.Iz.MetersToTheFourth);
        Close(0, result.Iyz.MetersToTheFourth, inertia);
        Close(inertia, result.I1.MetersToTheFourth);
        Close(inertia, result.I2.MetersToTheFourth);
        Assert.Equal(0, result.PrincipalAxisAngleRadians);
        foreach (var distance in new[] { result.Axis1PositiveDistance, result.Axis1NegativeDistance,
            result.Axis2PositiveDistance, result.Axis2NegativeDistance })
            Close(ro, distance.Meters);
        foreach (var w in new[] { result.W1Positive, result.W1Negative, result.W2Positive, result.W2Negative })
            Close(inertia / ro, w.CubicMeters);
    }

    [Fact]
    public void AsymmetricLMatchesIndependentRectangleUnionAndEigenvectors()
    {
        var result = new SectionGeometry(L()).CalculateProperties();
        const double cy = 27.0 / 14, cz = 13.0 / 14;
        // Union: 5x1 + 1x3 - 1x1, evaluated independently with the parallel-axis theorem.
        const double iy = 361.0 / 84, iz = 1369.0 / 84, iyz = -30.0 / 7;
        var i1 = (865 + 72 * Math.Sqrt(74)) / 84;
        var i2 = (865 - 72 * Math.Sqrt(74)) / 84;
        var c = Math.Sqrt((1 - 7 / Math.Sqrt(74)) / 2);
        var s = Math.Sqrt((1 + 7 / Math.Sqrt(74)) / 2);
        Close(7, result.Area.SquareMeters);
        Close(cy, result.Centroid.Y.Meters);
        Close(cz, result.Centroid.Z.Meters);
        Close(iy, result.Iy.MetersToTheFourth);
        Close(iz, result.Iz.MetersToTheFourth);
        Close(iyz, result.Iyz.MetersToTheFourth);
        Close(i1, result.I1.MetersToTheFourth);
        Close(i2, result.I2.MetersToTheFourth);
        Close(Math.Atan2(s, c), result.PrincipalAxisAngleRadians);
        var d1Positive = s * cy + c * (3 - cz); // vertex (0,3)
        var d1Negative = s * (5 - cy) + c * cz; // vertex (5,0)
        var d2Positive = c * (1 - cy) + s * (3 - cz); // vertex (1,3)
        var d2Negative = c * cy + s * cz; // vertex (0,0)
        Close(d1Positive, result.Axis1PositiveDistance.Meters);
        Close(d1Negative, result.Axis1NegativeDistance.Meters);
        Close(d2Positive, result.Axis2PositiveDistance.Meters);
        Close(d2Negative, result.Axis2NegativeDistance.Meters);
        Close(i1 / d1Positive, result.W1Positive.CubicMeters);
        Close(i1 / d1Negative, result.W1Negative.CubicMeters);
        Close(i2 / d2Positive, result.W2Positive.CubicMeters);
        Close(i2 / d2Negative, result.W2Negative.CubicMeters);
        Assert.NotEqual(result.W1Positive, result.W1Negative);
        Assert.NotEqual(result.W2Positive, result.W2Negative);
        var transformedProduct = (iy - iz) * s * c + iyz * (c * c - s * s);
        Close(0, transformedProduct, i1);
    }

    [Theory]
    [InlineData(0.3)]
    [InlineData(-0.3)]
    [InlineData(1.0471975511965976)]
    [InlineData(4.71238898038469)]
    [InlineData(-4.71238898038469)]
    public void CircularSectorMatchesIndependentPolarIntegralsIncludingMajorAndClockwiseArcs(double sweep)
    {
        const double radius = 2;
        var center = P(0, 0);
        var start = P(radius, 0);
        var end = P(radius * Math.Cos(sweep), radius * Math.Sin(sweep));
        var arc = new SectionArc(center, start, end,
            sweep > 0 ? ArcDirection.Counterclockwise : ArcDirection.Clockwise);
        var result = new SectionGeometry(new SectionContour(
            [new SectionLine(center, start), arc, new SectionLine(end, center)])).CalculateProperties();
        var sign = Math.Sign(sweep);
        var area = radius * radius * Math.Abs(sweep) / 2;
        var qy = sign * Math.Pow(radius, 3) * Math.Sin(sweep) / 3;
        var qz = sign * Math.Pow(radius, 3) * (1 - Math.Cos(sweep)) / 3;
        var iy = sign * Math.Pow(radius, 4) * (sweep / 8 - Math.Sin(2 * sweep) / 16) - qz * qz / area;
        var iz = sign * Math.Pow(radius, 4) * (sweep / 8 + Math.Sin(2 * sweep) / 16) - qy * qy / area;
        var iyz = sign * Math.Pow(radius, 4) * Math.Pow(Math.Sin(sweep), 2) / 8 - qy * qz / area;
        Close(area, result.Area.SquareMeters);
        Close(qy / area, result.Centroid.Y.Meters);
        Close(qz / area, result.Centroid.Z.Meters);
        Close(iy, result.Iy.MetersToTheFourth, Math.Max(iy, iz));
        Close(iz, result.Iz.MetersToTheFourth, Math.Max(iy, iz));
        Close(iyz, result.Iyz.MetersToTheFourth, Math.Max(iy, iz));
    }

    [Fact]
    public void SemicircleExtremaIncludeTheInteriorOfTheArc()
    {
        const double radius = 2;
        var contour = new SectionContour([
            new SectionArc(P(0, 0), P(radius, 0), P(-radius, 0), ArcDirection.Counterclockwise),
            new SectionLine(P(-radius, 0), P(radius, 0))]);
        var result = new SectionGeometry(contour).CalculateProperties();
        var cz = 4 * radius / (3 * Math.PI);
        var i1 = Math.PI * Math.Pow(radius, 4) / 8;
        var i2 = i1 - Math.PI * radius * radius / 2 * cz * cz;
        Close(cz, result.Centroid.Z.Meters);
        Close(i1, result.I1.MetersToTheFourth);
        Close(i2, result.I2.MetersToTheFourth);
        Close(-Math.PI / 2, result.PrincipalAxisAngleRadians);
        Close(radius, result.Axis1PositiveDistance.Meters);
        Close(radius, result.Axis1NegativeDistance.Meters);
        Close(cz, result.Axis2PositiveDistance.Meters);
        Close(radius - cz, result.Axis2NegativeDistance.Meters);
        Close(i2 / cz, result.W2Positive.CubicMeters);
        Close(i2 / (radius - cz), result.W2Negative.CubicMeters);
    }

    [Fact]
    public void SeveralOffCenterHolesUseSignedParallelAxisContributions()
    {
        var outer = Rectangle(10, 10, -5, -5);
        var holes = new[] { Rectangle(2, 2, -3, -3), Rectangle(1, 2, 2, 1) };
        var result = new SectionGeometry(outer, holes).CalculateProperties();
        var area = 100.0 - 4 - 2;
        var qy = -4 * -2 - 2 * 2.5;
        var qz = -4 * -2 - 2 * 2;
        var iy = 10000.0 / 12 - (16.0 / 12 + 4 * 4) - (8.0 / 12 + 2 * 4) - qz * qz / area;
        var iz = 10000.0 / 12 - (16.0 / 12 + 4 * 4) - (2.0 / 12 + 2 * 6.25) - qy * qy / area;
        var iyz = -4 * 4 - 2 * 5 - qy * qz / area;
        Close(area, result.Area.SquareMeters);
        Close(qy / area, result.Centroid.Y.Meters);
        Close(qz / area, result.Centroid.Z.Meters);
        Close(iy, result.Iy.MetersToTheFourth);
        Close(iz, result.Iz.MetersToTheFourth);
        Close(iyz, result.Iyz.MetersToTheFourth);
    }

    [Fact]
    public void EccentricCircularHoleHasAnalyticalCentroidAndTensor()
    {
        const double ro = 4, ri = 1, hy = 1, hz = 0.5;
        var result = new SectionGeometry(Circle(ro), [Circle(ri, hy, hz)]).CalculateProperties();
        var holeArea = Math.PI * ri * ri;
        var area = Math.PI * (ro * ro - ri * ri);
        var cy = -holeArea * hy / area;
        var cz = -holeArea * hz / area;
        var circleMoment = Math.PI * (Math.Pow(ro, 4) - Math.Pow(ri, 4)) / 4;
        Close(area, result.Area.SquareMeters);
        Close(cy, result.Centroid.Y.Meters);
        Close(cz, result.Centroid.Z.Meters);
        Close(circleMoment - holeArea * hz * hz - area * cz * cz, result.Iy.MetersToTheFourth);
        Close(circleMoment - holeArea * hy * hy - area * cy * cy, result.Iz.MetersToTheFourth);
        Close(-holeArea * hy * hz - area * cy * cz, result.Iyz.MetersToTheFourth);
        var theta = result.PrincipalAxisAngleRadians;
        var positive1 = ro - (-cy * Math.Sin(theta) + cz * Math.Cos(theta));
        var negative1 = ro + (-cy * Math.Sin(theta) + cz * Math.Cos(theta));
        Close(positive1, result.Axis1PositiveDistance.Meters);
        Close(negative1, result.Axis1NegativeDistance.Meters);
        Close(result.I1.MetersToTheFourth / positive1, result.W1Positive.CubicMeters);
        Close(result.I1.MetersToTheFourth / negative1, result.W1Negative.CubicMeters);
    }
}