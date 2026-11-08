using SpanDraft.Desktop.Controls;
using SpanDraft.Desktop.State;
using Xunit;

namespace SpanDraft.Tests;

public sealed class DistributedLoadGeometryTests : DesktopDistributedLoadRenderingTestSupport
{

    [Fact]
    public void ForcePositionSplitsAndRedistributesBothSides()
    {
        var visual = Visual();
        Assert.Equal(new[] { 82d, 114d }, DistributedLoadSymbol.InnerGlyphs([visual]).Select(g => g.X));
        Assert.Equal(new[] { 74d, 122d }, DistributedLoadSymbol.InnerGlyphs([visual], [98]).Select(g => g.X));
    }

    [Fact]
    public void MultipleForcePositionsSplitOneLoad()
    {
        var visual = Visual(192);
        Assert.Equal(new[] { 74d, 130d, 162d, 218d },
            DistributedLoadSymbol.InnerGlyphs([visual], [98, 194]).Select(g => g.X));
    }

    [Fact]
    public void MultipleFixedPositionsSplitOverlappingLoadsDeterministically()
    {
        var a = Visual(192);
        var b = Visual(96) with { StartX = 98, EndX = 194 };
        var glyphs = DistributedLoadSymbol.InnerGlyphs([a, b], [146, 74, 146]);
        Assert.Equal(new[] { 122d, 170d, 218d }, glyphs.Select(g => g.X));
        Assert.Equal(new[] { a, b }, glyphs[0].Entities);
        Assert.Equal(glyphs.Select(g => (g.X, g.Positive, g.Negative)),
            DistributedLoadSymbol.InnerGlyphs([b, a], [74, 146]).Select(g => (g.X, g.Positive, g.Negative)));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(10, 1)]
    [InlineData(24, 1)]
    [InlineData(32, 1)]
    [InlineData(48, 2)]
    [InlineData(72, 2)]
    [InlineData(76.8, 2)]
    [InlineData(80, 3)]
    [InlineData(96, 3)]
    public void ArrowIntervalsOptimizeScreenSpacingAndIncludeBothEndpoints(double width, int expected)
    {
        var visual = Visual(width);
        var positions = DistributedLoadSymbol.ArrowPositions(visual);
        Assert.Equal(expected, DistributedLoadSymbol.IntervalCount(width));
        Assert.Equal(expected + 1, positions.Count);
        Assert.Equal(visual.StartX, positions[0]);
        Assert.Equal(visual.EndX, positions[^1]);
        for (int i = 1; i < positions.Count; i++) NumericAssert.Close(width / expected, positions[i] - positions[i - 1]);
    }

    [Fact]
    public void IntervalSelectionMatchesAnExhaustiveIndependentSearch()
    {
        for (int tenth = 240; tenth <= 5000; tenth++)
        {
            double width = tenth / 10d;
            int actual = DistributedLoadSymbol.IntervalCount(width);
            double error = Math.Abs(width / actual - 32);
            Assert.True(width / actual >= 24);
            foreach (int candidate in Enumerable.Range(1, (int)Math.Floor(width / 24)))
                Assert.True(error <= Math.Abs(width / candidate - 32) + 1e-12);
        }
    }

    [Theory]
    [InlineData(10, 0)]
    [InlineData(24, 0)]
    [InlineData(32, 0)]
    [InlineData(48, 1)]
    [InlineData(76.8, 1)] // Existing tie-breaker chooses two intervals.
    [InlineData(96, 2)]
    public void SingleAndSharedSpansUseTheExistingIntervalRule(double width, int count)
    {
        var a = Visual(width);
        var expected = DistributedLoadSymbol.ArrowPositions(a).Skip(1).Take(count).ToArray();
        Assert.Equal(expected, DistributedLoadSymbol.InnerGlyphs([a]).Select(g => g.X));
        Assert.Equal(expected, DistributedLoadSymbol.InnerGlyphs([a, Visual(width)]).Select(g => g.X));
        Assert.Equal(count, DistributedLoadSymbol.InnerGlyphs([a]).Count);
    }

    [Fact]
    public void OnlyEndpointArrowsAreDraggableAndOverconstrainedSpansKeepOrderedArrows()
    {
        var visual = Visual();
        Assert.Equal(DistributedLoadEndpoint.Start, DistributedLoadSymbol.HitEndpoint(visual, visual.StartX, visual.BottomY));
        Assert.Equal(DistributedLoadEndpoint.End, DistributedLoadSymbol.HitEndpoint(visual, visual.EndX, visual.TopY));
        Assert.Null(DistributedLoadSymbol.HitEndpoint(visual, visual.CenterX, visual.TopY + 10));
        Assert.True(DistributedLoadSymbol.Contains(visual, visual.CenterX, visual.TopY + 10));
        var narrow = Visual(10);
        Assert.Equal(new[] { narrow.StartX, narrow.EndX }, DistributedLoadSymbol.ArrowPositions(narrow));
        Assert.Equal(DistributedLoadEndpoint.Start, DistributedLoadSymbol.HitEndpoint(narrow, narrow.CenterX, narrow.TopY + 10));
    }
}
