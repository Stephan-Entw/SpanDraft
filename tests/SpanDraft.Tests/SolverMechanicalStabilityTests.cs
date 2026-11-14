using SpanDraft.Solver.Kernel;
using Xunit;
using static SpanDraft.Solver.Kernel.RestrainedDirections;

namespace SpanDraft.Tests;

public sealed class SolverMechanicalStabilityTests
{
    private static (RigidRestraint[] Restraints, int Rank, bool Axial)[] Cases =>
    [
        ([], 0, false),
        ([new(0, U)], 0, true),
        ([new(1, Theta)], 1, false),
        ([new(0, U | W)], 1, true),
        ([new(0, W), new(2, W)], 2, false),
        ([new(0, U | W), new(2, W)], 2, true),
        ([new(1, U | W), new(1, W)], 1, true),
        ([new(1, U | W | Theta)], 2, true),
        ([new(1, U | W | Theta), new(1, U | W), new(1, W)], 2, true),
        ([new(1, U | W), new(Math.BitIncrement(1), W)], 2, true),
        ([new(0, Theta), new(1, U | W)], 2, true),
        ([new(-0d, U | W), new(0, W)], 1, true)
    ];

    [Fact]
    public void ExactRankAndAxialModeAreIndependent()
    {
        foreach ((RigidRestraint[] restraints, int rank, bool axial) in Cases)
        {
            MechanicalInput input = SolverBendingTestSupport.Model(restraints: restraints);
            MechanicalStability stability = MechanicalStability.Check(StationModel.Create(input));
            Assert.Equal(rank, stability.BendingRank);
            Assert.Equal(axial, stability.AxiallyRestrained);
            Assert.Equal(rank == 2 && axial, stability.IsStable);
            if (stability.IsStable)
                SolverKernel.BuildSystem(input);
            else
                Assert.Equal(stability, Assert.Throws<MechanicalMechanismException>(() => SolverKernel.BuildSystem(input)).Stability);
        }
    }

    private static (ElasticRestraint[] Elastic, RigidRestraint[] Rigid, int Rank, bool Axial)[] SpringCases =>
    [
        ([new(1, 5, 0, false)], [], 1, true),
        ([new(0, 5, 0, false), new(2, 7, 0, false)], [], 2, true),
        ([new(1, 0, 5, false)], [], 1, true),
        ([new(1, 5, 7, false)], [], 2, true),
        ([new(1, 0, 7, true)], [], 2, true),
        ([new(1, 5, 0, false)], [new(0, W)], 2, true),
        ([new(1, 0, 5, false)], [new(0, U | W)], 2, true),
        ([new(1, 0, 5, false)], [new(0, Theta)], 1, true),
        ([new(1, 5, 0, false), new(Math.BitIncrement(1), 5, 0, false)], [], 2, true),
        ([new(1, 5, 0, false)], [new(2, U | W)], 2, true)
    ];

    [Fact]
    public void SpringsCountThroughTheirActingDirectionIndependentOfStiffness()
    {
        foreach ((ElasticRestraint[] elastic, RigidRestraint[] rigid, int rank, bool axial) in SpringCases)
            foreach (double scale in new[] { 1, 1e-300, 1e300 })
            {
                ElasticRestraint[] scaled = [.. elastic.Select(e => e with
                {
                    TranslationalStiffness = e.TranslationalStiffness * scale,
                    RotationalStiffness = e.RotationalStiffness * scale
                })];
                MechanicalInput input = SolverBendingTestSupport.Model(restraints: rigid, elastic: scaled);
                MechanicalStability stability = MechanicalStability.Check(StationModel.Create(input));
                Assert.Equal(rank, stability.BendingRank);
                Assert.Equal(axial, stability.AxiallyRestrained);
            }
    }

    [Fact]
    public void ASpringSupportIsAxiallyRigidSoALoneRollerPairStaysUnstableButSpringsDoNot()
    {
        MechanicalInput rollers = SolverBendingTestSupport.Model(restraints: [new(0, W), new(2, W)]);
        MechanicalInput springs = SolverBendingTestSupport.Model(elastic: [new(0, 5, 0, false), new(2, 5, 0, false)]);
        Assert.False(MechanicalStability.Check(StationModel.Create(rollers)).IsStable);
        Assert.True(MechanicalStability.Check(StationModel.Create(springs)).IsStable);
    }

    [Fact]
    public void ASingleTranslationalOrRotationalSpringLeavesAMechanism()
    {
        foreach (ElasticRestraint elastic in new ElasticRestraint[] { new(1, 5, 0, false), new(1, 0, 5, false) })
            Assert.Throws<MechanicalMechanismException>(() =>
                SolverKernel.RequireStable([], [elastic]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MechanismPrecedesEveryNumericalPreparation(bool loaded)
    {
        // All coordinates are valid; both L and EI violate numerical contract limits.
        var input = SolverBendingTestSupport.Model(length: Math.ScaleB(1, -969), ei: double.Epsilon,
            restraints: [new(0, U | W)], forces: loaded ? [new(0, double.Epsilon)] : []);
        MechanicalMechanismException exception = Assert.Throws<MechanicalMechanismException>(() => SolverKernel.BuildSystem(input));
        Assert.Equal(1, exception.Stability.BendingRank);
        Assert.True(exception.Stability.AxiallyRestrained);
    }

    [Fact]
    public void ExactRankDoesNotCollapseTinyOrVeryLargeCoordinateDifferences()
    {
        foreach ((double x, double next, double length) in new[]
        {
            (0d, double.Epsilon, 2d),
            (Math.ScaleB(1, -900), Math.BitIncrement(Math.ScaleB(1, -900)), 2d),
            (Math.ScaleB(1, 900), Math.BitIncrement(Math.ScaleB(1, 900)), Math.ScaleB(1, 901))
        })
        {
            MechanicalInput input = SolverBendingTestSupport.Model(length: length,
                restraints: [new(x, U | W), new(next, W)]);
            Assert.Equal(new MechanicalStability(2, true), MechanicalStability.Check(StationModel.Create(input)));
        }
    }
}
