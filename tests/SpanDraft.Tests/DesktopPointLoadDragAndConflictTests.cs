using SpanDraft.Analysis;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Controls;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using Xunit;

namespace SpanDraft.Tests;

public sealed class DesktopPointLoadDragAndConflictTests
{
    private static Length Mm(double value) => Length.FromMillimeters(value);
    private sealed class Session
    {
        public int Calls { get; private set; }
        public EditorViewModel Editor { get; }
        public Guid LoadId => Editor.Document.Loads[0].Id;
        public Session(PointLoadKind kind, double position = 300)
        {
            Editor = new(new EditorDocument(Mm(1000.5), ProjectTemplates.Material, ProjectTemplates.Section,
                loads: [EditorPointLoad.Create(Guid.NewGuid(), Mm(position), kind, -50, "Load")]), () => { },
                b => { Calls++; return BeamAnalysis.Analyze(b); });
        }
    }

    [Theory]
    [InlineData(PointLoadKind.Force, true)]
    [InlineData(PointLoadKind.Moment, true)]
    [InlineData(PointLoadKind.Force, false)]
    [InlineData(PointLoadKind.Moment, false)]
    public void RepeatedDragsRetainDraftIdAndValueBufferAndCommitOnlyOnce(PointLoadKind kind, bool apply)
    {
        var s = new Session(kind);
        var document = s.Editor.Document;
        s.Editor.EditLoad(s.LoadId);
        var draft = s.Editor.LoadDraft!;
        draft.ValueText = "250";
        foreach (double position in new[] { 400, 600, 200 })
        {
            draft.PositionText = "700";
            Assert.True(s.Editor.BeginLoadDrag(s.LoadId));
            Assert.False(s.Editor.IsLoadFlyoutVisible);
            s.Editor.UpdateLoadDrag(Mm(position));
            Assert.False(s.Editor.ConfirmLoad());
            s.Editor.DeleteLoad();
            Assert.True(s.Editor.EndLoadDrag());
            Assert.Same(draft, s.Editor.LoadDraft);
            Assert.True(s.Editor.IsLoadFlyoutVisible);
            Assert.Equal(Mm(position), s.Editor.LoadPreview!.Position);
            Assert.Equal(UiNumbers.Format(position), draft.PositionText);
            Assert.Equal("250", draft.ValueText);
            Assert.Equal(250, s.Editor.LoadPreview.Value);
            Assert.Same(document, s.Editor.Document);
            Assert.Equal(1, s.Calls);
            Assert.True(s.Editor.EditLoad(s.LoadId));
            Assert.Same(draft, s.Editor.LoadDraft);
        }
        if (apply)
        {
            Assert.True(s.Editor.ConfirmLoad());
            Assert.Equal(document.Loads[0].Id, s.LoadId);
            Assert.Equal(Mm(200), s.Editor.Document.Loads[0].Position);
            Assert.Equal(250, s.Editor.Document.Loads[0].Value);
            Assert.Equal(2, s.Calls);
        }
        else
        {
            s.Editor.CancelEditorInteraction();
            Assert.Same(document, s.Editor.Document);
            Assert.Equal(1, s.Calls);
        }
    }

    [Theory]
    [InlineData(PointLoadKind.Force)]
    [InlineData(PointLoadKind.Moment)]
    public void InvalidRedragRestoresCanvasAndRawBuffersThenAllowsCorrection(PointLoadKind kind)
    {
        var s = new Session(kind);
        s.Editor.EditLoad(s.LoadId);
        var draft = s.Editor.LoadDraft!;
        draft.PositionText = "invalid position";
        draft.ValueText = "invalid value";
        var before = draft.Preview;
        Assert.True(s.Editor.BeginLoadDrag(s.LoadId));
        s.Editor.UpdateLoadDrag(Mm(500));
        s.Editor.UpdateLoadDrag(null);
        Assert.False(s.Editor.EndLoadDrag());
        Assert.Same(draft, s.Editor.LoadDraft);
        Assert.Equal(before, s.Editor.LoadPreview);
        Assert.Equal("invalid position", draft.PositionText);
        Assert.Equal("invalid value", draft.ValueText);
        Assert.True(s.Editor.IsLoadFlyoutVisible);
        Assert.True(s.Editor.HasLoadFeedback);
        Assert.Equal(1, s.Calls);
        draft.ValueText = "200";
        Assert.False(s.Editor.HasLoadFeedback);
        s.Editor.BeginLoadDrag(s.LoadId);
        s.Editor.UpdateLoadDrag(Mm(600));
        Assert.True(s.Editor.EndLoadDrag());
        Assert.True(s.Editor.ConfirmLoad());
        Assert.Equal(Mm(600), s.Editor.Document.Loads[0].Position);
        Assert.Equal(200, s.Editor.Document.Loads[0].Value);
        Assert.Equal(2, s.Calls);
    }

