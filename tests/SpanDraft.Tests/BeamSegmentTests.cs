using SpanDraft.Core.Beams;
using SpanDraft.Core.Loads;
using SpanDraft.Core.Materials;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Core.Validation;
using Xunit;

namespace SpanDraft.Tests;

public sealed class BeamSegmentTests
{
    private static Material Steel() => new("Steel", Pressure.FromMegapascals(210000), Pressure.FromMegapascals(235),
        MaterialStrengthType.Re, MassDensity.FromKilogramsPerCubicMeter(7850));
    private static Section Rectangle(double width, double height) =>
        new RectangleSection(Length.FromMillimeters(width), Length.FromMillimeters(height));
    private static Length M(double meters) => Length.FromMeters(meters);
    private static Support[] Supports() => [new(default, SupportType.Fixed)];

    private static BeamSegment Segment(double start, double end, Section? section = null,
        SectionAxisDesignation axis = SectionAxisDesignation.Y) =>
        new(M(start), M(end), Steel(), section ?? Rectangle(40, 80), axis);

    private static BeamModel Beam(double length, params BeamSegment[] segments) =>
        new(M(length), segments, Supports(), []);

    [Fact]
    public void SegmentsResolveTheirOwnAxisAndKeepTheirOwnProperties()
    {
        Section section = Rectangle(40, 80);
        Material material = Steel();
        var segment = new BeamSegment(M(0.5), M(2), material, section, SectionAxisDesignation.Y);

        Assert.Equal(M(0.5), segment.Start);
        Assert.Equal(M(2), segment.End);
        Assert.Same(material, segment.Material);
        Assert.Same(section, segment.Section);
        Assert.Equal(section.GetAxis(SectionAxisDesignation.Y), segment.BendingAxisProperties);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    public void ASegmentNeedsPositiveLength(double start, double end) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Segment(start, end));

    [Fact]
    public void ASegmentRejectsAnUnavailableOrUnknownAxis()
    {
        Assert.Throws<ArgumentException>(() => Segment(0, 1, axis: SectionAxisDesignation.Z));
        Assert.Throws<ArgumentOutOfRangeException>(() => Segment(0, 1, axis: (SectionAxisDesignation)99));
        Assert.Throws<ArgumentNullException>(() => new BeamSegment(M(0), M(1), null!, Rectangle(40, 80),
            SectionAxisDesignation.Y));
    }

    [Fact]
    public void ABeamNeedsAtLeastOneSegmentWithoutNullEntries()
    {
        Assert.Throws<ArgumentException>(() => Beam(1));
        Assert.Throws<ArgumentException>(() => new BeamModel(M(1), new BeamSegment[] { null! }, Supports(), []));
    }

