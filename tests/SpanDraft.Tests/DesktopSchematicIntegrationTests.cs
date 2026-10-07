using Avalonia;
using SpanDraft.Analysis;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Controls;
using SpanDraft.Desktop.Layout;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using Xunit;

namespace SpanDraft.Tests;

public sealed class DesktopSchematicIntegrationTests
{
    private static Length Mm(double x) => Length.FromMillimeters(x);
    private static EditorPointLoad Load(double x, double value, PointLoadKind kind, string name) => EditorPointLoad.Create(Guid.NewGuid(), Mm(x), kind, value, name);
    private static EditorDocument Document(EditorPointLoad[]? loads = null, EditorSupport[]? supports = null, double length = 1000) =>
        new(Mm(length), ProjectTemplates.Material, ProjectTemplates.Section, supports, loads);
    private static BeamLayoutFrame Frame(EditorDocument document, double width = 1100, double height = 600) => new BeamLayoutState().Update(document, width, height)!;
    private static BeamRenderState Scene(EditorDocument document, BeamLayoutFrame frame) => BeamRenderState.Create(document, frame, DesktopLayoutFixture.Measure);

    [Theory]
    [InlineData(SupportType.Fixed)]
    [InlineData(SupportType.Pinned)]
    [InlineData(SupportType.Roller)]
    public void MinimumPaneKeepsOneFixedLowerRowAndAdditionalLoadLabelsGrowOnlyTheUpperArea(SupportType type)
    {
        var support = new EditorSupport(Guid.NewGuid(), Mm(0), type, "A");
        double previousHeight = 0;
        foreach (int count in new[] { 1, 2, 6 })
        {
            var document = Document(Enumerable.Range(1, count)
                .Select(i => Load(500, -1000, PointLoadKind.Force, "F" + i)).ToArray(), [support]);
            var state = new BeamLayoutState();
            var initial = state.Update(document, 1100, SchematicMetrics.MinimumBeamPaneHeight)!;
            var request = Scene(document, initial);
            var frame = state.Update(document, 1100, request.MinimumPaneHeight)!;
            var scene = Scene(document, frame);
            Assert.Equal(56, scene.RequiredBelowBeamSpace);
            Assert.Equal(56, frame.Viewport.Height - frame.Viewport.BeamY);
            Assert.Equal(8, frame.Viewport.Height + CoordinateAxisLayout.AxisY -
                scene.Annotations.Where(a => a.IsSupport).Max(a => a.Bounds.Bottom));
            Assert.Same(initial.Layout, frame.Layout);
            Assert.Equal(request.MinimumPaneHeight, scene.MinimumPaneHeight);
            Assert.All(scene.Annotations, label =>
            {
                Assert.True(label.Bounds.Top >= 0);
                Assert.True(label.Bounds.Bottom <= frame.Viewport.Height);
            });
            if (count == 1)
            {
                Assert.Equal(180, frame.Viewport.Height);
                Assert.Equal(124, frame.Viewport.BeamY);
            }
            else Assert.True(frame.Viewport.Height > previousHeight);
            previousHeight = frame.Viewport.Height;

            // The committed pane already has room for a placement preview row.
            var preview = BeamRenderState.Create(document, frame, DesktopLayoutFixture.Measure,
                loadPreview: new(Mm(500), PointLoadKind.Force, -1000), loadName: "Next");
            Assert.Same(frame, preview.Frame);
            Assert.All(preview.Annotations, label =>
            {
                Assert.True(label.Bounds.Top >= 0);
                Assert.True(label.Bounds.Bottom <= frame.Viewport.Height);
            });
            Assert.Equal(scene.RequiredBelowBeamSpace, preview.RequiredBelowBeamSpace);
        }
    }

