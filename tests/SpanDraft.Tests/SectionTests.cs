using SpanDraft.Core.Sections;
using SpanDraft.Core.Units;
using Xunit;

namespace SpanDraft.Tests;

public class SectionTests
{
    [Fact]
    public void RectanglePropertiesUseHeightAsTheBendingDimension()
    {
        var section = new RectangleSection(Mm(40), Mm(80));

        AssertProperties(section, 3200, 1706666.6666666667, 42666.6666666667);
        var rotatedSection = new RectangleSection(Mm(80), Mm(40));
        AssertProperties(rotatedSection, 3200, 426666.6666666667, 21333.3333333333);
    }

    [Fact]
    public void RectangularHollowPropertiesSubtractTheInnerRectangle()
    {
        var section = new RectangularHollowSection(Mm(60), Mm(100), Mm(5));

        AssertProperties(section, 1500, 1962500, 39250);
        Assert.Equal(Mm(5), section.WallThickness);
    }

    [Fact]
    public void CirclePropertiesUseACentroidalDiameter()
    {
        var section = new CircleSection(Mm(20));

        AssertProperties(section, 100 * Math.PI, 2500 * Math.PI, 250 * Math.PI);
    }

    [Fact]
    public void CircularHollowPropertiesSubtractTheInnerCircle()
    {
        var section = new CircularHollowSection(Mm(40), Mm(5));

        AssertProperties(section, 175 * Math.PI, 27343.75 * Math.PI, 1367.1875 * Math.PI);
        Assert.Equal(Mm(40), section.OuterDiameter);
        Assert.Equal(Mm(5), section.WallThickness);
    }

    [Theory]
    [InlineData(0, 80)]
    [InlineData(40, 0)]
    public void RectangleRejectsZeroDimensions(double width, double height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RectangleSection(Mm(width), Mm(height)));
    }

    [Theory]
    [InlineData(60, 100, 0)]
    [InlineData(60, 100, 30)]
    [InlineData(60, 100, 31)]
    [InlineData(100, 60, 30)]
    [InlineData(100, 60, 31)]
    [InlineData(0, 100, 5)]
    [InlineData(60, 0, 5)]
    public void RectangularHollowRejectsInvalidGeometry(double width, double height, double wall)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RectangularHollowSection(Mm(width), Mm(height), Mm(wall)));
    }

    [Fact]
    public void CircleRejectsZeroDiameter()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CircleSection(default));
    }

    [Theory]
    [InlineData(40, 0)]
    [InlineData(40, 20)]
    [InlineData(40, 21)]
    [InlineData(0, 5)]
    public void CircularHollowRejectsInvalidGeometry(double diameter, double wall)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CircularHollowSection(Mm(diameter), Mm(wall)));
    }

    [Fact]
    public void ThinWallsRetainPositiveSectionProperties()
    {
        // At this scale, direct outer-minus-inner powers would round to zero.
        var rectangular = new RectangularHollowSection(Length.FromMeters(1), Length.FromMeters(1), Length.FromMeters(1e-18));
        var circular = new CircularHollowSection(Length.FromMeters(1), Length.FromMeters(1e-18));

        NumericAssert.Close(4e-18, rectangular.Area.SquareMeters);
        NumericAssert.Close(2e-18 / 3, rectangular.SecondMomentOfArea.MetersToTheFourth);
        NumericAssert.Close(Math.PI * 1e-18, circular.Area.SquareMeters);
        NumericAssert.Close(Math.PI * 1e-18 / 8, circular.SecondMomentOfArea.MetersToTheFourth);
    }

    [Theory]
    [InlineData(1e100)]
    [InlineData(1e-100)]
    public void UnrepresentableCalculatedPropertiesAreRejected(double dimension)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RectangleSection(Length.FromMeters(dimension), Length.FromMeters(dimension)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CircleSection(Length.FromMeters(dimension)));
    }

    [Fact]
    public void CustomPropertiesAreRetainedWithoutRecalculation()
    {
        var area = Area.FromSquareMillimeters(432);
        var inertia = SecondMomentOfArea.FromMillimetersToTheFourth(56789);
        var modulus = SectionModulus.FromCubicMillimeters(987);
        var section = new CustomSection(area, inertia, modulus);

        Assert.Equal(area, section.Area);
        Assert.Equal(inertia, section.SecondMomentOfArea);
        Assert.Equal(modulus, section.SectionModulus);
    }

    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(1, 0, 1)]
    [InlineData(1, 1, 0)]
    [InlineData(-1, 1, 1)]
    [InlineData(1, -1, 1)]
    [InlineData(1, 1, -1)]
    public void CustomRejectsNonPositiveProperties(double area, double inertia, double modulus)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CustomSection(Area.FromSquareMeters(area),
            SecondMomentOfArea.FromMetersToTheFourth(inertia), SectionModulus.FromCubicMeters(modulus)));
    }

    private static Length Mm(double value) => Length.FromMillimeters(value);

    private static void AssertProperties(Section section, double area, double inertia, double modulus)
    {
        NumericAssert.Close(area, section.Area.SquareMillimeters);
        NumericAssert.Close(inertia, section.SecondMomentOfArea.MillimetersToTheFourth);
        NumericAssert.Close(modulus, section.SectionModulus.CubicMillimeters);
    }
}
