using SpanDraft.Core.Loads;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using Xunit;

namespace SpanDraft.Tests;

public class SupportAndLoadTests
{
    [Theory]
    [InlineData(SupportType.Fixed)]
    [InlineData(SupportType.Pinned)]
    [InlineData(SupportType.Roller)]
    public void AllSupportTypesCanBeCreated(SupportType type)
    {
        var support = new Support(Length.FromMeters(0.5), type);

        Assert.Equal(Length.FromMeters(0.5), support.Position);
        Assert.Equal(type, support.Type);
    }

    [Fact]
    public void SupportAtTheOriginIsValid()
    {
        Assert.Equal(default, new Support(default, SupportType.Fixed).Position);
    }

    [Fact]
    public void SpringSupportsNeedTheirStiffnessAndCannotUseTheRigidConstructor()
    {
        Assert.Throws<ArgumentException>(() => new Support(default, SupportType.Spring));
    }

    [Theory]
    [InlineData(1e3, null, false)]
    [InlineData(null, 2e3, false)]
    [InlineData(1e3, 2e3, false)]
    [InlineData(null, 2e3, true)]
    public void SpringSupportsRetainTheirValidCombinations(double? translational, double? rotational, bool rigid)
    {
        var support = Support.CreateSpring(Length.FromMeters(0.5),
            translational is { } w ? TranslationalStiffness.FromNewtonsPerMeter(w) : null,
            rotational is { } t ? RotationalStiffness.FromNewtonMetersPerRadian(t) : null, rigid);

        Assert.Equal(SupportType.Spring, support.Type);
        Assert.Equal(Length.FromMeters(0.5), support.Position);
        Assert.Equal(translational, support.TranslationalSpring?.NewtonsPerMeter);
        Assert.Equal(rotational, support.RotationalSpring?.NewtonMetersPerRadian);
        Assert.Equal(rigid, support.RigidTranslation);
    }

    [Theory]
    [InlineData(null, null, false)]
    [InlineData(null, null, true)]
    [InlineData(1e3, 2e3, true)]
    [InlineData(1e3, null, true)]
    public void InvalidSpringCombinationsAreRejected(double? translational, double? rotational, bool rigid)
    {
        Assert.Throws<ArgumentException>(() => Support.CreateSpring(default,
            translational is { } w ? TranslationalStiffness.FromNewtonsPerMeter(w) : null,
            rotational is { } t ? RotationalStiffness.FromNewtonMetersPerRadian(t) : null, rigid));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void StiffnessMustBeFiniteAndStrictlyPositive(double value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TranslationalStiffness.FromNewtonsPerMeter(value));
        Assert.Throws<ArgumentOutOfRangeException>(() => RotationalStiffness.FromNewtonMetersPerRadian(value));
    }

    [Fact]
    public void ADefaultStiffnessIsNotAcceptedAsASpring()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Support.CreateSpring(default, default(TranslationalStiffness), null));
        Assert.Throws<ArgumentOutOfRangeException>(() => Support.CreateSpring(default, null, default(RotationalStiffness)));
    }

    [Fact]
    public void RigidSupportsHaveNoSprings()
    {
        var support = new Support(default, SupportType.Roller);
        Assert.Null(support.TranslationalSpring);
        Assert.Null(support.RotationalSpring);
        Assert.False(support.RigidTranslation);
    }

    [Fact]
    public void UnknownSupportTypeIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Support(default, (SupportType)99));
    }

    [Theory]
    [InlineData(-100)]
    [InlineData(0)]
    [InlineData(100)]
    public void LoadsRetainPositiveNegativeAndZeroMagnitudes(double magnitude)
    {
        var position = Length.FromMeters(0.5);
        var force = new PointForce(position, Force.FromNewtons(magnitude));
        var moment = new PointMoment(position, Moment.FromNewtonMeters(magnitude));
        var distributed = new DistributedLoad(default, position, ForcePerLength.FromNewtonsPerMeter(magnitude));

        Assert.Equal(position, force.Position);
        Assert.Equal(magnitude, force.Force.Newtons);
        Assert.Equal(position, moment.Position);
        Assert.Equal(magnitude, moment.Moment.NewtonMeters);
        Assert.Equal(default, distributed.StartPosition);
        Assert.Equal(position, distributed.EndPosition);
        Assert.Equal(magnitude, distributed.StartIntensity.NewtonsPerMeter);
        Assert.Equal(magnitude, distributed.EndIntensity.NewtonsPerMeter);
        Assert.True(distributed.IsConstant);
    }

    [Theory]
    [InlineData(-500, 0)]
    [InlineData(0, 250)]
    [InlineData(-500, 500)]
    [InlineData(0, 0)]
    public void DistributedLoadKeepsBothEndIntensitiesIncludingZeroAndSignChange(double start, double end)
    {
        var load = new DistributedLoad(default, Length.FromMeters(2),
            ForcePerLength.FromNewtonsPerMeter(start), ForcePerLength.FromNewtonsPerMeter(end));

        Assert.Equal(start, load.StartIntensity.NewtonsPerMeter);
        Assert.Equal(end, load.EndIntensity.NewtonsPerMeter);
        Assert.Equal(start == end, load.IsConstant);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    public void DistributedLoadRejectsEmptyOrReversedIntervals(double start, double end)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DistributedLoad(
            Length.FromMeters(start), Length.FromMeters(end), ForcePerLength.FromNewtonsPerMeter(-1)));
    }

    [Fact]
    public void NegativePositionsAreRejectedBeforeAnObjectCanBeCreated()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Support(Length.FromMeters(-1), SupportType.Fixed));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PointForce(Length.FromMeters(-1), default));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PointMoment(Length.FromMeters(-1), default));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DistributedLoad(Length.FromMeters(-1), Length.FromMeters(1), default));
    }
}
