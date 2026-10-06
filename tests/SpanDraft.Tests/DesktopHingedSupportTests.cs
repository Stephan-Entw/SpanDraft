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

public sealed class DesktopHingedSupportTests
{
    private static EditorSupport Support(SupportType type, double position = .5) =>
        new(Guid.NewGuid(), Length.FromMeters(position), type, "A");
    private static EditorDocument Document(EditorSupport support) => DesktopLayoutFixture.Document().WithSupports([support]);
    private static BeamRenderState Scene(EditorDocument document, BeamLayoutFrame frame, EditorViewModel? editor = null,
        EditorPresentationState? presentation = null) => BeamRenderState.Create(document, frame, DesktopLayoutFixture.Measure,
            editor?.Preview, editor?.HiddenSupportId, editor?.SupportPreviewName, presentation: presentation);

    [Theory]
    [InlineData(false, 14, 20, 2)]
    [InlineData(true, 11.2, 16, 3)]
    public void TrianglesRetainApprovedDimensionsAndOnlyRollerHasSeparateBase(bool roller, double halfWidth, double height, int count)
    {
        var geometry = new HingedSupportGeometry(roller);
        var lines = geometry.TriangleLines.ToArray();
        Assert.Equal(count, lines.Length);
        Assert.Equal(halfWidth, geometry.TriangleHalfWidth, 12);
        Assert.Equal(height, geometry.TriangleHeight);
        Assert.Equal(0, lines[0].StartX);
        Assert.Equal(0, lines[0].StartY);
        Assert.Equal(-halfWidth, lines[0].EndX, 12);
        Assert.Equal(height, lines[0].EndY);
        Assert.Equal(halfWidth, lines[1].StartX, 12);
        Assert.Equal(height, lines[1].StartY);
        Assert.Equal(0, lines[1].EndX);
        Assert.Equal(0, lines[1].EndY);
        if (roller)
        {
            Assert.Equal((-18d, 16d, 18d, 16d), lines[2]);
            Assert.Equal(4, geometry.Ground.StartY - lines[2].StartY);
        }
        else Assert.Equal(height, geometry.Ground.StartY);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GroundHatchesJointAndPaintedBoundsAreShared(bool roller)
    {
        var geometry = new HingedSupportGeometry(roller);
        Assert.Equal((-20d, 20d, 20d, 20d), geometry.Ground);
        Assert.Equal(2, SchematicMetrics.SupportGroundStrokeWidth);
        Assert.Equal((0d, 0d, 3d), geometry.Joint);
        var hatches = geometry.Hatches.ToArray();
        Assert.Equal(4, hatches.Length);
        double[] starts = [-11, -2d / 3, 29d / 3, 20];
        for (int i = 0; i < hatches.Length; i++)
        {
            var hatch = hatches[i];
            Assert.Equal(starts[i], hatch.StartX, 12);
            Assert.Equal(20, hatch.StartY);
            Assert.Equal(-9, hatch.EndX - hatch.StartX, 12);
            Assert.Equal(29, hatch.EndY);
        }
        Assert.Equal(-20, hatches[0].EndX);
        Assert.Equal(21, geometry.LeftExtent);
        Assert.Equal(21, geometry.RightExtent);
        Assert.Equal(-3.75, geometry.Top);
        Assert.Equal(29.75, geometry.Bottom);
        Assert.Equal(new HingedSupportGeometry(!roller).Hatches, hatches);
        Assert.All(new FixedSupportGeometry(false).Hatches, h =>
        {
            Assert.Equal(-9, h.EndX - h.StartX);
            Assert.Equal(9, h.EndY - h.StartY);
        });
    }

    [Theory]
    [InlineData(SupportType.Pinned)]
    [InlineData(SupportType.Roller)]
    public void CommittedAndPreviewHitsIncludeEveryContourAndRespectPaddedBounds(SupportType type)
    {
        var support = Support(type);
        var document = Document(support);
        var frame = new BeamLayoutState().Update(document, 1100, 600)!;
        double x = frame.Layout.Transform.PhysicalToScreen(.5), y = frame.Viewport.BeamY;
        var preview = new SupportPreview(support.Position, type);
        (double Dx, double Dy, bool Hit)[] points =
        [
            (0, -3.75, true), (20, 20, true), (-20, 29, true),
            (-26, 20, true), (26, 20, true), (0, -8.75, true), (0, 34.75, true),
            (-26.01, 20, false), (26.01, 20, false), (0, -8.76, false), (0, 34.76, false)
        ];
        foreach (var point in points)
        {
            Assert.Equal(point.Hit, SupportSymbol.Contains(preview, frame.Layout.Transform, y, x + point.Dx, y + point.Dy));
            Assert.Equal(point.Hit ? support.Id : (Guid?)null,
                SupportSymbol.HitTest(document.Supports, frame.Layout.Transform, y, x + point.Dx, y + point.Dy));
        }
    }

    [Theory]
    [InlineData(SupportType.Pinned)]
    [InlineData(SupportType.Roller)]
    public void LayoutReservesSymmetricExtentsAndClearanceIncludingSharedLoads(SupportType type)
    {
        var support = Support(type);
        var other = Support(type, .501) with { Name = "B" };
        var loads = new[]
        {
            EditorPointLoad.Create(Guid.NewGuid(), support.Position, PointLoadKind.Force, 100, "F1"),
            EditorPointLoad.Create(Guid.NewGuid(), support.Position, PointLoadKind.Moment, 100, "M1")
        };
        var document = new EditorDocument(Length.FromMeters(1), ProjectTemplates.Material, ProjectTemplates.Section, [support, other], loads);
        var requirements = StationRequirementBuilder.FromDocument(document);
        Assert.Equal(new StationRequirement(.5, 21, 21), requirements.Single(r => r.PhysicalX == .5));
        Assert.Equal(new StationRequirement(.501, 21, 21), requirements.Single(r => r.PhysicalX == .501));
        foreach (double width in new[] { 1100d, 1600d })
        {
            var frame = new BeamLayoutState().Update(document, width, 600)!;
            double distance = frame.Layout.Transform.PhysicalToScreen(.501) - frame.Layout.Transform.PhysicalToScreen(.5);
            Assert.True(distance >= 50 - 1e-9);
            Assert.True(frame.Layout.IsDistorted);
        }
    }

    [Theory]
    [InlineData(SupportType.Pinned)]
    [InlineData(SupportType.Roller)]
    public void PlacementAndEndpointCommitKeepTheSameGeometryWithoutMirroring(SupportType type)
    {
        int analyses = 0;
        var editor = new EditorViewModel(DesktopLayoutFixture.Document(), () => { },
            beam => { analyses++; return BeamAnalysis.Analyze(beam); });
        var layout = new BeamLayoutState();
        var frame = layout.Update(editor.Document, 1100, 600)!;
        layout.BeginInteraction(BeamPointerInteraction.SupportPlacement);
        editor.ToggleSupportTool(type);
        double endX = frame.Layout.Stations[^1].ScreenX;
        editor.HoverPlacement(SupportSnap.Placement(frame.Layout.Transform, frame.Viewport.BeamY, endX, frame.Viewport.BeamY));
        var preview = Assert.Single(Scene(editor.Document, frame, editor).Supports);
        Assert.True(preview.IsPreview);
        Assert.False(preview.IsMirrored);
        Assert.True(SupportSymbol.Contains(editor.Preview!, frame.Layout.Transform, frame.Viewport.BeamY, endX + 20, frame.Viewport.BeamY + 20));
        Assert.True(editor.PlaceSupport());
        Assert.True(Assert.Single(Scene(editor.Document, frame, editor).Supports).IsPreview);
        Assert.Equal(1, analyses);
        Assert.True(editor.ConfirmSupport());
        layout.EndInteraction();
        var committed = layout.Update(editor.Document, 1100, 600)!;
        Assert.Equal(2, analyses);
        Assert.Equal(21, committed.Layout.Stations[^1].LeftExtent);
        Assert.Equal(21, committed.Layout.Stations[^1].RightExtent);
        Assert.False(Assert.Single(Scene(editor.Document, committed).Supports).IsMirrored);
    }

    [Fact]
    public void TypeChangesAndDragPreserveLabelAnchorsAndManualOffsetsWithoutExtraAnalysis()
    {
        int analyses = 0;
        var support = Support(SupportType.Pinned);
        var editor = new EditorViewModel(Document(support), () => { }, beam => { analyses++; return BeamAnalysis.Analyze(beam); });
        var layout = new BeamLayoutState();
        var frame = layout.Update(editor.Document, 1100, 600)!;
        var presentation = new EditorPresentationState().WithOffset(support.Id, new(12, 7));
        var original = Assert.Single(Scene(editor.Document, frame, presentation: presentation).Annotations);
        Assert.Equal(frame.Viewport.BeamY + 40, original.AutoBounds.Top);
        Assert.Equal(original.AutoBounds.Translate(new Vector(12, 7)), original.Bounds);
        Assert.True(editor.EditSupport(support.Id));
        editor.SupportDraft!.Type = SupportType.Roller;
        var draft = Scene(editor.Document, frame, editor, presentation);
        Assert.Equal(SupportType.Roller, Assert.Single(draft.Supports).Preview.Type);
        Assert.Equal(original.AutoBounds, Assert.Single(draft.Annotations).AutoBounds);
        Assert.Equal(original.Bounds, Assert.Single(draft.Annotations).Bounds);
        Assert.Equal(1, analyses);
        Assert.True(editor.ConfirmSupport());
        Assert.Equal(2, analyses);
        frame = layout.Update(editor.Document, 1100, 600)!;
        layout.BeginInteraction(BeamPointerInteraction.SupportDrag);
        var gesture = new SupportDragGesture(support.Id, support.Position, frame.Layout.Transform.PhysicalToScreen(.5), frame.Layout.Transform);
        Assert.True(editor.BeginSupportDrag(support.Id));
        editor.UpdateSupportDrag(gesture.Update(frame.Layout.Transform.PhysicalToScreen(.7), frame.Viewport.BeamY, frame.Viewport.Height));
        var dragging = Assert.Single(Scene(editor.Document, frame, editor, presentation).Supports);
        Assert.Equal(Length.FromMeters(.7), dragging.Preview.Position);
        Assert.True(SupportSymbol.Contains(editor.Preview!, frame.Layout.Transform, frame.Viewport.BeamY, dragging.X + 20, frame.Viewport.BeamY + 20));
        Assert.Equal(2, analyses);
        Assert.Same(frame, layout.Update(editor.Document, 1600, 800));
        Assert.True(editor.EndSupportDrag());
        Assert.True(editor.ConfirmSupport());
        Assert.Equal(3, analyses);
        layout.EndInteraction();
        foreach (var size in new[] { (Width: 1100d, Height: 600d), (Width: 1600d, Height: 800d) })
        {
            var resized = layout.Update(editor.Document, size.Width, size.Height)!;
            var scene = Scene(editor.Document, resized, presentation: presentation);
            var label = Assert.Single(scene.Annotations);
            var visual = Assert.Single(scene.Supports);
            Assert.Equal(Length.FromMeters(.7), editor.Document.Supports[0].Position);
            Assert.Equal(resized.Viewport.BeamY + 40, label.AutoBounds.Top);
            Assert.Equal(label.AutoBounds.Translate(new Vector(12, 7)), label.Bounds);
            Assert.Equal(21, resized.Layout.Stations.Single(s => s.PhysicalX == .7).LeftExtent);
            Assert.Equal(support.Id, SupportSymbol.HitTest(editor.Document.Supports, resized.Layout.Transform,
                resized.Viewport.BeamY, visual.X, resized.Viewport.BeamY - 3.75));
            Assert.Equal(Scene(editor.Document, resized).MinimumPaneHeight, scene.MinimumPaneHeight);
        }
        Assert.Equal(3, analyses);
    }

    [Fact]
    public void AllSupportTypesKeepTheSameLabelRowAndPaneHeightReservation()
    {
        foreach (var type in new[] { SupportType.Fixed, SupportType.Pinned, SupportType.Roller })
        {
            var support = Support(type);
            var document = Document(support);
            var frame = DesktopLayoutFixture.Linear(0, 1000, 200, 1);
            var scene = Scene(document, frame);
            Assert.Equal(240, Assert.Single(scene.Annotations).AutoBounds.Top);
            Assert.Equal(220, scene.MinimumPaneHeight);
        }
    }
}
