using Avalonia;
using Avalonia.Media;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Controls;
using SpanDraft.Desktop.Layout;
using SpanDraft.Desktop.State;
using Xunit;

namespace SpanDraft.Tests;

[Collection("Schematic text")]
public sealed class DesktopDistributedLoadRenderingTests
{
    private static DistributedLoadVisual Visual(double width = 96, double q = -500) =>
        new(Guid.NewGuid(), new(Length.FromMeters(.2), Length.FromMeters(.8), q), "q1", 50, 50 + width, 100, false);

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
    [InlineData(-500)]
    [InlineData(500)]
    [InlineData(0)]
    public void DirectionIsSignedWithFixedTopLineHeightAndBeamClearance(double q)
    {
        var visual = Visual(q: q);
        var drawing = Draw(visual);
        var geometry = drawing.Children.Select(d => Assert.IsType<GeometryDrawing>(d)).ToArray();
        Assert.Equal(visual.TopY, geometry[0].Geometry!.Bounds.Top);
        Assert.Equal(0, geometry[0].Geometry!.Bounds.Height);
        Assert.Equal(visual.EndX - visual.StartX, geometry[0].Geometry!.Bounds.Width);
        Assert.All(geometry, g => Assert.Equal(SchematicMetrics.SymbolStrokeWidth, g.Pen!.Thickness));
        var heads = geometry.Where(g => g.Brush is not null).ToArray();
        Assert.Equal(q == 0 ? 0 : 4, heads.Length);
        foreach (var head in heads)
        {
            Assert.Equal(7, head.Geometry!.Bounds.Width, 6);
            Assert.Equal(7, head.Geometry.Bounds.Height, 6);
            Assert.Equal(q < 0 ? visual.BottomY : visual.TopY + 7, head.Geometry.Bounds.Bottom, 6);
        }
        var shafts = geometry.Skip(1).Where(g => g.Brush is null).ToArray();
        Assert.Equal(4, shafts.Length);
        Assert.All(shafts, shaft => Assert.Equal(28, shaft.Geometry!.Bounds.Height, 6));
        double paintedBottom = geometry.Max(g => g.Geometry!.GetRenderBounds(g.Pen!).Bottom);
        Assert.Equal(SchematicMetrics.ForceBeamGap, visual.BeamY - SchematicMetrics.BeamStrokeWidth / 2 - paintedBottom, 6);
    }

    [Fact]
    public void MagnitudeDoesNotChangeGeometryAndZeroRetainsTheSameShafts()
    {
        var a = Draw(Visual(q: -500)).Children.Cast<GeometryDrawing>().ToArray();
        var b = Draw(Visual(q: -1000)).Children.Cast<GeometryDrawing>().ToArray();
        Assert.Equal(a.Select(d => d.Geometry!.Bounds), b.Select(d => d.Geometry!.Bounds));
        var zero = Draw(Visual(q: 0)).Children.Cast<GeometryDrawing>().ToArray();
        Assert.Equal(a.Where(d => d.Brush is null).Select(d => d.Geometry!.Bounds), zero.Select(d => d.Geometry!.Bounds));
        Assert.Equal("q1 = -500 N/m", DistributedLoadSymbol.Label(Visual().Preview, "q1"));
    }

    [Fact]
    public void OverlapFillsUseTotalOpacityAndLeaveGapsUnfilled()
    {
        var a = Visual();
        var b = Visual() with { StartX = 70, EndX = 170, Name = "q2" };
        var group = new DrawingGroup();
        foreach (var visuals in new[] { new[] { a, b }, new[] { b, a } })
        {
            group = Fills(visuals);
            Assert.Equal(3, group.Children.Count);
            AssertFill(group.Children[0], .06, new Rect(50, a.TopY, 20, a.FillBounds.Height));
            AssertFill(group.Children[1], .26, new Rect(70, a.TopY, 76, a.FillBounds.Height));
            AssertFill(group.Children[2], .06, new Rect(146, a.TopY, 24, a.FillBounds.Height));
        }
        b = b with { StartX = 180, EndX = 210 };
        group = Fills([a, b]);
        Assert.Equal(2, group.Children.Count);
        AssertFill(group.Children[0], .06, a.FillBounds);
        AssertFill(group.Children[1], .06, b.FillBounds);
        Assert.Empty(Fills([]).Children);
    }

