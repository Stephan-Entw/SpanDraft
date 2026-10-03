using SpanDraft.Core.Materials;
using SpanDraft.Core.Units;
using Xunit;

namespace SpanDraft.Tests;

public class MaterialTests
{
    [Fact]
    public void ValidMaterialRetainsItsProperties()
    {
        var youngsModulus = Pressure.FromMegapascals(210000);
        var yieldStrength = Pressure.FromMegapascals(235);
        var material = new Material("Test material", youngsModulus, yieldStrength);

        Assert.Equal("Test material", material.Name);
        Assert.Equal(youngsModulus, material.YoungsModulus);
        Assert.Equal(yieldStrength, material.YieldStrength);
    }

    [Theory]
    [InlineData(0, 235)]
    [InlineData(-1, 235)]
    [InlineData(210000, 0)]
    [InlineData(210000, -1)]
    public void NonPositivePropertiesAreRejected(double youngsModulus, double yieldStrength)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Material("Test",
            Pressure.FromMegapascals(youngsModulus), Pressure.FromMegapascals(yieldStrength)));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\n")]
    public void BlankNamesAreRejected(string name)
    {
        Assert.Throws<ArgumentException>(() => new Material(name, Pressure.FromPascals(1), Pressure.FromPascals(1)));
    }

    [Fact]
    public void NullNameIsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new Material(null!, Pressure.FromPascals(1), Pressure.FromPascals(1)));
    }
}
