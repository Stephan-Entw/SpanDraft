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

public sealed class DesktopSpringAnnotationTests
{
    private static readonly Guid B = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid C = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static EditorDocument Document() => DesktopLayoutFixture.Document().WithSupports([
        new(B, Length.FromMeters(.5), SupportType.Spring, "B", new(TranslationalStiffness.FromNewtonsPerMeter(1e5), null)),
        new(C, Length.FromMeters(.5), SupportType.Spring, "C", new(null, RotationalStiffness.FromNewtonMetersPerRadian(1e4)))]);

    private static BeamRenderState Scene(EditorDocument document, EditorPresentationState? presentation = null) =>
        BeamRenderState.Create(document, new BeamLayoutState().Update(document, 1100, 600)!, DesktopLayoutFixture.Measure,
            presentation: presentation);

    private static BeamRenderState Scene(EditorViewModel editor) => BeamRenderState.Create(editor.Document,
        new BeamLayoutState().Update(editor.Document, 1100, 600)!, DesktopLayoutFixture.Measure,
        editor.Preview, editor.HiddenSupportId, editor.SupportPreviewName, presentation: editor.RenderPresentation);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SharedLabelHasBothEqualCandidatesAndFollowsReactionOrder(bool reverse)
    {
        var document = Document();
        if (reverse) document = document.WithSupports(document.Supports.Reverse());
        var editor = new EditorViewModel(document, () => { });
        var scene = Scene(editor);
        var label = Assert.Single(scene.Annotations);
        Assert.Null(label.Id);
        Assert.Equal(reverse ? "C + B" : "B + C", label.Text);
        Assert.Equal(Assert.Single(editor.Overview.Reactions).Name, label.Text);
        Assert.Equal(document.Supports.Select(s => s.Id), label.EntityIds);
        var point = label.Bounds.Center;
        Assert.Same(label, Assert.Single(scene.HitTestLabels(point.X, point.Y)));
        Assert.Equal(label.EntityIds, scene.HitTestLabelEntities(point.X, point.Y));
        Assert.Equal(label.EntityIds, scene.HitTestEntities(point.X, point.Y));
        var gesture = new LabelDragGesture(label, point);
        Assert.False(gesture.OpenOnClick(editor));
        Assert.Null(editor.SupportDraft);
        Assert.Empty(editor.Session.UndoHistory);
    }