    [Theory]
    [InlineData(13)]
    [InlineData(16)]
    [InlineData(23)]
    public void OverlappingSupportLabelsKeepRowSpacingWithoutTrailingPadding(double measuredHeight)
    {
        var document = Document(supports:
        [
            new(Guid.NewGuid(), Mm(500), SupportType.Pinned, "Left support"),
            new(Guid.NewGuid(), Mm(501), SupportType.Roller, "Right support")
        ]);
        var state = new BeamLayoutState();
        var initial = state.Update(document, 1100, 180)!;
        Size Measure(string _) => new(160, measuredHeight);
        var request = BeamRenderState.Create(document, initial, Measure);
        var frame = state.Update(document, 1100, request.MinimumPaneHeight, request.RequiredBelowBeamSpace)!;
        var scene = BeamRenderState.Create(document, frame, Measure);
        var labels = scene.Annotations.OrderBy(a => a.Bounds.Top).ToArray();
        Assert.Equal(2, labels.Length);
        double rowHeight = Math.Max(16, measuredHeight);
        Assert.Equal(rowHeight + 8, labels[1].Bounds.Top - labels[0].Bounds.Top);
        Assert.Equal(40 + 2 * rowHeight + 8, scene.RequiredBelowBeamSpace);
        Assert.Equal(rowHeight - measuredHeight,
            frame.Viewport.Height - labels[1].Bounds.Bottom, 6);
        Assert.InRange(frame.Viewport.Height + CoordinateAxisLayout.AxisY - labels[1].Bounds.Bottom, 8, 11);
        Assert.Equal(request.MinimumPaneHeight, scene.MinimumPaneHeight);
        Assert.Same(initial.Layout, frame.Layout);
    }

    [Fact]
    public void ChangingOnlyTheLowerOverflowReserveReusesStationsAndFreezesDuringInteractions()
    {
        var document = Document([Load(500, -1000, PointLoadKind.Force, "F1")]);
        var state = new BeamLayoutState();
        var initial = state.Update(document, 1100, 600)!;
        state.BeginInteraction(BeamPointerInteraction.LabelDrag);
        Assert.Same(initial, state.Update(document, 1100, 600, 300));
        state.EndInteraction();
        var overflow = state.Update(document, 1100, 600, 300)!;
        Assert.Same(initial.Layout, overflow.Layout);
        Assert.Equal(300, overflow.Viewport.BeamY);
        var reset = state.Update(document, 1100, 600)!;
        Assert.Same(initial.Layout, reset.Layout);
        Assert.Equal(initial.Viewport, reset.Viewport);
    }

    [Fact]
    public void BeamAndAxisShareOneLayoutAndExactlyEqualStationAnchors()
    {
        var support = new EditorSupport(Guid.NewGuid(), Mm(500), SupportType.Pinned, "A");
        var document = Document([Load(500, -1000, PointLoadKind.Force, "F1"), Load(500, 100, PointLoadKind.Moment, "M1"),
            Load(501, 100, PointLoadKind.Force, "F2")], [support]);
        var frame = Frame(document);
        var scene = Scene(document, frame);
        var axis = CoordinateAxisLayout.Create(frame.Layout, frame.Viewport.Width, DesktopLayoutFixture.Measure);
        Assert.Same(frame.Layout, axis.StationLayout);
        Assert.Equal(new[] { 0d, .5, .501, 1 }, axis.Labels.OrderBy(l => l.PhysicalX).Select(l => l.PhysicalX));
        Assert.Equal(4, frame.Layout.Stations.Count);
        double x = frame.Layout.Transform.PhysicalToScreen(.5);
        Assert.Equal(x, scene.Supports[0].X);
        Assert.All(scene.Loads.Take(2), load => Assert.Equal(x, load.X));
        Assert.True(scene.Loads[2].X > x);
        Assert.True(frame.Layout.IsDistorted);
        Assert.Equal(support.Id, SupportSymbol.HitTest(document.Supports, frame.Layout.Transform, frame.Viewport.BeamY, x, frame.Viewport.BeamY + 25));
        Assert.Equal(document.Loads[2].Id, PointLoadSymbol.HitTest(scene.Loads, scene.Loads[2].X, frame.Viewport.BeamY - 30));
    }

