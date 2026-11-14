using SpanDraft.Core.Beams;
using SpanDraft.Core.Loads;
using SpanDraft.Core.Materials;
using SpanDraft.Core.Releases;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Core.Validation;
using Xunit;

namespace SpanDraft.Tests;

public sealed class MomentReleaseModelTests
{
    private static Material Steel() => new("Steel", Pressure.FromMegapascals(210000), Pressure.FromMegapascals(235),
        MaterialStrengthType.Re);
    private static Length M(double meters) => Length.FromMeters(meters);
    private static MomentRelease Release(double x) => new(M(x));

    private static Support Spring(double x, double? kw, double? kt, bool rigid = false) =>
        Support.CreateSpring(M(x), kw is { } w ? TranslationalStiffness.FromNewtonsPerMeter(w) : null,
            kt is { } t ? RotationalStiffness.FromNewtonMetersPerRadian(t) : null, rigid);

    // A 4 m beam on a pinned end, a roller at 4 m and a roller at 2 m unless stated otherwise.
    private static BeamModel Beam(IEnumerable<MomentRelease> releases, IEnumerable<Support>? supports = null,
        IEnumerable<BeamLoad>? loads = null) =>
        new(M(4), [new BeamSegment(default, M(4), Steel(), new RectangleSection(Length.FromMillimeters(40),
            Length.FromMillimeters(80)), SectionAxisDesignation.Y)],
            supports ?? [new(default, SupportType.Pinned), new(M(2), SupportType.Roller), new(M(4), SupportType.Roller)],
            loads ?? [], releases);

    private static ValidationError Single(BeamModel beam) => Assert.Single(BeamModelValidator.Validate(beam));

    [Fact]
    public void ABeamWithoutReleasesHasAnEmptyListWhicheverConstructorIsUsed()
    {
        var section = new RectangleSection(Length.FromMillimeters(40), Length.FromMillimeters(80));
        BeamModel viaMaterial = new(M(4), Steel(), section, [new Support(default, SupportType.Pinned)], []);
        BeamModel viaSegments = new(M(4), viaMaterial.Segments, viaMaterial.Supports, []);
        Assert.Empty(viaMaterial.MomentReleases);
        Assert.Empty(viaSegments.MomentReleases);
        Assert.Empty(BeamModelValidator.Validate(viaSegments));
    }

    [Fact]
    public void ReleasesKeepTheirOrderAndRejectNullEntries()
    {
        BeamModel beam = Beam([Release(3), Release(1)]);
        Assert.Equal([3d, 1d], beam.MomentReleases.Select(r => r.Position.Meters));
        Assert.Empty(BeamModelValidator.Validate(beam));
        Assert.Throws<ArgumentException>(() => Beam([Release(1), null!]));
        Assert.Throws<ArgumentNullException>(() => Beam(null!));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(4.000001)]
    [InlineData(10)]
    public void AReleaseAtOrBeyondABeamEndIsReported(double position)
    {
        ValidationError error = Single(Beam([Release(position)]));
        Assert.Equal(ValidationErrorCode.MomentReleaseNotInsideBeam, error.Code);
        Assert.Equal("MomentReleases[0].Position", error.Path);
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }

    [Fact]
    public void ASignedZeroPositionIsTheBeamStart()
    {
        Assert.Equal(ValidationErrorCode.MomentReleaseNotInsideBeam,
            Single(Beam([Release(-0d)])).Code);
    }

    [Fact]
    public void TheNeighbourBitsOfBothEndsAreInside()
    {
        Assert.Empty(BeamModelValidator.Validate(Beam([Release(double.Epsilon), Release(Math.BitDecrement(4))])));
    }

    [Fact]
    public void TwoReleasesAtTheSamePositionAreReportedOnTheLaterOne()
    {
        ValidationError error = Single(Beam([Release(1), Release(1)]));
        Assert.Equal(ValidationErrorCode.DuplicateMomentRelease, error.Code);
        Assert.Equal("MomentReleases[1]", error.Path);
    }

    [Fact]
    public void ReleasesAtAdjacentBinary64PositionsAreDistinct()
    {
        Assert.Empty(BeamModelValidator.Validate(Beam([Release(1), Release(Math.BitIncrement(1))])));
    }

