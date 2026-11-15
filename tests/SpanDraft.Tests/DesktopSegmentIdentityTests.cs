using SpanDraft.Core.Materials;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Sections.Parametric;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.State;
using Xunit;

namespace SpanDraft.Tests;

/// <summary>A beam is the ordered chain of identified segments; their lengths are the source of the geometry.</summary>
public sealed class DesktopSegmentIdentityTests
{
    private static Length M(double value) => Length.FromMeters(value);
    private static readonly Material Steel = new("Steel", Pressure.FromPascals(210e9), Pressure.FromPascals(235e6),
        MaterialStrengthType.Re, MassDensity.FromKilogramsPerCubicMeter(7850));
    private static EditorSegment Segment(double length = 0, Guid id = default) =>
        new(Steel, new RectangleSectionGeometry(M(0.1), M(0.2)), SectionAxisDesignation.Y) { Length = M(length), Id = id };

    [Fact]
    public void TheBeamLengthAndTheBoundariesAreTheRunningSumsOfTheSegmentLengths()
    {
        var document = new EditorDocument([Segment(2), Segment(2.5), Segment(1.5)]);

        Assert.Equal(M(6), document.Length);
        Assert.Equal([M(2), M(4.5)], document.SegmentBoundaries);
        Assert.Equal((M(2), M(4.5)), document.SegmentRange(1));
        Assert.Equal((M(4.5), M(6)), document.SegmentRange(2));
        Assert.Equal(1, document.SegmentIndexAt(M(2)));
    }

    [Fact]
    public void TheSegmentsOfAChainMustHaveALength()
    {
        // Length itself is finite and non-negative; a segment of no extent is the remaining invalid case.
        Assert.Throws<ArgumentException>(() => new EditorDocument([Segment(1), Segment(0)]));
        Assert.Throws<ArgumentException>(() => new EditorDocument([Segment(0), Segment(1)]));
    }

    [Fact]
    public void ABeamHasAtLeastOneSegment() =>
        Assert.Throws<ArgumentException>(() => new EditorDocument(Array.Empty<EditorSegment>()));

    [Fact]
    public void SegmentsWithoutIdentityGetOneByPositionAndEqualDocumentsStayEqual()
    {
        var first = new EditorDocument([Segment(1), Segment(1)]);
        var second = new EditorDocument([Segment(1), Segment(1)]);

        Assert.All(first.Segments, segment => Assert.NotEqual(Guid.Empty, segment.Id));
        Assert.Equal(2, first.Segments.Select(segment => segment.Id).Distinct().Count());
        Assert.Equal(first.Segments.Select(segment => segment.Id), second.Segments.Select(segment => segment.Id));
        Assert.True(ProjectState.DocumentContentEquals(first, second));
    }

    [Fact]
    public void GivenIdentitiesAreKeptAndMustBeUnique()
    {
        var id = Guid.NewGuid();
        var document = new EditorDocument([Segment(1, id), Segment(1)]);

        Assert.Equal(id, document.Segments[0].Id);
        Assert.Equal(0, document.IndexOfSegment(id));
        Assert.Equal(-1, document.IndexOfSegment(Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => new EditorDocument([Segment(1, id), Segment(1, id)]));
    }

    [Fact]
    public void TheLegacyBoundaryShapeDerivesTheLengthsAndKeepsTheIdentities()
    {
        var id = Guid.NewGuid();
        var document = new EditorDocument(M(6), [M(2), M(4.5)], [Segment(0, id), Segment(), Segment()]);

        Assert.Equal([M(2), M(2.5), M(1.5)], document.Segments.Select(segment => segment.Length));
        Assert.Equal(id, document.Segments[0].Id);
        Assert.Equal(M(6), document.Length);
    }

    [Fact]
    public void SettingTheBeamLengthResizesTheLastSegmentAndKeepsEveryIdentity()
    {
        var document = new EditorDocument([Segment(2), Segment(2)]);
        var ids = document.Segments.Select(segment => segment.Id).ToArray();

        var longer = document with { Length = M(5) };

        Assert.Equal(M(5), longer.Length);
        Assert.Equal([M(2), M(3)], longer.Segments.Select(segment => segment.Length));
        Assert.Equal(ids, longer.Segments.Select(segment => segment.Id));
        Assert.Equal(M(4), document.Length);
        Assert.Throws<ArgumentException>(() => document with { Length = M(2) });
    }

    [Fact]
    public void EditingASegmentKeepsItsIdentityAndLength()
    {
        var document = new EditorDocument([Segment(1.5)]);
        var id = document.Segments[0].Id;

        var edited = document.WithSection(new RectangleSectionGeometry(M(0.2), M(0.3)), SectionAxisDesignation.Z, "Wide")
            with { Material = new Material("Other", Pressure.FromPascals(70e9), Pressure.FromPascals(100e6), MaterialStrengthType.Re) };

        Assert.Equal(id, edited.Segments[0].Id);
        Assert.Equal(M(1.5), edited.Segments[0].Length);
        Assert.Equal(M(1.5), edited.Length);
        Assert.Equal("Other", edited.Segments[0].Material.Name);
    }

    [Fact]
    public void SplittingKeepsTheLeftIdentityAndGivesTheRightHalfANewOne()
    {
        var document = new EditorDocument([Segment(4)]);
        var original = document.Segments[0].Id;

        var split = document.WithBoundaryAt(M(1));

        Assert.Equal(original, split.Segments[0].Id);
        Assert.NotEqual(original, split.Segments[1].Id);
        Assert.Equal([M(1), M(3)], split.Segments.Select(segment => segment.Length));
    }

    [Fact]
    public void ADifferentSegmentIdentityIsAContentChange()
    {
        var first = new EditorDocument([Segment(1, Guid.NewGuid())]);
        var second = new EditorDocument([Segment(1, Guid.NewGuid())]);

        Assert.False(ProjectState.DocumentContentEquals(first, second));
    }
}
