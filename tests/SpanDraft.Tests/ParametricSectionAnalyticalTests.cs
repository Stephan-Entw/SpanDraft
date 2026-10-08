using SpanDraft.Core.Sections;
using SpanDraft.Core.Sections.Parametric;
using Xunit;
using static SpanDraft.Tests.ParametricSectionTestSupport;
using static SpanDraft.Tests.SectionGeometryTestSupport;

namespace SpanDraft.Tests;

public class ParametricSectionAnalyticalTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void SharpShapesMatchIndependentAreaIntegralsAndAllFourModuli(int kind)
    {
        var actual = Geometry(Shape(kind)).CalculateProperties();
        var expected = Reference(kind);
        AssertTensor(expected, actual);
        AssertModuli(kind, expected, actual);
    }

    [Theory]
    [InlineData(4, 10, 6, 2, 1)]
    [InlineData(5, 10, 6, 2, 1)]
    [InlineData(6, 10, 6, 2, 1)]
    [InlineData(4, 4, 9, 0.5, 0.75)]
    [InlineData(5, 4, 9, 0.5, 0.75)]
    [InlineData(6, 4, 9, 0.5, 0.75)]
    [InlineData(7, 10, 6, 1, 1)]
    [InlineData(7, 5, 5, 0.5, 1)]
    public void ProfileAspectRatiosAndUnequalAnglesMatchIndependentReferences(int kind, double b, double h, double t, double tf)
    {
        var actual = Geometry(Shape(kind, b, h, t, tf)).CalculateProperties();
        var expected = Reference(kind, b, h, t, tf);
        AssertTensor(expected, actual);
        AssertModuli(kind, expected, actual, b, h, t, tf);
    }

    [Theory]
    [InlineData(1, 0.5)]
    [InlineData(1, 1)]
    [InlineData(1, 1.5)]
    [InlineData(1, 3)]
    [InlineData(4, 0.5)]
    [InlineData(4, 2.5)]
    [InlineData(5, 0.5)]
    [InlineData(5, 4)]
    [InlineData(6, 0.5)]
    [InlineData(6, 2.5)]
    [InlineData(7, 0.5)]
    [InlineData(7, 5)]
    public void RoundedShapesMatchIndependentRectangleAndQuarterDiskIntegrals(int kind, double r)
    {
        var actual = Geometry(Shape(kind, r: r)).CalculateProperties();
        var expected = Reference(kind, r: r);
        AssertTensor(expected, actual);
        if (kind != 7)
            AssertModuli(kind, expected, actual);
        var sharpArea = Reference(kind).A;
        var cornerArea = (1 - Math.PI / 4) * r * r;
        var change = kind switch
        {
            1 => -4 * (1 - Math.PI / 4) * (r * r - Math.Pow(Math.Max(0, r - 1), 2)),
            4 => 4 * cornerArea,
            5 or 6 => 2 * cornerArea,
            _ => cornerArea
        };
        Close(sharpArea + change, actual.Area.SquareMeters);
    }

    [Theory]
    [InlineData(4, 10, 6, 2, 1, 2)]
    [InlineData(5, 3, 6, 1, 1, 2)]
    [InlineData(6, 10, 3, 2, 1, 2)]
    [InlineData(7, 6, 3, 1, 1, 2)]
    [InlineData(1, 6, 6, 1, 1, 3)]
    public void OtherExactRadiusLimitsRetainValidAnalyticalProperties(int kind, double b, double h, double t, double tf, double r)
    {
        var actual = Geometry(Shape(kind, b, h, t, tf, r)).CalculateProperties();
        AssertTensor(Reference(kind, b, h, t, tf, r), actual);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ExistingAnalyticalSectionsRemainIndependentReferences(int kind)
    {
        Section section = kind switch
        {
            0 => new RectangleSection(M(6), M(10)),
            1 => new RectangularHollowSection(M(6), M(10), M(1)),
            2 => new CircleSection(M(6)),
            _ => new CircularHollowSection(M(6), M(1))
        };
        var actual = Geometry(Shape(kind)).CalculateProperties();
        Close(section.Area.SquareMeters, actual.Area.SquareMeters);
        Close(section.SecondMomentOfArea.MetersToTheFourth, actual.Iy.MetersToTheFourth);
        Close(section.SectionModulus.CubicMeters, actual.W1Positive.CubicMeters);
        Close(section.SectionModulus.CubicMeters, actual.W1Negative.CubicMeters);
        if (kind is 0 or 1)
        {
            Section swapped = kind == 0 ? new RectangleSection(M(10), M(6)) : new RectangularHollowSection(M(10), M(6), M(1));
            Close(swapped.SecondMomentOfArea.MetersToTheFourth, actual.Iz.MetersToTheFourth);
            Close(swapped.SectionModulus.CubicMeters, actual.W2Positive.CubicMeters);
            Close(swapped.SectionModulus.CubicMeters, actual.W2Negative.CubicMeters);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.5)]
    public void ProfileSymmetriesAndDirectedAngleAxesAreVisible(double r)
    {
        var i = Geometry(Shape(4, r: r)).CalculateProperties();
        var u = Geometry(Shape(5, r: r)).CalculateProperties();
        var t = Geometry(Shape(6, r: r)).CalculateProperties();
        var angle = Geometry(Shape(7, r: r)).CalculateProperties();
        Close(3, i.Centroid.Y.Meters);
        Close(5, i.Centroid.Z.Meters);
        Close(5, u.Centroid.Z.Meters);
        Assert.True(u.Centroid.Y.Meters < 3);
        Close(3, t.Centroid.Y.Meters);
        Assert.True(t.Centroid.Z.Meters > 5);
        foreach (var result in new[] { i, u, t })
            Close(0, result.Iyz.MetersToTheFourth, result.I1.MetersToTheFourth);
        Assert.True(angle.Iyz.MetersToTheFourth < 0);
        Assert.InRange(angle.PrincipalAxisAngleRadians, 0.01, Math.PI / 2 - 0.01);
        Assert.NotEqual(angle.W1Positive, angle.W1Negative);
        Assert.NotEqual(angle.W2Positive, angle.W2Negative);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void EngineeringSizedRoundedInputsMatchIndependentReferences(int kind)
    {
        var r = kind is 1 or >= 4 ? 0.012 : 0;
        var actual = Geometry(Shape(kind, 0.06, 0.1, 0.01, 0.01, r)).CalculateProperties();
        AssertTensor(Reference(kind, 0.06, 0.1, 0.01, 0.01, r), actual);
    }

    [Fact]
    public void RectangularTubeRetainsOnlyOneIndependentRadius()
    {
        foreach (var r in new[] { 0.0, 0.5, 1.0, 1.5, 3.0 })
        {
            var shape = new RectangularHollowSectionGeometry(M(6), M(10), M(1), M(r));
            Assert.Equal(M(r), shape.OuterRadius);
            Assert.Equal(M(Math.Max(0, r - 1)), shape.InnerRadius);
        }
    }
}
