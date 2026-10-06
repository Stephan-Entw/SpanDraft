using Avalonia;
using SpanDraft.Analysis;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Controls;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using Xunit;

namespace SpanDraft.Tests;

public sealed class DesktopAnnotationInteractionTests
{
    private sealed class Session
    {
        public Session(int kind, bool editing = false, bool manual = false)
        {
            EntityId = Guid.NewGuid();
            var position = Length.FromMillimeters(500);
            var document = DesktopLayoutFixture.Document();
            if (kind == 0) document = document.WithSupports([new(EntityId, position, SupportType.Pinned, "A")]);
            else document = document.WithLoads([EditorPointLoad.Create(EntityId, position,
                kind == 1 ? PointLoadKind.Force : PointLoadKind.Moment, 100, kind == 1 ? "F1" : "M1")]);
            Editor = new(document, () => { }, b => { AnalysisCount++; return BeamAnalysis.Analyze(b); });
            if (manual) Editor.SetAnnotationOffset(EntityId, new(10, 20));
            if (editing)
            {
                if (kind == 0) { Editor.EditSupport(EntityId); Editor.SupportDraft!.NameText = "Draft"; Editor.SupportDraft.PositionText = "7,"; }
                else { Editor.EditLoad(EntityId); Editor.LoadDraft!.NameText = "Draft"; Editor.LoadDraft.PositionText = "7,"; Editor.LoadDraft.ValueText = "-"; }
            }
            Frame = new BeamLayoutState().Update(document, 1100, 600)!;
            Label = BeamRenderState.Create(document, Frame, DesktopLayoutFixture.Measure,
                presentation: Editor.EditorPresentation).Annotations.Single();
            Gesture = new(Label, Label.Bounds.Center,
                Editor.EditorPresentation.AnnotationOffsets.TryGetValue(EntityId, out var offset) ? offset : null);
        }
        public Guid EntityId { get; }
        public int AnalysisCount { get; private set; }
        public EditorViewModel Editor { get; }
        public BeamLayoutFrame Frame { get; }
        public EntityAnnotation Label { get; }
        public LabelDragGesture Gesture { get; }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ClickOpensEntityWithoutSettingOffsetOrAnalyzing(int kind)
    {
        var s = new Session(kind);
        s.Gesture.Apply(s.Editor, s.Label.Bounds.Center + new Vector(1, 2));
        Assert.Null(s.Editor.SupportDraft); Assert.Null(s.Editor.LoadDraft);
        Assert.True(s.Gesture.OpenOnClick(s.Editor));
        Assert.Equal(s.EntityId, kind == 0 ? s.Editor.SupportDraft?.OriginalId : s.Editor.LoadDraft?.OriginalId);
        Assert.Empty(s.Editor.EditorPresentation.AnnotationOffsets); Assert.Equal(1, s.AnalysisCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void CompletedDragUpdatesOnlyAnnotationAndNeverOpensDraft(int kind)
    {
        var s = new Session(kind); var document = s.Editor.Document; var analysis = s.Editor.Presentation;
        s.Gesture.Apply(s.Editor, s.Label.Bounds.Center + new Vector(30, -650));
        Assert.Equal(new AnnotationOffset(30, -650), s.Editor.EditorPresentation.AnnotationOffsets[s.EntityId]);
        Assert.False(s.Gesture.OpenOnClick(s.Editor));
        Assert.Null(s.Editor.SupportDraft); Assert.Null(s.Editor.LoadDraft);
        Assert.Same(document, s.Editor.Document); Assert.Same(analysis, s.Editor.Presentation); Assert.Equal(1, s.AnalysisCount);
        var scene = BeamRenderState.Create(document, s.Frame, DesktopLayoutFixture.Measure, presentation: s.Editor.EditorPresentation);
        Assert.Equal(s.Label.Bounds.Translate(new Vector(30, -650)), scene.Annotations.Single().Bounds);
        Assert.Same(s.Frame.Layout.Transform, scene.Frame.Layout.Transform);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    public void CancelRestoresOriginalAutoOrManualStateWithoutDiscardingDraft(int kind, bool manual)
    {
        var s = new Session(kind, editing: true, manual: manual);
        var supportDraft = s.Editor.SupportDraft; var loadDraft = s.Editor.LoadDraft;
        var document = s.Editor.Document; var analysis = s.Editor.Presentation;
        s.Gesture.Apply(s.Editor, s.Label.Bounds.Center + new Vector(30, -20));
        Assert.True(s.Editor.EditorPresentation.AnnotationOffsets.ContainsKey(s.EntityId));
        s.Gesture.Cancel(s.Editor);
        Assert.Same(supportDraft, s.Editor.SupportDraft); Assert.Same(loadDraft, s.Editor.LoadDraft);
        Assert.Equal("7,", kind == 0 ? s.Editor.SupportDraft!.PositionText : s.Editor.LoadDraft!.PositionText);
        Assert.Equal("Draft", kind == 0 ? s.Editor.SupportDraft!.NameText : s.Editor.LoadDraft!.NameText);
        if (kind != 0) Assert.Equal("-", s.Editor.LoadDraft!.ValueText);
        if (manual) Assert.Equal(new AnnotationOffset(10, 20), s.Editor.EditorPresentation.AnnotationOffsets[s.EntityId]);
        else Assert.Empty(s.Editor.EditorPresentation.AnnotationOffsets);
        Assert.Same(document, s.Editor.Document); Assert.Same(analysis, s.Editor.Presentation); Assert.Equal(1, s.AnalysisCount);
    }
}
