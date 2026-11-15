using SpanDraft.Core.Materials;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Sections.Parametric;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Core.Validation;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using Xunit;

namespace SpanDraft.Tests;

/// <summary>The editor document of a beam with several segments; the single-segment view stays as it was.</summary>
public sealed class DesktopSegmentDocumentTests
{
    private static Length M(double value) => Length.FromMeters(value);
    private static Material Steel(double? density = 7850, string name = "Steel", double re = 235e6) => new(name,
        Pressure.FromPascals(210e9), Pressure.FromPascals(re), MaterialStrengthType.Re,
        density is { } rho ? MassDensity.FromKilogramsPerCubicMeter(rho) : null);
    private static ISectionDefinition Rectangle(double width, double height) =>
        new RectangleSectionGeometry(M(width), M(height));

    private static EditorSegment Segment(double width = 0.1, double height = 0.2, double? density = 7850,
        string? name = null, string material = "Steel", SectionAxisDesignation axis = SectionAxisDesignation.Y, double re = 235e6) =>
        new(Steel(density, material, re), Rectangle(width, height), axis, name);

    private static EditorDocument Three(bool selfWeight = false) => new(M(6), [M(2), M(4.5)],
        [Segment(), Segment(0.1, 0.3, name: "Thick"), Segment(0.05, 0.1, material: "Light")],
        [new(Guid.NewGuid(), M(0), SupportType.Fixed, "A"), new(Guid.NewGuid(), M(2), SupportType.Roller, "B")],
        selfWeight: selfWeight ? new(Guid.NewGuid(), true) : null);

    [Fact]
    public void ADocumentHasOneMoreSegmentThanBoundariesWhichAscendStrictlyInsideTheBeam()
    {
        Assert.Throws<ArgumentException>(() => new EditorDocument(M(6), [M(2)], [Segment()]));
        Assert.Throws<ArgumentException>(() => new EditorDocument(M(6), [], [Segment(), Segment()]));
        Assert.Throws<ArgumentException>(() => new EditorDocument(M(6), [M(4), M(2)], [Segment(), Segment(), Segment()]));
        Assert.Throws<ArgumentException>(() => new EditorDocument(M(6), [M(2), M(2)], [Segment(), Segment(), Segment()]));
        Assert.Throws<ArgumentException>(() => new EditorDocument(M(6), [M(0)], [Segment(), Segment()]));
        Assert.Throws<ArgumentException>(() => new EditorDocument(M(6), [M(6)], [Segment(), Segment()]));
        Assert.Throws<ArgumentException>(() => new EditorDocument(M(6), [M(7)], [Segment(), Segment()]));
        Assert.Equal(3, Three().Segments.Count);
    }

    [Fact]
    public void SegmentRangesFollowBoundariesAndLengthAndABoundaryBelongsToTheSegmentThatStartsThere()
    {
        EditorDocument document = Three();

        Assert.Equal((M(0), M(2)), document.SegmentRange(0));
        Assert.Equal((M(2), M(4.5)), document.SegmentRange(1));
        Assert.Equal((M(4.5), M(6)), document.SegmentRange(2));
        Assert.Equal([0, 0, 1, 1, 2, 2, 2], new[] { 0, 1.99, 2, 4.49, 4.5, 5, 6 }.Select(x => document.SegmentIndexAt(M(x))));
        Assert.Equal((M(4.5), M(8)), (document with { Length = M(8) }).SegmentRange(2));
    }

    [Fact]
    public void TheLengthCannotShrinkToTheLastBoundary()
    {
        EditorDocument document = Three();

        Assert.Throws<ArgumentException>(() => document with { Length = M(4.5) });
        Assert.Throws<ArgumentException>(() => document with { Length = M(3) });
        Assert.Equal(M(4.51), (document with { Length = M(4.51) }).Length);
        Assert.NotNull(BeamLengthConstraints.Error(document, M(4.5)));
        Assert.Null(BeamLengthConstraints.Error(document, M(5)));
    }

