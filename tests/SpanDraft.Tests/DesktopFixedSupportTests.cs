using SpanDraft.Analysis;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Controls;
using SpanDraft.Desktop.Layout;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using Xunit;

namespace SpanDraft.Tests;

public sealed class DesktopFixedSupportTests
{
    private static EditorSupport Support(double position, SupportType type = SupportType.Fixed) =>
        new(Guid.NewGuid(), Length.FromMeters(position), type, "A");
    private static EditorDocument Document(EditorSupport support, double length = 1) =>
        DesktopLayoutFixture.Document(length).WithSupports([support]);
    private static BeamRenderState Scene(EditorDocument document, BeamLayoutFrame frame, EditorViewModel? editor = null) =>
        BeamRenderState.Create(document, frame, DesktopLayoutFixture.Measure, editor?.Preview, editor?.HiddenSupportId);

    [Theory]
    [InlineData(false, -9, 9.75, 1)]
    [InlineData(true, 9, 1, 9.75)]
    public void GeometryHasFiveEquallySpaced45DegreeHatchesAndFlushBottom(bool mirrored, double endX, double left, double right)
    {
        var geometry = new FixedSupportGeometry(mirrored);
        Assert.Equal((0d, -24d, 0d, 24d), geometry.Wall);
        Assert.Equal(2, SchematicMetrics.FixedWallStrokeWidth);
        var hatches = geometry.Hatches.ToArray();
        Assert.Equal(5, hatches.Length);
        Assert.Equal(new[] { -24d, -14.25, -4.5, 5.25, 15 }, hatches.Select(h => h.StartY));
        Assert.All(hatches, hatch =>
        {
            Assert.Equal(0, hatch.StartX);
            Assert.Equal(endX, hatch.EndX);
            Assert.Equal(9, hatch.EndY - hatch.StartY);
        });
        Assert.Equal(geometry.Wall.EndY, hatches[^1].EndY);
        Assert.Equal(left, geometry.LeftExtent);
        Assert.Equal(right, geometry.RightExtent);
    }

    [Fact]
    public void MirroringNegatesOnlyTheHorizontalCoordinates()
    {
        var normal = new FixedSupportGeometry(false);
        var mirrored = new FixedSupportGeometry(true);
        Assert.Equal(normal.Wall, mirrored.Wall);
        Assert.Equal(normal.Hatches.Select(h => (-h.StartX, h.StartY, -h.EndX, h.EndY)), mirrored.Hatches);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(.5, false)]
    [InlineData(1, true)]
    public void SceneAndRequirementsAgreeOnFixedSupportOrientation(double position, bool mirrored)
    {
        var support = Support(position);
        var document = Document(support);
        var frame = new BeamLayoutState().Update(document, 1100, 600)!;
        var visual = Assert.Single(Scene(document, frame).Supports);
        Assert.Equal(mirrored, visual.IsMirrored);
        Assert.False(visual.IsPreview);
        var station = frame.Layout.Stations.Single(s => s.PhysicalX == position);
        Assert.Equal(mirrored ? 1 : 9.75, station.LeftExtent);
        Assert.Equal(mirrored ? 9.75 : 1, station.RightExtent);
        Assert.Equal(frame.Layout.Transform.PhysicalToScreen(position), visual.X);
    }

    [Fact]
    public void EvenTheAdjacentDoubleBeforeTheEndIsNotMirrored()
    {
        double position = Math.BitDecrement(1);
        var document = Document(Support(position));
        var frame = DesktopLayoutFixture.Linear(0, 1000, 200, 1);
        Assert.False(Assert.Single(Scene(document, frame).Supports).IsMirrored);
        Assert.False(FixedSupportGeometry.AtPosition(position, 1).IsMirrored);
        var requirement = StationRequirementBuilder.FromDocument(document).Single(r => r.PhysicalX == position);
        Assert.Equal(9.75, requirement.LeftExtent);
        Assert.Equal(1, requirement.RightExtent);
    }

