using SpanDraft.Core.Beams;
using SpanDraft.Core.Loads;
using SpanDraft.Core.Materials;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Core.Validation;
using Xunit;

namespace SpanDraft.Tests;

public class BeamModelTests
{
    private static Material TestMaterial() => new("Test", Pressure.FromMegapascals(210000), Pressure.FromMegapascals(235), MaterialStrengthType.Re);
    private static Section TestSection() => new RectangleSection(Length.FromMillimeters(40), Length.FromMillimeters(80));
    private static Support[] EndSupports() =>
        [new(default, SupportType.Pinned), new(Length.FromMeters(2), SupportType.Roller)];

    private static BeamModel Beam(IEnumerable<Support>? supports = null, IEnumerable<BeamLoad>? loads = null) =>
        new(Length.FromMeters(2), TestMaterial(), TestSection(), supports ?? EndSupports(), loads ?? []);

    [Fact]
    public void SimplySupportedBeamWithMidspanLoadIsValid()
    {
        var material = TestMaterial();
        var section = TestSection();
        var beam = new BeamModel(Length.FromMeters(2), material, section, EndSupports(),
            [new PointForce(Length.FromMeters(1), Force.FromNewtons(-1000))]);

        Assert.Equal(Length.FromMeters(2), beam.Length);
        Assert.Same(material, beam.Material);
        Assert.Same(section, beam.Section);
        Assert.Empty(BeamModelValidator.Validate(beam));
    }

    [Fact]
    public void EndpointLoadsAndFullLengthDistributedLoadAreValid()
    {
        var beam = Beam(loads:
        [
            new PointForce(default, Force.FromNewtons(1)),
            new PointForce(Length.FromMeters(2), Force.FromNewtons(-1)),
            new PointMoment(default, Moment.FromNewtonMeters(1)),
            new PointMoment(Length.FromMeters(2), Moment.FromNewtonMeters(-1)),
            new DistributedLoad(default, Length.FromMeters(2), ForcePerLength.FromNewtonsPerMeter(-1))
        ]);

        Assert.Empty(BeamModelValidator.Validate(beam));
    }

    [Fact]
    public void OverhangsAndOverlappingLoadsArePermitted()
    {
        var beam = Beam(
            [new(Length.FromMeters(0.5), SupportType.Pinned), new(Length.FromMeters(1.5), SupportType.Roller)],
            [new PointForce(default, Force.FromNewtons(-1)),
             new DistributedLoad(default, Length.FromMeters(2), ForcePerLength.FromNewtonsPerMeter(-1)),
             new DistributedLoad(Length.FromMeters(1), Length.FromMeters(2), ForcePerLength.FromNewtonsPerMeter(-2))]);

        Assert.Empty(BeamModelValidator.Validate(beam));
    }

    [Theory]
    [InlineData(2.000001)]
    [InlineData(3)]
    public void SupportOutsideBeamIsReported(double position)
    {
        var error = Assert.Single(BeamModelValidator.Validate(Beam([new(Length.FromMeters(position), SupportType.Fixed)])));

        Assert.Equal(ValidationErrorCode.SupportOutsideBeam, error.Code);
        Assert.Equal("Supports[0].Position", error.Path);
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }

    [Fact]
    public void PointForceOutsideBeamIsReported()
    {
        var error = Assert.Single(BeamModelValidator.Validate(Beam(loads:
            [new PointForce(Length.FromMeters(3), Force.FromNewtons(-1))])));

        Assert.Equal(ValidationErrorCode.PointForceOutsideBeam, error.Code);
        Assert.Equal("Loads[0].Position", error.Path);
    }

    [Fact]
    public void PointMomentOutsideBeamIsReported()
    {
        var error = Assert.Single(BeamModelValidator.Validate(Beam(loads:
            [new PointMoment(Length.FromMeters(3), Moment.FromNewtonMeters(-1))])));

        Assert.Equal(ValidationErrorCode.PointMomentOutsideBeam, error.Code);
        Assert.Equal("Loads[0].Position", error.Path);
    }

    [Theory]
    [InlineData(1, 3)]
    [InlineData(2.5, 3)]
    public void PartiallyOrEntirelyOutsideDistributedLoadIsReported(double start, double end)
    {
        var error = Assert.Single(BeamModelValidator.Validate(Beam(loads:
            [new DistributedLoad(Length.FromMeters(start), Length.FromMeters(end), default)])));

        Assert.Equal(ValidationErrorCode.DistributedLoadOutsideBeam, error.Code);
        Assert.Equal("Loads[0]", error.Path);
    }

    [Fact]
    public void MissingSupportsAreReported()
    {
        var error = Assert.Single(BeamModelValidator.Validate(Beam(supports: [])));

        Assert.Equal(ValidationErrorCode.MissingSupports, error.Code);
        Assert.Equal("Supports", error.Path);
    }

    [Fact]
    public void IdenticalSupportsAtTheSamePositionAreReported()
    {
        var error = Assert.Single(BeamModelValidator.Validate(Beam(
            [new(default, SupportType.Pinned), new(default, SupportType.Pinned)])));

        Assert.Equal(ValidationErrorCode.DuplicateSupport, error.Code);
        Assert.Equal("Supports[1]", error.Path);
    }

    [Fact]
    public void DifferentSupportTypesAtTheSamePositionAreNotIdentical()
    {
        Assert.Empty(BeamModelValidator.Validate(Beam(
            [new(default, SupportType.Pinned), new(default, SupportType.Roller)])));
    }

    private static Support Spring(double x, double? kw = 1000, double? kt = null, bool rigid = false) =>
        Support.CreateSpring(Length.FromMeters(x), kw is { } w ? TranslationalStiffness.FromNewtonsPerMeter(w) : null,
            kt is { } t ? RotationalStiffness.FromNewtonMetersPerRadian(t) : null, rigid);

