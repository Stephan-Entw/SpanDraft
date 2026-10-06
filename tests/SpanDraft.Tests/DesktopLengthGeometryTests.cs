using SpanDraft.Analysis;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Controls;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using Xunit;

namespace SpanDraft.Tests;

public sealed class DesktopLengthGeometryTests
{
    private sealed class Session
    {
        public int Analyses { get; private set; }
        public EditorViewModel Editor { get; }
        public Session() => Editor = new(new EditorDocument(Length.FromMillimeters(1000),
            ProjectTemplates.Material, ProjectTemplates.Section,
            [new(Guid.NewGuid(), Length.FromMillimeters(850), SupportType.Pinned, "A")]), () => { },
            beam => { Analyses++; return BeamAnalysis.Analyze(beam); });
        private readonly BeamLayoutState _layout = new();
        public BeamLayoutFrame Viewport => _layout.Update(Editor.Document, 1250, 600)!;
        public BeamConflictGeometry Geometry => BeamConflictGeometry.Create(Viewport.Layout.Transform, Editor.ConstraintConflict);
        public void Reject()
        {
            Editor.DimensionLength.Begin();
            Editor.DimensionLength.Text = "700";
            Assert.False(Editor.DimensionLength.Confirm());
        }
    }

    [Fact]
    public void RejectedShorteningMovesBeamOnlyWithinCommittedTransform()
    {
        var s = new Session();
        var document = s.Editor.Document;
        var viewport = s.Viewport;
        double supportX = viewport.Layout.Transform.PhysicalToScreen(0.85);
        s.Reject();
        Assert.Equal(viewport, s.Viewport);
        Assert.Equal(1.0, s.Viewport.Layout.Stations[^1].PhysicalX);
        Assert.Equal(viewport.Layout.Transform.PhysicalToScreen(0.7), s.Geometry.EndX);
        Assert.True(s.Geometry.HasGhost);
        Assert.True(s.Geometry.EndX < supportX && supportX < viewport.Layout.Stations[^1].ScreenX);
        Assert.Equal(supportX, s.Viewport.Layout.Transform.PhysicalToScreen(document.Supports[0].Position.Meters));
        Assert.Equal(document.Supports[0].Id, Assert.Single(s.Editor.ConflictEntityIds));
        Assert.Same(document, s.Editor.Document);
        Assert.Equal(1, s.Analyses);
    }

    [Fact]
    public void FractionalRequestedLengthRemainsExactWithoutSnapOrNewTransform()
    {
        var viewport = DesktopLayoutFixture.Fit(1250, 600, 1.0);
        var requested = Length.FromMeters(0.7005000000000001);
        var conflict = new ConstraintConflictState(requested, [Guid.NewGuid()]);
        var geometry = BeamConflictGeometry.Create(viewport.Layout.Transform, conflict);
        Assert.Equal(viewport.Layout.Transform.PhysicalToScreen(requested.Meters), geometry.EndX);
        Assert.NotEqual(viewport.Layout.Transform.PhysicalToScreen(0.701), geometry.EndX);
        Assert.Equal(requested, conflict.RequestedLength);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancelRestoresCommittedGeometryIncludingAfterRejectedFocusLoss(bool loseFocus)
    {
        var s = new Session();
        var document = s.Editor.Document;
        s.Reject();
        Assert.True(s.Geometry.HasGhost);
        Assert.Equal(s.Viewport.Layout.Transform.PhysicalToScreen(0.7), s.Geometry.EndX);
        Assert.True(s.Editor.DimensionLength.IsEditing);
        Assert.True(s.Editor.DimensionLength.HasError);
        Assert.Single(s.Editor.ConflictEntityIds);
        if (loseFocus)
        {
            s.Editor.DimensionLength.LoseFocus();
            Assert.False(s.Geometry.HasGhost);
            Assert.Equal(s.Viewport.Layout.Stations[^1].ScreenX, s.Geometry.EndX);
            Assert.Equal("1000", s.Editor.DimensionLength.Text);
            Assert.False(s.Editor.DimensionLength.IsEditing);
            Assert.False(s.Editor.DimensionLength.HasError);
            Assert.Empty(s.Editor.ConflictEntityIds);
            Assert.Null(s.Editor.ConstraintConflict);
            Assert.Same(document, s.Editor.Document);
            Assert.Equal(1, s.Analyses);
        }
        s.Editor.DimensionLength.Cancel();
        Assert.Equal(s.Viewport.Layout.Stations[^1].ScreenX, s.Geometry.EndX);
        Assert.False(s.Geometry.HasGhost);
        Assert.Null(s.Editor.ConstraintConflict);
        Assert.Same(document, s.Editor.Document);
        Assert.Equal(1, s.Analyses);
    }

    [Theory]
    [InlineData("900", 2)]
    [InlineData("1000", 1)]
    public void PermittedCorrectionRestoresNormalGeometryBeforeCommit(string text, int finalAnalyses)
    {
        var s = new Session();
        var document = s.Editor.Document;
        var viewport = s.Viewport;
        s.Reject();
        s.Editor.DimensionLength.Text = text;
        Assert.False(s.Geometry.HasGhost);
        Assert.Equal(viewport.Layout.Stations[^1].ScreenX, s.Geometry.EndX);
        Assert.Same(document, s.Editor.Document);
        Assert.Equal(1, s.Analyses);
        Assert.True(s.Editor.DimensionLength.Confirm());
        Assert.Equal(s.Viewport.Layout.Stations[^1].ScreenX, s.Geometry.EndX);
        Assert.False(s.Geometry.HasGhost);
        Assert.Equal(finalAnalyses, s.Analyses);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("-")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("NaN")]
    public void InvalidOrNonPositiveBufferRemovesGeometryPreviewWithoutCommit(string text)
    {
        var s = new Session();
        var document = s.Editor.Document;
        s.Reject();
        s.Editor.DimensionLength.Text = text;
        Assert.False(s.Editor.DimensionLength.Confirm());
        Assert.Null(s.Editor.ConstraintConflict);
        Assert.False(s.Geometry.HasGhost);
        Assert.Equal(s.Viewport.Layout.Stations[^1].ScreenX, s.Geometry.EndX);
        Assert.Same(document, s.Editor.Document);
        Assert.Equal(1, s.Analyses);
    }

    [Fact]
    public void RequestedLengthWithoutBlockingObjectsDoesNotPreviewGeometry()
    {
        var viewport = DesktopLayoutFixture.Fit(1250, 600, 1.0);
        var geometry = BeamConflictGeometry.Create(viewport.Layout.Transform, new ConstraintConflictState(Length.FromMillimeters(700), []));
        Assert.Equal(viewport.Layout.Stations[^1].ScreenX, geometry.EndX);
        Assert.False(geometry.HasGhost);
    }
}