    [Fact]
    public void TheSingleSegmentViewThrowsForSeveralSegmentsInsteadOfPickingTheFirst()
    {
        EditorDocument many = Three();
        Assert.Throws<InvalidOperationException>(() => many.Material);
        Assert.Throws<InvalidOperationException>(() => many.Section);
        Assert.Throws<InvalidOperationException>(() => many.BendingAxis);
        Assert.Throws<InvalidOperationException>(() => many.SectionDisplayName);
        Assert.Throws<InvalidOperationException>(() => many with { Material = Steel() });

        var single = new EditorDocument(M(3), Steel(), Rectangle(0.1, 0.2), SectionAxisDesignation.Y,
            sectionDisplayName: "Beam");
        Assert.Empty(single.SegmentBoundaries);
        Assert.Equal("Beam", single.SectionDisplayName);
        Assert.Equal("Beam", single.Segments[0].SectionDisplayName);
        Assert.Equal("Other", (single with { SectionDisplayName = "Other" }).Segments[0].SectionDisplayName);
        Assert.Equal("Light", (single with { Material = Steel(name: "Light") }).Segments[0].Material.Name);
    }

    private static EditorDocument Accepted(GeometryChange change)
    {
        Assert.True(change.IsAccepted);
        return change.Document!;
    }

    // The same segment with other properties, keeping its identity and length.
    private static EditorDocument Replace(EditorDocument document, int index, EditorSegment segment) =>
        Accepted(document.WithSegmentChain(document.Segments.Select((s, i) => i == index
            ? segment with { Id = s.Id, Length = s.Length } : s)));

    [Fact]
    public void DuplicatingCopiesEveryPropertyIncludingTheNameIntoTheNewSegment()
    {
        EditorDocument document = Three();
        EditorDocument duplicated = Accepted(document.DuplicateSegment(document.Segments[1].Id));

        Assert.Equal([M(2), M(4.5), M(7)], duplicated.SegmentBoundaries);
        Assert.Equal(4, duplicated.Segments.Count);
        // The copy carries every property of the original; only its identity differs.
        Assert.Equal(duplicated.Segments[1], duplicated.Segments[2] with { Id = duplicated.Segments[1].Id });
        Assert.NotEqual(duplicated.Segments[1].Id, duplicated.Segments[2].Id);
        Assert.Equal("Thick", duplicated.Segments[1].SectionDisplayName);
        Assert.Equal("Thick", duplicated.Segments[2].SectionDisplayName);
        Assert.Equal((M(2), M(4.5)), duplicated.SegmentRange(1));
        Assert.Equal((M(4.5), M(7)), duplicated.SegmentRange(2));
        Assert.Equal(document.Supports.Count, duplicated.Supports.Count);
    }

    [Fact]
    public void AnInsertedSegmentNeedsALengthAndAPlaceInTheChain()
    {
        EditorDocument document = Three();

        Assert.Throws<ArgumentException>(() => document.InsertSegment(1, Segment()));
        Assert.Throws<ArgumentOutOfRangeException>(() => document.InsertSegment(4, Segment() with { Length = M(1) }));
        Assert.Throws<ArgumentOutOfRangeException>(() => document.InsertSegment(-1, Segment() with { Length = M(1) }));
        Assert.Throws<ArgumentException>(() => document.ResizeSegment(document.Segments[0].Id, M(0)));
    }

    [Fact]
    public void RemovingASegmentKeepsTheOthersWithTheirOwnProperties()
    {
        EditorDocument document = Three();

        EditorDocument first = Accepted(document.RemoveSegment(document.Segments[0].Id));
        EditorDocument middle = Accepted(document.RemoveSegment(document.Segments[1].Id));

        Assert.Equal([M(2.5)], first.SegmentBoundaries);
        Assert.Equal("Thick", first.Segments[0].SectionDisplayName);
        Assert.Equal([M(2)], middle.SegmentBoundaries);
        Assert.Equal(["Steel", "Light"], middle.Segments.Select(s => s.Material.Name));
        Assert.Null(middle.Segments[0].SectionDisplayName);
        Assert.Equal(GeometryRejection.UnknownSegment, document.RemoveSegment(Guid.NewGuid()).Rejection);
    }