    [Theory]
    [InlineData(PointLoadKind.Force, false)]
    [InlineData(PointLoadKind.Moment, false)]
    [InlineData(PointLoadKind.Force, true)]
    [InlineData(PointLoadKind.Moment, true)]
    public void InvalidDragFromNeutralAndCancellationNeverCommit(PointLoadKind kind, bool cancel)
    {
        var s = new Session(kind);
        var document = s.Editor.Document;
        Assert.True(s.Editor.BeginLoadDrag(s.LoadId));
        s.Editor.UpdateLoadDrag(Mm(500));
        if (cancel) s.Editor.CancelEditorInteraction(); // Escape or capture loss.
        else
        {
            s.Editor.UpdateLoadDrag(null);
            Assert.False(s.Editor.EndLoadDrag());
            Assert.True(s.Editor.HasLoadFeedback);
        }
        Assert.Null(s.Editor.LoadDraft);
        Assert.Null(s.Editor.LoadPreview);
        Assert.Same(document, s.Editor.Document);
        Assert.Equal(1, s.Calls);
        Assert.Equal(LoadInteraction.Neutral, s.Editor.LoadState);
    }

    [Theory]
    [InlineData(PointLoadKind.Force, -10000, 0)]
    [InlineData(PointLoadKind.Moment, -10000, 0)]
    [InlineData(PointLoadKind.Force, 10000, 1000.5)]
    [InlineData(PointLoadKind.Moment, 10000, 1000.5)]
    public void CapturedGestureClampsBeyondSurfaceAndResumesWithGrabOffset(PointLoadKind kind, double pointerX, double expected)
    {
        var s = new Session(kind);
        var v = DesktopLayoutFixture.Linear(72, 1178, 200, 1.0005);
        double press = v.Layout.Transform.PhysicalToScreen(0.3) + 8;
        var gesture = new PointLoadDragGesture(s.LoadId, Mm(300), press, v.Layout.Transform);
        Assert.Null(gesture.Update(press + 3, 100, 500));
        Assert.False(gesture.IsDragging);
        var clamped = gesture.Update(pointerX, 100, 500);
        Assert.Equal(Mm(expected), clamped);
        s.Editor.BeginLoadDrag(s.LoadId);
        s.Editor.UpdateLoadDrag(clamped);
        Assert.True(s.Editor.EndLoadDrag());
        Assert.Equal(Mm(expected), s.Editor.LoadPreview!.Position);
        var returned = gesture.Update(v.Layout.Transform.PhysicalToScreen(0.65) + 8, 100, 500);
        Assert.Equal(Mm(650), returned);
        Assert.Null(gesture.Update(500, -1, 500));
        Assert.Null(gesture.Update(500, 501, 500));
        Assert.Equal(1, s.Calls);
    }

    [Theory]
    [InlineData(PointLoadKind.Force)]
    [InlineData(PointLoadKind.Moment)]
    public void EndpointPlacementAndDragUseExactFractionalLengthAndMillimeterSnap(PointLoadKind kind)
    {
        var s = new Session(kind);
        var v = DesktopLayoutFixture.Linear(72, 1178, 200, 1.0005);
        s.Editor.ToggleLoadTool(kind);
        s.Editor.HoverLoadPlacement(SupportSnap.Placement(v.Layout.Transform, v.Viewport.BeamY, v.Layout.Stations[^1].ScreenX - 2, 200));
        Assert.Equal(s.Editor.Document.Length, s.Editor.LoadPreview!.Position);
        Assert.True(s.Editor.PlaceLoad());
        Assert.True(s.Editor.ConfirmLoad());
        Assert.Equal(s.Editor.Document.Length, s.Editor.Document.Loads[1].Position);
        var gesture = new PointLoadDragGesture(s.LoadId, Mm(300), v.Layout.Transform.PhysicalToScreen(0.3), v.Layout.Transform);
        Assert.Equal(Mm(456), gesture.Update(v.Layout.Transform.PhysicalToScreen(0.4564), 200, 500));
        Assert.Equal(s.Editor.Document.Length, gesture.Update(v.Layout.Stations[^1].ScreenX - 2, 200, 500));
        Assert.Null(SupportSnap.Placement(v.Layout.Transform, v.Viewport.BeamY, 600, 219));
        Assert.Null(SupportSnap.Placement(v.Layout.Transform, v.Viewport.BeamY, -100, 200));
    }