    [Fact]
    public void LayoutCacheReusesHorizontalResultAcrossHeightAndTransientChanges()
    {
        var document = Document([Load(400, 100, PointLoadKind.Force, "F1")]);
        var state = new BeamLayoutState();
        var initial = state.Update(document, 1100, 600)!;
        Assert.Same(initial, state.Update(document, 1100, 600));
        var taller = state.Update(document, 1100, 800)!;
        Assert.Same(initial.Layout, taller.Layout);
        Assert.Equal(800 - SchematicMetrics.BelowBeamSpace, taller.Viewport.BeamY);
        var preview = new PointLoadPreview(Mm(401), PointLoadKind.Force, -100);
        var scene = BeamRenderState.Create(document, taller, DesktopLayoutFixture.Measure, loadPreview: preview, loadName: "F2");
        Assert.Equal(3, scene.Frame.Layout.Stations.Count);
        Assert.DoesNotContain(scene.Frame.Layout.Stations, s => s.PhysicalX == .401);
        Assert.Equal(taller.Layout.Transform.PhysicalToScreen(.401), scene.Loads[^1].X);
        Assert.Same(taller.Layout, state.Update(document, 1100, 800)!.Layout);
    }

    [Fact]
    public void ResizeReflowsCommittedLayoutWithoutChangingEntities()
    {
        var document = Document([Load(500, 100, PointLoadKind.Force, "F1"), Load(500.1, 100, PointLoadKind.Moment, "M1")]);
        var state = new BeamLayoutState();
        var old = state.Update(document, 1100, 600)!;
        var resized = state.Update(document, 1500, 700)!;
        Assert.NotSame(old.Layout, resized.Layout);
        Assert.Equal(old.Layout.Stations.Select(s => s.PhysicalX), resized.Layout.Stations.Select(s => s.PhysicalX));
        Assert.True(resized.Layout.Stations[^1].ScreenX > old.Layout.Stations[^1].ScreenX);
        Assert.Equal(Mm(500.1), document.Loads[1].Position);
        Assert.Same(resized.Layout, CoordinateAxisLayout.Create(resized.Layout, 1500, DesktopLayoutFixture.Measure).StationLayout);
    }

