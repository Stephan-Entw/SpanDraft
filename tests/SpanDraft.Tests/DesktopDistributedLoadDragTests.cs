using SpanDraft.Analysis;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Controls;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using Xunit;

namespace SpanDraft.Tests;

public sealed class DesktopDistributedLoadDragTests
{
    private static Length Mm(double value) => Length.FromMillimeters(value);
    private sealed class Session
    {
        public readonly EditorUniformDistributedLoad Load = new(Guid.NewGuid(), Mm(200), Mm(800), ForcePerLength.FromNewtonsPerMeter(-500), "q1");
        public EditorViewModel Editor { get; }
        public int Calls;
        public Session() => Editor = new(new EditorDocument(Mm(1000), ProjectTemplates.Material, ProjectTemplates.Section,
            distributedLoads: [Load]), () => { }, beam => { Calls++; return BeamAnalysis.Analyze(beam); });
    }

    [Theory]
    [InlineData(DistributedLoadEndpoint.Start, false)]
    [InlineData(DistributedLoadEndpoint.End, false)]
    [InlineData(DistributedLoadEndpoint.Start, true)]
    [InlineData(DistributedLoadEndpoint.End, true)]
    public void RepeatedEndpointDragsRetainBuffersAndCommitOnce(DistributedLoadEndpoint endpoint, bool cancel)
    {
        var s = new Session();
        var editor = s.Editor;
        editor.EditDistributedLoad(s.Load.Id);
        var draft = editor.DistributedLoadDraft!;
        draft.NameText = "Payload";
        draft.IntensityText = "unfinished";
        if (endpoint == DistributedLoadEndpoint.Start) draft.EndText = "850"; else draft.StartText = "150";
        Assert.True(editor.BeginDistributedLoadDrag(s.Load.Id, endpoint));
        editor.UpdateDistributedLoadDrag(Mm(endpoint == DistributedLoadEndpoint.Start ? 300 : 700));
        Assert.False(editor.IsDistributedLoadFlyoutVisible);
        Assert.False(editor.ConfirmDistributedLoad());
        editor.DeleteDistributedLoad();
        Assert.Single(editor.Document.DistributedLoads);
        Assert.True(editor.EndDistributedLoadDrag());
        Assert.Same(draft, editor.DistributedLoadDraft);
        Assert.Equal("unfinished", draft.IntensityText);
        Assert.Equal("Payload", draft.NameText);
        Assert.Equal(endpoint == DistributedLoadEndpoint.Start ? "850" : "150",
            endpoint == DistributedLoadEndpoint.Start ? draft.EndText : draft.StartText);
        Assert.True(editor.BeginDistributedLoadDrag(s.Load.Id, endpoint));
        editor.UpdateDistributedLoadDrag(Mm(endpoint == DistributedLoadEndpoint.Start ? 350 : 650));
        Assert.True(editor.EndDistributedLoadDrag());
        Assert.Equal(Mm(endpoint == DistributedLoadEndpoint.Start ? 800 : 200),
            endpoint == DistributedLoadEndpoint.Start ? draft.Preview.EndPosition : draft.Preview.StartPosition);
        Assert.Equal(s.Load, editor.Document.DistributedLoads[0]);
        Assert.Equal(1, s.Calls);
        draft.IntensityText = "0";
        if (cancel) editor.CancelEditorInteraction(); else Assert.True(editor.ConfirmDistributedLoad());
        var load = editor.Document.DistributedLoads[0];
        Assert.Equal(s.Load.Id, load.Id);
        Assert.Equal(cancel ? s.Load : s.Load with { Name = "Payload", StartPosition = Mm(endpoint == DistributedLoadEndpoint.Start ? 350 : 150),
            EndPosition = Mm(endpoint == DistributedLoadEndpoint.End ? 650 : 850), Intensity = ForcePerLength.FromNewtonsPerMeter(0) }, load);
        Assert.Equal(cancel ? 1 : 2, s.Calls);
    }

