using System.Globalization;
using Avalonia;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Controls;
using SpanDraft.Desktop.Layout;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.State;
using Xunit;

namespace SpanDraft.Tests;

/// <summary>Geometry, hit testing and labels of linearly varying distributed loads, without any control.</summary>
public sealed class DesktopLinearLoadSymbolTests
{
    private static EditorDistributedLoad Load(double q1, double q2, double start = .2, double end = .8, string name = "q1") =>
        new(Guid.NewGuid(), Length.FromMeters(start), Length.FromMeters(end), ForcePerLength.FromNewtonsPerMeter(q1),
            ForcePerLength.FromNewtonsPerMeter(q2), name);
    private static EditorDocument Document(params EditorDistributedLoad[] loads) =>
        new(Length.FromMeters(1), ProjectTemplates.Material, ProjectTemplates.Section, distributedLoads: loads);
    private static BeamRenderState Scene(EditorDocument doc) =>
        BeamRenderState.Create(doc, new BeamLayoutState().Update(doc, 1100, 600)!, DesktopLayoutFixture.Measure);
    private static DistributedLoadVisual Visual(double q1, double q2) => Assert.Single(Scene(Document(Load(q1, q2))).DistributedLoads);

    [Fact]
    public void AConstantLoadKeepsItsRectangularEnvelopeAtFullHeight()
    {
        var visual = Visual(-500, -500);
        Assert.False(visual.IsLinear);
        Assert.Equal(new[] { new Point(visual.StartX, visual.TopY), new Point(visual.EndX, visual.TopY) }, visual.EnvelopePoints());
        Assert.Equal(visual.BottomY - visual.TopY, visual.EnvelopeHeightAt(.5));
        Assert.Equal(visual.TopY, visual.HighestTopY);
    }

    [Fact]
    public void ATriangleHasZeroHeightAtItsZeroEndAndFullHeightAtTheLargerEnd()
    {
        var visual = Visual(0, -400);
        Assert.True(visual.IsLinear);
        NumericAssert.Close(visual.BottomY, visual.StartTopY);
        NumericAssert.Close(visual.TopY, visual.EndTopY);
        NumericAssert.Close((visual.BottomY - visual.TopY) / 2, visual.EnvelopeHeightAt(.5));
        Assert.Equal(2, visual.EnvelopePoints().Count);
        // The zero end carries no arrow, the large end does and points down for a negative intensity.
        Assert.Equal(0, DistributedLoadSymbol.LinearArrowHeight(visual, visual.StartX));
        NumericAssert.Close(visual.BottomY - visual.TopY, DistributedLoadSymbol.LinearArrowHeight(visual, visual.EndX));
        Assert.Equal(-1, DistributedLoadSymbol.LinearArrowSign(visual, visual.EndX));
    }

    [Fact]
    public void ATrapezoidScalesBothEndsByTheLargerMagnitude()
    {
        var visual = Visual(-200, -800);
        double full = visual.BottomY - visual.TopY;
        NumericAssert.Close(full / 4, visual.BottomY - visual.StartTopY);
        NumericAssert.Close(full, visual.BottomY - visual.EndTopY);
        Assert.Equal(visual.TopY, visual.HighestTopY);
        var reversed = Visual(800, 200);
        NumericAssert.Close(full, reversed.BottomY - reversed.StartTopY);
        NumericAssert.Close(full / 4, reversed.BottomY - reversed.EndTopY);
        Assert.Equal(1, DistributedLoadSymbol.LinearArrowSign(reversed, reversed.StartX));
    }

    [Fact]
    public void ASignChangeTouchesTheBaseLineAtTheZeroCrossingAndFlipsTheArrowDirection()
    {
        var visual = Visual(-300, 900);
        var points = visual.EnvelopePoints();
        Assert.Equal(3, points.Count);
        double crossing = visual.StartX + (visual.EndX - visual.StartX) * 300 / 1200;
        NumericAssert.Close(crossing, points[1].X);
        Assert.Equal(visual.BottomY, points[1].Y);
        Assert.Equal(-1, DistributedLoadSymbol.LinearArrowSign(visual, visual.StartX));
        Assert.Equal(1, DistributedLoadSymbol.LinearArrowSign(visual, visual.EndX));
        // Next to the crossing the arrows are shorter than their head and omitted.
        Assert.Equal(0, DistributedLoadSymbol.LinearArrowHeight(visual, crossing));
        Assert.True(DistributedLoadSymbol.LinearArrowHeight(visual, visual.StartX) > 0);
    }