    [Fact]
    public void SharedLabelLeavesBothSupportSymbolsIndependentlyHittable()
    {
        var document = Document();
        var scene = Scene(document);
        Assert.Equal(new[] { B, C }, scene.Supports.Select(s => s.Id!.Value));
        double x = scene.Frame.Layout.Transform.PhysicalToScreen(.5), y = scene.Frame.Viewport.BeamY;
        Assert.Equal(B, SupportSymbol.HitTest(document.Supports, scene.Frame.Layout.Transform, y, x, y + 5));
        Assert.Equal(C, SupportSymbol.HitTest(document.Supports, scene.Frame.Layout.Transform, y,
            x + SpringSupportGeometry.SpiralOuterRadius, y));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DraftRenameAndMoveImmediatelyReplaceAnnotationsWithoutDuplicates(bool editC)
    {
        var editor = new EditorViewModel(Document(), () => { });
        var original = editor.Document;
        var id = editC ? C : B;
        Assert.True(editor.EditSupport(id));
        editor.SupportDraft!.NameText = "Renamed";
        Assert.Equal(editC ? "B + Renamed" : "Renamed + C", Assert.Single(Scene(editor).Annotations).Text);
        Assert.True(editor.BeginSupportDrag(id));
        editor.UpdateSupportDrag(Length.FromMeters(.7));
        var separated = Scene(editor);
        Assert.Equal(2, separated.Annotations.Count);
        Assert.Equal("Renamed", separated.Annotations.Single(a => a.Id == id).Text);
        Assert.Equal(editC ? "B" : "C", separated.Annotations.Single(a => a.Id != id).Text);
        Assert.True(editor.EndSupportDrag());
        Assert.Equal(2, Scene(editor).Annotations.Count);
        Assert.True(editor.BeginSupportDrag(id));
        editor.UpdateSupportDrag(Length.FromMeters(.5));
        Assert.Equal(editC ? "B + Renamed" : "Renamed + C", Assert.Single(Scene(editor).Annotations).Text);
        Assert.True(editor.EndSupportDrag());
        editor.CancelSupportInteraction();
        Assert.Same(original, editor.Document);
        Assert.Equal("B + C", Assert.Single(Scene(editor).Annotations).Text);

        Assert.True(editor.EditSupport(id));
        editor.SupportDraft!.NameText = "Confirmed";
        Assert.True(editor.ConfirmSupport());
        Assert.Equal(editC ? "B + Confirmed" : "Confirmed + C", Assert.Single(Scene(editor).Annotations).Text);
        Assert.True(editor.BeginSupportDrag(id));
        editor.UpdateSupportDrag(Length.FromMeters(.7));
        Assert.True(editor.EndSupportDrag());
        Assert.True(editor.ConfirmSupport());
        Assert.Equal(2, Scene(editor).Annotations.Count);
        Assert.Equal("Confirmed", Scene(editor).Annotations.Single(a => a.Id == id).Text);
    }

    [Fact]
    public void DifferentManualOffsetsKeepSeparateLabelsUntilConflictIsRemoved()
    {
        var document = Document();
        var offsets = new EditorPresentationState().WithOffset(B, new(10, -5)).WithOffset(C, new(-20, 30));
        var scene = Scene(document, offsets);
        Assert.Equal(2, scene.Annotations.Count);
        foreach (var label in scene.Annotations)
        {
            var offset = offsets.AnnotationOffsets[label.Id!.Value];
            Assert.Equal(label.AutoBounds.Translate(new Vector(offset.Dx, offset.Dy)), label.Bounds);
        }
        Assert.Equal(new AnnotationOffset(10, -5), offsets.AnnotationOffsets[B]);
        Assert.Equal(new AnnotationOffset(-20, 30), offsets.AnnotationOffsets[C]);
        Assert.Equal("B + C", Assert.Single(Scene(document, offsets.WithOffset(C, null)).Annotations).Text);
        Assert.Equal("B + C", Assert.Single(Scene(document, offsets.WithOffset(C, new(10, -5))).Annotations).Text);
    }

    [Theory]
    [InlineData(0)] // Both auto.
    [InlineData(1)] // Only B manual.
    [InlineData(2)] // Only C manual.
    [InlineData(3)] // Both manual with the same offset.
    public void SharedDragIsOnePresentationCommitAndRetainsOffsetsAfterSeparation(int manual)
    {
        var initial = new EditorPresentationState();
        if ((manual & 1) != 0) initial = initial.WithOffset(B, new(12, -8));
        if ((manual & 2) != 0) initial = initial.WithOffset(C, new(12, -8));
        int analyses = 0;
        var editor = EditorViewModel.ForSession(ProjectSession.Create(new(Document(), initial)), () => { },
            beam => { analyses++; return BeamAnalysis.Analyze(beam); });
        var document = editor.Document;
        var analysis = editor.Presentation;
        var label = Assert.Single(Scene(editor).Annotations);
        var start = manual == 0 ? new AnnotationOffset(0, 0) : new AnnotationOffset(12, -8);
        Assert.Equal(label.AutoBounds.Translate(new Vector(start.Dx, start.Dy)), label.Bounds);
        Assert.Same(initial, editor.EditorPresentation);
        var gesture = new LabelDragGesture(label, label.Bounds.Center);
        gesture.Apply(editor, label.Bounds.Center + new Vector(25, -15));
        var expected = new AnnotationOffset(start.Dx + 25, start.Dy - 15);
        Assert.Empty(editor.Session.UndoHistory);
        Assert.Same(initial, editor.EditorPresentation);
        Assert.All(new[] { B, C }, id => Assert.Equal(expected, editor.RenderPresentation.AnnotationOffsets[id]));
        Assert.Equal(label.Bounds.Translate(new Vector(25, -15)), Assert.Single(Scene(editor).Annotations).Bounds);
        gesture.Complete(editor);
        Assert.Single(editor.Session.UndoHistory);
        Assert.False(gesture.OpenOnClick(editor));
        Assert.Null(editor.SupportDraft);
        Assert.Same(document, editor.Document);
        Assert.Same(analysis, editor.Presentation);
        Assert.Equal(1, analyses);
        var separated = document.WithSupports(document.Supports.Select(s => s.Id == C ? s with { Position = Length.FromMeters(.7) } : s));
        var individual = Scene(separated, editor.EditorPresentation);
        Assert.Equal(new[] { "B", "C" }, individual.Annotations.Select(a => a.Text));
        foreach (var single in individual.Annotations)
            Assert.Equal(single.AutoBounds.Translate(new Vector(expected.Dx, expected.Dy)), single.Bounds);
        Assert.True(editor.Undo());
        Assert.True(initial.ContentEquals(editor.EditorPresentation));
        Assert.True(editor.Redo());
        Assert.All(new[] { B, C }, id => Assert.Equal(expected, editor.EditorPresentation.AnnotationOffsets[id]));
        Assert.Equal(1, analyses);
        Assert.Same(analysis, editor.Presentation);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancellingSharedDragPreservesMissingOffsetsAndOpenDraft(bool manual)
    {
        var initial = manual ? new EditorPresentationState().WithOffset(C, new(12, -8)) : new();
        var editor = EditorViewModel.ForSession(ProjectSession.Create(new(Document(), initial)), () => { });
        Assert.True(editor.EditSupport(C));
        editor.SupportDraft!.NameText = "Draft";
        var draft = editor.SupportDraft;
        var label = Assert.Single(Scene(editor).Annotations);
        var gesture = new LabelDragGesture(label, label.Bounds.Center);
        gesture.Apply(editor, label.Bounds.Center + new Vector(25, -15));
        Assert.Equal(2, editor.RenderPresentation.AnnotationOffsets.Count);
        gesture.Cancel(editor);
        Assert.Same(initial, editor.RenderPresentation);
        Assert.Same(draft, editor.SupportDraft);
        Assert.Equal("Draft", draft.NameText);
        Assert.Empty(editor.Session.UndoHistory);
    }
}