    [Theory]
    [InlineData(DistributedLoadEndpoint.Start, 800)]
    [InlineData(DistributedLoadEndpoint.Start, 900)]
    [InlineData(DistributedLoadEndpoint.End, 200)]
    [InlineData(DistributedLoadEndpoint.End, 100)]
    public void CrossedOrCoincidentEndpointsNeverChangeTheDraftRange(DistributedLoadEndpoint endpoint, double position)
    {
        var s = new Session();
        s.Editor.EditDistributedLoad(s.Load.Id);
        var draft = s.Editor.DistributedLoadDraft!;
        draft.StartText = "250"; draft.EndText = "750"; draft.IntensityText = "broken";
        var preview = draft.Preview;
        Assert.True(s.Editor.BeginDistributedLoadDrag(s.Load.Id, endpoint));
        s.Editor.UpdateDistributedLoadDrag(Mm(position));
        Assert.True(s.Editor.DistributedLoadPreview!.StartPosition.Meters < s.Editor.DistributedLoadPreview.EndPosition.Meters);
        Assert.True(s.Editor.DistributedLoadPreview.IsInvalid);
        Assert.False(s.Editor.EndDistributedLoadDrag());
        Assert.Same(draft, s.Editor.DistributedLoadDraft);
        Assert.Equal(preview, draft.Preview);
        Assert.Equal("250", draft.StartText);
        Assert.Equal("750", draft.EndText);
        Assert.Equal("broken", draft.IntensityText);
        Assert.Equal(1, s.Calls);
    }

    [Theory]
    [InlineData(DistributedLoadEndpoint.Start)]
    [InlineData(DistributedLoadEndpoint.End)]
    public void InvalidDragFromNeutralCancelsButPointerCanRecoverBeforeRelease(DistributedLoadEndpoint endpoint)
    {
        var s = new Session();
        Assert.True(s.Editor.BeginDistributedLoadDrag(s.Load.Id, endpoint));
        s.Editor.UpdateDistributedLoadDrag(null);
        Assert.False(s.Editor.EndDistributedLoadDrag());
        Assert.True(s.Editor.IsEditorNeutral);
        Assert.True(s.Editor.BeginDistributedLoadDrag(s.Load.Id, endpoint));
        s.Editor.UpdateDistributedLoadDrag(Mm(endpoint == DistributedLoadEndpoint.Start ? 900 : 100));
        s.Editor.UpdateDistributedLoadDrag(Mm(endpoint == DistributedLoadEndpoint.Start ? 300 : 700));
        Assert.True(s.Editor.EndDistributedLoadDrag());
        Assert.True(s.Editor.IsDistributedLoadFlyoutVisible);
        Assert.Equal(1, s.Calls);
        s.Editor.CancelEditorInteraction();
        Assert.Equal(s.Load, s.Editor.Document.DistributedLoads[0]);
    }

    [Fact]
    public void NewDraftEndpointsCanBeDraggedWithoutAssigningIdOrConsumingCandidate()
    {
        var editor = new EditorViewModel(new(Mm(1000), ProjectTemplates.Material, ProjectTemplates.Section), () => { });
        editor.ToggleDistributedLoadTool();
        editor.HoverDistributedLoadPlacement(Mm(200)); editor.PlaceDistributedLoadEndpoint();
        editor.HoverDistributedLoadPlacement(Mm(800)); editor.PlaceDistributedLoadEndpoint();
        var draft = editor.DistributedLoadDraft!;
        Assert.True(editor.BeginDistributedLoadDrag(null, DistributedLoadEndpoint.Start));
        editor.UpdateDistributedLoadDrag(Mm(100));
        Assert.True(editor.EndDistributedLoadDrag());
        Assert.Same(draft, editor.DistributedLoadDraft);
        Assert.Null(draft.OriginalId);
        Assert.Equal(Mm(100), draft.Preview.StartPosition);
        Assert.Empty(editor.Document.DistributedLoads);
        Assert.Equal(1, editor.Document.NamingState.NextDistributedLoadNumber);
        Assert.True(editor.ConfirmDistributedLoad());
        Assert.NotEqual(Guid.Empty, editor.Document.DistributedLoads[0].Id);
    }

