using Avalonia;
using SpanDraft.Analysis;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Controls;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using Xunit;

namespace SpanDraft.Tests;

public sealed class DesktopAnnotationTests
{
    private static Length Mm(double x) => Length.FromMillimeters(x);
    private static EditorPointLoad Load(string name, double x = 500, PointLoadKind kind = PointLoadKind.Force) =>
        EditorPointLoad.Create(Guid.NewGuid(), Mm(x), kind, 100, name);
    private static EditorDocument Document(params EditorPointLoad[] loads) => DesktopLayoutFixture.Document().WithLoads(loads);
    private static BeamLayoutFrame Frame(EditorDocument document, double width = 1100, double height = 600) =>
        new BeamLayoutState().Update(document, width, height)!;
    private static BeamRenderState Scene(EditorDocument document, BeamLayoutFrame frame, EditorPresentationState? state = null) =>
        BeamRenderState.Create(document, frame, DesktopLayoutFixture.Measure, presentation: state);
    private static EntityAnnotation Label(BeamRenderState scene, Guid id) => scene.Annotations.Single(a => a.Id == id);
    private static EditorPresentationState Offset(Guid id, double dx, double dy) => new EditorPresentationState().WithOffset(id, new(dx, dy));
    private static void AssertOffset(EntityAnnotation label, AnnotationOffset offset)
    {
        Assert.True(label.IsManual);
        Assert.Equal(label.AutoBounds.Translate(new Vector(offset.Dx, offset.Dy)), label.Bounds);
    }

    [Fact]
    public void MissingOffsetPreservesAutomaticBoundsAndZeroOffsetIsExplicitlyManual()
    {
        var load = Load("F1"); var document = Document(load); var frame = Frame(document);
        var automatic = Label(Scene(document, frame), load.Id);
        Assert.False(automatic.IsManual); Assert.Equal(automatic.AutoBounds, automatic.Bounds);
        var zero = Label(Scene(document, frame, Offset(load.Id, 0, 0)), load.Id);
        AssertOffset(zero, new(0, 0)); Assert.Equal(automatic.AutoBounds, zero.AutoBounds);
    }

    [Theory]
    [InlineData(-2000, -700)]
    [InlineData(2000, 700)]
    [InlineData(12.5, -3.25)]
    public void ManualBoundsTranslateExactlyWithoutClamping(double dx, double dy)
    {
        var load = Load("F1"); var document = Document(load); var frame = Frame(document);
        var state = Offset(load.Id, dx, dy);
        var label = Label(Scene(document, frame, state), load.Id);
        AssertOffset(label, new(dx, dy));
        Assert.Equal(Label(Scene(document, frame), load.Id).AutoBounds, label.AutoBounds);
        Assert.Equal(new AnnotationOffset(dx, dy), state.AnnotationOffsets[load.Id]);
    }

    [Fact]
    public void AutoReferencesAreIndependentOfEveryManualObstacle()
    {
        var first = Load("F1"); var second = Load("F2"); var document = Document(first, second); var frame = Frame(document);
        var baseline = Scene(document, frame);
        var state = Offset(first.Id, 0, -24);
        var scene = Scene(document, frame, state);
        Assert.Equal(baseline.Annotations.Select(a => a.AutoBounds), scene.Annotations.Select(a => a.AutoBounds));
        Assert.NotEqual(Label(baseline, second.Id).Bounds, Label(scene, second.Id).Bounds);
        Assert.False(Label(scene, first.Id).Bounds.Intersects(Label(scene, second.Id).Bounds));
        Assert.Equal(scene.Annotations, Scene(document, frame, state).Annotations);
    }

