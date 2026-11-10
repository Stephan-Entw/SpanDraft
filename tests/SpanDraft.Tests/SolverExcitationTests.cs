using SpanDraft.Solver.Kernel;
using Xunit;
using static SpanDraft.Tests.SolverExactNumber;

namespace SpanDraft.Tests;

public sealed class SolverExcitationTests
{
    private static readonly double Tiny = BitConverter.UInt64BitsToDouble(1);
    private const double Normal = 2.2250738585072014e-308;
    private const double Max = double.MaxValue;
    private static readonly double Short = Math.ScaleB(1, -550);

    [Fact]
    public void EnclosesTheExactNormAcrossTheBoundaryFamilies()
    {
        Check(Model(lines: [new(0, Short, Short)]), unloaded: false);
        Check(Model(forces: [new(2, Tiny)]), unloaded: false);
        Check(Model(moments: [new(2, -Tiny)]), unloaded: false);
        Check(Model(forces: [new(2, Max), new(2, -Max), new(3, Tiny)]), unloaded: false);
        Check(Model(forces: [new(2, Normal)], moments: [new(3, Math.BitDecrement(Normal))]), unloaded: false);
        Check(Model(forces: [new(2, Max)], moments: [new(3, Tiny)]), unloaded: false);
        Check(Model(forces: [new(2, 4), new(2, -4)]), unloaded: false);
        Check(Model(forces: [new(2, 4), new(2, -4)], lines: [new(0, 1, -1)]), unloaded: false);
        Check(Model(moments: [new(2, 1)], length: 3), unloaded: false);
        Check(Model(moments: [new(2, Max)], length: Tiny), unloaded: false);
        Check(Model(moments: [new(2, Tiny)], length: Max), unloaded: false);
        Check(Model(lines: [new(-Max, Max, Max)], length: Max), unloaded: false);
        Check(Model(lines: [new(0, Tiny, Tiny)]), unloaded: false);
        Check(Model(lines: [new(0, Short, 0)]), unloaded: true);
        Check(Model(forces: [new(2, -0d)], moments: [new(2, 0)], lines: [new(0, Short, -0d)]), unloaded: true);
        Check(Model(), unloaded: true);
    }

    [Fact]
    public void ExactWidthCanPutAnApparentlyUnitContributionBelowOne()
    {
        MechanicalInput input = Model(lines: [new(Math.ScaleB(1, -54), 1, 1)]);
        Assert.Equal(-1, Check(input, unloaded: false).Exponent);
    }

    [Fact]
    public void ExactExponentFallbackIncludesAllThreeLoadKinds()
    {
        TransversePointForce[] forces = [new(2, Math.BitDecrement(0.5))];
        AppliedPointMoment[] moments = [new(2, 1)];
        Assert.Equal(-1, Check(Model(forces, moments, [new(0, 0.25, 1)]), unloaded: false).Exponent);
        Assert.Equal(0, Check(Model(forces, moments, [new(0, 0.25, Math.BitIncrement(1))]), unloaded: false).Exponent);
    }

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(2, 1, 1)]
    public void ExcitationRejectsNonPositiveExactWidthForNonzeroIntensity(double start, double end, double intensity) =>
        Assert.Throws<NumericalRangeException>(() => BendingExcitation.Excitation(Model(lines: [new(start, end, intensity)])));

    [Theory]
    [InlineData(1, 1, -0d)]
    [InlineData(2, 1, 0d)]
    public void ExcitationIgnoresZeroIntensityWithNonPositiveWidth(double start, double end, double intensity) =>
        Check(Model(lines: [new(start, end, intensity)]), unloaded: true);

    [Fact]
    public void ExcitationRejectsInvalidMomentLengthAndNonFiniteLoads()
    {
        foreach (double length in new[] { 0, -1, double.PositiveInfinity, double.NaN })
            Assert.Throws<NumericalRangeException>(() =>
                BendingExcitation.Excitation(Model(moments: [new(0, 1)], length: length)));

        Assert.Throws<NumericalRangeException>(() =>
            BendingExcitation.Excitation(Model(forces: [new(0, double.NaN)])));
        Assert.Throws<NumericalRangeException>(() =>
            BendingExcitation.Excitation(Model(lines: [new(0, 1, double.PositiveInfinity)])));
    }

    private static MechanicalInput Model(TransversePointForce[]? forces = null, AppliedPointMoment[]? moments = null,
        ConstantLineLoad[]? lines = null, double length = 4) =>
        new(length, 1, [], forces ?? [], moments ?? [], lines ?? []);

    private static ExcitationBounds Check(MechanicalInput input, bool unloaded)
    {
        SolverExactNumber exact = Integer(0);
        foreach (TransversePointForce force in input.PointForces)
            exact += From(force.Newtons).Abs();
        foreach (AppliedPointMoment moment in input.PointMoments)
            exact += From(moment.NewtonMeters).Abs() / From(input.Length);
        foreach (ConstantLineLoad line in input.LineLoads)
            exact += From(line.NewtonsPerMeter).Abs() * (From(line.End) - From(line.Start));

        Assert.Equal(unloaded, exact.Numerator.IsZero);
        Assert.Equal(unloaded, BendingExcitation.IsUnloaded(input));
        ExcitationBounds bounds = BendingExcitation.Excitation(input);
        if (unloaded)
            Assert.Equal(new ExcitationBounds(0, 0, 0), bounds);
        else
        {
            exact.AssertExponent(bounds.Exponent);
            AssertEncloses(bounds.Lower, exact / PowerOfTwo(bounds.Exponent), bounds.Upper);
        }
        return bounds;
    }
}