    [Fact]
    public void ASingleSegmentConstructorBuildsExactlyOneSegmentOverTheBeam()
    {
        Material material = Steel();
        Section section = Rectangle(40, 80);
        var beam = new BeamModel(M(3), material, section, Supports(), []);

        BeamSegment segment = Assert.Single(beam.Segments);
        Assert.Equal(default, segment.Start);
        Assert.Equal(M(3), segment.End);
        Assert.Same(material, beam.Material);
        Assert.Same(section, beam.Section);
        Assert.Equal(SectionAxisDesignation.Y, beam.BendingAxis);
        Assert.Same(segment.BendingAxisProperties, beam.BendingAxisProperties);
        Assert.Empty(BeamModelValidator.Validate(beam));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BeamModel(default, material, section, Supports(), []));
    }

    [Fact]
    public void SingleSegmentPropertiesThrowForSeveralSegmentsInsteadOfPickingTheFirst()
    {
        BeamModel beam = Beam(2, Segment(0, 1), Segment(1, 2, Rectangle(60, 100)));

        Assert.Equal(2, beam.Segments.Count);
        Assert.Throws<InvalidOperationException>(() => beam.Material);
        Assert.Throws<InvalidOperationException>(() => beam.Section);
        Assert.Throws<InvalidOperationException>(() => beam.BendingAxis);
        Assert.Throws<InvalidOperationException>(() => beam.BendingAxisProperties);
    }

    [Fact]
    public void TilingSegmentsAreValidAndMayShareBoundariesWithSupportsAndLoads()
    {
        var beam = new BeamModel(M(3), [Segment(0, 1), Segment(1, 2, Rectangle(60, 100)), Segment(2, 3)],
            [new(M(1), SupportType.Pinned), Support.CreateSpring(M(2), TranslationalStiffness.FromNewtonsPerMeter(1000), null)],
            [new PointForce(M(1), Force.FromNewtons(-1)), new PointMoment(M(2), Moment.FromNewtonMeters(1)),
                new DistributedLoad(M(0.5), M(2.5), ForcePerLength.FromNewtonsPerMeter(-1))]);

        Assert.Empty(BeamModelValidator.Validate(beam));
    }

    [Fact]
    public void SegmentTilingErrorsAreReportedWithTheirPath()
    {
        ValidationError[] gap = BeamModelValidator.Validate(Beam(3, Segment(0, 1), Segment(1.5, 3))).ToArray();
        ValidationError[] overlap = BeamModelValidator.Validate(Beam(3, Segment(0, 2), Segment(1, 3))).ToArray();
        ValidationError[] late = BeamModelValidator.Validate(Beam(3, Segment(0.5, 3))).ToArray();
        ValidationError[] short_ = BeamModelValidator.Validate(Beam(3, Segment(0, 2))).ToArray();
        ValidationError[] long_ = BeamModelValidator.Validate(Beam(3, Segment(0, 4))).ToArray();
        ValidationError[] unordered = BeamModelValidator.Validate(Beam(3, Segment(2, 3), Segment(0, 2))).ToArray();

        Assert.Equal("Segments[1].Start", Assert.Single(gap).Path);
        Assert.Equal("Segments[1].Start", Assert.Single(overlap).Path);
        Assert.Equal("Segments[0].Start", Assert.Single(late).Path);
        Assert.Equal("Segments[0].End", Assert.Single(short_).Path);
        Assert.Equal("Segments[0].End", Assert.Single(long_).Path);
        Assert.Equal(["Segments[0].Start", "Segments[1].Start", "Segments[1].End"], unordered.Select(e => e.Path));
        Assert.All(gap.Concat(overlap).Concat(late).Concat(short_).Concat(long_).Concat(unordered),
            error => Assert.Equal(ValidationErrorCode.SegmentsNotContiguous, error.Code));
    }

    [Fact]
    public void ContiguityIsExactToTheLastBit()
    {
        double boundary = 0.1 + 0.2;
        double next = Math.BitIncrement(boundary);

        Assert.Empty(BeamModelValidator.Validate(Beam(1, Segment(0, boundary), Segment(boundary, 1))));
        Assert.Equal("Segments[1].Start",
            Assert.Single(BeamModelValidator.Validate(Beam(1, Segment(0, boundary), Segment(next, 1)))).Path);
        Assert.Equal("Segments[0].End",
            Assert.Single(BeamModelValidator.Validate(Beam(boundary, Segment(0, next)))).Path);
    }

    [Fact]
    public void SegmentSelfWeightOverloadCoversTheSegmentOnlyAndFullSpanMatchesTheLegacyFactory()
    {
        MassDensity density = MassDensity.FromKilogramsPerCubicMeter(7850);
        Area area = Area.FromSquareMeters(0.003);

        DistributedLoad part = DistributedLoad.FromSelfWeight(M(1), M(2.5), density, area);
        DistributedLoad full = DistributedLoad.FromSelfWeight(M(3), density, area);
        DistributedLoad same = DistributedLoad.FromSelfWeight(default, M(3), density, area);

        Assert.Equal(M(1), part.StartPosition);
        Assert.Equal(M(2.5), part.EndPosition);
        Assert.True(part.IsConstant);
        Assert.Equal(-7850 * 0.003 * 9.80665, part.StartIntensity.NewtonsPerMeter);
        Assert.Equal(full.StartIntensity, same.StartIntensity);
        Assert.Equal(full.EndPosition, same.EndPosition);
        Assert.Throws<ArgumentOutOfRangeException>(() => DistributedLoad.FromSelfWeight(M(2), M(2), density, area));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DistributedLoad.FromSelfWeight(M(1), M(2), MassDensity.FromKilogramsPerCubicMeter(0), area));
    }
}