    [Fact]
    public void ResizingASegmentMovesTheBoundariesBehindItAndKeepsTheOthers()
    {
        EditorDocument document = Three();

        EditorDocument longer = Accepted(document.ResizeSegment(document.Segments[0].Id, M(3)));

        Assert.Equal([M(3), M(5.5)], longer.SegmentBoundaries);
        Assert.Equal(M(7), longer.Length);
        Assert.Equal(document.Segments.Select(s => s.Id), longer.Segments.Select(s => s.Id));
    }

    [Fact]
    public void ReplacingASegmentLeavesTheOthersAndTheBoundaries()
    {
        EditorDocument changed = Replace(Three(), 1, Segment(0.2, 0.4, name: "Heavy"));

        Assert.Equal([M(2), M(4.5)], changed.SegmentBoundaries);
        Assert.Equal("Heavy", changed.Segments[1].SectionDisplayName);
        Assert.Null(changed.Segments[0].SectionDisplayName);
        Assert.Equal("Light", changed.Segments[2].Material.Name);
    }

    [Fact]
    public void ThePhysicalBeamTilesExactlyAndSupportsAndLoadsMayStandOnBoundaries()
    {
        EditorDocument document = Three().WithLoads([EditorPointLoad.Create(Guid.NewGuid(), M(4.5),
            PointLoadKind.Force, -1000, "F1")]);
        var beam = document.ToBeamModel();

        Assert.Equal([0, 2, 4.5], beam.Segments.Select(s => s.Start.Meters));
        Assert.Equal([2, 4.5, 6], beam.Segments.Select(s => s.End.Meters));
        Assert.Equal(["Steel", "Steel", "Light"], beam.Segments.Select(s => s.Material.Name));
        Assert.Equal(0.1 * 0.3 * 0.3 * 0.1 / 12 * 0 + 0.1 * Math.Pow(0.3, 3) / 12,
            beam.Segments[1].BendingAxisProperties.SecondMomentOfArea.MetersToTheFourth, 1e-12);
        Assert.Empty(BeamModelValidator.Validate(beam));
    }

    [Fact]
    public void SelfWeightIsOneConstantLoadPerSegmentFromItsOwnDensityAndArea()
    {
        EditorDocument document = Three(selfWeight: true);
        var loads = document.SelfWeightLoads;

        Assert.Equal(3, loads.Count);
        Assert.Equal([0, 2, 4.5], loads.Select(l => l.StartPosition.Meters));
        Assert.Equal([2, 4.5, 6], loads.Select(l => l.EndPosition.Meters));
        Assert.Equal(-7850 * 0.1 * 0.2 * 9.80665, loads[0].StartIntensity.NewtonsPerMeter, 9);
        Assert.Equal(-7850 * 0.1 * 0.3 * 9.80665, loads[1].StartIntensity.NewtonsPerMeter, 9);
        Assert.Equal(-7850 * 0.05 * 0.1 * 9.80665, loads[2].StartIntensity.NewtonsPerMeter, 9);
        Assert.All(loads, l => Assert.True(l.IsConstant));
        Assert.Equal(3, document.ToBeamModel().Loads.Count);
        Assert.Throws<InvalidOperationException>(() => document.SelfWeightLoad);
        Assert.Empty(Three().SelfWeightLoads);
    }

    [Fact]
    public void SelfWeightNeedsADensityInEverySegmentAndNamesTheFirstOneWithout()
    {
        EditorDocument document = new(M(6), [M(2), M(4.5)], [Segment(), Segment(density: null), Segment(density: null)],
            selfWeight: new(Guid.NewGuid(), true));

        Assert.Contains("2", document.SelfWeightActivationError);
        Assert.NotEqual(SpanDraft.Desktop.Resources.Strings.SelfWeightMissingDensity, document.SelfWeightActivationError);
        Assert.Throws<ArgumentException>(() => document.SelfWeightLoads);
        var single = new EditorDocument(M(3), Steel(null), Rectangle(0.1, 0.2), SectionAxisDesignation.Y);
        Assert.Equal(SpanDraft.Desktop.Resources.Strings.SelfWeightMissingDensity, single.SelfWeightActivationError);
    }