    [Fact]
    public void ManualSupportCanObstructAutomaticLoadAndViceVersa()
    {
        var support = new EditorSupport(Guid.NewGuid(), Mm(500), SupportType.Pinned, "Support");
        var load = Load("F1"); var document = Document(load).WithSupports([support]); var frame = Frame(document);
        var baseline = Scene(document, frame);
        foreach (var manualId in new[] { support.Id, load.Id })
        {
            var autoId = manualId == support.Id ? load.Id : support.Id;
            var manual = Label(baseline, manualId); var automatic = Label(baseline, autoId);
            var state = Offset(manualId, automatic.Bounds.X - manual.AutoBounds.X, automatic.Bounds.Y - manual.AutoBounds.Y);
            var scene = Scene(document, frame, state);
            Assert.False(Label(scene, manualId).Bounds.Intersects(Label(scene, autoId).Bounds));
            Assert.Equal(automatic.AutoBounds, Label(scene, autoId).AutoBounds);
            Assert.NotEqual(automatic.Bounds, Label(scene, autoId).Bounds);
        }
    }

    [Fact]
    public void ManualLabelsMayOverlapAndHitTestingFollowsPaintOrder()
    {
        var first = Load("F1"); var second = Load("F2"); var document = Document(first, second); var frame = Frame(document);
        var baseline = Scene(document, frame);
        var firstLabel = Label(baseline, first.Id); var secondLabel = Label(baseline, second.Id);
        var state = Offset(first.Id, 20, 10).WithOffset(second.Id, new(
            firstLabel.AutoBounds.X + 20 - secondLabel.AutoBounds.X,
            firstLabel.AutoBounds.Y + 10 - secondLabel.AutoBounds.Y));
        var scene = Scene(document, frame, state);
        Assert.Equal(Label(scene, first.Id).Bounds, Label(scene, second.Id).Bounds);
        var point = Label(scene, first.Id).Bounds.Center;
        Assert.Equal(second.Id, scene.HitTestLabel(point.X, point.Y)!.Id);
        Assert.Equal(state.AnnotationOffsets[second.Id], new AnnotationOffset(20, 34));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResizeOrDistortedMechanicalReflowRecomputesAnchorAndPreservesOffset(bool mechanical)
    {
        var load = Load("F1"); var original = Document(load); var state = Offset(load.Id, 21, -15);
        var before = Label(Scene(original, Frame(original), state), load.Id);
        var document = mechanical ? original.WithLoads([load, Load("F2", 501)]) : original;
        var frame = Frame(document, mechanical ? 1100 : 1500, 800);
        var after = Label(Scene(document, frame, state), load.Id);
        AssertOffset(after, new(21, -15)); Assert.NotEqual(before.AutoBounds, after.AutoBounds);
        if (mechanical) Assert.True(frame.Layout.IsDistorted);
        Assert.Equal(new AnnotationOffset(21, -15), state.AnnotationOffsets[load.Id]);
        Assert.Equal(Mm(500), load.Position);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RenameAndValueWidthChangesUseNewReferenceAndPreserveOffset(bool value)
    {
        var load = Load("F1"); int calls = 0;
        var editor = new EditorViewModel(Document(load), () => { }, b => { calls++; return BeamAnalysis.Analyze(b); });
        editor.SetAnnotationOffset(load.Id, new(18, 9));
        var before = Label(Scene(editor.Document, Frame(editor.Document), editor.EditorPresentation), load.Id);
        editor.EditLoad(load.Id);
        if (value) editor.LoadDraft!.ValueText = "-123456"; else editor.LoadDraft!.NameText = "A much longer name";
        Assert.True(editor.ConfirmLoad());
        var after = Label(Scene(editor.Document, Frame(editor.Document), editor.EditorPresentation), load.Id);
        AssertOffset(after, new(18, 9)); Assert.NotEqual(before.AutoBounds.Width, after.AutoBounds.Width);
        Assert.Equal(value ? 2 : 1, calls);
    }

    [Fact]
    public void ADisplacedAutomaticLabelStartsManualDragWithoutJump()
    {
        var first = Load("F1"); var second = Load("F2"); var document = Document(first, second); var frame = Frame(document);
        var state = Offset(first.Id, 0, -24); var scene = Scene(document, frame, state);
        var start = Label(scene, second.Id); Assert.NotEqual(start.AutoBounds, start.Bounds);
        var gesture = new LabelDragGesture(start, start.Bounds.Center, null);
        var delta = new Vector(4, 0);
        var offset = gesture.Update(start.Bounds.Center + delta)!.Value;
        var next = Label(Scene(document, frame, state.WithOffset(second.Id, offset)), second.Id);
        Assert.Equal(start.Bounds.Translate(delta), next.Bounds);
        AssertOffset(next, offset);
    }

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(3, 0, false)]
    [InlineData(0, 3, false)]
    [InlineData(4, 0, true)]
    [InlineData(0, -4, true)]
    [InlineData(3, 3, true)]
    public void LabelThresholdUsesTwoDimensionsAndDoesNotChangeOffsetForClick(double dx, double dy, bool dragging)
    {
        var load = Load("F1"); var document = Document(load); var label = Label(Scene(document, Frame(document)), load.Id);
        var gesture = new LabelDragGesture(label, label.Bounds.Center, null);
        var result = gesture.Update(label.Bounds.Center + new Vector(dx, dy));
        Assert.Equal(dragging, gesture.IsDragging);
        if (dragging) Assert.Equal(new AnnotationOffset(dx, dy), result); else Assert.Null(result);
        Assert.Null(gesture.OriginalOffset);
    }

    [Fact]
    public void ReturningToStartAfterThresholdRemainsDragAndInvalidPointerIsIgnored()
    {
        var load = Load("F1"); var document = Document(load); var label = Label(Scene(document, Frame(document)), load.Id);
        var gesture = new LabelDragGesture(label, label.Bounds.Center, null);
        Assert.Null(gesture.Update(new(double.NaN, 0))); Assert.False(gesture.IsDragging);
        Assert.NotNull(gesture.Update(label.Bounds.Center + new Vector(10, 0)));
        Assert.Equal(new AnnotationOffset(0, 0), gesture.Update(label.Bounds.Center)); Assert.True(gesture.IsDragging);
        Assert.Null(gesture.Update(new(double.PositiveInfinity, 0))); Assert.True(gesture.IsDragging);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LabelDragChangesOnlySessionStateAndRollbackPreservesAnOpenDraft(bool support)
    {
        var load = Load("F1"); var bearing = new EditorSupport(Guid.NewGuid(), Mm(500), SupportType.Pinned, "A");
        int calls = 0; var document = Document(load).WithSupports([bearing]);
        var editor = new EditorViewModel(document, () => { }, b => { calls++; return BeamAnalysis.Analyze(b); });
        var id = support ? bearing.Id : load.Id;
        editor.SetAnnotationOffset(id, new(7, 8));
        if (support) { editor.EditSupport(id); editor.SupportDraft!.NameText = "Draft name"; editor.SupportDraft.PositionText = "7,"; }
        else { editor.EditLoad(id); editor.LoadDraft!.NameText = "Draft name"; editor.LoadDraft.PositionText = "7,"; editor.LoadDraft.ValueText = "-"; }
        var supportDraft = editor.SupportDraft; var loadDraft = editor.LoadDraft; var analysis = editor.Presentation;
        var frame = Frame(document); var state = editor.EditorPresentation;
        var label = Label(Scene(document, frame, state), id);
        var gesture = new LabelDragGesture(label, label.Bounds.Center, state.AnnotationOffsets[id]);
        editor.SetAnnotationOffset(id, gesture.Update(label.Bounds.Center + new Vector(30, -20)));
        AssertOffset(Label(Scene(document, frame, editor.EditorPresentation), id), new(37, -12));
        Assert.Same(document, editor.Document); Assert.Same(analysis, editor.Presentation); Assert.Equal(1, calls);
        editor.SetAnnotationOffset(id, gesture.OriginalOffset);
        Assert.Equal(new AnnotationOffset(7, 8), editor.EditorPresentation.AnnotationOffsets[id]);
        Assert.Same(supportDraft, editor.SupportDraft); Assert.Same(loadDraft, editor.LoadDraft);
        Assert.Equal("7,", support ? editor.SupportDraft!.PositionText : editor.LoadDraft!.PositionText);
        if (!support) Assert.Equal("-", editor.LoadDraft!.ValueText);
        Assert.Equal("Draft name", support ? editor.SupportDraft!.NameText : editor.LoadDraft!.NameText);
        Assert.Same(document, editor.Document); Assert.Equal(1, calls);
    }

    [Fact]
    public void LabelSnapshotDefersResizeAndFinalHeightFitsManualBoundsWithoutChangingTransform()
    {
        var load = Load("F1"); var document = Document(load); var layout = new BeamLayoutState();
        var frame = layout.Update(document, 1100, 220)!; layout.BeginInteraction(BeamPointerInteraction.LabelDrag);
        var state = Offset(load.Id, 5, -600);
        var moving = Scene(document, layout.Update(document, 1500, 800)!, state);
        Assert.Same(frame, moving.Frame); Assert.Equal(110, moving.Frame.Viewport.BeamY);
        Assert.Same(frame.Layout.Transform, moving.Frame.Layout.Transform);
        layout.EndInteraction();
        var resized = layout.Update(document, 1100, moving.MinimumPaneHeight)!;
        var released = Scene(document, resized, state);
        var label = Label(released, load.Id);
        Assert.True(label.Bounds.Top >= 0 && label.Bounds.Bottom <= resized.Viewport.Height);
        Assert.Same(frame.Layout.Transform, resized.Layout.Transform);
        Assert.Equal(moving.MinimumPaneHeight, released.MinimumPaneHeight);
        AssertOffset(label, new(5, -600));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeleteCleansManualStateAndNewSessionStartsEmpty(bool support)
    {
        var load = Load("F1"); var bearing = new EditorSupport(Guid.NewGuid(), Mm(500), SupportType.Pinned, "A");
        var editor = new EditorViewModel(Document(load).WithSupports([bearing]), () => { });
        var id = support ? bearing.Id : load.Id; editor.SetAnnotationOffset(id, new(5, -7));
        if (support) { editor.EditSupport(id); editor.DeleteSupport(); } else { editor.EditLoad(id); editor.DeleteLoad(); }
        Assert.False(editor.EditorPresentation.AnnotationOffsets.ContainsKey(id));
        Assert.DoesNotContain(Scene(editor.Document, Frame(editor.Document), editor.EditorPresentation).Annotations, a => a.Id == id);
        var next = new EditorViewModel(Document(load), () => { }); Assert.Empty(next.EditorPresentation.AnnotationOffsets);
    }

    [Fact]
    public void OffsetsLeaveAxisAndSharedGlyphsAndPhysicalDragsUnchanged()
    {
        var first = Load("F1"); var second = Load("F2"); var document = Document(first, second); var frame = Frame(document);
        var before = Scene(document, frame); var after = Scene(document, frame, Offset(first.Id, 200, -100));
        var glyph = Assert.Single(after.Glyphs);
        var original = Assert.Single(before.Glyphs);
        Assert.Equal(original.Kind, glyph.Kind); Assert.Equal(original.Bounds, glyph.Bounds);
        Assert.Equal(original.Positive, glyph.Positive); Assert.Equal(original.Negative, glyph.Negative);
        Assert.Equal(original.Entities.ToArray(), glyph.Entities.ToArray());
        Assert.Null(PointLoadSymbol.ResolveEntity(glyph)); Assert.Equal(first.Id, PointLoadSymbol.ResolveEntity(glyph, first.Id));
        var axis = CoordinateAxisLayout.Create(frame.Layout, 1100, DesktopLayoutFixture.Measure);
        Assert.Equal(3, axis.Labels.Count);
        foreach (var tick in axis.Labels) Assert.Null(after.HitTestLabel(tick.Bounds.Center.X, frame.Viewport.Height + tick.Bounds.Center.Y));
        var gesture = new PointLoadDragGesture(first.Id, first.Position, glyph.X, frame.Layout.Transform);
        Assert.Equal(Mm(700), gesture.Update(frame.Layout.Transform.PhysicalToScreen(.7), frame.Viewport.BeamY, 600));
        Assert.Equal(Mm(500), first.Position);
    }
}
