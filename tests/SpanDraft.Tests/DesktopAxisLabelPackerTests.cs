using System.Globalization;
using SpanDraft.Desktop.Layout;
using SpanDraft.Desktop.State;
using Xunit;

namespace SpanDraft.Tests;

public sealed class DesktopAxisLabelPackerTests
{
    [Fact]
    public void Regression1200To1400UsesExactlyTwoLanesAndReusesTheLowestLane()
    {
        AxisLabelRequirement[] source = [new(600, 40, 1200), new(625, 40, 1250), new(650, 40, 1300), new(700, 40, 1400, AxisEndpointRole.End)];
        var result = AxisLabelPacker.Pack(source, 0, 700);
        Assert.Equal(2, result.LaneCount);
        Assert.Equal(new[] { 0, 1, 0, 1 }, result.Labels.Select(l => l.Lane));
        Assert.Equal(64, result.PaneHeight);
        Assert.Equal(660, result.Labels[^1].Left);
        Assert.Equal(700, result.Labels[^1].Requirement.StationAnchorX);
        Assert.Equal(result.Labels, AxisLabelPacker.Pack(source.Reverse(), 0, 700).Labels);
        AssertCollisionFree(result, 8);
    }

    [Fact]
    public void SeparatedAndDifferentlyMeasuredLabelsRemainInOneLane()
    {
        var result = AxisLabelPacker.Pack([new(50, 20, 0), new(150, 100, 1), new(220, 20, 2)], 0, 300);
        Assert.Equal(1, result.LaneCount);
        Assert.Equal(new[] { 20d, 100, 20 }, result.Labels.Select(l => l.Right - l.Left));
        Assert.Equal(48, result.PaneHeight);
        AssertCollisionFree(result, 8);
    }

    [Fact]
    public void AThirdLaneIsUsedOnlyWhenThreePaddedIntervalsOverlap()
    {
        var result = AxisLabelPacker.Pack([new(100, 40, 0), new(110, 40, 1), new(120, 40, 2), new(200, 20, 3)], 0, 300);
        Assert.Equal(3, result.LaneCount);
        Assert.Equal(new[] { 0, 1, 2, 0 }, result.Labels.Select(l => l.Lane));
        Assert.Equal(80, result.PaneHeight);
        AssertCollisionFree(result, 8);
    }

    [Fact]
    public void EndpointClampingPreservesWidthAndTickAnchors()
    {
        var result = AxisLabelPacker.Pack([new(0, 40, 0, AxisEndpointRole.Start), new(100, 40, 1, AxisEndpointRole.End)], 0, 100);
        Assert.Equal(0, result.Labels[0].Left);
        Assert.Equal(40, result.Labels[0].Right);
        Assert.Equal(60, result.Labels[1].Left);
        Assert.Equal(100, result.Labels[1].Right);
        Assert.Equal(new[] { 0d, 100 }, result.Labels.Select(l => l.Requirement.StationAnchorX));
    }

    [Fact]
    public void OversizedEndpointsKeepTheirMeasuredWidthsAndAreAlignedInward()
    {
        var result = AxisLabelPacker.Pack([new(0, 120, 0, AxisEndpointRole.Start), new(100, 120, 1, AxisEndpointRole.End)], 0, 100);
        var start = result.Labels.Single(l => l.Requirement.EndpointRole == AxisEndpointRole.Start);
        var end = result.Labels.Single(l => l.Requirement.EndpointRole == AxisEndpointRole.End);
        Assert.Equal(0, start.Left);
        Assert.Equal(120, start.Right);
        Assert.Equal(-20, end.Left);
        Assert.Equal(100, end.Right);
        Assert.Equal(2, result.LaneCount);
    }

    [Fact]
    public void EqualLeftBoundsUseStableKeysRatherThanAnchorOrIterationOrder()
    {
        AxisLabelRequirement[] source = [new(10, 20, 2), new(20, 40, 1)];
        var result = AxisLabelPacker.Pack(source, 0, 100);
        Assert.Equal(new long[] { 1, 2 }, result.Labels.Select(l => l.Requirement.StableOrderKey));
        Assert.Equal(new[] { 0, 1 }, result.Labels.Select(l => l.Lane));
        Assert.Equal(result.Labels, AxisLabelPacker.Pack(source.Reverse(), 0, 100).Labels);
    }

