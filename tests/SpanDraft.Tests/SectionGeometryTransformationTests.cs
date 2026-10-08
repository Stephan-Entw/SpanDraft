using SpanDraft.Core.Sections.Geometry;
using Xunit;
using static SpanDraft.Tests.SectionGeometryTestSupport;

namespace SpanDraft.Tests;

public class SectionGeometryTransformationTests
{
    private static SectionGeometry Example(int kind) => kind switch
    {
        0 => new(L()),
        1 => new(Circle(3), [Circle(1, 0.5, 0.25)]),
        _ => new(Rectangle(8, 6, -4, -3), [Rectangle(2, 1, -2, -1), Circle(0.5, 2, 1)])
    };

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void TranslationPreservesTheTensorAxesDistancesAndModuli(int kind)
    {
        var geometry = Example(kind);
        var before = geometry.CalculateProperties();
        var after = Transform(geometry, p => P(p.Y.Meters - 16, p.Z.Meters + 32)).CalculateProperties();
        SameProperties(before, after);
        Close(before.Centroid.Y.Meters - 16, after.Centroid.Y.Meters);
        Close(before.Centroid.Z.Meters + 32, after.Centroid.Z.Meters);
    }

    [Theory]
    [InlineData(0, 0.4)]
    [InlineData(0, 2.3)]
    [InlineData(0, -2.1)]
    [InlineData(1, 0.7)]
    [InlineData(1, 2.6)]
    [InlineData(2, -1.9)]
    public void RotationTransformsTensorAndDirectedFiberSignsConsistently(int kind, double angle)
    {
        var geometry = Example(kind);
        var before = geometry.CalculateProperties();
        var after = Transform(geometry, p => Rotate(p, angle)).CalculateProperties();
        SameIntrinsic(before, after);
        var (s, c) = Math.SinCos(angle);
        var iy = before.Iy.MetersToTheFourth;
        var iz = before.Iz.MetersToTheFourth;
        var iyz = before.Iyz.MetersToTheFourth;
        Close(iy * c * c + iz * s * s + 2 * iyz * s * c, after.Iy.MetersToTheFourth);
        Close(iy * s * s + iz * c * c - 2 * iyz * s * c, after.Iz.MetersToTheFourth);
        Close((iz - iy) * s * c + iyz * (c * c - s * s), after.Iyz.MetersToTheFourth,
            before.I1.MetersToTheFourth);
        var centroid = Rotate(before.Centroid, angle);
        Close(centroid.Y.Meters, after.Centroid.Y.Meters, 5);
        Close(centroid.Z.Meters, after.Centroid.Z.Meters, 5);
        var axisChange = after.PrincipalAxisAngleRadians - before.PrincipalAxisAngleRadians - angle;
        Close(0, Math.Sin(axisChange), 1);
        var reversedAxes = Math.Cos(axisChange) < 0;
        Close((reversedAxes ? before.Axis1NegativeDistance : before.Axis1PositiveDistance).Meters,
            after.Axis1PositiveDistance.Meters);
        Close((reversedAxes ? before.Axis1PositiveDistance : before.Axis1NegativeDistance).Meters,
            after.Axis1NegativeDistance.Meters);
        Close((reversedAxes ? before.Axis2NegativeDistance : before.Axis2PositiveDistance).Meters,
            after.Axis2PositiveDistance.Meters);
        Close((reversedAxes ? before.Axis2PositiveDistance : before.Axis2NegativeDistance).Meters,
            after.Axis2NegativeDistance.Meters);
        Close((reversedAxes ? before.W1Negative : before.W1Positive).CubicMeters, after.W1Positive.CubicMeters);
        Close((reversedAxes ? before.W1Positive : before.W1Negative).CubicMeters, after.W1Negative.CubicMeters);
        Close((reversedAxes ? before.W2Negative : before.W2Positive).CubicMeters, after.W2Positive.CubicMeters);
        Close((reversedAxes ? before.W2Positive : before.W2Negative).CubicMeters, after.W2Negative.CubicMeters);
    }