    [Fact]
    public void AReleaseOnAFixedSupportIsReportedEvenWhenAnotherSupportSharesThePosition()
    {
        Support[] supports = [new(M(1), SupportType.Fixed), new(M(4), SupportType.Roller)];
        ValidationError error = Single(Beam([Release(1)], supports));
        Assert.Equal(ValidationErrorCode.MomentReleaseAtFixedSupport, error.Code);
        Assert.Equal("MomentReleases[0]", error.Path);
    }

    [Theory]
    [InlineData(SupportType.Pinned)]
    [InlineData(SupportType.Roller)]
    public void AReleaseOnAPinnedOrRollerSupportIsValid(SupportType type)
    {
        Assert.Empty(BeamModelValidator.Validate(Beam([Release(2)],
            [new(default, SupportType.Pinned), new(M(2), type), new(M(4), SupportType.Roller)])));
    }

    [Fact]
    public void AReleaseOnABeamEndSupportIsNotInside()
    {
        Assert.Equal(ValidationErrorCode.MomentReleaseNotInsideBeam, Single(Beam([Release(4)])).Code);
    }

    [Theory]
    [InlineData(1d)]
    [InlineData(0d)]
    public void AReleaseOnAPointMomentIsReportedWhateverItsValue(double newtonMeters)
    {
        ValidationError error = Single(Beam([Release(1)], loads: [new PointMoment(M(1), Moment.FromNewtonMeters(newtonMeters))]));
        Assert.Equal(ValidationErrorCode.MomentReleaseAtPointMoment, error.Code);
        Assert.Equal("MomentReleases[0]", error.Path);
    }

    [Fact]
    public void AReleaseNextToAPointMomentIsValid()
    {
        Assert.Empty(BeamModelValidator.Validate(Beam([Release(1)],
            loads: [new PointMoment(M(Math.BitIncrement(1)), Moment.FromNewtonMeters(5))])));
    }

    [Fact]
    public void AReleaseOnARotationalOrPartialSpringIsReported()
    {
        foreach (Support spring in new[] { Spring(1, null, 50), Spring(1, 100, 50), Spring(1, null, 50, rigid: true) })
        {
            ValidationError error = Single(Beam([Release(1)], [new(default, SupportType.Pinned), spring,
                new(M(4), SupportType.Roller)]));
            Assert.Equal(ValidationErrorCode.MomentReleaseAtRotationalSpring, error.Code);
        }
    }

    [Fact]
    public void AReleaseOnATranslationalSpringIsValid()
    {
        Assert.Empty(BeamModelValidator.Validate(Beam([Release(1)],
            [new(default, SupportType.Pinned), Spring(1, 100, null), new(M(4), SupportType.Roller)])));
    }

    [Fact]
    public void ReleasesMayShareThePositionWithForcesLoadBoundariesAndSegmentBoundaries()
    {
        var section = new RectangleSection(Length.FromMillimeters(40), Length.FromMillimeters(80));
        BeamModel beam = new(M(4),
            [new BeamSegment(default, M(2), Steel(), section, SectionAxisDesignation.Y),
             new BeamSegment(M(2), M(4), Steel(), section, SectionAxisDesignation.Y)],
            [new Support(default, SupportType.Pinned), new Support(M(1), SupportType.Roller), new Support(M(4), SupportType.Roller)],
            [new PointForce(M(2), Force.FromNewtons(-1)),
             new DistributedLoad(M(2), M(3), ForcePerLength.FromNewtonsPerMeter(-1))],
            [Release(2)]);
        Assert.Empty(BeamModelValidator.Validate(beam));
    }

    [Fact]
    public void SeveralViolationsAreAllReported()
    {
        BeamModel beam = Beam([Release(0), Release(1), Release(1)], [new(M(1), SupportType.Fixed), new(M(4), SupportType.Roller)],
            [new PointMoment(M(1), Moment.FromNewtonMeters(1))]);
        Assert.Equal([ValidationErrorCode.MomentReleaseNotInsideBeam, ValidationErrorCode.MomentReleaseAtFixedSupport,
                ValidationErrorCode.MomentReleaseAtPointMoment, ValidationErrorCode.DuplicateMomentRelease,
                ValidationErrorCode.MomentReleaseAtFixedSupport, ValidationErrorCode.MomentReleaseAtPointMoment],
            BeamModelValidator.Validate(beam).Select(e => e.Code));
    }
}