    [Fact]
    public void OneLabelPerUniqueLayoutStationPreventsSharedEntityDuplicates()
    {
        var layout = StationLayout.Compute(1, 0, 300, [new(.5, 4, 4), new(.5, 18, 18)]);
        var labels = layout.Stations.Select((s, i) => new AxisLabelRequirement(s.ScreenX, 20, i));
        var result = AxisLabelPacker.Pack(labels, 0, 300);
        Assert.Equal(3, result.Labels.Count);
        Assert.Equal(1, result.LaneCount);
        Assert.Throws<ArgumentException>(() => AxisLabelPacker.Pack([new(50, 20, 0), new(50, 20, 1)], 0, 100));
        Assert.Throws<ArgumentException>(() => AxisLabelPacker.Pack([new(50, 20, 0), new(80, 20, 0)], 0, 100));
    }

    [Fact]
    public void HeightUsesTheSuppliedMetricsAndEmptyInputHasNoLanes()
    {
        var empty = AxisLabelPacker.Pack([], 0, 100, baseHeight: 10, lineHeight: 17, verticalPadding: 3);
        Assert.Empty(empty.Labels);
        Assert.Equal(0, empty.LaneCount);
        Assert.Equal(13, empty.PaneHeight);
        var result = AxisLabelPacker.Pack([new(10, 30, 0), new(20, 30, 1)], 0, 100,
            baseHeight: 10, lineHeight: 17, verticalPadding: 3);
        Assert.Equal(47, result.PaneHeight);
        Assert.Throws<NotSupportedException>(() => ((IList<PackedAxisLabel>)result.Labels).Clear());
    }

    [Fact]
    public void ExactPaddingBoundaryReusesALane()
    {
        Assert.Equal(1, AxisLabelPacker.Pack([new(10, 20, 0), new(38, 20, 1)], 0, 100).LaneCount);
        Assert.Equal(2, AxisLabelPacker.Pack([new(10, 20, 0), new(37.99, 20, 1)], 0, 100).LaneCount);
    }

    [Fact]
    public void LaneCountEqualsIndependentMaximumIntervalOverlapAcrossMeasuredWidths()
    {
        var random = new Random(7);
        for (int trial = 0; trial < 100; trial++)
        {
            var input = Enumerable.Range(0, 30).Select(i => new AxisLabelRequirement(i * 5, random.Next(5, 70), i)).ToArray();
            var result = AxisLabelPacker.Pack(input, -100, 300);
            var events = result.Labels.SelectMany(l => new[] { (X: l.Left, Delta: 1), (X: l.Right + 8, Delta: -1) })
                .OrderBy(e => e.X).ThenBy(e => e.Delta);
            int occupied = 0, maximum = 0;
            foreach (var e in events) { occupied += e.Delta; maximum = Math.Max(maximum, occupied); }
            Assert.Equal(maximum, result.LaneCount);
            Assert.Equal(result.Labels, AxisLabelPacker.Pack(input.Reverse(), -100, 300).Labels);
            AssertCollisionFree(result, 8);
        }
    }

    [Theory]
    [InlineData("de-DE", "1200,5")]
    [InlineData("en-US", "1200.5")]
    public void CultureFormattingAndMeasurementHappenBeforePacking(string culture, string expected)
    {
        var previous = CultureInfo.CurrentUICulture;
        var numericCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            string longText = UiNumbers.Format(1200.5);
            string shortText = UiNumbers.Format(1);
            Assert.Equal(expected, longText);
            // Deterministic measurement fixture; real Avalonia measurement belongs to Stage 2.
            double Measure(string text) => text.Length * 8;
            var result = AxisLabelPacker.Pack([new(50, Measure(longText), 0), new(80, Measure(shortText), 1)], 0, 100);
            Assert.Equal(2, result.LaneCount);
            Assert.Equal(48, result.Labels[0].Right - result.Labels[0].Left);
            Assert.Equal(8, result.Labels[1].Right - result.Labels[1].Left);
        }
        finally { CultureInfo.CurrentUICulture = previous; CultureInfo.CurrentCulture = numericCulture; }
    }

    [Theory]
    [InlineData(double.NaN, 20)]
    [InlineData(50, -1)]
    [InlineData(50, double.PositiveInfinity)]
    public void InvalidMeasuredInputsAreRejected(double anchor, double width) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => AxisLabelPacker.Pack([new(anchor, width, 0)], 0, 100));

    private static void AssertCollisionFree(AxisLabelPackingResult result, double padding)
    {
        foreach (var lane in result.Labels.GroupBy(l => l.Lane))
        {
            var ordered = lane.OrderBy(l => l.Left).ToArray();
            for (int i = 1; i < ordered.Length; i++) Assert.True(ordered[i].Left - ordered[i - 1].Right >= padding);
        }
    }
}