    [Fact]
    public void OtherObjectsCannotReplaceOrDragDuringActiveLoadSession()
    {
        var s = new Session(PointLoadKind.Force);
        var other = EditorPointLoad.Create(Guid.NewGuid(), Mm(700), PointLoadKind.Moment, 100, "Other moment");
        var editor = new EditorViewModel(s.Editor.Document.WithLoads([s.Editor.Document.Loads[0], other])
            .WithSupports([new(Guid.NewGuid(), Mm(0), SupportType.Fixed, "A")]), () => { });
        editor.EditLoad(s.LoadId);
        var draft = editor.LoadDraft;
        Assert.False(editor.EditLoad(other.Id));
        Assert.False(editor.BeginLoadDrag(other.Id));
        Assert.False(editor.EditSupport(editor.Document.Supports[0].Id));
        Assert.False(editor.BeginSupportDrag(editor.Document.Supports[0].Id));
        Assert.Same(draft, editor.LoadDraft);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MixedLengthConflictIncludesEveryBlockingEntityAndFocusLossFullyRestores(bool loseFocus)
    {
        EditorSupport[] supports = [new(Guid.NewGuid(), Mm(800), SupportType.Roller, "A"), new(Guid.NewGuid(), Mm(0), SupportType.Fixed, "B")];
        EditorPointLoad[] loads = [EditorPointLoad.Create(Guid.NewGuid(), Mm(850), PointLoadKind.Force, -1000, "F1"),
            EditorPointLoad.Create(Guid.NewGuid(), Mm(900), PointLoadKind.Moment, 100, "M1"),
            EditorPointLoad.Create(Guid.NewGuid(), Mm(600), PointLoadKind.Force, 0, "F2")];
        var document = new EditorDocument(Mm(1000), ProjectTemplates.Material, ProjectTemplates.Section, supports, loads);
        int calls = 0;
        var editor = new EditorViewModel(document, () => { }, b => { calls++; return BeamAnalysis.Analyze(b); });
        var presentation = editor.Presentation;
        editor.DimensionLength.Begin();
        editor.DimensionLength.Text = "700";
        Assert.False(editor.DimensionLength.Confirm());
        Assert.Equal(new[] { supports[0].Id, loads[0].Id, loads[1].Id }, editor.ConflictEntityIds);
        Assert.Equal(Mm(700), editor.ConstraintConflict!.RequestedLength);
        Assert.Same(document, editor.Document);
        Assert.Same(presentation, editor.Presentation);
        Assert.Equal(1, calls);
        var viewport = DesktopLayoutFixture.Fit(1250, 600, 1);
        var geometry = BeamConflictGeometry.Create(viewport.Layout.Transform, editor.ConstraintConflict);
        Assert.True(geometry.HasGhost);
        Assert.Equal(viewport.Layout.Transform.PhysicalToScreen(0.7), geometry.EndX);
        if (loseFocus) editor.DimensionLength.LoseFocus();
        else editor.DimensionLength.Cancel();
        Assert.False(editor.DimensionLength.IsEditing);
        Assert.False(editor.DimensionLength.HasError);
        Assert.Null(editor.ConstraintConflict);
        Assert.Empty(editor.ConflictEntityIds);
        Assert.False(BeamConflictGeometry.Create(viewport.Layout.Transform, editor.ConstraintConflict).HasGhost);
        Assert.Equal("1000", editor.DimensionLength.Text);
        Assert.Same(document, editor.Document);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void LoadCommitsReevaluateConflictWithoutCommittingRequestedLength()
    {
        var s = new Session(PointLoadKind.Force, 850);
        s.Editor.ToggleLoadTool(PointLoadKind.Moment);
        s.Editor.HoverLoadPlacement(Mm(900));
        s.Editor.PlaceLoad();
        s.Editor.ConfirmLoad();
        s.Editor.DimensionLength.Begin();
        s.Editor.DimensionLength.Text = "700";
        Assert.False(s.Editor.DimensionLength.Confirm());
        Assert.Equal(2, s.Editor.ConflictEntityIds.Count);
        s.Editor.EditLoad(s.LoadId);
        s.Editor.LoadDraft!.PositionText = "600";
        s.Editor.ConfirmLoad();
        Assert.Single(s.Editor.ConflictEntityIds);
        Assert.True(s.Editor.DimensionLength.HasError);
        s.Editor.EditLoad(s.Editor.Document.Loads[1].Id);
        s.Editor.DeleteLoad();
        Assert.Null(s.Editor.ConstraintConflict);
        Assert.False(s.Editor.DimensionLength.HasError);
        Assert.Equal(Mm(1000.5), s.Editor.Document.Length);
        Assert.Equal(4, s.Calls);
    }

    [Theory]
    [InlineData(PointLoadKind.Force)]
    [InlineData(PointLoadKind.Moment)]
    public void CorrectedLengthCanEndExactlyAtOutermostLoad(PointLoadKind kind)
    {
        var s = new Session(kind, 850);
        s.Editor.DimensionLength.Begin();
        s.Editor.DimensionLength.Text = "700";
        Assert.False(s.Editor.DimensionLength.Confirm());
        s.Editor.DimensionLength.Text = "invalid";
        Assert.Null(s.Editor.ConstraintConflict);
        Assert.False(s.Editor.DimensionLength.Confirm());
        s.Editor.DimensionLength.Text = "850";
        Assert.True(s.Editor.DimensionLength.Confirm());
        Assert.Equal(Mm(850), s.Editor.Document.Length);
        Assert.Equal(Mm(850), s.Editor.Document.Loads[0].Position);
        Assert.Equal(2, s.Calls);
    }

    [Fact]
    public void SharedStationLabelsAreIndividuallySelectableAndGlyphsStayStableDuringTyping()
    {
        EditorPointLoad[] loads = [EditorPointLoad.Create(Guid.NewGuid(), Mm(300), PointLoadKind.Force, -1000, "F1"),
            EditorPointLoad.Create(Guid.NewGuid(), Mm(300), PointLoadKind.Moment, 100, "M1"),
            EditorPointLoad.Create(Guid.NewGuid(), Mm(300), PointLoadKind.Force, 0, "F2")];
        var document = new EditorDocument(Mm(1000), ProjectTemplates.Material, ProjectTemplates.Section, loads: loads);
        var frame = new BeamLayoutState().Update(document, 1250, 600)!;
        var scene = BeamRenderState.Create(document, frame, DesktopLayoutFixture.Measure);
        Assert.Equal(2, scene.Glyphs.Count);
        for (int i = 0; i < scene.Loads.Count; i++)
        {
            var label = scene.Annotations.Single(a => a.Id == loads[i].Id);
            Assert.Equal(loads[i].Id, scene.HitTestLabel(label.Bounds.Center.X, label.Bounds.Center.Y)!.Id);
            Assert.Equal(frame.Layout.Transform.PhysicalToScreen(0.3), scene.Loads[i].X);
            Assert.Equal(frame.Viewport.BeamY, scene.Loads[i].Y);
        }
        var editor = new EditorViewModel(document, () => { });
        editor.EditLoad(loads[1].Id);
        editor.LoadDraft!.PositionText = "700";
        editor.LoadDraft.ValueText = "-200";
        var changed = PointLoadSymbol.Layout(loads, frame, editor.LoadPreview, loads[1].Id);
        Assert.Equal(scene.Loads[1].X, changed[1].X);
        Assert.Equal(scene.Loads[1].Y, changed[1].Y);
        Assert.True(changed[1].IsPreview);
        Assert.Equal(-200, changed[1].Preview.Value);
        var resized = BeamRenderState.Create(document, new BeamLayoutState().Update(document, 1600, 800)!, DesktopLayoutFixture.Measure);
        var resizedLabel = resized.Annotations.Single(a => a.Id == loads[2].Id);
        Assert.Equal(loads[2].Id, resized.HitTestLabel(resizedLabel.Bounds.Center.X, resizedLabel.Bounds.Center.Y)!.Id);
        Assert.Equal(Mm(300), resized.Loads[2].Preview.Position);
    }

    [Fact]
    public void HitTestingUsesStationGlyphBoundsAndSignGeometryUsesCoreConvention()
    {
        var first = EditorPointLoad.Create(Guid.NewGuid(), Mm(300), PointLoadKind.Force, -1000, "F1");
        var second = EditorPointLoad.Create(Guid.NewGuid(), Mm(320), PointLoadKind.Moment, 100, "M1");
        var frame = DesktopLayoutFixture.Linear(0, 1000, 200, 1);
        var visuals = PointLoadSymbol.Layout([first, second], frame);
        Assert.Equal(first.Id, PointLoadSymbol.HitTest(visuals, 300, 180));
        Assert.Equal(second.Id, PointLoadSymbol.HitTest(visuals, 320, 200));
        Assert.Null(PointLoadSymbol.HitTest(visuals, 310, frame.Viewport.BeamY + 30));
        Assert.True(PointLoadSymbol.ForceTip(100).Y < 0);
        Assert.Equal(0, PointLoadSymbol.ForceTip(-100).Y);
        Assert.Equal(PointLoadSymbol.ForceTip(-1), PointLoadSymbol.ForceTip(-1000000));
        Assert.True(PointLoadSymbol.MomentSweep(100) > 0);
        Assert.True(PointLoadSymbol.MomentSweep(-100) < 0);
        Assert.Equal(0, PointLoadSymbol.MomentSweep(0));
        Assert.Equal(PointLoadSymbol.MomentSweep(1), PointLoadSymbol.MomentSweep(1000000));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(10)]
    public void CanvasMinimumHeightKeepsEveryCoincidentLabelAndSharedGlyphVisible(int count)
    {
        var loads = Enumerable.Range(0, count).Select(i => EditorPointLoad.Create(Guid.NewGuid(), Mm(500),
            i % 2 == 0 ? PointLoadKind.Force : PointLoadKind.Moment, 100, "Load " + i)).ToArray();
        var document = new EditorDocument(Mm(1000), ProjectTemplates.Material, ProjectTemplates.Section, loads: loads);
        var state = new BeamLayoutState();
        var initial = BeamRenderState.Create(document, state.Update(document, 1100, 220)!, DesktopLayoutFixture.Measure);
        var frame = state.Update(document, 1100, initial.MinimumPaneHeight)!;
        var scene = BeamRenderState.Create(document, frame, DesktopLayoutFixture.Measure);
        Assert.All(scene.Annotations, label =>
        {
            Assert.True(label.Bounds.Top >= 0);
            Assert.Equal(label.Id, scene.HitTestLabel(label.Bounds.Center.X, label.Bounds.Center.Y)!.Id);
        });
        Assert.All(scene.Glyphs, glyph => Assert.True(glyph.Bounds.Top >= 0));
        Assert.Equal(Math.Min(2, count), scene.Glyphs.Count);
        var preview = new PointLoadPreview(Mm(500), PointLoadKind.Moment, -100);
        var withPreview = BeamRenderState.Create(document, frame, DesktopLayoutFixture.Measure, loadPreview: preview, loadName: "M2");
        Assert.All(withPreview.Annotations, label => Assert.True(label.Bounds.Top >= 0));
        Assert.Same(frame.Layout.Transform, withPreview.Frame.Layout.Transform);
        Assert.Equal(count, document.Loads.Count);
    }

    [Fact]
    public void CommittedLabelSpaceRemainsStableAcrossToolHoverLeaveAndNewDraft()
    {
        var loads = Enumerable.Range(0, 4).Select(i => EditorPointLoad.Create(Guid.NewGuid(), Mm(500),
            PointLoadKind.Force, -1000, "Load " + i)).ToArray();
        var editor = new EditorViewModel(new(Mm(1000), ProjectTemplates.Material, ProjectTemplates.Section, loads: loads), () => { });
        var frame = new BeamLayoutState().Update(editor.Document, 1100, 600)!;
        double Height() => BeamRenderState.Create(editor.Document, frame, DesktopLayoutFixture.Measure).MinimumPaneHeight;
        double neutral = Height();
        editor.ToggleLoadTool(PointLoadKind.Moment);
        double reserved = Height();
        Assert.Equal(neutral, reserved);
        editor.HoverLoadPlacement(Mm(500));
        Assert.Equal(reserved, Height());
        editor.HoverLoadPlacement(null);
        Assert.Equal(reserved, Height());
        editor.HoverLoadPlacement(Mm(700));
        Assert.True(editor.PlaceLoad());
        Assert.Equal(reserved, Height());
        editor.CancelLoadInteraction();
        Assert.Equal(neutral, Height());
    }
}