    [Fact]
    public void CanvasDrawsFillSectionsBeforeBeamAndSharedSymbols()
    {
        EditorUniformDistributedLoad[] loads = [new(Guid.NewGuid(), Length.FromMeters(.2), Length.FromMeters(.8), ForcePerLength.FromNewtonsPerMeter(-500), "q1"),
            new(Guid.NewGuid(), Length.FromMeters(.4), Length.FromMeters(.9), ForcePerLength.FromNewtonsPerMeter(100), "q2")];
        var doc = new EditorDocument(Length.FromMeters(1), ProjectTemplates.Material, ProjectTemplates.Section, distributedLoads: loads);
        var scene = BeamRenderState.Create(doc, new BeamLayoutState().Update(doc, 1100, 600)!, DesktopLayoutFixture.Measure);
        var canvas = new BeamCanvas { Scene = scene, BeamBrush = Brushes.Gray, CanvasBackgroundBrush = Brushes.White,
            AccentBrush = Brushes.Blue, ErrorBrush = Brushes.Red };
        var drawing = new DrawingGroup();
        using (var context = drawing.Open()) canvas.Render(context);
        Assert.Equal(0.06, Assert.IsType<DrawingGroup>(drawing.Children[0]).Opacity);
        Assert.Equal(0.26, Assert.IsType<DrawingGroup>(drawing.Children[1]).Opacity);
        Assert.Equal(0.06, Assert.IsType<DrawingGroup>(drawing.Children[2]).Opacity);
        var beam = Assert.IsType<GeometryDrawing>(drawing.Children[3]);
        Assert.Equal(SchematicMetrics.BeamStrokeWidth, beam.Pen!.Thickness);
        Assert.Equal(2, scene.DistributedLoads.Count);
    }

