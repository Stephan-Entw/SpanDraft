using SpanDraft.Core.Sections.Geometry;
using SpanDraft.Core.Units;
using Xunit;
using static SpanDraft.Tests.SectionGeometryTestSupport;

namespace SpanDraft.Tests;

public class SectionGeometryInputTests
{
    [Theory]
    [InlineData(-1.25)]
    [InlineData(0)]
    [InlineData(3.5)]
    public void SignedDomainTypesRetainValuesAndEngineeringUnitConversions(double value)
    {
        var coordinate = SectionCoordinate.FromMeters(value);
        var product = ProductMomentOfArea.FromMetersToTheFourth(value);
        Assert.Equal(value, coordinate.Meters);
        Assert.Equal(value * 1000, coordinate.Millimeters);
        Assert.Equal(coordinate, SectionCoordinate.FromMillimeters(value * 1000));
        Assert.Equal(value, product.MetersToTheFourth);
        Assert.Equal(value * 1e12, product.MillimetersToTheFourth);
        Assert.Equal(product, ProductMomentOfArea.FromMillimetersToTheFourth(value * 1e12));
        Assert.Equal(default, SectionCoordinate.FromMeters(0));
        Assert.Equal(default, ProductMomentOfArea.FromMetersToTheFourth(0));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void CoordinatesAndProductMomentRejectEveryNonFiniteInput(double value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SectionCoordinate.FromMeters(value));
        Assert.Throws<ArgumentOutOfRangeException>(() => SectionCoordinate.FromMillimeters(value));
        Assert.Throws<ArgumentOutOfRangeException>(() => ProductMomentOfArea.FromMetersToTheFourth(value));
        Assert.Throws<ArgumentOutOfRangeException>(() => ProductMomentOfArea.FromMillimetersToTheFourth(value));
        Assert.Throws<ArgumentOutOfRangeException>(() => SectionPoint.FromMeters(value, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => SectionPoint.FromMeters(0, value));
        Assert.Throws<ArgumentOutOfRangeException>(() => SectionPoint.FromMillimeters(value, 0));
    }

    [Fact]
    public void OriginalNonNegativeUnitSemanticsRemainUnchanged()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Length.FromMeters(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => SecondMomentOfArea.FromMetersToTheFourth(-1));
        var point = SectionPoint.FromMillimeters(-12.5, 25);
        Assert.Equal(-0.0125, point.Y.Meters);
        Assert.Equal(0.025, point.Z.Meters);
        Assert.Equal(default, SectionPoint.FromMeters(0, 0));
    }

    [Fact]
    public void ZeroLengthLineIsRejected() =>
        Assert.Throws<ArgumentException>(() => new SectionLine(P(1, 2), P(1, 2)));

