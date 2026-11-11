using SpanDraft.Solver.Kernel;
using Xunit;
using static SpanDraft.Tests.SolverBendingTestSupport;
using static SpanDraft.Solver.Kernel.RestrainedDirections;

namespace SpanDraft.Tests;

public sealed class SolverBendingRangeTests
{
    [Fact]
    public void LengthLimitIsInclusiveAndUnloadedStillBuilds()
    {
        double limit = Math.ScaleB(1, -968);
        Assert.Throws<NumericalRangeException>(() => SolverKernel.BuildSystem(Model(length: Math.BitDecrement(limit))));
        foreach (double length in new[] { limit, Math.BitIncrement(limit) })
        {
            BendingSystem system = SolverKernel.BuildSystem(Model(length: length));
            Assert.Equal(4, system.Dimension);
            Assert.True(system.IsUnloaded);
            Assert.Equal(0, system.EF);
            Assert.Equal(default, system.Excitation);
            Assert.All(system.B, value => Assert.True(Binary64.IsZero(value)));
            Assert.All(system.BB, value => Assert.Equal(0UL, BitConverter.DoubleToUInt64Bits(value)));
            Assert.Contains(system.A, value => value != 0);
        }
    }

    [Fact]
    public void RequiredNonzeroInputsMustBeNormalAndFinite()
    {
        foreach (double invalid in new[] { double.Epsilon, Math.BitDecrement(Binary64.Lambda),
            double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            Assert.Throws<NumericalRangeException>(() => SolverKernel.BuildSystem(Model(length: invalid)));
            Assert.Throws<NumericalRangeException>(() => SolverKernel.BuildSystem(Model(ei: invalid)));
            Assert.Throws<NumericalRangeException>(() => SolverKernel.BuildSystem(Model(forces: [new(1, invalid)])));
            Assert.Throws<NumericalRangeException>(() => SolverKernel.BuildSystem(Model(moments: [new(1, invalid)])));
            Assert.Throws<NumericalRangeException>(() => SolverKernel.BuildSystem(Model(lines: [new(0, 1, invalid)])));
        }
        foreach (double invalid in new[] { 0d, -0d, -1d })
        {
            Assert.Throws<NumericalRangeException>(() => SolverKernel.BuildSystem(Model(length: invalid)));
            Assert.Throws<NumericalRangeException>(() => SolverKernel.BuildSystem(Model(ei: invalid)));
        }
        Assert.Throws<NumericalRangeException>(() => SolverKernel.BuildSystem(Model(forces: [new(double.Epsilon, 0)])));
        Assert.Throws<NumericalRangeException>(() => SolverKernel.BuildSystem(Model(moments: [new(double.Epsilon, 0)])));
        Assert.Throws<NumericalRangeException>(() => SolverKernel.BuildSystem(Model(lines: [new(double.Epsilon, 1, 0)])));
        Assert.Throws<NumericalRangeException>(() => SolverKernel.BuildSystem(Model(
            restraints: [new(0, U | W | Theta), new(double.Epsilon, W)])));
    }

    [Fact]
    public void NormalizedNonzeroLoadsMustBeStrictlyAboveLambda()
    {
        foreach (double value in new[] { Binary64.Lambda, Math.BitIncrement(Binary64.Lambda) })
        {
            MechanicalInput[] models =
            [
                Model(length: 1, forces: [new(0, 1), new(1, value)]),
                Model(length: 1, forces: [new(0, 1)], moments: [new(1, value)]),
                Model(length: 1, forces: [new(0, 1)], lines: [new(0, 1, value)])
            ];
            foreach (MechanicalInput input in models)
                if (value == Binary64.Lambda)
                    Assert.Throws<NumericalRangeException>(() => SolverKernel.BuildSystem(input));
                else
                    SolverKernel.BuildSystem(input);
        }
        Assert.Throws<NumericalRangeException>(() => SolverKernel.BuildSystem(Model(length: 1,
            forces: [new(0, double.MaxValue), new(1, Binary64.Lambda)])));
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(1d)]
    public void DistributedLengthLimitUsesTheRoundedNormalizedDistanceIncludingZeroIntensity(double q)
    {
        double limit = Math.ScaleB(1, -969);
        Assert.Throws<NumericalRangeException>(() => SolverKernel.BuildSystem(Model(length: 1,
            lines: [new(0, Math.BitDecrement(limit), q)])));
        foreach (double width in new[] { limit, Math.BitIncrement(limit) })
            Assert.Equal(q == 0, SolverKernel.BuildSystem(Model(length: 1, lines: [new(0, width, q)])).IsUnloaded);

        // Global coordinates can be adjacent stored doubles without becoming a mechanical station.
        BendingSystem shortLoad = SolverKernel.BuildSystem(Model(lines: [new(1, Math.BitIncrement(1), q)]));
        Assert.Equal(2, shortLoad.Stations.Length);
    }

    [Fact]
    public void StationDistanceIsCheckedBeforeCoefficientChainsWithoutMergingCoordinates()
    {
        foreach (double gap in new[] { Math.BitDecrement(Binary64.Lambda), Binary64.Lambda, Math.BitIncrement(Binary64.Lambda) })
        {
            MechanicalInput input = Model(length: 1, restraints: [new(0, U | W | Theta), new(gap, W)]);
            StationModel stations = StationModel.Create(input);
            Assert.Equal(3, stations.MechanicalStations.Length);
            Assert.Equal(gap, stations.MechanicalStations[1].Position);
            if (gap <= Binary64.Lambda)
                Assert.Throws<NumericalRangeException>(() => BendingPreparation.Create(input, stations));
            else
                BendingPreparation.Create(input, stations);
            Assert.Throws<NumericalRangeException>(() => SolverKernel.BuildSystem(input));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CoefficientIntermediateUnderflowRejectsEvenUnloaded(bool loaded)
    {
        double gap = Math.ScaleB(1, -340);
        MechanicalInput input = Model(length: 1, restraints: [new(0, U | W | Theta), new(gap, W)],
            forces: loaded ? [new(1, 1)] : []);
        Assert.True(MechanicalStability.Check(StationModel.Create(input)).IsStable);
        Prepare(input); // Raw station distances are normal; the cubic chain is not.
        Assert.Throws<NumericalRangeException>(() => SolverKernel.BuildSystem(input));
    }

    [Fact]
    public void ExponentsFollowUnitsWithoutMaterializingReferencePowers()
    {
        MechanicalInput input = Model(length: Math.ScaleB(1, 900), ei: Math.ScaleB(1, -900),
            forces: [new(0, Math.ScaleB(1, 900))]);
        BendingSystem system = SolverKernel.BuildSystem(input);
        Assert.Equal(900, system.EL);
        Assert.Equal(-900, system.EEI);
        Assert.Equal(3600, system.EF);
        Assert.Equal(new[] { 4500, 3600, 1800, 900 }, system.StateExponents);
        Assert.Equal(-900, system.ForceExponent);
        Assert.Equal(-1800, system.MomentExponent);
        Assert.Equal(0, system.LineLoadExponent);
        SolverExactNumber.AssertEncloses(system.Excitation.Lower, SolverExactNumber.Integer(1), system.Excitation.Upper);
    }

    [Fact]
    public void EachLoadKindUsesTheExactExcitationExponentAndItsOwnUnitScaling()
    {
        MechanicalInput[] inputs =
        [
            Model(forces: [new(1, 7)]),
            Model(moments: [new(1, 3)]),
            Model(lines: [new(0.5, 1, 3)])
        ];
        int[] forceExponents = [2, 0, 0]; // 7, 3/2, and 3*(1-1/2).
        for (int i = 0; i < inputs.Length; i++)
        {
            BendingSystem system = SolverKernel.BuildSystem(inputs[i]);
            Assert.Equal(1, system.EL);
            Assert.Equal(0, system.EEI);
            Assert.Equal(forceExponents[i] + 2, system.EF);
            int f = forceExponents[i] + 2;
            Assert.Equal(new[] { 1 + f, f, -1 + f, -2 + f }, system.StateExponents);
            Assert.Equal(2 - f, system.ForceExponent);
            Assert.Equal(1 - f, system.MomentExponent);
            Assert.Equal(3 - f, system.LineLoadExponent);
        }
    }
}
