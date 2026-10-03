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
        var distributed = new UniformDistributedLoad(default, position, ForcePerLength.FromNewtonsPerMeter(magnitude));

        Assert.Equal(position, force.Position);
        Assert.Equal(magnitude, force.Force.Newtons);
        Assert.Equal(position, moment.Position);
        Assert.Equal(magnitude, moment.Moment.NewtonMeters);
        Assert.Equal(default, distributed.StartPosition);
        Assert.Equal(position, distributed.EndPosition);
        Assert.Equal(magnitude, distributed.Intensity.NewtonsPerMeter);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    public void DistributedLoadRejectsEmptyOrReversedIntervals(double start, double end)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new UniformDistributedLoad(
            Length.FromMeters(start), Length.FromMeters(end), ForcePerLength.FromNewtonsPerMeter(-1)));
    }

    [Fact]
    public void NegativePositionsAreRejectedBeforeAnObjectCanBeCreated()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Support(Length.FromMeters(-1), SupportType.Fixed));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PointForce(Length.FromMeters(-1), default));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PointMoment(Length.FromMeters(-1), default));
        Assert.Throws<ArgumentOutOfRangeException>(() => new UniformDistributedLoad(Length.FromMeters(-1), Length.FromMeters(1), default));
    }
}
