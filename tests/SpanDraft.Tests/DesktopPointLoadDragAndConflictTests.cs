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
                loads: [EditorPointLoad.Create(Guid.NewGuid(), Mm(position), kind, -50)]), () => { },
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
        var v = new BeamViewport(72, 1178, 200, 160, 1.0005);
        double press = v.BeamToScreen(0.3) + 8;
        var gesture = new PointLoadDragGesture(s.LoadId, Mm(300), press);
        Assert.Null(gesture.Update(v, press + 3, 100, 1250, 500));
        Assert.False(gesture.IsDragging);
        var clamped = gesture.Update(v, pointerX, 100, 1250, 500);
        Assert.Equal(Mm(expected), clamped);
        s.Editor.BeginLoadDrag(s.LoadId);
        s.Editor.UpdateLoadDrag(clamped);
        Assert.True(s.Editor.EndLoadDrag());
        Assert.Equal(Mm(expected), s.Editor.LoadPreview!.Position);
        var returned = gesture.Update(v, v.BeamToScreen(0.65) + 8, 100, 1250, 500);
        Assert.Equal(Mm(650), returned);
        Assert.Null(gesture.Update(v, 500, -1, 1250, 500));
        Assert.Null(gesture.Update(v, 500, 501, 1250, 500));
        Assert.Equal(1, s.Calls);
    }

    [Theory]
    [InlineData(PointLoadKind.Force)]
    [InlineData(PointLoadKind.Moment)]
    public void EndpointPlacementAndDragUseExactFractionalLengthAndMillimeterSnap(PointLoadKind kind)
    {
        var s = new Session(kind);
        var v = new BeamViewport(72, 1178, 200, 160, 1.0005);
        s.Editor.ToggleLoadTool(kind);
        s.Editor.HoverLoadPlacement(SupportSnap.Placement(v, v.Right - 2, 200));
        Assert.Equal(s.Editor.Document.Length, s.Editor.LoadPreview!.Position);
        Assert.True(s.Editor.PlaceLoad());
        Assert.True(s.Editor.ConfirmLoad());
        Assert.Equal(s.Editor.Document.Length, s.Editor.Document.Loads[1].Position);
        var gesture = new PointLoadDragGesture(s.LoadId, Mm(300), v.BeamToScreen(0.3));
        Assert.Equal(Mm(456), gesture.Update(v, v.BeamToScreen(0.4564), 200, 1250, 500));
        Assert.Equal(s.Editor.Document.Length, gesture.Update(v, v.Right - 2, 200, 1250, 500));
        Assert.Null(SupportSnap.Placement(v, 600, 219));
        Assert.Null(SupportSnap.Placement(v, -100, 200));
    }

    [Fact]
    public void OtherObjectsCannotReplaceOrDragDuringActiveLoadSession()
    {
        var s = new Session(PointLoadKind.Force);
        var other = EditorPointLoad.Create(Guid.NewGuid(), Mm(700), PointLoadKind.Moment, 100);
        var editor = new EditorViewModel(s.Editor.Document.WithLoads([s.Editor.Document.Loads[0], other])
            .WithSupports([new(Guid.NewGuid(), Mm(0), SupportType.Fixed)]), () => { });
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
        EditorSupport[] supports = [new(Guid.NewGuid(), Mm(800), SupportType.Roller), new(Guid.NewGuid(), Mm(0), SupportType.Fixed)];
        EditorPointLoad[] loads = [EditorPointLoad.Create(Guid.NewGuid(), Mm(850), PointLoadKind.Force, -1000),
            EditorPointLoad.Create(Guid.NewGuid(), Mm(900), PointLoadKind.Moment, 100),
            EditorPointLoad.Create(Guid.NewGuid(), Mm(600), PointLoadKind.Force, 0)];
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
        var viewport = BeamViewport.Fit(1250, 600, 1);
        var geometry = BeamLengthGeometry.Create(viewport, editor.ConstraintConflict);
        Assert.True(geometry.HasGhost);
        Assert.Equal(viewport.BeamToScreen(0.7), geometry.EndX);
        if (loseFocus) editor.DimensionLength.LoseFocus();
        else editor.DimensionLength.Cancel();
        Assert.False(editor.DimensionLength.IsEditing);
        Assert.False(editor.DimensionLength.HasError);
        Assert.Null(editor.ConstraintConflict);
        Assert.Empty(editor.ConflictEntityIds);
        Assert.False(BeamLengthGeometry.Create(viewport, editor.ConstraintConflict).HasGhost);
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
    public void StackedSymbolsAreIndividuallySelectableAndStableDuringValueAndPositionTyping()
    {
        EditorPointLoad[] loads = [EditorPointLoad.Create(Guid.NewGuid(), Mm(300), PointLoadKind.Force, -1000),
            EditorPointLoad.Create(Guid.NewGuid(), Mm(300), PointLoadKind.Moment, 100),
            EditorPointLoad.Create(Guid.NewGuid(), Mm(300), PointLoadKind.Force, 0)];
        var viewport = BeamViewport.Fit(1250, 600, 1);
        var visuals = PointLoadSymbol.Layout(loads, viewport);
        for (int i = 0; i < visuals.Count; i++)
        {
            Assert.Equal(loads[i].Id, PointLoadSymbol.HitTest(visuals, visuals[i].X, visuals[i].Y));
            Assert.Equal(viewport.BeamToScreen(0.3), visuals[i].X);
            if (i > 0) Assert.Equal(PointLoadSymbol.LaneSpacing, visuals[i - 1].Y - visuals[i].Y);
        }
        var editor = new EditorViewModel(new(Mm(1000), ProjectTemplates.Material, ProjectTemplates.Section, loads: loads), () => { });
        editor.EditLoad(loads[1].Id);
        editor.LoadDraft!.PositionText = "700";
        editor.LoadDraft.ValueText = "-200";
        var changed = PointLoadSymbol.Layout(loads, viewport, editor.LoadPreview, loads[1].Id);
        Assert.Equal(visuals[1].X, changed[1].X);
        Assert.Equal(visuals[1].Y, changed[1].Y);
        Assert.True(changed[1].IsPreview);
        Assert.Equal(-200, changed[1].Preview.Value);
        var resized = PointLoadSymbol.Layout(loads, BeamViewport.Fit(1600, 800, 1));
        Assert.Equal(loads[2].Id, PointLoadSymbol.HitTest(resized, resized[2].X, resized[2].Y));
        Assert.Equal(Mm(300), resized[2].Preview.Position);
    }

    [Fact]
    public void HitTestingUsesNearestThenDocumentOrderAndSignGeometryUsesCoreConvention()
    {
        var first = EditorPointLoad.Create(Guid.NewGuid(), Mm(300), PointLoadKind.Force, -1000);
        var second = EditorPointLoad.Create(Guid.NewGuid(), Mm(320), PointLoadKind.Moment, 100);
        var viewport = new BeamViewport(0, 1000, 200, 160, 1);
        var visuals = PointLoadSymbol.Layout([first, second], viewport);
        Assert.Equal(first.Id, PointLoadSymbol.HitTest(visuals, 310, visuals[0].Y));
        Assert.Equal(second.Id, PointLoadSymbol.HitTest(visuals, 315, visuals[0].Y));
        Assert.Null(PointLoadSymbol.HitTest(visuals, 310, viewport.BeamY));
        Assert.True(PointLoadSymbol.ForceTip(100).Y < 0);
        Assert.True(PointLoadSymbol.ForceTip(-100).Y > 0);
        Assert.Equal(PointLoadSymbol.ForceTip(-1), PointLoadSymbol.ForceTip(-1000000));
        Assert.True(PointLoadSymbol.MomentSweep(100) > 0); // Mathematical positive angle, screen Y is inverted.
        Assert.True(PointLoadSymbol.MomentSweep(-100) < 0);
        Assert.Equal(0, PointLoadSymbol.MomentSweep(0));
        Assert.Equal(PointLoadSymbol.MomentSweep(1), PointLoadSymbol.MomentSweep(1000000));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(10)]
    public void CanvasMinimumHeightKeepsEveryCoincidentSymbolAndHitZoneVisible(int count)
    {
        var loads = Enumerable.Range(0, count).Select(i => EditorPointLoad.Create(Guid.NewGuid(), Mm(500),
            i % 2 == 0 ? PointLoadKind.Force : PointLoadKind.Moment, 100)).ToArray();
        double height = PointLoadSymbol.MinimumHeight(loads);
        var visuals = PointLoadSymbol.Layout(loads, BeamViewport.Fit(1100, height, 1));
        Assert.All(visuals, v =>
        {
            Assert.True(v.Y - PointLoadSymbol.HalfSize - PointLoadSymbol.HitPadding >= 0);
            Assert.Equal(v.Id, PointLoadSymbol.HitTest(visuals, v.X, v.Y));
        });
        var preview = new PointLoadPreview(Mm(500), PointLoadKind.Moment, -100);
        Assert.Equal(height + 2 * PointLoadSymbol.LaneSpacing, PointLoadSymbol.MinimumHeight(loads, preview));
        Assert.Equal(height, PointLoadSymbol.MinimumHeight(loads, preview, loads[0].Id));
        Assert.Equal(0, PointLoadSymbol.MinimumHeight([]));
    }

    [Fact]
    public void ActiveToolReservesCanvasSpaceBeforeHoverAndRetainsItOnHoverLeave()
    {
        var loads = Enumerable.Range(0, 4).Select(_ => EditorPointLoad.Create(Guid.NewGuid(), Mm(500),
            PointLoadKind.Force, -1000)).ToArray();
        var editor = new EditorViewModel(new(Mm(1000), ProjectTemplates.Material, ProjectTemplates.Section, loads: loads), () => { });
        double Height() => PointLoadSymbol.MinimumHeight(editor.Document.Loads,
            reserveAdditionalLane: editor.LoadState != LoadInteraction.Neutral);
        double neutral = Height();
        editor.ToggleLoadTool(PointLoadKind.Moment);
        double reserved = Height();
        Assert.Equal(neutral + 2 * PointLoadSymbol.LaneSpacing, reserved);
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
