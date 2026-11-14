using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Controls;
using SpanDraft.Desktop.Layout;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using Xunit;

namespace SpanDraft.Tests;

/// <summary>The measured scene of moment releases: visuals, labels, hit tests, station requirements and snapping.</summary>
public sealed class DesktopMomentReleaseSceneTests : DesktopHingedSupportTestSupport
{
    private static Length M(double meters) => Length.FromMeters(meters);

    private static EditorDocument Document(params (double Position, string Name)[] releases) => DesktopLayoutFixture.Document()
        .WithSupports([new(Guid.NewGuid(), M(0), SupportType.Pinned, "A"), new(Guid.NewGuid(), M(1), SupportType.Roller, "B")])
        .WithMomentReleases(releases.Select(r => new EditorMomentRelease(Guid.NewGuid(), M(r.Position), r.Name)));

    private static BeamLayoutFrame Frame(EditorDocument document) => new(
        StationLayout.Compute(document.Length.Meters, 40, 560, StationRequirementBuilder.FromDocument(document)), new(600, 220, 60));

    [Fact]
    public void EveryReleaseBecomesARingAtItsScreenPositionAndItsLabelSharesTheSupportRowBelowTheBeam()
    {
        var document = Document((.5, "G1"), (.25, "Gelenk 2"));
        var frame = Frame(document);
        var scene = Scene(document, frame);
        Assert.Equal(2, scene.MomentReleases.Count);
        Assert.All(scene.MomentReleases, v => Assert.Equal(frame.Layout.Transform.PhysicalToScreen(v.Preview.Position.Meters), v.X));
        var labels = scene.Annotations.Where(a => a.Id is not null && document.MomentReleases.Any(r => r.Id == a.Id)).ToArray();
        Assert.Equal(2, labels.Length);
        Assert.All(labels, label =>
        {
            Assert.True(label.IsSupport);
            Assert.True(label.Bounds.Top > frame.Viewport.BeamY);
            Assert.Equal(label.AutoBounds, label.Bounds);
        });
        Assert.Contains(labels, l => l.Text == "Gelenk 2");
        // Labels of supports and releases never overlap each other.
        var below = scene.Annotations.Where(a => a.IsSupport).ToArray();
        for (int i = 0; i < below.Length; i++)
            for (int j = i + 1; j < below.Length; j++)
                Assert.False(below[i].Bounds.Intersects(below[j].Bounds), $"{below[i].Text} overlaps {below[j].Text}");
    }

    [Fact]
    public void AReleaseOnASupportKeepsBothLabelsSeparateAndAHitOnTheRingSelectsOnlyTheHinge()
    {
        var document = Document((1, "G1"));
        var frame = Frame(document);
        var scene = Scene(document, frame);
        var release = document.MomentReleases[0];
        var support = document.Supports.First(s => s.Position == M(1));
        var visual = scene.MomentReleases.Single();
        // On the ring: only the hinge. Off the ring but on the support symbol: only the support.
        Assert.Equal([release.Id], scene.HitTestEntities(visual.X, frame.Viewport.BeamY));
        Assert.Equal([release.Id], scene.HitTestEntities(visual.X + 3, frame.Viewport.BeamY - 3));
        Assert.Equal([support.Id], scene.HitTestEntities(visual.X, frame.Viewport.BeamY + 18));
        var labelHits = scene.Annotations.Where(a => a.Id == release.Id || a.Id == support.Id).ToArray();
        Assert.Equal(2, labelHits.Length);
        Assert.False(labelHits[0].Bounds.Intersects(labelHits[1].Bounds));
    }

    [Fact]
    public void AReleaseAwayFromSupportsIsHitWithinItsPaddedRingOnly()
    {
        var document = Document((.5, "G1"));
        var frame = Frame(document);
        var scene = Scene(document, frame);
        var visual = scene.MomentReleases.Single();
        double y = frame.Viewport.BeamY;
        Assert.Equal(document.MomentReleases[0].Id, Assert.Single(scene.HitTestEntities(visual.X + 8, y)));
        Assert.Empty(scene.HitTestEntities(visual.X + 14, y));
        Assert.Empty(scene.HitTestEntities(visual.X, y + 14));
        Assert.True(MomentReleaseSymbol.Contains(visual, y, visual.X, y));
        Assert.False(MomentReleaseSymbol.Contains(visual, y, visual.X, y + SchematicMetrics.MomentReleaseRadius + SchematicMetrics.MomentReleaseHitPadding + 1));
    }

