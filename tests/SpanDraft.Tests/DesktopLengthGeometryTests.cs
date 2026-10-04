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
            [new(Guid.NewGuid(), Length.FromMillimeters(850), SupportType.Pinned)]), () => { },
            beam => { Analyses++; return BeamAnalysis.Analyze(beam); });
        public BeamViewport Viewport => BeamViewport.Fit(1250, 600, Editor.Document.Length.Meters);
        public BeamLengthGeometry Geometry => BeamLengthGeometry.Create(Viewport, Editor.ConstraintConflict);
        public void Reject()
        {
            Editor.DimensionLength.Begin();
            Editor.DimensionLength.Text = "700";
            Assert.False(Editor.DimensionLength.Confirm());
        }
    }

    [Fact]
    public void RejectedShorteningMovesBeamAndDimensionOnlyWithinCommittedViewport()
    {
        var s = new Session();
        var document = s.Editor.Document;
        var viewport = s.Viewport;
        double supportX = viewport.BeamToScreen(0.85);
        s.Reject();
        Assert.Equal(viewport, s.Viewport);
        Assert.Equal(1.0, s.Viewport.LengthMeters);
        Assert.Equal(viewport.BeamToScreen(0.7), s.Geometry.EndX);
        Assert.Equal(viewport.BeamToScreen(0.7 / 2), s.Geometry.DimensionMidpoint);
        Assert.True(s.Geometry.HasGhost);
        Assert.True(s.Geometry.EndX < supportX && supportX < viewport.Right);
        Assert.Equal(supportX, s.Viewport.BeamToScreen(document.Supports[0].Position.Meters));
        Assert.Equal(document.Supports[0].Id, Assert.Single(s.Editor.ConflictEntityIds));
        Assert.Same(document, s.Editor.Document);
        Assert.Equal(1, s.Analyses);
    }

    [Fact]
    public void FractionalRequestedLengthRemainsExactWithoutSnapOrNewTransform()
    {
        var viewport = BeamViewport.Fit(1250, 600, 1.0);
        var requested = Length.FromMeters(0.7005000000000001);
        var conflict = new ConstraintConflictState(requested, [Guid.NewGuid()]);
        var geometry = BeamLengthGeometry.Create(viewport, conflict);
        Assert.Equal(viewport.BeamToScreen(requested.Meters), geometry.EndX);
        Assert.Equal(viewport.BeamToScreen(requested.Meters / 2), geometry.DimensionMidpoint);
        Assert.NotEqual(viewport.BeamToScreen(0.701), geometry.EndX);
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
        if (loseFocus)
        {
            s.Editor.DimensionLength.LoseFocus();
            Assert.True(s.Geometry.HasGhost);
            Assert.Equal(s.Viewport.BeamToScreen(0.7), s.Geometry.EndX);
            Assert.True(s.Editor.DimensionLength.HasError);
        }
        s.Editor.DimensionLength.Cancel();
        Assert.Equal(s.Viewport.Right, s.Geometry.EndX);
        Assert.Equal(s.Viewport.Midpoint, s.Geometry.DimensionMidpoint);
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
        Assert.Equal(viewport.Right, s.Geometry.EndX);
        Assert.Equal(viewport.Midpoint, s.Geometry.DimensionMidpoint);
        Assert.Same(document, s.Editor.Document);
        Assert.Equal(1, s.Analyses);
        Assert.True(s.Editor.DimensionLength.Confirm());
        Assert.Equal(s.Viewport.Right, s.Geometry.EndX);
        Assert.Equal(s.Viewport.Midpoint, s.Geometry.DimensionMidpoint);
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
        Assert.Equal(s.Viewport.Right, s.Geometry.EndX);
        Assert.Equal(s.Viewport.Midpoint, s.Geometry.DimensionMidpoint);
        Assert.Same(document, s.Editor.Document);
        Assert.Equal(1, s.Analyses);
    }

    [Fact]
    public void RequestedLengthWithoutBlockingObjectsDoesNotPreviewGeometry()
    {
        var viewport = BeamViewport.Fit(1250, 600, 1.0);
        var geometry = BeamLengthGeometry.Create(viewport,
            new ConstraintConflictState(Length.FromMillimeters(700), []));
        Assert.Equal(viewport.Right, geometry.EndX);
        Assert.Equal(viewport.Midpoint, geometry.DimensionMidpoint);
        Assert.False(geometry.HasGhost);
    }
}
