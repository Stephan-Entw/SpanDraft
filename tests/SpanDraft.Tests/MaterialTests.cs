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
        Assert.Null(material.Density);
        Assert.Null(material.PoissonRatio);
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
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void OptionalPropertiesAreIndependentAndDoNotChangeAnalysis(bool density, bool poisson)
    {
        var material = new Material("Test steel", Pressure.FromPascals(210e9), Pressure.FromMegapascals(235),
            density ? MassDensity.FromKilogramsPerCubicMeter(7850) : null,
            poisson ? PoissonRatio.FromValue(.3) : null);
        Assert.Equal(density ? 7850d : (double?)null, material.Density?.KilogramsPerCubicMeter);
        Assert.Equal(poisson ? .3 : (double?)null, material.PoissonRatio?.Value);
        var loads = new[] { SolverTestSupport.Point(1, -1000) };
        var baseline = SolverTestSupport.Solve(SolverTestSupport.Beam(loads: loads));
        var actual = SolverTestSupport.Solve(SolverTestSupport.Beam(loads: loads, material: material));
        SolverTestSupport.SameResults(baseline, actual);
        var expectedEngineering = SpanDraft.Engineering.BeamEngineeringAnalysis.Analyze(baseline);
        var actualEngineering = SpanDraft.Engineering.BeamEngineeringAnalysis.Analyze(actual);
        Assert.Equal(expectedEngineering.MaximumBendingStress, actualEngineering.MaximumBendingStress);
        Assert.Equal(expectedEngineering.SafetyFactor, actualEngineering.SafetyFactor);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void InvalidDensityIsRejected(double value) => Assert.Throws<ArgumentOutOfRangeException>(() =>
        MassDensity.FromKilogramsPerCubicMeter(value));

    [Fact]
    public void MaterialRejectsDefaultDensity() => Assert.Throws<ArgumentOutOfRangeException>(() =>
        new Material("Test", Pressure.FromPascals(1), Pressure.FromPascals(1), default(MassDensity), null));

    [Theory]
    [InlineData(-1)]
    [InlineData(.5)]
    [InlineData(-2)]
    [InlineData(1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void InvalidPoissonRatioIsRejected(double value) => Assert.Throws<ArgumentOutOfRangeException>(() =>
        PoissonRatio.FromValue(value));

    [Theory]
    [InlineData(-.999999999)]
    [InlineData(-.5)]
    [InlineData(0)]
    [InlineData(.3)]
    [InlineData(.499999999)]
    public void ValidPoissonRatioIsPreserved(double value) => Assert.Equal(value, PoissonRatio.FromValue(value).Value);

}