    [Fact]
    public void ZeroEndsStillOfferAnEndpointHandleOnTheBaseLine()
    {
        var visual = Visual(0, -400);
        Assert.Equal(DistributedLoadEndpoint.Start, DistributedLoadSymbol.HitEndpoint(visual, visual.StartX, visual.BottomY - 2));
        Assert.Equal(DistributedLoadEndpoint.End, DistributedLoadSymbol.HitEndpoint(visual, visual.EndX, visual.TopY + 4));
        Assert.Null(DistributedLoadSymbol.HitEndpoint(visual, visual.StartX, visual.TopY - 40));
    }

    [Fact]
    public void ATriangleDoesNotClaimThePointsAboveItsEnvelope()
    {
        var visual = Visual(0, -400);
        double x = visual.StartX + (visual.EndX - visual.StartX) * .25;
        // Inside the symbol rectangle but far above the envelope: nothing.
        Assert.False(DistributedLoadSymbol.Contains(visual, x, visual.TopY + 1));
        Assert.True(DistributedLoadSymbol.Contains(visual, x, visual.BottomY - 1));
        Assert.True(DistributedLoadSymbol.Contains(visual, visual.StartX + (visual.EndX - visual.StartX) * .9, visual.TopY + 6));
        var constant = Visual(-500, -500);
        Assert.True(DistributedLoadSymbol.Contains(constant, constant.StartX + 20, constant.TopY + 1));
    }

    [Fact]
    public void LinearLoadsNeverShareInteriorShaftsButConstantOnesStillDo()
    {
        var doc = Document(Load(-500, -500, .1, .9, "q1"), Load(-100, -700, .1, .9, "q2"), Load(-300, -300, .3, .7, "q3"));
        var scene = Scene(doc);
        var glyphs = DistributedLoadSymbol.InnerGlyphs(scene.DistributedLoads);
        Assert.All(glyphs, glyph => Assert.All(glyph.Entities, e => Assert.False(e.IsLinear)));
        Assert.Contains(glyphs, glyph => glyph.Entities.Count == 2);
        var linearOnly = Scene(Document(Load(0, -400)));
        Assert.Empty(DistributedLoadSymbol.InnerGlyphs(linearOnly.DistributedLoads));
    }

    [Fact]
    public void TheFillOfALinearLoadFollowsTheEnvelopeAndStaysAboveTheBeam()
    {
        var visual = Visual(-300, 900);
        var points = DistributedLoadSymbol.FillPoints(visual);
        NumericAssert.Close(visual.StartX, points.Min(p => p.X));
        NumericAssert.Close(visual.EndX, points.Max(p => p.X));
        NumericAssert.Close(visual.HighestTopY, points.Min(p => p.Y));
        NumericAssert.Close(visual.FillBottomY, points.Max(p => p.Y));
        // The envelope corner at the zero crossing lies on the arrow base line, above the beam.
        Assert.Contains(points, p => p.Y == visual.BottomY);
        Assert.True(visual.BottomY < visual.FillBottomY);
    }

    [Fact]
    public void EachEndReservesArrowHeadSpaceOnlyWhereItHasAnIntensity()
    {
        double stroke = SchematicMetrics.SymbolStrokeWidth / 2;
        var triangle = StationRequirementBuilder.FromDocument(Document(Load(0, -400)));
        Assert.Equal(stroke, triangle.Single(r => r.PhysicalX == .2).LeftExtent);
        Assert.Equal(SchematicMetrics.DistributedArrowHeadHalfWidth + stroke, triangle.Single(r => r.PhysicalX == .8).RightExtent);
        var both = StationRequirementBuilder.FromDocument(Document(Load(-100, 100)));
        Assert.Equal(SchematicMetrics.DistributedArrowHeadHalfWidth + stroke, both.Single(r => r.PhysicalX == .2).LeftExtent);
    }

    [Theory]
    [InlineData("en-US", "q1 = -500 … 300\u00A0N/m")]
    [InlineData("de-DE", "q1 = -500 … 300\u00A0N/m")]
    public void ALinearLabelShowsBothEndsInOneUnitAndAConstantLabelIsUnchanged(string culture, string expected)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            string linear = DistributedLoadSymbol.Label(new DistributedLoadPreview(Length.FromMeters(.2), Length.FromMeters(.8), -500, 300), "q1");
            Assert.Equal(expected.Replace("-", linear[5] == '−' ? "−" : "-"), linear);
            string constant = DistributedLoadSymbol.Label(new DistributedLoadPreview(Length.FromMeters(.2), Length.FromMeters(.8), -500), "q1");
            Assert.DoesNotContain("…", constant);
            Assert.EndsWith(" N/m", constant);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }
}
