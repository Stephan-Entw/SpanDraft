using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Layout;
using SpanDraft.Desktop.State;
using Xunit;

namespace SpanDraft.Tests;

public sealed class DesktopStationLayoutTests
{
    [Fact]
    public void SufficientSpaceRemainsLinearAndKeepsExactEndAnchors()
    {
        var result = StationLayout.Compute(1.4, 72, 772, [new(.2, 10, 10), new(.8, 10, 10)]);
        Assert.Equal(new[] { 0, .2, .8, 1.4 }, result.Stations.Select(s => s.PhysicalX));
        Assert.Equal(72, result.Stations[0].ScreenX);
        Assert.Equal(772, result.Stations[^1].ScreenX);
        NumericAssert.Close(172, result.Stations[1].ScreenX);
        NumericAssert.Close(472, result.Stations[2].ScreenX);
        Assert.False(result.IsDistorted);
        Assert.False(result.IsOverconstrained);
    }

    [Fact]
    public void OneActiveMinimumRedistributesTheRemainingPhysicalGapsProportionally()
    {
        var result = StationLayout.Compute(1, 0, 100, [new(.01, 10, 0), new(.5, 0, 0)]);
        Assert.True(result.IsDistorted);
        Assert.False(result.IsOverconstrained);
        NumericAssert.Close(18, result.Stations[1].ScreenX);
        NumericAssert.Close(82 * .49 / .99, result.Stations[2].ScreenX - result.Stations[1].ScreenX);
        NumericAssert.Close(82 * .5 / .99, result.Stations[3].ScreenX - result.Stations[2].ScreenX);
    }

    [Fact]
    public void MultipleActiveMinimaKeepTheirRequiredWidths()
    {
        var result = StationLayout.Compute(1, 0, 100, [new(.01, 10, 10), new(.02, 10, 10)]);
        Assert.Equal(new[] { 0d, 18, 46, 100 }, result.Stations.Select(s => s.ScreenX));
        Assert.True(result.IsDistorted);
        Assert.False(result.IsOverconstrained);
    }

    [Fact]
    public void AsymmetricAndEndpointExtentsUseTheCorrectSides()
    {
        var result = StationLayout.Compute(1, 0, 67, [new(0, 100, 7), new(.5, 9, 15), new(1, 20, 200)]);
        Assert.Equal(new[] { 0d, 24, 67 }, result.Stations.Select(s => s.ScreenX));
        Assert.Equal(100, result.Stations[0].LeftExtent);
        Assert.Equal(200, result.Stations[^1].RightExtent);
        var margins = StationOuterMargins.Calculate(new(0, 100, 7), new(1, 20, 200));
        Assert.Equal(new StationOuterMargins(108, 208), margins);
        Assert.Equal(new StationOuterMargins(72, 72), StationOuterMargins.Calculate(new(0, 12, 0), new(1, 0, 18)));
    }

    [Fact]
    public void EqualCoordinatesUnionExtentsButAdjacentDoublesRemainDifferentStations()
    {
        double near = Math.BitIncrement(.5);
        StationRequirement[] source = [new(.5, 2, 9), new(.5, 11, 3), new(near, 0, 0)];
        var result = StationLayout.Compute(1, 0, 500, source);
        Assert.Equal(4, result.Stations.Count);
        Assert.Equal(11, result.Stations[1].LeftExtent);
        Assert.Equal(9, result.Stations[1].RightExtent);
        Assert.Equal(near, result.Stations[2].PhysicalX);
        Assert.True(result.Stations[2].ScreenX > result.Stations[1].ScreenX);
        Assert.Equal(result.Stations, StationLayout.Compute(1, 0, 500, source.Reverse()).Stations);
    }

    [Fact]
    public void OverconstrainedFallbackScalesMinimaDeterministically()
    {
        StationRequirement[] requirements = [new(0, 0, 4), new(.5, 12, 4), new(1, 8, 0)];
        var result = StationLayout.Compute(1, 0, 30, requirements);
        Assert.True(result.IsOverconstrained);
        Assert.True(result.IsDistorted);
        NumericAssert.Close(30d * 24 / 44, result.Stations[1].ScreenX);
        Assert.Equal(30, result.Stations[^1].ScreenX);
        Assert.Equal(result.Stations, StationLayout.Compute(1, 0, 30, requirements.Reverse()).Stations);
    }

    [Fact]
    public void OverconstrainedDoesNotNecessarilyMeanDistorted()
    {
        var result = StationLayout.Compute(1, 0, 10, [new(.5, 12, 12)]);
        Assert.True(result.IsOverconstrained);
        Assert.False(result.IsDistorted);
        Assert.Equal(5, result.Stations[1].ScreenX);
    }