    [Theory]
    [InlineData(0, 0.125)]
    [InlineData(0, 32)]
    [InlineData(0, 1e-50)]
    [InlineData(0, 1e50)]
    [InlineData(1, 0.125)]
    [InlineData(1, 16)]
    [InlineData(2, 0.25)]
    [InlineData(2, 8)]
    public void ScalingUsesAreaSquaredInertiaFourthAndModulusCubed(int kind, double factor)
    {
        var geometry = Example(kind);
        var before = geometry.CalculateProperties();
        var after = Transform(geometry, p => P(p.Y.Meters * factor, p.Z.Meters * factor)).CalculateProperties();
        var k2 = factor * factor;
        var k3 = k2 * factor;
        var k4 = k2 * k2;
        Close(before.Area.SquareMeters * k2, after.Area.SquareMeters);
        Close(before.Centroid.Y.Meters * factor, after.Centroid.Y.Meters, factor * 5);
        Close(before.Centroid.Z.Meters * factor, after.Centroid.Z.Meters, factor * 5);
        Close(before.Iy.MetersToTheFourth * k4, after.Iy.MetersToTheFourth);
        Close(before.Iz.MetersToTheFourth * k4, after.Iz.MetersToTheFourth);
        Close(before.Iyz.MetersToTheFourth * k4, after.Iyz.MetersToTheFourth, before.I1.MetersToTheFourth * k4);
        Close(before.I1.MetersToTheFourth * k4, after.I1.MetersToTheFourth);
        Close(before.I2.MetersToTheFourth * k4, after.I2.MetersToTheFourth);
        Close(before.PrincipalAxisAngleRadians, after.PrincipalAxisAngleRadians, 1);
        Close(before.Axis1PositiveDistance.Meters * factor, after.Axis1PositiveDistance.Meters);
        Close(before.Axis1NegativeDistance.Meters * factor, after.Axis1NegativeDistance.Meters);
        Close(before.Axis2PositiveDistance.Meters * factor, after.Axis2PositiveDistance.Meters);
        Close(before.Axis2NegativeDistance.Meters * factor, after.Axis2NegativeDistance.Meters);
        Close(before.W1Positive.CubicMeters * k3, after.W1Positive.CubicMeters);
        Close(before.W1Negative.CubicMeters * k3, after.W1Negative.CubicMeters);
        Close(before.W2Positive.CubicMeters * k3, after.W2Positive.CubicMeters);
        Close(before.W2Negative.CubicMeters * k3, after.W2Negative.CubicMeters);
    }

    [Theory]
    [InlineData(0, true, false)]
    [InlineData(1, false, true)]
    [InlineData(1, true, true)]
    [InlineData(2, true, true)]
    public void ReversedContoursPreserveAllProperties(int kind, bool outer, bool holes)
    {
        var geometry = Example(kind);
        var reversed = new SectionGeometry(outer ? geometry.OuterContour.Reversed() : geometry.OuterContour,
            geometry.Holes.Select(hole => holes ? hole.Reversed() : hole));
        var before = geometry.CalculateProperties();
        var after = reversed.CalculateProperties();
        SameProperties(before, after);
        Close(before.Centroid.Y.Meters, after.Centroid.Y.Meters, 5);
        Close(before.Centroid.Z.Meters, after.Centroid.Z.Meters, 5);
    }

    [Fact]
    public void ReflectionChangesTheProductSignAndPrincipalQuadrant()
    {
        var geometry = new SectionGeometry(L());
        var before = geometry.CalculateProperties();
        var after = Transform(geometry, p => P(-p.Y.Meters, p.Z.Meters), true).CalculateProperties();
        SameIntrinsic(before, after);
        Close(before.Iy.MetersToTheFourth, after.Iy.MetersToTheFourth);
        Close(before.Iz.MetersToTheFourth, after.Iz.MetersToTheFourth);
        Close(-before.Iyz.MetersToTheFourth, after.Iyz.MetersToTheFourth);
        Close(-before.PrincipalAxisAngleRadians, after.PrincipalAxisAngleRadians);
        Close(before.Axis1PositiveDistance.Meters, after.Axis1PositiveDistance.Meters);
        Close(before.Axis1NegativeDistance.Meters, after.Axis1NegativeDistance.Meters);
        Close(before.Axis2NegativeDistance.Meters, after.Axis2PositiveDistance.Meters);
        Close(before.Axis2PositiveDistance.Meters, after.Axis2NegativeDistance.Meters);
    }

    [Fact]
    public void ReorderingHoleAndContourStartDoesNotChangeResults()
    {
        var geometry = Example(2);
        var segments = geometry.OuterContour.Segments;
        var shifted = new SectionContour(segments.Skip(2).Concat(segments.Take(2)));
        SameProperties(geometry.CalculateProperties(),
            new SectionGeometry(shifted, geometry.Holes.Reverse()).CalculateProperties());
    }

    [Theory]
    [InlineData(0.33)]
    [InlineData(2.27)]
    [InlineData(-1.19)]
    public void CircleSubdivisionsAndRotationsKeepTheDeterministicAxes(double angle)
    {
        var circle = new SectionGeometry(Transform(Circle(2), p => Rotate(p, angle))).CalculateProperties();
        var full = new SectionGeometry(Transform(Circle(2, fullTurn: true), p => Rotate(p, angle))).CalculateProperties();
        SameProperties(circle, full);
        Assert.Equal(0, circle.PrincipalAxisAngleRadians);
        Assert.Equal(0, full.PrincipalAxisAngleRadians);
    }
}