    [Theory]
    [InlineData(1, .06)]
    [InlineData(2, .26)]
    [InlineData(3, .4174468085106383)]
    [InlineData(4, .5413942960615663)]
    [InlineData(5, .6389699777505947)]
    [InlineData(6, .715784876101532)]
    [InlineData(7, .75)]
    [InlineData(20, .75)]
    public void FinalOpacityCountsEveryLoadRegardlessOfDirectionOrMagnitude(int count, double expected)
    {
        var a = Visual();
        var visuals = Enumerable.Range(0, count).Select(i => a with
            { Preview = a.Preview with { Intensity = i % 3 == 0 ? 0 : i % 3 == 1 ? -500 : 2000 } }).ToArray();
        AssertFill(Assert.Single(Fills(visuals).Children), expected, a.FillBounds, 1e-12);
        Assert.Equal(0, SchematicMetrics.DistributedFillOpacityForCount(0));
        Assert.Equal(.06, SchematicMetrics.DistributedFillOpacityForCount(1));
        Assert.Equal(.26, SchematicMetrics.DistributedFillOpacityForCount(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => SchematicMetrics.DistributedFillOpacityForCount(-1));
    }

    [Theory]
    [InlineData(-500, -1000)]
    [InlineData(500, 1000)]
    public void SameDirectionOverlapHasOneSharedInteriorArrow(double first, double second)
    {
        var a = Visual(q: first);
        var b = Visual(q: second) with { StartX = 82, EndX = 178 };
        var glyph = Assert.Single(DistributedLoadSymbol.InnerGlyphs([a, b]));
        Assert.Equal(114, glyph.X);
        Assert.Equal(new[] { a, b }, glyph.Entities);
        var geometry = DrawInner(glyph).Children.Cast<GeometryDrawing>().ToArray();
        Assert.Equal(2, geometry.Length); // One shaft and one head, regardless of contributor count.
        Assert.Equal(28, geometry[0].Geometry!.Bounds.Height);
        Assert.Equal(first < 0 ? a.BottomY : a.TopY + 7, geometry[1].Geometry!.Bounds.Bottom, 6);
        int previousInnerCount = new[] { a, b }.Sum(v => DistributedLoadSymbol.ArrowPositions(v).Count - 2);
        Assert.True(DistributedLoadSymbol.InnerGlyphs([a, b]).Count < previousInnerCount);
    }

    [Theory]
    [InlineData(-500, 500, true, true)]
    [InlineData(-500, 0, false, true)]
    [InlineData(0, 500, true, false)]
    [InlineData(0, 0, false, false)]
    public void OppositeDirectionsShareOneShaftAndZeroDoesNotAddAHead(double first, double second,
        bool positive, bool negative)
    {
        var a = Visual(q: first);
        var b = Visual(q: second);
        var zero = Visual(q: 0);
        var glyphs = DistributedLoadSymbol.InnerGlyphs([a, b, zero]);
        Assert.Equal(new[] { 82d, 114d }, glyphs.Select(g => g.X));
        foreach (var glyph in glyphs)
        {
            Assert.Equal(positive, glyph.Positive);
            Assert.Equal(negative, glyph.Negative);
            var geometry = DrawInner(glyph).Children.Cast<GeometryDrawing>().ToArray();
            var shaft = Assert.Single(geometry, g => g.Brush is null);
            Assert.Equal(new Rect(glyph.X, a.TopY, 0, 28), shaft.Geometry!.Bounds);
            var heads = geometry.Where(g => g.Brush is not null).ToArray();
            Assert.Equal((positive ? 1 : 0) + (negative ? 1 : 0), heads.Length);
            if (positive) Assert.Contains(heads, h => h.Geometry!.Bounds.Top == a.TopY);
            if (negative) Assert.Contains(heads, h => h.Geometry!.Bounds.Bottom == a.BottomY);
            Assert.All(geometry, g => Assert.Equal(SchematicMetrics.SymbolStrokeWidth, g.Pen!.Thickness));
        }
    }

    [Theory]
    [InlineData(50, 146)] // Identical spans.
    [InlineData(74, 122)] // Nested span.
    [InlineData(82, 178)] // Partial overlap.
    public void EveryEntityKeepsBothExactEndpointArrowsAndItsHitTargets(double start, double end)
    {
        var a = Visual();
        var b = Visual(q: 500) with { StartX = start, EndX = end };
        foreach (var visual in new[] { a, b })
        {
            var group = new DrawingGroup();
            using (var context = group.Open()) DistributedLoadSymbol.DrawEndpoints(context, visual, Brushes.Gray);
            var geometry = group.Children.Cast<GeometryDrawing>().ToArray();
            Assert.Equal(5, geometry.Length); // Connector, two shafts and two independent heads.
            Assert.Equal(visual.StartX, geometry[1].Geometry!.Bounds.X);
            Assert.Equal(visual.EndX, geometry[3].Geometry!.Bounds.X);
            Assert.Equal(DistributedLoadEndpoint.Start,
                DistributedLoadSymbol.HitEndpoint(visual, visual.StartX, visual.TopY));
            Assert.Equal(DistributedLoadEndpoint.End,
                DistributedLoadSymbol.HitEndpoint(visual, visual.EndX, visual.BottomY));
        }
        var scene = Scene([a, b]);
        Assert.Equal(new[] { a.Id!.Value, b.Id!.Value }, scene.HitTestEntities((Math.Max(a.StartX, b.StartX)
            + Math.Min(a.EndX, b.EndX)) / 2, a.TopY + 10));
        var drawing = Canvas(scene);
        var shafts = Geometry(drawing).Where(g => g.Brush is null && g.Geometry!.Bounds.Height == 28).ToArray();
        int shared = DistributedLoadSymbol.InnerGlyphs([a, b]).Count;
        Assert.Equal(shared + 4, shafts.Length);
        Assert.Equal(new[] { a.StartX, a.EndX, b.StartX, b.EndX }, shafts.Skip(shared).Select(g => g.Geometry!.Bounds.X));
    }

    [Fact]
    public void SharedLayoutPreservesSpacingIsDeterministicAndDoesNotCreateStations()
    {
        var random = new Random(412);
        for (int trial = 0; trial < 50; trial++)
        {
            var visuals = Enumerable.Range(0, 5).Select(i =>
            {
                double start = random.Next(0, 400), width = random.Next(1, 250);
                return Visual(width, i % 2 == 0 ? -500 : 500) with { StartX = start, EndX = start + width };
            }).ToArray();
            var glyphs = DistributedLoadSymbol.InnerGlyphs(visuals);
            Assert.Equal(glyphs.Select(g => (g.X, g.Positive, g.Negative)),
                DistributedLoadSymbol.InnerGlyphs(visuals.Reverse().ToArray()).Select(g => (g.X, g.Positive, g.Negative)));
            var endpoints = visuals.SelectMany(v => new[] { v.StartX, v.EndX }).ToArray();
            foreach (var glyph in glyphs)
            {
                Assert.All(endpoints, x => Assert.True(Math.Abs(x - glyph.X) >= 24 - 1e-10));
                Assert.Equal(visuals.Where(v => v.StartX < glyph.X && v.EndX > glyph.X), glyph.Entities);
            }
            for (int i = 1; i < glyphs.Count; i++) Assert.True(glyphs[i].X - glyphs[i - 1].X >= 24 - 1e-10);
        }
        var scene = Scene([Visual(), Visual() with { StartX = 82, EndX = 178 }]);
        var transform = scene.Frame.Layout.Transform;
        var stations = scene.Frame.Layout.Stations.ToArray();
        _ = DistributedLoadSymbol.InnerGlyphs(scene.DistributedLoads);
        _ = Canvas(scene);
        Assert.Same(transform, scene.Frame.Layout.Transform);
        Assert.Equal(stations, scene.Frame.Layout.Stations);
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

    [Theory]
    [InlineData(false, false, false, false, "Gray", 1)]
    [InlineData(true, false, false, false, "Blue", .75)]
    [InlineData(false, true, false, false, "Blue", 1)]
    [InlineData(true, true, true, false, "Red", .75)]
    [InlineData(false, true, false, true, "Red", 1)]
    public void SharedGlyphStyleUsesAllContributorsWithErrorPriority(bool preview, bool highlight,
        bool invalid, bool conflict, string color, double opacity)
    {
        var a = Visual();
        var b = Visual() with { IsPreview = preview, Preview = a.Preview with { IsInvalid = invalid } };
        var scene = Scene([a, b]);
        var canvas = NewCanvas(scene);
        canvas.HighlightedDistributedLoadId = highlight ? b.Id : null;
        canvas.ConflictEntityIds = conflict ? [b.Id!.Value] : [];
        var group = new DrawingGroup();
        using (var context = group.Open()) canvas.Render(context);
        var inner = Assert.IsType<DrawingGroup>(group.Children[2]); // One fill, beam, first shared glyph.
        Assert.Equal(opacity, inner.Opacity);
        var brush = color == "Red" ? Brushes.Red : color == "Blue" ? Brushes.Blue : Brushes.Gray;
        Assert.All(Geometry(inner), g => Assert.Same(brush, g.Pen!.Brush));
        var endpoints = Geometry(group).Where(g => g.Brush is null && g.Geometry!.Bounds.Height == 28)
            .Skip(2).ToArray();
        Assert.Equal(4, endpoints.Length);
        Assert.Same(Brushes.Gray, endpoints[0].Pen!.Brush); // Other entity keeps its own style.
        Assert.Same(brush, endpoints[2].Pen!.Brush);
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

    private static DrawingGroup Draw(DistributedLoadVisual visual)
    {
        var drawing = new DrawingGroup();
        using (var context = drawing.Open()) DistributedLoadSymbol.Draw(context, visual, Brushes.Black);
        return drawing;
    }

    private static DrawingGroup DrawInner(DistributedLoadInnerGlyph glyph)
    {
        var drawing = new DrawingGroup();
        using (var context = drawing.Open()) DistributedLoadSymbol.DrawInner(context, glyph, Brushes.Gray);
        return drawing;
    }

    private static DrawingGroup Fills(IReadOnlyList<DistributedLoadVisual> visuals)
    {
        var drawing = new DrawingGroup();
        using (var context = drawing.Open()) DistributedLoadSymbol.DrawFills(context, visuals, Brushes.Gray);
        return drawing;
    }

    private static void AssertFill(Drawing drawing, double opacity, Rect bounds, double tolerance = 0)
    {
        var group = Assert.IsType<DrawingGroup>(drawing);
        if (tolerance == 0) Assert.Equal(opacity, group.Opacity);
        else Assert.InRange(group.Opacity, opacity - tolerance, opacity + tolerance);
        var fill = Assert.IsType<GeometryDrawing>(Assert.Single(group.Children));
        Assert.Same(Brushes.Gray, fill.Brush);
        Assert.Null(fill.Pen);
        Assert.Equal(bounds, fill.Geometry!.Bounds);
    }

    private static BeamRenderState Scene(IReadOnlyList<DistributedLoadVisual> visuals)
    {
        var doc = new EditorDocument(Length.FromMeters(1), ProjectTemplates.Material, ProjectTemplates.Section);
        return BeamRenderState.Create(doc, new BeamLayoutState().Update(doc, 1100, 600)!, DesktopLayoutFixture.Measure)
            with { DistributedLoads = visuals, Annotations = [] };
    }

    private static BeamCanvas NewCanvas(BeamRenderState scene) => new()
    {
        Scene = scene, BeamBrush = Brushes.Gray, CanvasBackgroundBrush = Brushes.White,
        AccentBrush = Brushes.Blue, ErrorBrush = Brushes.Red
    };

    private static DrawingGroup Canvas(BeamRenderState scene)
    {
        var group = new DrawingGroup();
        using (var context = group.Open()) NewCanvas(scene).Render(context);
        return group;
    }

    private static IEnumerable<GeometryDrawing> Geometry(Drawing drawing) => drawing is DrawingGroup group
        ? group.Children.SelectMany(Geometry) : drawing is GeometryDrawing geometry ? [geometry] : [];
}