    [Theory]
    [InlineData(BeamPointerInteraction.SupportPlacement)]
    [InlineData(BeamPointerInteraction.LoadPlacement)]
    [InlineData(BeamPointerInteraction.SupportDrag)]
    [InlineData(BeamPointerInteraction.LoadDrag)]
    public void EachPointerPathFreezesResizeOnlyUntilGestureEnds(BeamPointerInteraction interaction)
    {
        var document = Document([Load(500, 100, PointLoadKind.Force, "F1"), Load(501, 100, PointLoadKind.Force, "F2")]);
        var state = new BeamLayoutState();
        var original = state.Update(document, 1100, 600)!;
        Assert.Same(original, state.BeginInteraction(interaction));
        double screen = original.Layout.Transform.PhysicalToScreen(.65);
        var snap = SupportSnap.Placement(original.Layout.Transform, original.Viewport.BeamY, screen, original.Viewport.BeamY);
        var frozen = state.Update(document, 1500, 800)!;
        Assert.Same(original, frozen);
        Assert.Equal(snap, SupportSnap.Placement(frozen.Layout.Transform, frozen.Viewport.BeamY, screen, frozen.Viewport.BeamY));
        state.EndInteraction();
        Assert.Null(state.Snapshot);
        Assert.Null(state.Interaction);
        var resized = state.Update(document, 1500, 800)!;
        Assert.NotSame(original.Layout, resized.Layout);
        Assert.Equal(800 - SchematicMetrics.BelowBeamSpace, resized.Viewport.BeamY);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PlacementDraftResizesAfterReleaseAndCommitIntroducesStation(bool load)
    {
        int calls = 0;
        var editor = new EditorViewModel(Document(), () => { }, beam => { calls++; return BeamAnalysis.Analyze(beam); });
        var state = new BeamLayoutState();
        var frame = state.Update(editor.Document, 1100, 600)!;
        state.BeginInteraction(load ? BeamPointerInteraction.LoadPlacement : BeamPointerInteraction.SupportPlacement);
        if (load) { editor.ToggleLoadTool(PointLoadKind.Force); editor.HoverLoadPlacement(Mm(400)); Assert.True(editor.PlaceLoad()); }
        else { editor.ToggleSupportTool(SupportType.Pinned); editor.HoverPlacement(Mm(400)); Assert.True(editor.PlaceSupport()); }
        Assert.Same(frame, state.Update(editor.Document, 1500, 800));
        state.EndInteraction();
        var draftFrame = state.Update(editor.Document, 1500, 800)!;
        Assert.NotSame(frame.Layout, draftFrame.Layout);
        Assert.Equal(2, draftFrame.Layout.Stations.Count);
        Assert.Equal(Mm(400), load ? editor.LoadDraft!.CanvasPosition : editor.SupportDraft!.CanvasPosition);
        Assert.Equal(1, calls);
        if (load) Assert.True(editor.ConfirmLoad()); else Assert.True(editor.ConfirmSupport());
        var committed = state.Update(editor.Document, 1500, 800)!;
        Assert.Equal(3, committed.Layout.Stations.Count);
        Assert.Contains(committed.Layout.Stations, s => s.PhysicalX == .4);
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DragRetainsSnapshotGrabOffsetAndDraftPhysicsAcrossResize(bool load)
    {
        var support = new EditorSupport(Guid.NewGuid(), Mm(400), SupportType.Pinned, "A");
        var pointLoad = Load(400, 100, PointLoadKind.Force, "F1");
        var document = Document([pointLoad], [support]);
        var editor = new EditorViewModel(document, () => { });
        var state = new BeamLayoutState();
        var initial = state.Update(document, 1100, 600)!;
        state.BeginInteraction(load ? BeamPointerInteraction.LoadDrag : BeamPointerInteraction.SupportDrag);
        double press = initial.Layout.Transform.PhysicalToScreen(.4) + 7;
        double target = initial.Layout.Transform.PhysicalToScreen(.65) + 7;
        var gesture = new SupportDragGesture(load ? pointLoad.Id : support.Id, Mm(400), press, initial.Layout.Transform);
        Assert.Null(gesture.Update(press + 3, 300, 600));
        Assert.Same(initial, state.Update(document, 1500, 800));
        var position = gesture.Update(target, 300, 600);
        Assert.Equal(Mm(650), position);
        if (load)
        {
            Assert.True(editor.BeginLoadDrag(pointLoad.Id)); editor.UpdateLoadDrag(position); Assert.True(editor.EndLoadDrag());
        }
        else
        {
            Assert.True(editor.BeginSupportDrag(support.Id)); editor.UpdateSupportDrag(position); Assert.True(editor.EndSupportDrag());
        }
        state.EndInteraction();
        var resized = state.Update(document, 1500, 800)!;
        Assert.Equal(Mm(650), load ? editor.LoadDraft!.CanvasPosition : editor.SupportDraft!.CanvasPosition);
        var scene = BeamRenderState.Create(document, resized, DesktopLayoutFixture.Measure, editor.Preview, editor.HiddenSupportId,
            editor.SupportPreviewName, editor.LoadPreview, editor.HiddenLoadId, editor.LoadPreviewName);
        double previewX = load ? scene.Loads.Single(l => l.IsPreview).X : scene.Supports.Single(s => s.IsPreview).X;
        Assert.Equal(resized.Layout.Transform.PhysicalToScreen(.65), previewX);
        Assert.Same(document, editor.Document);
        Assert.DoesNotContain(resized.Layout.Stations, s => s.PhysicalX == .65);
    }

    [Fact]
    public void LoadGestureUsesItsOwnImmutableSnapshotAndClampsFractionalEndpoints()
    {
        var frame = Frame(Document(length: 1000.5));
        double press = frame.Layout.Transform.PhysicalToScreen(.4) + 8;
        var gesture = new PointLoadDragGesture(Guid.NewGuid(), Mm(400), press, frame.Layout.Transform);
        Assert.Same(frame.Layout.Transform, gesture.Snapshot);
        Assert.Equal(Mm(1000.5), gesture.Update(100000, 300, 600));
        Assert.Equal(Mm(0), gesture.Update(-100000, 300, 600));
        Assert.Equal(Mm(650), gesture.Update(frame.Layout.Transform.PhysicalToScreen(.65) + 8, 300, 600));
        Assert.Null(gesture.Update(press + 100, 601, 600));
    }

    [Fact]
    public void HoverPreviewCannotFeedBackIntoMappingOrAxisTicks()
    {
        var document = Document([Load(500, 100, PointLoadKind.Force, "F1"), Load(501, 100, PointLoadKind.Force, "F2")]);
        var state = new BeamLayoutState();
        var frame = state.Update(document, 1100, 600)!;
        state.BeginInteraction(BeamPointerInteraction.LoadPlacement);
        double pointer = frame.Layout.Transform.PhysicalToScreen(.7);
        for (int i = 0; i < 20; i++)
        {
            var position = SupportSnap.Placement(frame.Layout.Transform, frame.Viewport.BeamY, pointer, frame.Viewport.BeamY)!.Value;
            var preview = new PointLoadPreview(position, PointLoadKind.Moment, 100);
            var scene = BeamRenderState.Create(document, state.Update(document, 1100 + i, 600)!, DesktopLayoutFixture.Measure,
                loadPreview: preview, loadName: "M1");
            Assert.Equal(Mm(700), position);
            Assert.Same(frame.Layout.Transform, scene.Frame.Layout.Transform);
            Assert.Equal(pointer, scene.Loads[^1].X);
            Assert.Equal(4, scene.Frame.Layout.Stations.Count);
        }
    }

    [Fact]
    public void CancelRestoresSharedStationAndLaterCommitReflowsBothPanes()
    {
        var first = Load(500, 100, PointLoadKind.Force, "F1");
        var second = Load(500, -100, PointLoadKind.Force, "F2");
        var editor = new EditorViewModel(Document([first, second]), () => { });
        var state = new BeamLayoutState();
        var initial = state.Update(editor.Document, 1100, 600)!;
        Assert.Single(Scene(editor.Document, initial).Glyphs);
        editor.BeginLoadDrag(first.Id); editor.UpdateLoadDrag(Mm(501)); editor.EndLoadDrag();
        editor.CancelLoadInteraction();
        Assert.Same(initial.Layout, state.Update(editor.Document, 1100, 600)!.Layout);
        editor.BeginLoadDrag(first.Id); editor.UpdateLoadDrag(Mm(501)); editor.EndLoadDrag();
        Assert.True(editor.ConfirmLoad());
        var changed = state.Update(editor.Document, 1100, 600)!;
        Assert.True(changed.Layout.IsDistorted);
        Assert.Equal(2, Scene(editor.Document, changed).Glyphs.Count);
        Assert.Same(changed.Layout, CoordinateAxisLayout.Create(changed.Layout, 1100, DesktopLayoutFixture.Measure).StationLayout);
    }

    [Theory]
    [InlineData(100, false, true)]
    [InlineData(-100, true, false)]
    [InlineData(0, false, false)]
    public void IndividualForceHasCorrectDirectionNameAndBeamAttachment(double value, bool negative, bool positive)
    {
        var document = Document([Load(500, value, PointLoadKind.Force, "F7")]);
        var scene = Scene(document, Frame(document));
        var glyph = Assert.Single(scene.Glyphs);
        Assert.Equal(negative, glyph.Negative); Assert.Equal(positive, glyph.Positive);
        Assert.Equal(scene.Frame.Viewport.BeamY, glyph.Y);
        Assert.True(glyph.Bounds.Top < glyph.Y);
        var top = new Point(glyph.X, glyph.Y + PointLoadSymbol.ForceTip(1).Y);
        var bottom = new Point(glyph.X, glyph.Y + PointLoadSymbol.ForceTip(-1).Y);
        Assert.True(glyph.Bounds.Contains(top));
        Assert.True(glyph.Bounds.Contains(bottom));
        Assert.Equal(document.Loads[0].Id, PointLoadSymbol.HitTest(scene.Loads, top.X, top.Y));
        Assert.Equal(document.Loads[0].Id, PointLoadSymbol.HitTest(scene.Loads, bottom.X, bottom.Y));
        var label = Assert.Single(scene.Annotations);
        Assert.True(label.Bounds.Bottom < top.Y - SchematicMetrics.SymbolStrokeWidth / 2);
        Assert.Equal("F7 = " + UiNumbers.Compact(value) + " N", label.Text);
        Assert.Equal(document.Loads[0].Id, PointLoadSymbol.ResolveEntity(glyph));
        Assert.True(SchematicMetrics.ForceHeight > 0);
    }

    [Theory]
    [InlineData(100, 200, false, true)]
    [InlineData(-100, -200, true, false)]
    [InlineData(100, -100, true, true)]
    [InlineData(0, 0, false, false)]
    [InlineData(0, 100, false, true)]
    public void SharedForceUsesSignUnionWithoutResultant(double first, double second, bool negative, bool positive)
    {
        var document = Document([Load(500, first, PointLoadKind.Force, "F1"), Load(500, second, PointLoadKind.Force, "F2")]);
        var scene = Scene(document, Frame(document));
        var glyph = Assert.Single(scene.Glyphs);
        Assert.Equal(negative, glyph.Negative); Assert.Equal(positive, glyph.Positive);
        Assert.Equal(2, glyph.Entities.Count); Assert.Equal(2, scene.Annotations.Count);
        Assert.Null(PointLoadSymbol.ResolveEntity(glyph));
        Assert.Equal(document.Loads[1].Id, PointLoadSymbol.ResolveEntity(glyph, document.Loads[1].Id));
        Assert.Null(PointLoadSymbol.ResolveEntity(glyph, Guid.NewGuid()));
        Assert.Null(PointLoadSymbol.HitTest(scene.Loads, glyph.X, glyph.Y - 30));
        Assert.Equal(document.Loads[0].Id, PointLoadSymbol.HitTest(scene.Loads, glyph.X, glyph.Y - 30, document.Loads[0].Id));
        Assert.Equal(first, document.Loads[0].ToCore() is SpanDraft.Core.Loads.PointForce force ? force.Force.Newtons : double.NaN);
    }

    [Theory]
    [InlineData(100, 0, false, true)]
    [InlineData(-100, 0, true, false)]
    [InlineData(100, 200, false, true)]
    [InlineData(-100, -200, true, false)]
    [InlineData(100, -100, true, true)]
    [InlineData(0, -100, true, false)]
    [InlineData(0, 0, false, false)]
    public void MomentCenterAndTerminalArrowheadsFollowCoreSigns(double first, double second, bool negative, bool positive)
    {
        var document = Document([Load(500, first, PointLoadKind.Moment, "M1"), Load(500, second, PointLoadKind.Moment, "M2")]);
        var scene = Scene(document, Frame(document));
        var glyph = Assert.Single(scene.Glyphs);
        Assert.Equal(negative, glyph.Negative); Assert.Equal(positive, glyph.Positive);
        Assert.Equal(scene.Frame.Viewport.BeamY, glyph.Y);
        Assert.Equal(-2.6047226650, PointLoadSymbol.MomentTip(true).X, 10);
        Assert.Equal(14.7721162952, PointLoadSymbol.MomentTip(true).Y, 10);
        Assert.Equal(-PointLoadSymbol.MomentTip(true).X, PointLoadSymbol.MomentTip(false).X);
        Assert.Equal(PointLoadSymbol.MomentTip(true).Y, PointLoadSymbol.MomentTip(false).Y);
        Assert.Equal("M1 = " + UiNumbers.Compact(first) + " Nm", scene.Annotations.Single(a => a.Id == document.Loads[0].Id).Text);
        Assert.Equal(2, document.ToBeamModel().Loads.Count);
        Assert.Equal(2, glyph.Entities.Count);
        Assert.Equal(2, scene.Annotations.Count);
        Assert.Null(PointLoadSymbol.ResolveEntity(glyph));
        Assert.True(SchematicMetrics.MomentRadius + SchematicMetrics.MomentArrowHalfWidth <= SchematicMetrics.PointLoadHalfSize);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(10)]
    public void ObjectLabelsHaveUniqueMeasuredBoundsAndDeterministicOrder(int count)
    {
        var document = Document(Enumerable.Range(0, count).Select(i => Load(500, i, PointLoadKind.Force, "Force " + i)).ToArray());
        var scene = Scene(document, Frame(document, height: 1000));
        Assert.Equal(count, scene.Annotations.Count);
        for (int i = 0; i < scene.Annotations.Count; i++)
        {
            var label = scene.Annotations[i];
            Assert.Equal(document.Loads[i].Id, label.Id);
            Assert.Equal(label.Id, scene.HitTestLabel(label.Bounds.Center.X, label.Bounds.Center.Y)!.Id);
            Assert.Equal(DesktopLayoutFixture.Measure(label.Text).Width, label.Bounds.Width);
            Assert.DoesNotContain(scene.Annotations.Skip(i + 1), other => label.Bounds.Intersects(other.Bounds));
        }
        Assert.Equal(scene.Annotations, Scene(document, scene.Frame).Annotations);
    }

    [Theory]
    [InlineData(1100)]
    [InlineData(144)]
    [InlineData(80)]
    [InlineData(1)]
    public void NarrowPaneHasFiniteStrictlyOrderedBestEffortLayout(double width)
    {
        var document = Document(Enumerable.Range(1, 30).Select(i => Load(i, 100, PointLoadKind.Moment, "M" + i)).ToArray());
        var frame = Frame(document, width);
        Assert.True(frame.Layout.IsOverconstrained);
        Assert.All(frame.Layout.Stations, station => Assert.True(double.IsFinite(station.ScreenX)));
        Assert.All(frame.Layout.Stations.Zip(frame.Layout.Stations.Skip(1)), pair => Assert.True(pair.First.ScreenX < pair.Second.ScreenX));
        Assert.Equal(0, frame.Layout.Stations[0].PhysicalX);
        Assert.Equal(1, frame.Layout.Stations[^1].PhysicalX);
    }

    [Fact]
    public void UnarrangedPaneHasNoLayoutAndValidArrangeRecovers()
    {
        var state = new BeamLayoutState();
        var document = Document();
        Assert.Null(state.Update(document, 0, 600)); Assert.Null(state.Update(document, 1100, 0));
        Assert.Null(state.Update(document, double.NaN, 600));
        Assert.Null(state.BeginInteraction(BeamPointerInteraction.SupportPlacement));
        Assert.NotNull(state.Update(document, 1100, 600));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void AxisUsesPackerForOneTwoOrThreeMeasuredLanes(int lanes)
    {
        var layout = StationLayout.Compute(1, 100, 500, [new(.4, 0, 0), new(.5, 0, 0), new(.6, 0, 0)]);
        double width = lanes == 1 ? 20 : lanes == 2 ? 50 : 90;
        var axis = CoordinateAxisLayout.Create(layout, 600, _ => new Size(width, 16));
        Assert.Equal(lanes, axis.Packing.LaneCount);
        Assert.Equal(SchematicMetrics.AxisBaseHeight + lanes * 16 + SchematicMetrics.AxisVerticalPadding, axis.PaneHeight);
        Assert.Equal(5, axis.Labels.Count);
        Assert.DoesNotContain(axis.Labels, l => l.Text == "x [mm]");
    }

    [Fact]
    public void DistortionHintHeightFollowsOnlyLayoutFlagAndOverconstraintCanStayLinear()
    {
        var linear = StationLayout.Compute(1, 0, 1000, []);
        var dense = StationLayout.Compute(1, 0, 100, [new(.001, 20, 20)]);
        var constrainedLinear = StationLayout.Compute(1, 0, 1, []);
        Assert.False(linear.IsDistorted); Assert.True(dense.IsDistorted);
        Assert.True(constrainedLinear.IsOverconstrained); Assert.False(constrainedLinear.IsDistorted);
        var plain = CoordinateAxisLayout.Create(linear, 1100, DesktopLayoutFixture.Measure);
        var schem = CoordinateAxisLayout.Create(dense, 1100, DesktopLayoutFixture.Measure);
        Assert.Equal(plain.Packing.PaneHeight, plain.PaneHeight);
        Assert.Equal(schem.Packing.PaneHeight + schem.LineHeight + 4, schem.PaneHeight);
        var constrained = CoordinateAxisLayout.Create(constrainedLinear, 100, DesktopLayoutFixture.Measure);
        Assert.Equal(constrained.Packing.PaneHeight, constrained.PaneHeight);
    }

    [Fact]
    public void AxisEditingReservesFixedBoundsIndependentOfBufferAndKeepsCommittedEndpoint()
    {
        var document = Document([Load(850, -100, PointLoadKind.Force, "F1")]);
        var frame = Frame(document);
        var normal = CoordinateAxisLayout.Create(frame.Layout, 1100, DesktopLayoutFixture.Measure);
        var edit = CoordinateAxisLayout.Create(frame.Layout, 1100, DesktopLayoutFixture.Measure, editing: true);
        var end = edit.Labels.Single(l => l.Role == AxisEndpointRole.End);
        Assert.Equal(1, end.PhysicalX); Assert.Equal("1000", end.Text);
        Assert.Equal(SchematicMetrics.LengthInputWidth + 8 + DesktopLayoutFixture.Measure("mm").Width, end.Bounds.Width);
        Assert.Equal(SchematicMetrics.LengthInputHeight, edit.LineHeight);
        Assert.True(edit.PaneHeight > normal.PaneHeight);
        var editor = new EditorViewModel(document, () => { });
        editor.DimensionLength.Begin(); editor.DimensionLength.Text = "700";
        Assert.False(editor.DimensionLength.Confirm());
        Assert.Same(document, editor.Document);
        Assert.Equal(frame.Layout.Transform.PhysicalToScreen(.7), BeamConflictGeometry.Create(frame.Layout.Transform, editor.ConstraintConflict).EndX);
        Assert.Equal(end, edit.Labels.Single(l => l.Role == AxisEndpointRole.End));
        editor.DimensionLength.LoseFocus();
        Assert.Null(editor.ConstraintConflict); Assert.False(editor.DimensionLength.HasError);
        Assert.Equal("1000", editor.DimensionLength.Text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NamingPreviewAndCommittedLabelsShareTransactionalNamesWithoutAnalysisOnRename(bool load)
    {
        var support = new EditorSupport(Guid.NewGuid(), Mm(500), SupportType.Pinned, "A");
        var pointLoad = Load(500, -100, PointLoadKind.Force, "F1");
        int calls = 0;
        var editor = new EditorViewModel(Document([pointLoad], [support]), () => { }, beam => { calls++; return BeamAnalysis.Analyze(beam); });
        var before = editor.Document;
        if (load) { editor.EditLoad(pointLoad.Id); editor.LoadDraft!.NameText = "Motor"; }
        else { editor.EditSupport(support.Id); editor.SupportDraft!.NameText = "Bearing"; }
        var scene = BeamRenderState.Create(before, Frame(before), DesktopLayoutFixture.Measure, editor.Preview, editor.HiddenSupportId,
            editor.SupportPreviewName, editor.LoadPreview, editor.HiddenLoadId, editor.LoadPreviewName);
        Assert.Contains(scene.Annotations, a => a.IsPreview && a.Text == (load ? "Motor = -100 N" : "Bearing"));
        Assert.Equal(1, calls);
        if (load) Assert.True(editor.ConfirmLoad()); else Assert.True(editor.ConfirmSupport());
        Assert.Equal(1, calls);
        Assert.Contains(Scene(editor.Document, Frame(editor.Document)).Annotations, a => a.Text == (load ? "Motor = -100 N" : "Bearing"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NamingCancelRestoresOriginalVisibleNameAndAutoCandidatesRemainAvailable(bool load)
    {
        var editor = new EditorViewModel(Document(), () => { });
        if (load) { editor.ToggleLoadTool(PointLoadKind.Force); editor.HoverLoadPlacement(Mm(400)); editor.PlaceLoad(); editor.LoadDraft!.NameText = "Changed"; }
        else { editor.ToggleSupportTool(SupportType.Pinned); editor.HoverPlacement(Mm(400)); editor.PlaceSupport(); editor.SupportDraft!.NameText = "Changed"; }
        Assert.Equal("Changed", load ? editor.LoadPreviewName : editor.SupportPreviewName);
        editor.CancelEditorInteraction();
        Assert.Empty(editor.Document.NamedEntities);
        Assert.Equal(load ? "F1" : "A", EntityNaming.Peek(editor.Document, load ? AutoNameKind.Force : AutoNameKind.Support).Name);
    }
}