    [Theory]
    [InlineData(1000)]
    [InlineData(100)]
    [InlineData(10)]
    public void TransformUsesStationAnchorsAndRoundTripsEachSegmentAndExterior(double width)
    {
        var result = StationLayout.Compute(1, 20, 20 + width, [new(.01, 10, 10), new(.02, 10, 10), new(.6, 5, 7)]);
        var transform = result.Transform;
        foreach (var station in result.Stations)
        {
            Assert.Equal(station.ScreenX, transform.PhysicalToScreen(station.PhysicalX));
            Assert.Equal(station.PhysicalX, transform.ScreenToPhysical(station.ScreenX));
        }
        foreach (double x in new[] { -.01, .005, .015, .1, .5, .8, 1.01 })
            NumericAssert.Close(x, transform.ScreenToPhysical(transform.PhysicalToScreen(x)));
        for (int i = 0; i < result.Stations.Count - 1; i++)
        {
            var a = result.Stations[i];
            var b = result.Stations[i + 1];
            NumericAssert.Close((a.ScreenX + b.ScreenX) / 2, transform.PhysicalToScreen((a.PhysicalX + b.PhysicalX) / 2));
            NumericAssert.Close((a.PhysicalX + b.PhysicalX) / 2, transform.ScreenToPhysical((a.ScreenX + b.ScreenX) / 2));
        }
        Assert.True(transform.PhysicalToScreen(-.01) < result.Stations[0].ScreenX);
        Assert.True(transform.ScreenToPhysical(20 + width + 1) > 1);
    }

    [Theory]
    [InlineData(1e-300)]
    [InlineData(1e300)]
    public void ExtremePhysicalScalesStayFiniteAndReversible(double length)
    {
        var result = StationLayout.Compute(length, 72, 972, [new(length / 4, 10, 10)]);
        Assert.False(result.IsDistorted);
        Assert.All(result.Stations, s => Assert.True(double.IsFinite(s.ScreenX)));
        NumericAssert.Close(length / 2, result.Transform.ScreenToPhysical(result.Transform.PhysicalToScreen(length / 2)));
    }

    [Fact]
    public void FiniteExtentsWithAnOverflowingUnscaledSumHaveAFiniteFallback()
    {
        var result = StationLayout.Compute(1, 0, 100, [new(.25, double.MaxValue, double.MaxValue), new(.5, double.MaxValue, double.MaxValue)]);
        Assert.True(result.IsOverconstrained);
        Assert.All(result.Stations, s => Assert.True(double.IsFinite(s.ScreenX)));
        Assert.Equal(new[] { 0d, 25, 75, 100 }, result.Stations.Select(s => s.ScreenX));
    }

    [Fact]
    public void DenseLayoutsPreserveOrderAndAreIndependentOfInputIteration()
    {
        var random = new Random(42);
        var source = Enumerable.Range(1, 100).Select(i => new StationRequirement(i / 101d,
            random.NextDouble() * 30, random.NextDouble() * 30)).ToArray();
        foreach (double width in new[] { 20d, 1000, 10000 })
        {
            var result = StationLayout.Compute(1, 72, 72 + width, source);
            Assert.Equal(result.Stations, StationLayout.Compute(1, 72, 72 + width, source.Reverse()).Stations);
            for (int i = 1; i < result.Stations.Count; i++)
            {
                Assert.True(result.Stations[i].PhysicalX > result.Stations[i - 1].PhysicalX);
                Assert.True(result.Stations[i].ScreenX > result.Stations[i - 1].ScreenX);
                Assert.True(double.IsFinite(result.Stations[i].ScreenX));
            }
        }
    }

    [Fact]
    public void FeasibleActiveSetsMatchAnIndependentBisectionOfTheSpecifiedEquation()
    {
        var random = new Random(83);
        for (int trial = 0; trial < 100; trial++)
        {
            var source = Enumerable.Range(1, 9).Select(i => new StationRequirement(
                i < 5 ? i * .001 + random.NextDouble() * .0001 : i / 10d,
                random.NextDouble() * 15, random.NextDouble() * 15)).ToArray();
            var physical = new[] { new StationRequirement(0, 0, 0) }.Concat(source)
                .Append(new(1, 0, 0)).ToArray();
            var minima = Enumerable.Range(0, physical.Length - 1)
                .Select(i => physical[i].RightExtent + 8 + physical[i + 1].LeftExtent).ToArray();
            var distances = Enumerable.Range(0, minima.Length)
                .Select(i => physical[i + 1].PhysicalX - physical[i].PhysicalX).ToArray();
            double width = minima.Sum() * 1.4;
            var result = StationLayout.Compute(1, 72, 72 + width, source);
            Assert.False(result.IsOverconstrained);
            Assert.True(result.IsDistorted);
            double low = 0, high = width;
            for (int iteration = 0; iteration < 100; iteration++)
            {
                double middle = (low + high) / 2;
                double used = Enumerable.Range(0, minima.Length).Sum(i => Math.Max(minima[i], middle * distances[i]));
                if (used < width) low = middle; else high = middle;
            }
            for (int i = 0; i < minima.Length; i++)
                NumericAssert.Close(Math.Max(minima[i], high * distances[i]), result.Stations[i + 1].ScreenX - result.Stations[i].ScreenX);
        }
    }