    [Fact]
    public void ASingleSegmentSelfWeightIsTheSameLoadAsBefore()
    {
        var document = new EditorDocument(M(3), Steel(), Rectangle(0.1, 0.2), SectionAxisDesignation.Y,
            selfWeight: new(Guid.NewGuid(), true));
        var expected = SpanDraft.Core.Loads.DistributedLoad.FromSelfWeight(M(3), MassDensity.FromKilogramsPerCubicMeter(7850),
            document.Segments[0].Section.Area);

        var load = Assert.Single(document.SelfWeightLoads);
        Assert.Equal(expected.StartIntensity, load.StartIntensity);
        Assert.Equal(expected.EndPosition, load.EndPosition);
        Assert.Equal(load.StartIntensity, document.SelfWeightLoad!.StartIntensity);
    }

    [Fact]
    public void SegmentChangesAreMechanicalExceptNamesAndEqualContentIsNone()
    {
        ProjectState state = new(Three(), new EditorPresentationState());
        EditorChangeKind Classify(EditorDocument next) => EditorChangeClassifier.Classify(state, state with { Document = next });

        EditorDocument Same(EditorDocument document) => Replace(document, 0, document.Segments[0]);
        var ids = state.Document.Segments.Select(g => g.Id).ToArray();
        Assert.Equal(EditorChangeKind.None, Classify(Same(state.Document)));
        Assert.Equal(EditorChangeKind.MetadataOnly, Classify(Replace(state.Document, 0, Segment(name: "Renamed"))));
        Assert.Equal(EditorChangeKind.Mechanical, Classify(Replace(state.Document, 0, Segment(0.1, 0.25))));
        Assert.Equal(EditorChangeKind.Mechanical, Classify(Replace(state.Document, 0, Segment(re: 355e6))));
        Assert.Equal(EditorChangeKind.Mechanical, Classify(Replace(state.Document, 2, Segment(axis: SectionAxisDesignation.Z))));
        Assert.Equal(EditorChangeKind.Mechanical, Classify(Accepted(state.Document.ResizeSegment(ids[0], M(2.5)))));
        Assert.Equal(EditorChangeKind.Mechanical, Classify(Accepted(state.Document.DuplicateSegment(ids[2]))));
        Assert.Equal(EditorChangeKind.Mechanical, Classify(Accepted(state.Document.RemoveSegment(ids[1]))));
    }

    [Fact]
    public void BoundariesEnterTheStationRequirementsWithoutExtent()
    {
        var requirements = StationRequirementBuilder.FromDocument(Three());

        foreach (double x in new[] { 2.0, 4.5 })
            Assert.Contains(requirements, r => r.PhysicalX == x);
        Assert.Equal(0, requirements.Single(r => r.PhysicalX == 4.5).LeftExtent);
        Assert.Equal(0, requirements.Single(r => r.PhysicalX == 4.5).RightExtent);
        // The support on the boundary at 2 keeps its extent through the maximum of both requirements.
        Assert.True(requirements.Single(r => r.PhysicalX == 2).LeftExtent > 0);
    }

    [Fact]
    public void TheEditorAnalyzesABeamOfSeveralSegmentsAndListsThemInTheOverview()
    {
        var editor = new EditorViewModel(Three(selfWeight: true), () => { });

        Assert.True(editor.Overview.HasSeveralSegments);
        Assert.Equal(3, editor.Overview.Segments.Count);
        Assert.Equal("", editor.Overview.Section);
        Assert.Equal("Thick", editor.Overview.Segments[1].Section);
        Assert.All(editor.Overview.Segments, s => Assert.NotEmpty(s.Range));
        Assert.Equal(3, editor.Overview.DistributedLoads.Count(l => l.Name == EditorSelfWeight.Symbol));
        Assert.NotNull(editor.Overview.Analysis.Result);
        Assert.Equal(3, editor.Overview.Analysis.Result!.Engineering.SegmentAssessments.Count);
        Assert.StartsWith("g = ", editor.SelfWeightLabel);
    }
}
