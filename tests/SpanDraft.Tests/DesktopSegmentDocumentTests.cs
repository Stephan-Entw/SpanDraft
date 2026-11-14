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

    [Fact]
    public void SplittingCopiesEveryPropertyIncludingTheNameIntoBothHalves()
    {
        EditorDocument split = Three().WithBoundaryAt(M(3));

        Assert.Equal([M(2), M(3), M(4.5)], split.SegmentBoundaries);
        Assert.Equal(4, split.Segments.Count);
        Assert.Same(split.Segments[1], split.Segments[2]);
        Assert.Equal("Thick", split.Segments[1].SectionDisplayName);
        Assert.Equal("Thick", split.Segments[2].SectionDisplayName);
        Assert.Equal((M(2), M(3)), split.SegmentRange(1));
        Assert.Equal((M(3), M(4.5)), split.SegmentRange(2));
        Assert.Equal(1, split.Supports.Count - 1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(2)]
    [InlineData(7)]
    public void ABoundaryNeedsAFreePositionStrictlyInsideTheBeam(double position) =>
        Assert.Throws<ArgumentException>(() => Three().WithBoundaryAt(M(position)));

    [Fact]
    public void RemovingABoundaryKeepsTheLeftNeighbourByDefaultOrTheRightOne()
    {
        EditorDocument left = Three().WithoutBoundary(0);
        EditorDocument right = Three().WithoutBoundary(0, keepLeft: false);

        Assert.Equal([M(4.5)], left.SegmentBoundaries);
        Assert.Equal(["Steel", "Light"], left.Segments.Select(s => s.Material.Name));
        Assert.Null(left.Segments[0].SectionDisplayName);
        Assert.Equal("Thick", right.Segments[0].SectionDisplayName);
        Assert.Throws<ArgumentOutOfRangeException>(() => Three().WithoutBoundary(2));
    }

    [Fact]
    public void MovingABoundaryCannotPassItsNeighbours()
    {
        EditorDocument document = Three();

        Assert.Equal(M(1), document.WithBoundary(0, M(1)).SegmentBoundaries[0]);
        Assert.Throws<ArgumentException>(() => document.WithBoundary(0, M(4.5)));
        Assert.Throws<ArgumentException>(() => document.WithBoundary(1, M(2)));
        Assert.Throws<ArgumentException>(() => document.WithBoundary(1, M(6)));
    }

    [Fact]
    public void ReplacingASegmentLeavesTheOthersAndTheBoundaries()
    {
        EditorDocument changed = Three().WithSegment(1, Segment(0.2, 0.4, name: "Heavy"));

        Assert.Equal([M(2), M(4.5)], changed.SegmentBoundaries);
        Assert.Equal("Heavy", changed.Segments[1].SectionDisplayName);
        Assert.Null(changed.Segments[0].SectionDisplayName);
        Assert.Equal("Light", changed.Segments[2].Material.Name);
        Assert.Throws<ArgumentOutOfRangeException>(() => Three().WithSegment(3, Segment()));
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

        Assert.Equal(EditorChangeKind.None, Classify(state.Document.WithSegments(state.Document.SegmentBoundaries,
            state.Document.Segments.ToArray())));
        Assert.Equal(EditorChangeKind.MetadataOnly, Classify(state.Document.WithSegment(0, Segment(name: "Renamed"))));
        Assert.Equal(EditorChangeKind.Mechanical, Classify(state.Document.WithSegment(0, Segment(0.1, 0.25))));
        Assert.Equal(EditorChangeKind.Mechanical, Classify(state.Document.WithSegment(0, Segment(re: 355e6))));
        Assert.Equal(EditorChangeKind.Mechanical, Classify(state.Document.WithSegment(2, Segment(axis: SectionAxisDesignation.Z))));
        Assert.Equal(EditorChangeKind.Mechanical, Classify(state.Document.WithBoundary(0, M(1.5))));
        Assert.Equal(EditorChangeKind.Mechanical, Classify(state.Document.WithBoundaryAt(M(3))));
        Assert.Equal(EditorChangeKind.Mechanical, Classify(state.Document.WithoutBoundary(1)));
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
