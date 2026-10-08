using SpanDraft.Core.Sections.Geometry;
using Xunit;
using static SpanDraft.Tests.SectionGeometryTestSupport;

namespace SpanDraft.Tests;

public class SectionGeometryNumericalTests
{
    [Theory]
    [InlineData(1099511627776, -1099511627776)]
    [InlineData(-1099511627776, 549755813888)]
    public void LargeTranslationsRetainSmallSectionCentroidalProperties(double y, double z)
    {
        var geometry = new SectionGeometry(L());
        var expected = geometry.CalculateProperties();
        var actual = Transform(geometry, p => P(p.Y.Meters + y, p.Z.Meters + z)).CalculateProperties();
        SameProperties(expected, actual);
        // Compare the correctly rounded global centroid rather than a loose global relative tolerance.
        Assert.Equal(expected.Centroid.Y.Meters + y, actual.Centroid.Y.Meters);
        Assert.Equal(expected.Centroid.Z.Meters + z, actual.Centroid.Z.Meters);
    }

    [Fact]
    public void ThinRotatedRectangleDoesNotLoseTheSmallerEigenvalue()
    {
        var t = Math.ScaleB(1, -25);
        // All vertices are binary-exact; orthogonal sides have lengths sqrt(2) and sqrt(2)*t.
        var contour = Polygon(P(0, 0), P(1, 1), P(1 + t, 1 - t), P(t, -t));
        var result = new SectionGeometry(contour).CalculateProperties();
        var i1 = t / 3;
        var i2 = t * t * t / 3;
        Close(2 * t, result.Area.SquareMeters);
        Close(i1, result.I1.MetersToTheFourth);
        Close(i2, result.I2.MetersToTheFourth);
        Close(-Math.PI / 4, result.PrincipalAxisAngleRadians);
        Close((i1 + i2) / 2, result.Iy.MetersToTheFourth);
        Close((i1 + i2) / 2, result.Iz.MetersToTheFourth);
        Close((i1 - i2) / 2, result.Iyz.MetersToTheFourth);
        Close(1 / Math.Sqrt(2), result.Axis1PositiveDistance.Meters);

        var centroid = P((1 + t) / 2, (1 - t) / 2);
        var axisY = 1 / Math.Sqrt(2);
        var axisZ = -axisY;
        // Nearly cancelling centroidal projection terms round at the geometric
        // input scale, not at the tiny resulting extreme-fiber distance.
        var geometryScale = contour.Segments.Max(segment =>
            Math.Abs(axisY * (segment.Start.Y.Meters - centroid.Y.Meters)) +
            Math.Abs(axisZ * (segment.Start.Z.Meters - centroid.Z.Meters)));
        var distance2 = t / Math.Sqrt(2);
        CloseProjected(distance2, result.Axis2PositiveDistance.Meters, geometryScale);
        CloseProjected(distance2, result.Axis2NegativeDistance.Meters, geometryScale);

        var w2 = i2 / distance2;
        // Propagate distance roundoff through W2 = I2 / d: |dW2/dd| = I2/d^2 = |W2|/d.
        CloseProjected(w2, result.W2Positive.CubicMeters, Math.Abs(w2) * geometryScale / distance2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ThinAnnulusRetainsCancellationBits(bool fullTurn)
    {
        const double ro = 1;
        var ri = 1 - Math.ScaleB(1, -26);
        var result = new SectionGeometry(Circle(ro, fullTurn: fullTurn),
            [Circle(ri, fullTurn: fullTurn)]).CalculateProperties();
        var area = Math.PI * (ro - ri) * (ro + ri);
        var inertia = area * (ro * ro + ri * ri) / 4;
        Close(area, result.Area.SquareMeters);
        Close(inertia, result.Iy.MetersToTheFourth);
        Close(inertia, result.Iz.MetersToTheFourth);
        Close(inertia, result.I1.MetersToTheFourth);
        Close(inertia, result.I2.MetersToTheFourth);
        Close(inertia / ro, result.W1Positive.CubicMeters);
        Assert.Equal(0, result.PrincipalAxisAngleRadians);
    }

    [Fact]
    public void ReversingRotatedThinCircularBoundariesPreservesTheirExactArcGeometry()
    {
        var outer = Transform(Circle(1, 4, -3), p => Rotate(p, 0.37));
        var inner = Transform(Circle(1 - Math.ScaleB(1, -26), 4, -3), p => Rotate(p, 0.37));
        var geometry = new SectionGeometry(outer, [inner]);
        var reversed = new SectionGeometry(outer.Reversed(), [inner.Reversed()]);
        SameProperties(geometry.CalculateProperties(), reversed.CalculateProperties());
    }

    [Fact]
    public void ThinRectangularWallRetainsCancellationBits()
    {
        var t = Math.ScaleB(1, -26);
        var inner = 1 - 2 * t;
        var result = new SectionGeometry(Rectangle(1, 1, -0.5, -0.5),
            [Rectangle(inner, inner, -0.5 + t, -0.5 + t)]).CalculateProperties();
        var area = 4 * t * (1 - t);
        var inertia = t * (1 - t) * (1 + inner * inner) / 3;
        Close(area, result.Area.SquareMeters);
        Close(inertia, result.I1.MetersToTheFourth);
        Close(inertia, result.I2.MetersToTheFourth);
        Close(2 * inertia, result.W1Positive.CubicMeters);
        Assert.Equal(0, result.PrincipalAxisAngleRadians);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.61)]
    [InlineData(2.21)]
    public void RoundoffEqualMomentsChooseHorizontalAxisWithoutNoise(double angle)
    {
        var square = new SectionGeometry(Transform(Rectangle(2, 2, -1, -1),
            p => Rotate(p, angle))).CalculateProperties();
        var nearSquare = new SectionGeometry(Transform(Rectangle(2, 2 * (1 + 2 * Epsilon), -1, -1),
            p => Rotate(p, angle))).CalculateProperties();
        Assert.Equal(0, square.PrincipalAxisAngleRadians);
        Assert.Equal(0, nearSquare.PrincipalAxisAngleRadians);
        Assert.True(nearSquare.I1.MetersToTheFourth >= nearSquare.I2.MetersToTheFourth);
    }

    [Fact]
    public void ResolvableMomentDifferenceIsNotTreatedAsAnIsotropicSection()
    {
        var result = new SectionGeometry(Rectangle(2 * (1 + 1e-11), 2)).CalculateProperties();
        Close(-Math.PI / 2, result.PrincipalAxisAngleRadians);
        Assert.True(result.I1.MetersToTheFourth > result.I2.MetersToTheFourth);
    }

    [Theory]
    [InlineData(1e-5)]
    [InlineData(1e-7)]
    public void ShallowCircularCapUsesStableAnalyticalIntegrals(double half)
    {
        var start = P(Math.Cos(half), -Math.Sin(half));
        var end = P(Math.Cos(half), Math.Sin(half));
        var result = new SectionGeometry(new SectionContour([
            new SectionArc(P(0, 0), start, end, ArcDirection.Counterclockwise),
            new SectionLine(end, start)])).CalculateProperties();
        var h2 = half * half;
        // Independent small-angle reference expansions; omitted terms are O(h^4)
        // relative (<=1e-20 here), well below one double rounding unit.
        var area = Math.Pow(half, 3) * (2.0 / 3 - 2.0 / 15 * h2);
        var iy = Math.Pow(half, 5) * (2.0 / 15 - 4.0 / 63 * h2);
        var iz = Math.Pow(half, 7) * (2.0 / 175 - 32.0 / 7875 * h2);
        var distanceFromChord = h2 * (1.0 / 5 - 13.0 / 1050 * h2);
        var sagitta = 2 * Math.Pow(Math.Sin(half / 2), 2);
        Close(area, result.Area.SquareMeters);
        Close(iy, result.Iy.MetersToTheFourth);
        Close(iz, result.Iz.MetersToTheFourth);
        Close(iy, result.I1.MetersToTheFourth);
        Close(iz, result.I2.MetersToTheFourth);
        Close(0, result.Iyz.MetersToTheFourth, iy);
        Close(0, result.PrincipalAxisAngleRadians, 1);
        Close(Math.Sin(half), result.Axis1PositiveDistance.Meters);
        Close(Math.Sin(half), result.Axis1NegativeDistance.Meters);
        Close(sagitta - distanceFromChord, result.Axis2PositiveDistance.Meters);
        Close(distanceFromChord, result.Axis2NegativeDistance.Meters);
        Close(iz / distanceFromChord, result.W2Negative.CubicMeters);
    }

    [Theory]
    [InlineData(1e100)]
    [InlineData(1e-100)]
    public void UnrepresentablePositiveResultsAreRejected(double size)
    {
        var geometry = new SectionGeometry(Rectangle(size, size));
        Assert.Throws<ArgumentOutOfRangeException>(() => geometry.CalculateProperties());
        var circle = new SectionGeometry(Circle(size));
        Assert.Throws<ArgumentOutOfRangeException>(() => circle.CalculateProperties());
    }

    [Fact]
    public void VerySmallButRepresentableSectionDoesNotUnderflowIntermediatePowers()
    {
        var k = Math.ScaleB(1, -260);
        var result = new SectionGeometry(Rectangle(2 * k, 4 * k)).CalculateProperties();
        Assert.Equal(Math.ScaleB(8, -520), result.Area.SquareMeters);
        Close(Math.ScaleB(128.0 / 12, -1040), result.I1.MetersToTheFourth);
        Close(Math.ScaleB(32.0 / 12, -1040), result.I2.MetersToTheFourth);
        Close(Math.ScaleB(16.0 / 3, -780), result.W1Positive.CubicMeters);
    }
}