    [Fact]
    public void LayoutAndTransformAreDefensivelyImmutableAndResizeCreatesANewResult()
    {
        StationRequirement[] source = [new(.01, 10, 10)];
        var result = StationLayout.Compute(1, 0, 100, source);
        var original = result.Stations.ToArray();
        source[0] = new(.7, 0, 0);
        var resized = StationLayout.Compute(1, 0, 1000, original.Select(s => new StationRequirement(s.PhysicalX, s.LeftExtent, s.RightExtent)));
        Assert.Equal(original, result.Stations);
        Assert.Equal(original.Select(s => s.PhysicalX), resized.Stations.Select(s => s.PhysicalX));
        Assert.Throws<NotSupportedException>(() => ((IList<LayoutStation>)result.Stations).Clear());
        var transform = new StationTransform(original);
        original[1] = new(.3, 70, 0, 0);
        Assert.Equal(18, transform.PhysicalToScreen(.01));
    }

    [Theory]
    [InlineData(0, 0, 100, 8)]
    [InlineData(double.NaN, 0, 100, 8)]
    [InlineData(double.PositiveInfinity, 0, 100, 8)]
    [InlineData(1, 0, 0, 8)]
    [InlineData(1, 100, 0, 8)]
    [InlineData(1, 0, double.PositiveInfinity, 8)]
    [InlineData(1, 0, 100, 0)]
    [InlineData(1, 0, 100, double.NaN)]
    public void InvalidLayoutArgumentsAreRejected(double length, double left, double right, double clearance) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => StationLayout.Compute(length, left, right, [], clearance));

    [Theory]
    [InlineData(-.1, 0, 0)]
    [InlineData(1.1, 0, 0)]
    [InlineData(double.NaN, 0, 0)]
    [InlineData(.5, -1, 0)]
    [InlineData(.5, 0, double.PositiveInfinity)]
    public void InvalidRequirementsAreRejected(double x, double left, double right) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => StationLayout.Compute(1, 0, 100, [new(x, left, right)]));

    [Fact]
    public void ImpossibleScreenPrecisionAndNonFiniteTransformQueriesAreRejected()
    {
        Assert.Throws<ArgumentException>(() => StationLayout.Compute(1, 1e20, Math.BitIncrement(1e20), [new(.5, 0, 0)]));
        var transform = StationLayout.Compute(1, 0, 100, []).Transform;
        Assert.Throws<ArgumentOutOfRangeException>(() => transform.PhysicalToScreen(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => transform.ScreenToPhysical(double.PositiveInfinity));
        Assert.Throws<ArgumentException>(() => new StationTransform([new(0, 0, 0, 0), new(1, 0, 0, 0)]));
        Assert.Throws<ArgumentException>(() => new StationTransform([new(0, 0, 0, 0), new(0, 100, 0, 0)]));
    }

    [Fact]
    public void EntityBoundaryUnionsSharedGlyphsAndIgnoresNamesAndTexts()
    {
        var support = new EditorSupport(Guid.NewGuid(), Length.FromMeters(.5), SupportType.Pinned, "A");
        EditorPointLoad[] loads = [EditorPointLoad.Create(Guid.NewGuid(), support.Position, PointLoadKind.Force, -1000, "F1"),
            EditorPointLoad.Create(Guid.NewGuid(), support.Position, PointLoadKind.Moment, 100, "M1")];
        var document = new EditorDocument(Length.FromMeters(1), ProjectTemplates.Material, ProjectTemplates.Section, [support], loads);
        var requirements = StationRequirementBuilder.FromDocument(document);
        Assert.Equal(3, requirements.Count);
        Assert.Equal(21, requirements[1].LeftExtent);
        Assert.Equal(21, requirements[1].RightExtent);
        Assert.Equal(requirements, StationRequirementBuilder.FromDocument(document.WithSupports([support with { Name = "An arbitrarily long name" }])));
        Assert.Equal(new StationRequirement(0, 0, 0), requirements[0]);
        Assert.Equal(new StationRequirement(1, 0, 0), requirements[^1]);
    }
}