    [Fact]
    public void GestureUsesThresholdGrabOffsetFractionalEndpointsAndFrozenTransform()
    {
        var frame = DesktopLayoutFixture.Linear(72, 1072, 200, 1.0005);
        double x = frame.Layout.Transform.PhysicalToScreen(.2);
        var gesture = new DistributedLoadDragGesture(Guid.NewGuid(), DistributedLoadEndpoint.Start, Mm(200), x + 3, frame.Layout.Transform);
        Assert.Null(gesture.Update(x + 6, 200, 400));
        Assert.False(gesture.IsDragging);
        var position = gesture.Update(x + 103, 200, 400);
        Assert.Equal(SupportSnap.AtX(frame.Layout.Transform, x + 100), position);
        Assert.Same(frame.Layout.Transform, gesture.Snapshot);
        Assert.Equal(Mm(0), gesture.Update(-1000, 200, 400));
        Assert.Equal(Length.FromMeters(1.0005), gesture.Update(3000, 200, 400));
        Assert.Null(gesture.Update(x + 103, -1, 400));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MixedLengthConflictIncludesUdlAndRejectedFocusLossFullyRestores(bool loseFocus)
    {
        var s = new Session();
        var support = new EditorSupport(Guid.NewGuid(), Mm(900), SupportType.Pinned, "A");
        var point = new EditorPointForce(Guid.NewGuid(), Mm(950), Force.FromNewtons(-100), "F1");
        var editor = new EditorViewModel(s.Editor.Document.WithSupports([support]).WithLoads([point]), () => { });
        var original = editor.Document;
        editor.DimensionLength.Begin(); editor.DimensionLength.Text = "700";
        if (loseFocus) editor.DimensionLength.LoseFocus(); else Assert.False(editor.DimensionLength.Confirm());
        Assert.Same(original, editor.Document);
        if (loseFocus)
        {
            Assert.Null(editor.ConstraintConflict);
            Assert.False(editor.DimensionLength.HasError);
            Assert.Equal("1000", editor.DimensionLength.Text);
        }
        else Assert.Equal(new[] { support.Id, point.Id, s.Load.Id }, editor.ConflictEntityIds);
    }

    [Fact]
    public void UdlEditsAndDeleteReevaluateConflictAndLengthCanEndExactlyAtUdlEnd()
    {
        var s = new Session();
        var editor = s.Editor;
        editor.DimensionLength.Begin(); editor.DimensionLength.Text = "700";
        Assert.False(editor.DimensionLength.Confirm());
        editor.EditDistributedLoad(s.Load.Id);
        editor.DistributedLoadDraft!.NameText = "Payload";
        Assert.True(editor.ConfirmDistributedLoad());
        Assert.Contains(s.Load.Id, editor.ConflictEntityIds);
        Assert.Equal(1, s.Calls);
        editor.EditDistributedLoad(s.Load.Id);
        editor.DistributedLoadDraft!.EndText = "700";
        Assert.True(editor.ConfirmDistributedLoad());
        Assert.Null(editor.ConstraintConflict);
        Assert.Equal(Mm(1000), editor.Document.Length);
        Assert.Equal(2, s.Calls);
        editor.DimensionLength.Begin(); editor.DimensionLength.Text = "700";
        Assert.True(editor.DimensionLength.Confirm());
        Assert.Equal(3, s.Calls);
        editor.DimensionLength.Begin(); editor.DimensionLength.Text = "650";
        Assert.False(editor.DimensionLength.Confirm());
        editor.EditDistributedLoad(s.Load.Id); editor.DeleteDistributedLoad();
        Assert.Null(editor.ConstraintConflict);
        Assert.Equal(4, s.Calls);
    }

    [Fact]
    public void ToolSwitchAndSessionCancelClearDraftAndBlockOtherEntityInteractions()
    {
        var s = new Session();
        s.Editor.EditDistributedLoad(s.Load.Id);
        Assert.False(s.Editor.EditLoad(Guid.NewGuid()));
        Assert.False(s.Editor.BeginSupportDrag(Guid.NewGuid()));
        s.Editor.ToggleLoadTool(PointLoadKind.Force);
        Assert.Null(s.Editor.DistributedLoadDraft);
        Assert.True(s.Editor.IsForceTool);
        s.Editor.ToggleDistributedLoadTool();
        Assert.Null(s.Editor.LoadTool);
        Assert.True(s.Editor.IsDistributedLoadTool);
        s.Editor.ToggleSupportTool(SupportType.Fixed);
        Assert.False(s.Editor.IsDistributedLoadTool);
        Assert.True(s.Editor.IsFixedTool);
        s.Editor.CancelEditorInteraction();
        Assert.True(s.Editor.IsEditorNeutral);
        Assert.Equal(1, s.Calls);
    }
}