    [Theory]
    [InlineData(SupportType.Fixed)]
    [InlineData(SupportType.Pinned)]
    [InlineData(SupportType.Roller)]
    public void SpringAtTheSamePositionAsARigidSupportIsReportedOnTheLaterSupport(SupportType rigid)
    {
        var spring = Spring(1);
        var rigidSupport = new Support(Length.FromMeters(1), rigid);
        var after = Assert.Single(BeamModelValidator.Validate(Beam([rigidSupport, spring])));
        var before = Assert.Single(BeamModelValidator.Validate(Beam([spring, rigidSupport])));

        Assert.Equal(ValidationErrorCode.SpringSupportCoincidence, after.Code);
        Assert.Equal("Supports[1]", after.Path);
        Assert.Equal(ValidationErrorCode.SpringSupportCoincidence, before.Code);
        Assert.Equal("Supports[1]", before.Path);
    }

    [Fact]
    public void TwoSpringsAtTheSamePositionAreACoincidenceNotADuplicate()
    {
        var error = Assert.Single(BeamModelValidator.Validate(Beam([Spring(1), Spring(1, null, 5)])));
        var identical = Assert.Single(BeamModelValidator.Validate(Beam([Spring(1), Spring(1)])));

        Assert.Equal(ValidationErrorCode.SpringSupportCoincidence, error.Code);
        Assert.Equal(ValidationErrorCode.SpringSupportCoincidence, identical.Code);
    }

    [Fact]
    public void SpringsAtDistinctAdjacentPositionsAreValid()
    {
        Assert.Empty(BeamModelValidator.Validate(Beam(
            [Spring(1), Spring(Math.BitIncrement(1)), new(Length.FromMeters(1.5), SupportType.Pinned)])));
    }

    [Fact]
    public void SpringOutsideTheBeamIsReported()
    {
        var error = Assert.Single(BeamModelValidator.Validate(Beam([Spring(3)])));
        Assert.Equal(ValidationErrorCode.SupportOutsideBeam, error.Code);
    }

    [Fact]
    public void LoadsAtTheSpringPositionAreAllowed()
    {
        Assert.Empty(BeamModelValidator.Validate(Beam([Spring(1)],
            [new PointForce(Length.FromMeters(1), Force.FromNewtons(-1)),
             new PointMoment(Length.FromMeters(1), Moment.FromNewtonMeters(1))])));
    }

    [Fact]
    public void ValidationDoesNotAssessStability()
    {
        Assert.Empty(BeamModelValidator.Validate(Beam([new(default, SupportType.Roller)])));
    }

    [Fact]
    public void ValidationCollectsAllErrorsWithTheirSourceIndices()
    {
        var beam = Beam([new(Length.FromMeters(3), SupportType.Fixed), new(Length.FromMeters(3), SupportType.Fixed)],
            [new PointForce(Length.FromMeters(3), default),
             new PointMoment(Length.FromMeters(3), default),
             new DistributedLoad(default, Length.FromMeters(3), default)]);

        var errors = BeamModelValidator.Validate(beam);

        Assert.Equal(6, errors.Count);
        Assert.Equal(2, errors.Count(error => error.Code == ValidationErrorCode.SupportOutsideBeam));
        Assert.Contains(errors, error => error.Code == ValidationErrorCode.DuplicateSupport && error.Path == "Supports[1]");
        Assert.Contains(errors, error => error.Code == ValidationErrorCode.PointForceOutsideBeam && error.Path == "Loads[0].Position");
        Assert.Contains(errors, error => error.Code == ValidationErrorCode.PointMomentOutsideBeam && error.Path == "Loads[1].Position");
        Assert.Contains(errors, error => error.Code == ValidationErrorCode.DistributedLoadOutsideBeam && error.Path == "Loads[2]");
        Assert.Throws<NotSupportedException>(() => ((IList<ValidationError>)errors).Clear());
    }

    [Fact]
    public void CollectionsAreCopiedAndCannotBeChangedThroughTheModel()
    {
        var support = new Support(default, SupportType.Fixed);
        var load = new PointForce(Length.FromMeters(1), Force.FromNewtons(-1));
        var supports = new List<Support> { support };
        BeamLoad[] loads = [load];
        var beam = Beam(supports, loads);

        supports.Clear();
        loads[0] = new PointMoment(default, default);

        Assert.Same(support, Assert.Single(beam.Supports));
        Assert.Same(load, Assert.Single(beam.Loads));
        Assert.Throws<NotSupportedException>(() => ((IList<Support>)beam.Supports).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<BeamLoad>)beam.Loads)[0] = new PointMoment(default, default));
    }

    [Fact]
    public void ZeroBeamLengthIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BeamModel(default, TestMaterial(), TestSection(), [], []));
    }

    [Fact]
    public void NullRequiredObjectsAndCollectionsAreRejected()
    {
        var length = Length.FromMeters(1);
        Assert.Throws<ArgumentNullException>(() => new BeamModel(length, null!, TestSection(), [], []));
        Assert.Throws<ArgumentNullException>(() => new BeamModel(length, TestMaterial(), null!, [], []));
        Assert.Throws<ArgumentNullException>(() => new BeamModel(length, TestMaterial(), TestSection(), null!, []));
        Assert.Throws<ArgumentNullException>(() => new BeamModel(length, TestMaterial(), TestSection(), [], null!));
        Assert.Throws<ArgumentNullException>(() => BeamModelValidator.Validate(null!));
    }

    [Fact]
    public void NullCollectionEntriesAreRejected()
    {
        Assert.Throws<ArgumentException>(() => Beam(supports: [null!]));
        Assert.Throws<ArgumentException>(() => Beam(loads: [null!]));
    }
}