    [Theory]
    [InlineData(SupportType.Pinned)]
    [InlineData(SupportType.Roller)]
    public void OtherSupportTypesRemainUnmirroredAtTheEnd(SupportType type)
    {
        var document = Document(Support(1, type));
        var frame = new BeamLayoutState().Update(document, 1100, 600)!;
        Assert.False(Assert.Single(Scene(document, frame).Supports).IsMirrored);
    }

    [Theory]
    [InlineData(0, -1)]
    [InlineData(.5, -1)]
    [InlineData(1, 1)]
    public void CommittedAndPreviewHitZonesFollowTheHatchSide(double position, double side)
    {
        var support = Support(position);
        var document = Document(support);
        var frame = new BeamLayoutState().Update(document, 1100, 600)!;
        double x = frame.Layout.Transform.PhysicalToScreen(position), y = frame.Viewport.BeamY;
        var preview = new SupportPreview(support.Position, support.Type);
        // The generous existing area extends beyond the actual nine-DIP hatch.
        Assert.Equal(support.Id, SupportSymbol.HitTest(document.Supports, frame.Layout.Transform, y, x + side * 20, y + 20));
        Assert.True(SupportSymbol.Contains(preview, frame.Layout.Transform, y, x + side * 20, y + 20));
        Assert.Null(SupportSymbol.HitTest(document.Supports, frame.Layout.Transform, y, x - side * 10, y));
        Assert.False(SupportSymbol.Contains(preview, frame.Layout.Transform, y, x - side * 10, y));
        Assert.False(SupportSymbol.Contains(preview, frame.Layout.Transform, y, x, y + 30));
    }

    [Fact]
    public void PlacementUsesExactEndpointSnapIncludingNonMillimeterBeamLength()
    {
        var document = DesktopLayoutFixture.Document(1.0005);
        var editor = new EditorViewModel(document, () => { });
        var layout = new BeamLayoutState();
        var frame = layout.Update(document, 1100, 600)!;
        editor.ToggleSupportTool(SupportType.Fixed);
        Assert.Same(frame, layout.BeginInteraction(BeamPointerInteraction.SupportPlacement));
        double endX = frame.Layout.Stations[^1].ScreenX;
        editor.HoverPlacement(SupportSnap.Placement(frame.Layout.Transform, frame.Viewport.BeamY, endX - 3, frame.Viewport.BeamY));
        var endPreview = Assert.Single(Scene(document, frame, editor).Supports);
        Assert.Equal(document.Length, endPreview.Preview.Position);
        Assert.True(endPreview.IsPreview);
        Assert.True(endPreview.IsMirrored);
        editor.HoverPlacement(Length.FromMeters(.5));
        Assert.False(Assert.Single(Scene(document, frame, editor).Supports).IsMirrored);
        Assert.Same(frame, layout.Update(document, 1600, 800));
        editor.HoverPlacement(document.Length);
        Assert.True(editor.PlaceSupport());
        Assert.True(Assert.Single(Scene(document, frame, editor).Supports).IsMirrored);
        Assert.True(editor.ConfirmSupport());
        layout.EndInteraction();
        var committedFrame = layout.Update(editor.Document, 1100, 600)!;
        Assert.True(Assert.Single(Scene(editor.Document, committedFrame).Supports).IsMirrored);
        Assert.Equal(9.75, committedFrame.Layout.Stations[^1].RightExtent);
    }

