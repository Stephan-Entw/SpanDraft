using SpanDraft.Core.Loads;
using SpanDraft.Core.Units;
using Xunit;

namespace SpanDraft.Tests;

public sealed class SelfWeightLoadTests
{
    [Theory]
    [InlineData(3, 7850, .02, -1539.64405)]
    [InlineData(6, 7850, .02, -1539.64405)]
    [InlineData(3, 15700, .02, -3079.2881)]
    [InlineData(3, 7850, .04, -3079.2881)]
    [InlineData(1, 800, .0036, -28.243152)]
    public void CreatesDownwardFullSpanLoadFromCurrentDensityAndArea(
        double length, double density, double area, double expectedIntensity)
    {
        var load = UniformDistributedLoad.FromSelfWeight(Length.FromMeters(length),
            MassDensity.FromKilogramsPerCubicMeter(density), Area.FromSquareMeters(area));

        Assert.Equal(default, load.StartPosition);
        Assert.Equal(Length.FromMeters(length), load.EndPosition);
        Assert.Equal(expectedIntensity, load.Intensity.NewtonsPerMeter, 8);
    }

    [Theory]
    [InlineData("length")]
    [InlineData("density")]
    [InlineData("area")]
    public void RejectsZeroDefaults(string parameter)
    {
        var length = parameter == "length" ? default : Length.FromMeters(3);
        var density = parameter == "density" ? default : MassDensity.FromKilogramsPerCubicMeter(7850);
        var area = parameter == "area" ? default : Area.FromSquareMeters(.02);

        var error = Assert.Throws<ArgumentOutOfRangeException>(() =>
            UniformDistributedLoad.FromSelfWeight(length, density, area));

        Assert.Equal(parameter, error.ParamName);
    }

    [Fact]
    public void RejectsOverflowInsteadOfPublishingAnInfiniteLoad()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => UniformDistributedLoad.FromSelfWeight(
            Length.FromMeters(1), MassDensity.FromKilogramsPerCubicMeter(double.MaxValue), Area.FromSquareMeters(1)));
    }
}
