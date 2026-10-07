using Avalonia;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Controls;
using SpanDraft.Desktop.Layout;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using Xunit;

namespace SpanDraft.Tests;

public sealed class DesktopDistributedLoadLayoutTests
{
    private static EditorUniformDistributedLoad Load(double start, double end, string name = "q1") =>
        new(Guid.NewGuid(), Length.FromMeters(start), Length.FromMeters(end), ForcePerLength.FromNewtonsPerMeter(-500), name);
    private static EditorDocument Document(params EditorUniformDistributedLoad[] loads) =>
        new(Length.FromMeters(1), ProjectTemplates.Material, ProjectTemplates.Section, distributedLoads: loads);
    private static BeamRenderState Scene(EditorDocument doc, double width = 1100) =>
        BeamRenderState.Create(doc, new BeamLayoutState().Update(doc, width, 600)!, DesktopLayoutFixture.Measure);

    [Fact]
    public void UdlEndpointsAreAxisStationsWithExactSharedTransformAndMinimumWidth()
    {
        var load = Load(.5, .50001);
        var doc = Document(load);
        var scene = Scene(doc);
        var frame = scene.Frame;
        var axis = CoordinateAxisLayout.Create(frame.Layout, frame.Viewport.Width, DesktopLayoutFixture.Measure);
        Assert.Same(frame.Layout, axis.StationLayout);
        Assert.Equal(new[] { 0d, .5, .50001, 1 }, frame.Layout.Stations.Select(s => s.PhysicalX));
        Assert.Equal(frame.Layout.Stations.Select(s => s.PhysicalX), axis.Labels.OrderBy(l => l.PhysicalX).Select(l => l.PhysicalX));
        var visual = Assert.Single(scene.DistributedLoads);
        Assert.Equal(frame.Layout.Transform.PhysicalToScreen(.5), visual.StartX);
        Assert.Equal(frame.Layout.Transform.PhysicalToScreen(.50001), visual.EndX);
        NumericAssert.Close(24, visual.EndX - visual.StartX);
        Assert.True(frame.Layout.IsDistorted);
        Assert.False(frame.Layout.IsOverconstrained);
        Assert.Equal(new SpanRequirement(.5, .50001, 24), Assert.Single(StationRequirementBuilder.SpansFromDocument(doc)));
        Assert.Equal(4.25, StationRequirementBuilder.FromDocument(doc)[1].LeftExtent);
    }

    [Fact]
    public void RedundantSpansLeaveExistingLayoutUnchangedAndDuplicateSpansAreNotAdded()
    {
        StationRequirement[] requirements = [new(.1, 4.25, 4.25), new(.9, 4.25, 4.25)];
        var original = StationLayout.Compute(1, 72, 972, requirements);
        var redundant = StationLayout.Compute(1, 72, 972, requirements, spanRequirements: [new(.1, .9, 24)]);
        Assert.Equal(original.Stations, redundant.Stations);
        Assert.Equal(original.IsDistorted, redundant.IsDistorted);
        SpanRequirement span = new(.5, .50001, 24);
        var single = StationLayout.Compute(1, 0, 100, [], spanRequirements: [span]);
        var duplicates = StationLayout.Compute(1, 0, 100, [], spanRequirements: [span, span]);
        Assert.Equal(single.Stations, duplicates.Stations);
        Assert.False(duplicates.IsOverconstrained);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(200)]
    [InlineData(1000)]
    public void OverlappingAndNestedSpansPreserveStationMinimaOrderAndReversibility(double width)
    {
        StationRequirement[] requirements = [new(.1, 3, 5), new(.11, 7, 2), new(.12, 4, 9), new(.9, 2, 3)];
        SpanRequirement[] spans = [new(.1, .11, 24), new(.11, .12, 24), new(.1, .12, 60), new(.11, .9, 90), new(0, 1, 120)];
        var layout = StationLayout.Compute(1, 72, 72 + width, requirements, spanRequirements: spans);
        Assert.Equal(layout.Stations, StationLayout.Compute(1, 72, 72 + width, requirements.Reverse(), spanRequirements: spans.Reverse()).Stations);
        Assert.Equal(72, layout.Stations[0].ScreenX);
        Assert.Equal(72 + width, layout.Stations[^1].ScreenX);
        for (int i = 1; i < layout.Stations.Count; i++)
        {
            var previous = layout.Stations[i - 1]; var current = layout.Stations[i];
            Assert.True(current.ScreenX > previous.ScreenX);
            Assert.True(double.IsFinite(current.ScreenX));
            NumericAssert.Close(current.PhysicalX, layout.Transform.ScreenToPhysical(current.ScreenX));
            if (!layout.IsOverconstrained)
                Assert.True(current.ScreenX - previous.ScreenX >= previous.RightExtent + 8 + current.LeftExtent - 1e-10);
        }
        if (!layout.IsOverconstrained)
            Assert.All(spans, s => Assert.True(layout.Transform.PhysicalToScreen(s.EndPhysicalX)
                - layout.Transform.PhysicalToScreen(s.StartPhysicalX) >= s.MinimumScreenWidth - 1e-10));
        foreach (double x in new[] { -.1, .05, .105, .115, .5, .95, 1.1 })
            NumericAssert.Close(x, layout.Transform.ScreenToPhysical(layout.Transform.PhysicalToScreen(x)));
        Assert.Equal(width < 120, layout.IsOverconstrained);
    }

