using Avalonia;
using SpanDraft.Analysis;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Controls;
using SpanDraft.Desktop.State;
using SpanDraft.Solver;
using Xunit;

namespace SpanDraft.Tests;

public sealed class ResultDiagramGeometryTests
{
    [Theory]
    [InlineData(new double[] { 2, -2 }, 2)]
    [InlineData(new double[] { 2, -2, 3, -1 }, 4)]
    [InlineData(new double[] { 2, 0, 0, -2, 0 }, 2)]
    [InlineData(new double[] { 0, 1e-10, -1e-10 }, 0)]
    [InlineData(new double[] { -1, -3, -2 }, 1)]
    public void SignedRegionsCutAtZeroAndGenerateContainedVerticalVectorHatching(double[] values, int count)
    {
        var points = values.Select((value, index) => new ResultDiagramPoint(Length.FromMeters(index),
            EvaluationSide.Right, value * 1000, new Point(index * 24, 100 - value * 10), value)).ToArray();
        var regions = ResultDiagramRegions.Create(points, 100);
        Assert.Equal(count, regions.Count);
        foreach (var region in regions)
        {
            Assert.All(region.Points, point => Assert.True(point.SiValue == 0 || Math.Sign(point.SiValue) == region.Sign));
            Assert.Equal(region.Points.Max(p => Math.Abs(p.SiValue)), Math.Abs(region.Extremum.SiValue));
            var lines = ResultDiagramRegions.Hatch(region, 100).ToArray();
            Assert.NotEmpty(lines);
            foreach (var (start, end) in lines)
            {
                Assert.Equal(start.X, end.X); Assert.Equal(100, start.Y);
                Assert.InRange(end.X, region.Points[0].Screen.X, region.Points[^1].Screen.X);
                Assert.True(region.Sign > 0 ? end.Y < 100 : end.Y > 100);
            }
        }
        for (int i = 1; i < regions.Count; i++)
        {
            Assert.Equal(regions[i - 1].Points[^1].Screen, regions[i].Points[0].Screen);
            Assert.Equal(100, regions[i].Points[0].Screen.Y);
        }
        if (values.SequenceEqual(new double[] { 2, -2 })) Assert.Equal(12, regions[0].Points[^1].Screen.X);
    }

    [Fact]
    public void SectionsOnEitherSideOfJumpNeverShareHatchingAndOneSidedScaleStopsAtZero()
    {
        ResultDiagramPoint P(double x, double value) => new(Length.FromMeters(x), EvaluationSide.Right, value,
            new Point(x * 24, 100 - value * 10), value);
        var left = Assert.Single(ResultDiagramRegions.Create(new[] { P(0, 2), P(1, 3) }, 100));
        var right = Assert.Single(ResultDiagramRegions.Create(new[] { P(1, -1), P(2, -2) }, 100));
        Assert.All(ResultDiagramRegions.Hatch(left, 100), line => Assert.True(line.End.Y < 100));
        Assert.All(ResultDiagramRegions.Hatch(right, 100), line => Assert.True(line.End.Y > 100));
        Assert.Equal(0, new ResultDiagramScale(0, 173, 0, 160).LowerTickIndex);
        Assert.Equal(0, new ResultDiagramScale(-173, 0, 0, 160).UpperTickIndex);
    }

    [Fact]
    public void MomentRoundoffAtSupportDoesNotReserveAnEmptyOppositeSignTickInterval()
    {
        var document = ReportTestProjects.Create().Document;
        var result = BeamAnalysis.Analyze(document.ToBeamModel());
        var presentation = AnalysisPresentationState.FromOutcome(result);
        var layout = new BeamLayoutState().Update(document, 800, 240)!.Layout;
        var plot = ResultDiagramProjection.Create(presentation.Result!.Solution, ResultDiagramKind.BendingMoment, layout, 36, 160);
        Assert.Equal(0, plot.Scale.LowerTickIndex);
        Assert.Equal(0, plot.Scale.Minimum);
        Assert.True(plot.Markers.Max(m => m.SiValue) > 0);
    }
}