    [Fact]
    public void ArcRejectsZeroRadiusAndEndpointsOnDifferentCircles()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SectionArc(P(0, 0), P(0, 0), P(1, 0), ArcDirection.Counterclockwise));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SectionArc(P(0, 0), P(1, 0), P(0, 0), ArcDirection.Counterclockwise));
        Assert.Throws<ArgumentException>(() =>
            new SectionArc(P(0, 0), P(1, 0), P(0, 2), ArcDirection.Counterclockwise));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SectionArc(P(0, 0), P(1, 0), P(0, 1), (ArcDirection)7));
    }

    [Fact]
    public void ArcDirectionSelectsMinorMajorAndFullSweepsUnambiguously()
    {
        var ccw = new SectionArc(P(0, 0), P(1, 0), P(0, 1), ArcDirection.Counterclockwise);
        var cw = new SectionArc(P(0, 0), P(1, 0), P(0, 1), ArcDirection.Clockwise);
        Close(Math.PI / 2, ccw.SweepRadians);
        Close(-3 * Math.PI / 2, cw.SweepRadians);
        Close(-Math.PI / 2, ccw.Reversed().SweepRadians);
        Close(3 * Math.PI / 2, cw.Reversed().SweepRadians);
        var full = new SectionArc(P(0, 0), P(1, 0), P(1, 0), ArcDirection.Clockwise);
        Assert.Equal(-Math.Tau, full.SweepRadians);
        Assert.Equal(Math.Tau, full.Reversed().SweepRadians);
        Assert.Equal(Length.FromMeters(1), ccw.Radius);
        Assert.Equal(P(0, 0), ccw.Center);
        Assert.Equal(ccw.Start, ccw.Reversed().End);
        Assert.Equal(ccw.End, ccw.Reversed().Start);
    }

    [Fact]
    public void ArcAcceptsOnlyRoundingSizedRadiusDifferences()
    {
        var rounded = new SectionArc(P(0, 0), P(1, 0), P(0, 1 + Epsilon), ArcDirection.Counterclockwise);
        Close(1, rounded.Radius.Meters);
        Assert.Equal(P(0, 1 + Epsilon), rounded.End);
        Assert.Throws<ArgumentException>(() =>
            new SectionArc(P(0, 0), P(1, 0), P(0, 1 + 1e-12), ArcDirection.Counterclockwise));
    }

    [Fact]
    public void DistinctEndpointsOnTheSameRayDoNotSilentlyProduceAFullCircle()
    {
        Assert.Throws<ArgumentException>(() =>
            new SectionArc(P(0, 0), P(1, 0), P(1 + Epsilon, 0), ArcDirection.Counterclockwise));
        Assert.Throws<ArgumentException>(() =>
            new SectionArc(P(0, 0), P(1, 0), P(1 + Epsilon, 0), ArcDirection.Clockwise));
    }

    [Fact]
    public void OverflowingCoordinateDifferencesAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SectionArc(P(-double.MaxValue, 0), P(double.MaxValue, 0),
                P(0, double.MaxValue), ArcDirection.Counterclockwise));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Polygon(P(-double.MaxValue, 0), P(double.MaxValue, 0), P(0, 1)));
    }

    [Fact]
    public void NullAndEmptyCollectionsAreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new SectionContour(null!));
        Assert.Throws<ArgumentException>(() => new SectionContour([]));
        Assert.Throws<ArgumentException>(() => new SectionContour([null!]));
        Assert.Throws<ArgumentNullException>(() => new SectionGeometry(null!));
        Assert.Throws<ArgumentException>(() => new SectionGeometry(Rectangle(1, 1), [null!]));
    }

    [Fact]
    public void OpenDisconnectedAndAlmostConnectedContoursAreRejectedWithoutSnapping()
    {
        Assert.Throws<ArgumentException>(() =>
            new SectionContour([new SectionLine(P(0, 0), P(1, 0))]));
        Assert.Throws<ArgumentException>(() => new SectionContour([
            new SectionLine(P(0, 0), P(1, 0)), new SectionLine(P(2, 0), P(0, 0))]));
        Assert.Throws<ArgumentException>(() => new SectionContour([
            new SectionLine(P(0, 0), P(1, 0)), new SectionLine(P(1, Epsilon), P(1, 1)),
            new SectionLine(P(1, 1), P(0, 0))]));
    }

    [Fact]
    public void PolygonSelfCrossingsTouchingsAndOverlapsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => Polygon(P(0, 0), P(2, 2), P(0, 2), P(2, 0)));
        Assert.Throws<ArgumentException>(() => Polygon(P(0, 0), P(2, 0), P(1, 0), P(1, 1)));
        Assert.Throws<ArgumentException>(() => Polygon(P(0, 0), P(1, 0), P(2, 0)));
        Assert.Throws<ArgumentException>(() => Polygon(P(0, 0), P(2, 0), P(2, 2),
            P(1, 1), P(0, 2), P(1, 1)));
    }

    [Fact]
    public void MixedLineArcSelfIntersectionIsRejected()
    {
        // Upper semicircle, followed by a line crossing the arc before going below it.
        Assert.Throws<ArgumentException>(() => new SectionContour([
            new SectionArc(P(0, 0), P(1, 0), P(-1, 0), ArcDirection.Counterclockwise),
            new SectionLine(P(-1, 0), P(0, 2)),
            new SectionLine(P(0, 2), P(0, -1)),
            new SectionLine(P(0, -1), P(1, 0))]));
    }

    [Fact]
    public void OverlappingCocircularArcsAndRepeatedFullCirclesAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new SectionContour([
            new SectionArc(P(0, 0), P(1, 0), P(-1, 0), ArcDirection.Counterclockwise),
            new SectionArc(P(0, 0), P(-1, 0), P(1, 0), ArcDirection.Clockwise)]));
        Assert.Throws<ArgumentException>(() => new SectionContour([
            new SectionArc(P(0, 0), P(1, 0), P(1, 0), ArcDirection.Counterclockwise),
            new SectionArc(P(0, 0), P(1, 0), P(1, 0), ArcDirection.Counterclockwise)]));
    }

    [Fact]
    public void ArcArcCrossingInsideClosedContourIsRejected()
    {
        Assert.Throws<ArgumentException>(() => new SectionContour([
            new SectionArc(P(0, 0), P(1, 0), P(-1, 0), ArcDirection.Counterclockwise),
            new SectionLine(P(-1, 0), P(0, 0)),
            new SectionArc(P(1, 0), P(0, 0), P(2, 0), ArcDirection.Clockwise),
            new SectionLine(P(2, 0), P(1, 0))]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.093)]
    [InlineData(0.558)]
    [InlineData(1.426)]
    [InlineData(-2.201)]
    public void TangentArcsWithSharedEndpointRemainValidAfterRotationAndReversal(double angle)
    {
        var contour = new SectionContour([
            new SectionArc(P(0, 0), P(-1, 0), P(1, 0), ArcDirection.Clockwise),
            new SectionArc(P(2, 0), P(1, 0), P(3, 0), ArcDirection.Clockwise),
            new SectionLine(P(3, 0), P(3, -2)),
            new SectionLine(P(3, -2), P(-1, -2)),
            new SectionLine(P(-1, -2), P(-1, 0))]);
        var expected = new SectionGeometry(contour).CalculateProperties();
        var rotated = Transform(contour, p => Rotate(p, angle));
        var actual = new SectionGeometry(rotated).CalculateProperties();
        Close(8 + Math.PI, actual.Area.SquareMeters);
        SameIntrinsic(expected, actual);
        // Reverse before construction as well, so either arc can be the first operand.
        var reversed = Transform(contour.Reversed(), p => Rotate(p, angle));
        SameProperties(actual, new SectionGeometry(reversed).CalculateProperties());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.093)]
    [InlineData(-1.426)]
    public void DifferentCirclesWithTwoSharedEndpointsFormAValidLens(double angle)
    {
        var contour = new SectionContour([
            new SectionArc(P(0, 0), P(3, 4), P(3, -4), ArcDirection.Clockwise),
            new SectionArc(P(6, 0), P(3, -4), P(3, 4), ArcDirection.Clockwise)]);
        var rotated = Transform(contour, p => Rotate(p, angle));
        var result = new SectionGeometry(rotated).CalculateProperties();
        Close(50 * Math.Atan2(4, 3) - 24, result.Area.SquareMeters);
        SameIntrinsic(new SectionGeometry(contour).CalculateProperties(), result);
        SameProperties(result, new SectionGeometry(rotated.Reversed()).CalculateProperties());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.093)]
    [InlineData(-1.426)]
    public void SharedArcEndpointDoesNotHideASecondIntersection(double angle)
    {
        SectionPoint Map(double y, double z) => Rotate(P(y, z), angle);
        // Circles intersect at (3, +/-4); only (3, 4) is a declared endpoint.
        // The closing line at z = -5 touches each circle only at its endpoint.
        SectionSegment[] segments = [
            new SectionArc(Map(0, 0), Map(0, -5), Map(3, 4), ArcDirection.Counterclockwise),
            new SectionArc(Map(6, 0), Map(3, 4), Map(6, -5), ArcDirection.Counterclockwise),
            new SectionLine(Map(6, -5), Map(0, -5))];
        Assert.Throws<ArgumentException>(() => new SectionContour(segments));
        Assert.Throws<ArgumentException>(() =>
            new SectionContour(segments.Reverse().Select(segment => segment.Reversed())));

        // The clockwise second arc excludes (3, -4), so this contour is simple.
        segments[1] = new SectionArc(Map(6, 0), Map(3, 4), Map(6, -5), ArcDirection.Clockwise);
        var valid = new SectionContour(segments);
        var result = new SectionGeometry(valid).CalculateProperties();
        SameProperties(result, new SectionGeometry(valid.Reversed()).CalculateProperties());
    }

    [Fact]
    public void SharedFullCircleEndpointDoesNotPermitTouchingHoles()
    {
        var first = new SectionContour([
            new SectionArc(P(0, 0), P(1, 0), P(1, 0), ArcDirection.Counterclockwise)]);
        var second = new SectionContour([
            new SectionArc(P(2, 0), P(1, 0), P(1, 0), ArcDirection.Counterclockwise)]);
        Assert.Throws<ArgumentException>(() =>
            new SectionGeometry(Rectangle(8, 8, -3, -4), [first, second]));
    }

    [Fact]
    public void HoleOutsideCrossingTouchingOrIdenticalToOuterContourIsRejected()
    {
        var outer = Rectangle(4, 4);
        Assert.Throws<ArgumentException>(() => new SectionGeometry(outer, [Rectangle(1, 1, 5, 1)]));
        Assert.Throws<ArgumentException>(() => new SectionGeometry(outer, [Rectangle(2, 1, 3, 1)]));
        Assert.Throws<ArgumentException>(() => new SectionGeometry(outer, [Rectangle(1, 1, 0, 1)]));
        Assert.Throws<ArgumentException>(() => new SectionGeometry(outer, [outer]));
        Assert.Throws<ArgumentException>(() => new SectionGeometry(outer, [Rectangle(8, 8, -2, -2)]));
    }

    [Fact]
    public void HoleInTheEmptyPartOfAConcaveOuterBoundaryIsRejected() =>
        Assert.Throws<ArgumentException>(() => new SectionGeometry(L(), [Rectangle(0.5, 0.5, 2, 2)]));

    [Fact]
    public void OverlappingTouchingNestedAndDuplicateHolesAreRejected()
    {
        var outer = Rectangle(10, 10);
        var a = Rectangle(3, 3, 1, 1);
        Assert.Throws<ArgumentException>(() => new SectionGeometry(outer, [a, Rectangle(3, 3, 2, 2)]));
        Assert.Throws<ArgumentException>(() => new SectionGeometry(outer, [a, Rectangle(1, 1, 4, 2)]));
        Assert.Throws<ArgumentException>(() => new SectionGeometry(outer, [a, Rectangle(1, 1, 2, 2)]));
        Assert.Throws<ArgumentException>(() => new SectionGeometry(outer, [a, a.Reversed()]));
    }

    [Fact]
    public void CircularHolesCannotTouchIntersectOrContainEachOther()
    {
        var outer = Circle(10);
        Assert.Throws<ArgumentException>(() => new SectionGeometry(outer, [Circle(2, 0, 0), Circle(2, 3, 0)]));
        Assert.Throws<ArgumentException>(() => new SectionGeometry(outer, [Circle(2, 0, 0), Circle(2, 4, 0)]));
        Assert.Throws<ArgumentException>(() => new SectionGeometry(outer, [Circle(2), Circle(1)]));
        Assert.Throws<ArgumentException>(() => new SectionGeometry(outer, [Circle(1, 9, 0)]));
        Assert.Throws<ArgumentException>(() => new SectionGeometry(outer, [Circle(1, 9.5, 0)]));
        Assert.Throws<ArgumentException>(() => new SectionGeometry(outer, [Circle(1, 12, 0)]));
    }

    [Fact]
    public void LineArcTangentAndCrossingHoleBoundariesAreRejected()
    {
        var outer = Rectangle(10, 10, -5, -5);
        Assert.Throws<ArgumentException>(() => new SectionGeometry(outer, [Circle(1, 4, 0)]));
        Assert.Throws<ArgumentException>(() => new SectionGeometry(outer, [Circle(1, 4.5, 0)]));
        Assert.Throws<ArgumentException>(() => new SectionGeometry(outer,
            [Rectangle(2, 2, -1, -1), Circle(0.5, 1.5, 0)]));
        Assert.Throws<ArgumentException>(() => new SectionGeometry(Circle(5), [Rectangle(2, 1, 4, 0)]));
    }

    [Fact]
    public void DisjointHolesAtArcRayVerticesRemainValid()
    {
        var result = new SectionGeometry(Circle(10, fullTurn: true),
            [Circle(1, -3, 0, true), Circle(1, 3, 0, true), Rectangle(1, 1, 0, -1)])
            .CalculateProperties();
        Close(100 * Math.PI - 2 * Math.PI - 1, result.Area.SquareMeters);
    }

    [Fact]
    public void TwoComplementaryArcsAreAValidCircleAndCollinearSuccessiveLinesAreValid()
    {
        var arcs = new SectionContour([
            new SectionArc(P(0, 0), P(1, 0), P(-1, 0), ArcDirection.Counterclockwise),
            new SectionArc(P(0, 0), P(-1, 0), P(1, 0), ArcDirection.Counterclockwise)]);
        Close(Math.PI, new SectionGeometry(arcs).CalculateProperties().Area.SquareMeters);
        var splitRectangle = Polygon(P(0, 0), P(1, 0), P(2, 0), P(2, 1), P(0, 1));
        Close(2, new SectionGeometry(splitRectangle).CalculateProperties().Area.SquareMeters);
    }

    [Fact]
    public void ImmutableModelsCopyCollectionsAndExposeReadOnlyViews()
    {
        var source = Rectangle(2, 2).Segments.ToArray();
        var contour = new SectionContour(source);
        var original = contour.Segments[0];
        source[0] = new SectionLine(P(10, 10), P(20, 20));
        Assert.Same(original, contour.Segments[0]);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<SectionSegment>)contour.Segments)[0] = source[0]);
        var hole = Rectangle(0.5, 0.5, 0.5, 0.5);
        var holes = new List<SectionContour> { hole };
        var geometry = new SectionGeometry(contour, holes);
        holes.Clear();
        Assert.Single(geometry.Holes);
        Assert.Same(hole, geometry.Holes[0]);
        Assert.Throws<NotSupportedException>(() => ((IList<SectionContour>)geometry.Holes).Clear());
        var first = geometry.CalculateProperties();
        SameProperties(first, geometry.CalculateProperties());
    }
}
