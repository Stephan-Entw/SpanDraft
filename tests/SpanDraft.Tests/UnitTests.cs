using SpanDraft.Core.Units;
using Xunit;

namespace SpanDraft.Tests;

public class UnitTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1000)]
    [InlineData(0.0125, 12.5)]
    public void LengthConvertsBetweenMetersAndMillimeters(double meters, double millimeters)
    {
        NumericAssert.Close(meters, Length.FromMillimeters(millimeters).Meters);
        NumericAssert.Close(millimeters, Length.FromMeters(meters).Millimeters);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(210000000000, 210000)]
    [InlineData(-1250000, -1.25)]
    public void PressureConvertsBetweenPascalsAndMegapascals(double pascals, double megapascals)
    {
        NumericAssert.Close(pascals, Pressure.FromMegapascals(megapascals).Pascals);
        NumericAssert.Close(megapascals, Pressure.FromPascals(pascals).Megapascals);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1.5, 1500)]
    [InlineData(-2, -2000)]
    public void MomentConvertsBetweenNewtonMetersAndNewtonMillimeters(double newtonMeters, double newtonMillimeters)
    {
        NumericAssert.Close(newtonMeters, Moment.FromNewtonMillimeters(newtonMillimeters).NewtonMeters);
        NumericAssert.Close(newtonMillimeters, Moment.FromNewtonMeters(newtonMeters).NewtonMillimeters);
    }

    [Fact]
    public void SectionPropertyConversionsRespectPowersOfLength()
    {
        NumericAssert.Close(0.0002, Area.FromSquareMillimeters(200).SquareMeters);
        NumericAssert.Close(200, Area.FromSquareMeters(0.0002).SquareMillimeters);
        NumericAssert.Close(3e-8, SecondMomentOfArea.FromMillimetersToTheFourth(30000).MetersToTheFourth);
        NumericAssert.Close(30000, SecondMomentOfArea.FromMetersToTheFourth(3e-8).MillimetersToTheFourth);
        NumericAssert.Close(4e-6, SectionModulus.FromCubicMillimeters(4000).CubicMeters);
        NumericAssert.Close(4000, SectionModulus.FromCubicMeters(4e-6).CubicMillimeters);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void AllUnitsRejectNonFiniteValues(double value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Length.FromMeters(value));
        Assert.Throws<ArgumentOutOfRangeException>(() => Length.FromMillimeters(value));
        Assert.Throws<ArgumentOutOfRangeException>(() => Force.FromNewtons(value));
        Assert.Throws<ArgumentOutOfRangeException>(() => Moment.FromNewtonMeters(value));
        Assert.Throws<ArgumentOutOfRangeException>(() => Moment.FromNewtonMillimeters(value));
        Assert.Throws<ArgumentOutOfRangeException>(() => Pressure.FromPascals(value));
        Assert.Throws<ArgumentOutOfRangeException>(() => Pressure.FromMegapascals(value));
        Assert.Throws<ArgumentOutOfRangeException>(() => Area.FromSquareMeters(value));
        Assert.Throws<ArgumentOutOfRangeException>(() => Area.FromSquareMillimeters(value));
        Assert.Throws<ArgumentOutOfRangeException>(() => SecondMomentOfArea.FromMetersToTheFourth(value));
        Assert.Throws<ArgumentOutOfRangeException>(() => SecondMomentOfArea.FromMillimetersToTheFourth(value));
        Assert.Throws<ArgumentOutOfRangeException>(() => SectionModulus.FromCubicMeters(value));
        Assert.Throws<ArgumentOutOfRangeException>(() => SectionModulus.FromCubicMillimeters(value));
        Assert.Throws<ArgumentOutOfRangeException>(() => ForcePerLength.FromNewtonsPerMeter(value));
    }

    [Fact]
    public void GeometryUnitsRejectNegativeValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Length.FromMeters(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Length.FromMillimeters(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Area.FromSquareMeters(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Area.FromSquareMillimeters(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => SecondMomentOfArea.FromMetersToTheFourth(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => SecondMomentOfArea.FromMillimetersToTheFourth(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => SectionModulus.FromCubicMeters(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => SectionModulus.FromCubicMillimeters(-1));
    }

    [Fact]
    public void DefaultsRepresentZeroAndEqualExplicitZero()
    {
        Assert.Equal(default, Length.FromMeters(0));
        Assert.Equal(default, Force.FromNewtons(0));
        Assert.Equal(default, Moment.FromNewtonMeters(0));
        Assert.Equal(default, Pressure.FromPascals(0));
        Assert.Equal(default, Area.FromSquareMeters(0));
        Assert.Equal(default, SecondMomentOfArea.FromMetersToTheFourth(0));
        Assert.Equal(default, SectionModulus.FromCubicMeters(0));
        Assert.Equal(default, ForcePerLength.FromNewtonsPerMeter(0));
    }

    [Fact]
    public void ConversionOverflowIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Pressure.FromMegapascals(double.MaxValue));
    }

    [Fact]
    public void EquivalentCanonicalQuantitiesHaveValueEquality()
    {
        Assert.Equal(Length.FromMeters(1), Length.FromMillimeters(1000));
        Assert.Equal(Moment.FromNewtonMeters(-1), Moment.FromNewtonMillimeters(-1000));
    }
}