    [Fact]
    public void ThePreviewReplacesTheCommittedReleaseAndAFreshOneIsAnExtraVisual()
    {
        var document = Document((.5, "G1"));
        var frame = Frame(document);
        var id = document.MomentReleases[0].Id;
        var moved = BeamRenderState.Create(document, frame, DesktopLayoutFixture.Measure,
            releasePreview: new(M(.75)), hiddenReleaseId: id, releaseName: "G1");
        var visual = Assert.Single(moved.MomentReleases);
        Assert.True(visual.IsPreview);
        Assert.Equal(id, visual.Id);
        Assert.Equal(frame.Layout.Transform.PhysicalToScreen(.75), visual.X);
        var added = BeamRenderState.Create(document, frame, DesktopLayoutFixture.Measure,
            releasePreview: new(M(.25), true), releaseName: "G2");
        Assert.Equal(2, added.MomentReleases.Count);
        var fresh = added.MomentReleases.Single(v => v.Id is null);
        Assert.True(fresh.IsPreview && fresh.Preview.IsInvalid);
        Assert.Contains(added.Annotations, a => a.Id is null && a.Text == "G2" && a.IsInvalid);
    }

    [Fact]
    public void EveryReleaseIsAStationOfTheLayoutWithItsOwnExtentAndNoEndSnap()
    {
        var document = Document((.5, "G1"));
        var requirements = StationRequirementBuilder.FromDocument(document);
        var station = Assert.Single(requirements, r => r.PhysicalX == .5);
        double extent = SchematicMetrics.MomentReleaseRadius + SchematicMetrics.SymbolStrokeWidth / 2;
        Assert.Equal((extent, extent), (station.LeftExtent, station.RightExtent));
        Assert.Equal(2 * extent, StationRequirementBuilder.FromDocument(document, 2).Single(r => r.PhysicalX == .5).LeftExtent);
        var layout = StationLayout.Compute(document.Length.Meters, 40, 560, requirements);
        Assert.Contains(layout.Stations, s => s.PhysicalX == .5);
        // The ends never attract a ring; ordinary placement still snaps to them.
        var transform = layout.Transform;
        double beamY = 60;
        Assert.Null(SupportSnap.ReleasePlacement(transform, beamY, 0, 200));
        Assert.Equal(M(1), SupportSnap.Placement(transform, beamY, transform.PhysicalToScreen(1) - 4, beamY));
        Assert.Equal(M(Math.Round(transform.ScreenToPhysical(transform.PhysicalToScreen(1) - 4) * 1000) / 1000),
            SupportSnap.ReleasePlacement(transform, beamY, transform.PhysicalToScreen(1) - 4, beamY));
        Assert.Equal(M(1), SupportSnap.ReleasePlacement(transform, beamY, transform.PhysicalToScreen(1), beamY));
    }

    [Fact]
    public void ADragGestureForAReleaseDoesNotSnapToABeamEnd()
    {
        var document = Document((.5, "G1"));
        var transform = StationLayout.Compute(document.Length.Meters, 40, 560, StationRequirementBuilder.FromDocument(document)).Transform;
        double start = transform.PhysicalToScreen(.5);
        var gesture = new SupportDragGesture(document.MomentReleases[0].Id, M(.5), start, transform, endpointRadius: 0);
        double nearEnd = transform.PhysicalToScreen(1) - 3;
        var position = gesture.Update(start + (nearEnd - start), 30, 200);
        Assert.NotEqual(M(1), position);
        var snapping = new SupportDragGesture(document.MomentReleases[0].Id, M(.5), start, transform);
        Assert.Equal(M(1), snapping.Update(start + (nearEnd - start), 30, 200));
    }

    [Fact]
    public void ANoReleaseDocumentHasNoRingAndItsRequirementsAreUnchanged()
    {
        var plain = DesktopLayoutFixture.Document().WithSupports([new(Guid.NewGuid(), M(0), SupportType.Pinned, "A")]);
        Assert.Empty(Scene(plain, Frame(plain)).MomentReleases);
        Assert.Equal(StationRequirementBuilder.FromDocument(plain).Select(r => r.PhysicalX),
            [0, plain.Length.Meters]);
    }
}