    [Fact]
    public void ActiveSpanLayoutsSatisfyIndependentRandomConstraintsWithoutChangingPhysicalValues()
    {
        var random = new Random(47);
        for (int trial = 0; trial < 100; trial++)
        {
            var requirements = Enumerable.Range(1, 9).Select(i => new StationRequirement(i / 10d,
                random.NextDouble() * 10, random.NextDouble() * 10)).ToArray();
            var spans = Enumerable.Range(0, 12).Select(_ =>
            {
                int start = random.Next(0, 9), end = random.Next(start + 1, 11);
                return new SpanRequirement(start / 10d, end / 10d, 24 + random.NextDouble() * 100);
            }).ToArray();
            var layout = StationLayout.Compute(1, 0, 1000, requirements, spanRequirements: spans);
            Assert.False(layout.IsOverconstrained);
            Assert.Equal(layout.Stations, StationLayout.Compute(1, 0, 1000, requirements.Reverse(), spanRequirements: spans.Reverse()).Stations);
            Assert.All(spans, s => Assert.True(layout.Transform.PhysicalToScreen(s.EndPhysicalX)
                - layout.Transform.PhysicalToScreen(s.StartPhysicalX) >= s.MinimumScreenWidth - 1e-9));
        }
    }

    [Theory]
    [InlineData(-.1, .5, 24)]
    [InlineData(.5, .5, 24)]
    [InlineData(.6, .5, 24)]
    [InlineData(.5, 1.1, 24)]
    [InlineData(.1, .5, -1)]
    [InlineData(.1, .5, double.PositiveInfinity)]
    [InlineData(double.NaN, .5, 24)]
    public void InvalidSpanRequirementsAreRejected(double start, double end, double minimum) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => StationLayout.Compute(1, 0, 100, [], spanRequirements: [new(start, end, minimum)]));

    [Theory]
    [InlineData(BeamPointerInteraction.DistributedLoadPlacement)]
    [InlineData(BeamPointerInteraction.DistributedLoadDrag)]
    public void PointerSnapshotsFreezeResizeAndPreviewCannotIntroduceStations(BeamPointerInteraction interaction)
    {
        var doc = Document(Load(.4, .6));
        var state = new BeamLayoutState();
        var frame = state.Update(doc, 1100, 600)!;
        state.BeginInteraction(interaction);
        Assert.Same(frame, state.Update(doc, 1600, 800));
        var preview = new DistributedLoadPreview(Length.FromMeters(.401), Length.FromMeters(.7), 100);
        var scene = BeamRenderState.Create(doc, frame, DesktopLayoutFixture.Measure, distributedPreview: preview,
            hiddenDistributedId: doc.DistributedLoads[0].Id, distributedName: "q1");
        Assert.DoesNotContain(scene.Frame.Layout.Stations, s => s.PhysicalX == .401 || s.PhysicalX == .7);
        Assert.Equal(frame.Layout.Transform.PhysicalToScreen(.401), scene.DistributedLoads[0].StartX);
        state.EndInteraction();
        var resized = state.Update(doc, 1600, 800)!;
        Assert.NotSame(frame.Layout, resized.Layout);
        Assert.Equal(frame.Layout.Stations.Select(s => s.PhysicalX), resized.Layout.Stations.Select(s => s.PhysicalX));
    }

    [Fact]
    public void NeutralHitsIncludeAllOverlappingEntitiesAndDeduplicateEndpointAndAreaHits()
    {
        var a = Load(.2, .8);
        var b = Load(.2, .7, "q2");
        var point = new EditorPointForce(Guid.NewGuid(), a.StartPosition, Force.FromNewtons(-100), "F1");
        var doc = Document(a, b).WithLoads([point]);
        var scene = Scene(doc);
        var visual = scene.DistributedLoads[0];
        var hits = scene.HitTestEntities(visual.StartX, visual.TopY + 8);
        Assert.Equal(new[] { point.Id, a.Id, b.Id }, hits);
        Assert.Equal(DistributedLoadEndpoint.Start, DistributedLoadSymbol.HitEndpoint(visual, visual.StartX, visual.TopY + 8));
        Assert.Equal(new[] { a.Id, b.Id }, scene.HitTestEntities(visual.CenterX, visual.TopY + 8));
        var label = scene.Annotations.Single(l => l.Id == a.Id);
        Assert.Equal(new[] { a.Id }, scene.HitTestEntities(label.Bounds.Center.X, label.Bounds.Center.Y));
        var editor = new EditorViewModel(doc, () => { });
        Assert.True(editor.EditDistributedLoad(a.Id));
        Assert.True(editor.BeginDistributedLoadDrag(a.Id, DistributedLoadEndpoint.Start));
    }

    [Fact]
    public void LabelDragIsPresentationOnlyAndManualLabelsRemainSelectionCandidates()
    {
        var a = Load(.2, .8);
        var b = Load(.2, .8, "q2");
        var doc = Document(a, b);
        var editor = new EditorViewModel(doc, () => { });
        var scene = Scene(doc);
        var label = scene.Annotations.Single(l => l.Id == a.Id);
        var gesture = new LabelDragGesture(label, label.Bounds.Position, null);
        gesture.Apply(editor, label.Bounds.Position + new Vector(20, -30));
        Assert.Same(doc, editor.Document);
        Assert.Equal(new AnnotationOffset(20, -30), editor.EditorPresentation.AnnotationOffsets[a.Id]);
        Assert.False(gesture.OpenOnClick(editor));
        var click = new LabelDragGesture(label, label.Bounds.Position, null);
        Assert.True(click.OpenOnClick(editor));
        var draft = editor.DistributedLoadDraft!;
        draft.StartText = "unfinished";
        draft.IntensityText = "0";
        gesture.Cancel(editor);
        Assert.Same(draft, editor.DistributedLoadDraft);
        Assert.Equal("unfinished", draft.StartText);
        Assert.Equal("0", draft.IntensityText);
        Assert.DoesNotContain(a.Id, editor.EditorPresentation.AnnotationOffsets.Keys);
        var other = scene.Annotations.Single(l => l.Id == b.Id);
        var offset = new AnnotationOffset(label.Bounds.X - other.AutoBounds.X, label.Bounds.Y - other.AutoBounds.Y);
        var overlapping = BeamRenderState.Create(doc, scene.Frame, DesktopLayoutFixture.Measure,
            presentation: new EditorPresentationState().WithOffset(a.Id, new(0, 0)).WithOffset(b.Id, offset));
        Assert.Equal(new[] { a.Id, b.Id }, overlapping.HitTestEntities(label.Bounds.Center.X, label.Bounds.Center.Y));
    }
}