    [Fact]
    public void DragAndEditFollowCurrentPositionWithoutMovingTheFrozenLayout()
    {
        int analyses = 0;
        var support = Support(.5);
        var document = Document(support);
        var editor = new EditorViewModel(document, () => { }, beam => { analyses++; return BeamAnalysis.Analyze(beam); });
        var layout = new BeamLayoutState();
        var frame = layout.Update(document, 1100, 600)!;
        layout.BeginInteraction(BeamPointerInteraction.SupportDrag);
        var gesture = new SupportDragGesture(support.Id, support.Position, frame.Layout.Transform.PhysicalToScreen(.5), frame.Layout.Transform);
        Assert.True(editor.BeginSupportDrag(support.Id));
        editor.UpdateSupportDrag(gesture.Update(frame.Layout.Stations[^1].ScreenX, frame.Viewport.BeamY, frame.Viewport.Height));
        Assert.True(Assert.Single(Scene(document, frame, editor).Supports).IsMirrored);
        editor.UpdateSupportDrag(gesture.Update(frame.Layout.Transform.PhysicalToScreen(.5), frame.Viewport.BeamY, frame.Viewport.Height));
        Assert.False(Assert.Single(Scene(document, frame, editor).Supports).IsMirrored);
        editor.UpdateSupportDrag(document.Length);
        Assert.True(editor.EndSupportDrag());
        Assert.True(Assert.Single(Scene(document, frame, editor).Supports).IsMirrored);
        Assert.Same(document, editor.Document);
        Assert.Equal(1, analyses);
        Assert.Same(frame, layout.Update(document, 1600, 800));
        layout.EndInteraction();
        Assert.True(editor.ConfirmSupport());
        var committedFrame = layout.Update(editor.Document, 1100, 600)!;
        Assert.True(Assert.Single(Scene(editor.Document, committedFrame).Supports).IsMirrored);
        Assert.Equal(2, analyses);
        Assert.True(editor.EditSupport(support.Id));
        editor.SupportDraft!.PositionText = "500";
        // Position text is a commit buffer; the visible preview stays at the end until OK.
        Assert.True(Assert.Single(Scene(editor.Document, committedFrame, editor).Supports).IsMirrored);
        Assert.True(editor.ConfirmSupport());
        Assert.False(Assert.Single(Scene(editor.Document, layout.Update(editor.Document, 1100, 600)!).Supports).IsMirrored);
        Assert.Equal(3, analyses);
    }

    [Fact]
    public void RejectedLengthChangeKeepsOrientationAndExtensionTurnsItBack()
    {
        var document = Document(Support(1));
        var editor = new EditorViewModel(document, () => { });
        var layout = new BeamLayoutState();
        var frame = layout.Update(document, 1100, 600)!;
        editor.DimensionLength.Begin();
        editor.DimensionLength.Text = "900";
        Assert.False(editor.DimensionLength.Confirm());
        Assert.Same(document, editor.Document);
        Assert.True(Assert.Single(Scene(editor.Document, frame).Supports).IsMirrored);
        editor.DimensionLength.Cancel();
        editor.DimensionLength.Begin();
        editor.DimensionLength.Text = "1200";
        Assert.True(editor.DimensionLength.Confirm());
        var extendedFrame = layout.Update(editor.Document, 1100, 600)!;
        Assert.False(Assert.Single(Scene(editor.Document, extendedFrame).Supports).IsMirrored);
        var station = extendedFrame.Layout.Stations.Single(s => s.PhysicalX == 1);
        Assert.Equal(9.75, station.LeftExtent);
        Assert.Equal(1, station.RightExtent);
    }

    [Fact]
    public void FixedSupportExtentsUnionWithLoadsAtBothEndpoints()
    {
        var supports = new[] { Support(0), Support(1) with { Name = "B" } };
        var loads = supports.Select((s, i) => EditorPointLoad.Create(Guid.NewGuid(), s.Position, PointLoadKind.Force, 100, "F" + i));
        var document = new EditorDocument(Length.FromMeters(1), ProjectTemplates.Material, ProjectTemplates.Section, supports, loads);
        var requirements = StationRequirementBuilder.FromDocument(document);
        Assert.Equal(new StationRequirement(0, 9.75, 4.75), requirements[0]);
        Assert.Equal(new StationRequirement(1, 4.75, 9.75), requirements[^1]);
        var frame = new BeamLayoutState().Update(document, 1100, 600)!;
        Assert.True(frame.Layout.Stations[0].ScreenX >= 9.75);
        Assert.True(frame.Layout.Stations[^1].ScreenX + 9.75 <= frame.Viewport.Width);
    }
}
