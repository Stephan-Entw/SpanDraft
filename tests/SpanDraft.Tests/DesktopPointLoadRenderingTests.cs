using Avalonia;
using Avalonia.Media;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Controls;
using SpanDraft.Desktop.Layout;
using SpanDraft.Desktop.State;
using Xunit;

namespace SpanDraft.Tests;

[Collection("Schematic text")]
public sealed class DesktopPointLoadRenderingTests
{
    private const double Center = 40;

    [Theory]
    [InlineData(100, 0, 1)]
    [InlineData(-100, 0, 1)]
    [InlineData(100, -100, 2)]
    [InlineData(0, 0, 0)]
    public void ForceContourClearsTheUpperBeamEdgeAndArrowheadsStayFilled(double first, double second, int arrowheads)
    {
        var drawings = DrawLoad(PointLoadKind.Force, first, second);
        var shaft = Assert.Single(drawings, d => d.Brush is null);
        Assert.Equal(arrowheads, drawings.Count(d => d.Brush is not null));
        Assert.Equal(SchematicMetrics.ForceHeight, shaft.Geometry!.Bounds.Height, 10);
        double beamTop = Center - SchematicMetrics.BeamStrokeWidth / 2;
        double paintedBottom = drawings.Max(d => d.Geometry!.GetRenderBounds(d.Pen!).Bottom);
        Assert.Equal(SchematicMetrics.ForceBeamGap, beamTop - paintedBottom, 6);
        Assert.All(drawings, d => Assert.Equal(SchematicMetrics.SymbolStrokeWidth, d.Pen!.Thickness));
    }

    [Fact]
    public void NegativeMomentMirrorsTheEntirePositiveArcAndFilledArrowhead()
    {
        var positive = Draw(100);
        var negative = Draw(-100);
        Assert.Equal(2, positive.Length);
        Assert.Equal(2, negative.Length);
        for (int i = 0; i < positive.Length; i++)
        {
            var original = positive[i];
            var mirror = negative[i];
            Assert.Equal(original.Pen!.Thickness, mirror.Pen!.Thickness);
            Assert.Equal(SchematicMetrics.SymbolStrokeWidth, original.Pen.Thickness);
            Assert.Equal(original.Brush is null, mirror.Brush is null);
            // Check the recorded drawing, including the filled triangle, on a
            // grid offset from vertices to avoid floating-point boundary ties.
            for (double x = Center - 20 + 0.123; x < Center + 20; x += 0.5)
                for (double y = Center - 20 + 0.321; y < Center + 20; y += 0.5)
                {
                    var point = new Point(x, y);
                    var reflected = new Point(2 * Center - x, y);
                    Assert.Equal(original.Geometry!.StrokeContains(original.Pen, point),
                        mirror.Geometry!.StrokeContains(mirror.Pen, reflected));
                    if (original.Brush is not null)
                        Assert.Equal(original.Geometry.FillContains(point), mirror.Geometry.FillContains(reflected));
                }
        }
        Assert.NotNull(positive[1].Brush);
    }

    [Theory]
    [InlineData(100, -100)]
    [InlineData(-200, 100)]
    public void BothSignsDrawOneUnionArcAndTwoFilledArrowheads(double first, double second)
    {
        var drawings = Draw(first, second);
        Assert.Equal(3, drawings.Length);
        var arc = Assert.Single(drawings, d => d.Brush is null);
        Assert.Equal(2, drawings.Count(d => d.Brush is not null));
        // The union covers the former openings at 140 and 220 degrees, while
        // the small gap between the two arrow tips at 180 degrees stays open.
        foreach (double angle in new[] { 0d, 90, 140, 170, 190, 220, 270 })
            Assert.True(arc.Geometry!.StrokeContains(arc.Pen!, OnCircle(angle)));
        Assert.False(arc.Geometry!.StrokeContains(arc.Pen!, OnCircle(180)));
    }

    [Fact]
    public void ZeroMomentKeepsTheNeutralArcWithoutAnArrowhead()
    {
        var neutral = Assert.Single(Draw(0));
        var positiveArc = Draw(100)[0];
        Assert.Null(neutral.Brush);
        Assert.Equal(positiveArc.Geometry!.Bounds, neutral.Geometry!.Bounds);
        Assert.True(neutral.Geometry.StrokeContains(neutral.Pen!, OnCircle(0)));
        Assert.False(neutral.Geometry.StrokeContains(neutral.Pen!, OnCircle(150)));
    }

    private static Point OnCircle(double degrees)
    {
        double angle = degrees * Math.PI / 180;
        return new(Center + SchematicMetrics.MomentRadius * Math.Sin(angle),
            Center - SchematicMetrics.MomentRadius * Math.Cos(angle));
    }

    private static GeometryDrawing[] Draw(params double[] values) => DrawLoad(PointLoadKind.Moment, values);

    private static GeometryDrawing[] DrawLoad(PointLoadKind kind, params double[] values)
    {
        var entities = values.Select(value => new PointLoadVisual(Guid.NewGuid(),
            new(Length.FromMillimeters(500), kind, value), "Load", Center, Center, false)).ToArray();
        var drawing = new DrawingGroup();
        using (var context = drawing.Open())
            PointLoadSymbol.Draw(context, new(kind, Center, Center, entities), Brushes.Black);
        return drawing.Children.Select(child => Assert.IsType<GeometryDrawing>(child)).ToArray();
    }
}
