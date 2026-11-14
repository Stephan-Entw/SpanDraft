using SpanDraft.Core.Beams;
using SpanDraft.Core.Loads;
using SpanDraft.Core.Supports;
using SpanDraft.Solver;
using SpanDraft.Solver.Kernel;
using Xunit;
using static SpanDraft.Solver.Kernel.RestrainedDirections;
using static SpanDraft.Tests.SolverBendingTestSupport;
using static SpanDraft.Tests.SolverTestSupport;
using static SpanDraft.Tests.SolverSpringAnalyticalTests;

namespace SpanDraft.Tests;

/// <summary>Mechanical invariants of spring supports that hold for every load and stiffness.</summary>
public sealed class SolverSpringInvariantTests
{
    private static void AssertBits(IEnumerable<double> expected, IEnumerable<double> actual) =>
        Assert.Equal(expected.Select(BitConverter.DoubleToUInt64Bits), actual.Select(BitConverter.DoubleToUInt64Bits));

    [Fact]
    public void LoadsAndLoadStationsLeaveTheSpringMatrixAndRoundingCountsBitIdentical()
    {
        foreach ((RigidRestraint[] rigid, ElasticRestraint[] elastic) in new[]
        {
            (new RigidRestraint[] { new(0, U | W | Theta) }, new ElasticRestraint[] { new(1, 2e4, 0, false) }),
            ([new(0, U | W), new(2, W)], [new(1, 9e8, 0, false)]),
            ([], [new(0, 5e3, 0, false), new(1, 7e5, 0, false), new(Math.BitIncrement(1), 1e3, 0, false)])
        })
        {
            BendingSystem baseline = SolverKernel.BuildSystem(Model(restraints: rigid, elastic: elastic));
            MechanicalInput[] inputs =
            [
                Model(restraints: rigid, elastic: elastic, forces: [new(0.75, -3)]),
                Model(restraints: rigid, elastic: elastic, moments: [new(1.25, 11)]),
                Model(restraints: rigid, elastic: elastic, lines: [new(0.25, 1.75, -7)]),
                Model(restraints: rigid, elastic: elastic, forces: [new(1, 13)], moments: [new(1, -17)]),
                Model(restraints: rigid, elastic: elastic, forces: [new(Math.BitIncrement(1), -0d)],
                    moments: [new(0.3, 0)], lines: [new(0.1, 1.9, -0d)])
            ];
            foreach (MechanicalInput input in inputs)
            {
                BendingSystem loaded = SolverKernel.BuildSystem(input);
                Assert.Equal(baseline.FreeComponents, loaded.FreeComponents);
                AssertBits(baseline.A, loaded.A);
                Assert.Equal(baseline.K, loaded.K);
            }
        }
    }

    [Fact]
    public void ZeroLoadsOnASpringBeamGiveTheExactNullPathWithZeroReactions()
    {
        BeamSolution solution = Solve(Beam([Spring(0, 1e5), Spring(L, 3e5)], [Point(1, 0)]));
        foreach (BeamNodeResult node in solution.Nodes)
        {
            Assert.Equal(0, node.TransverseDisplacement.Meters);
            Assert.Equal(0, node.RotationRadians);
        }
        Assert.Equal(0d, At(solution, 0).ReactionY!.Value.Newtons);
        Assert.Equal(0d, At(solution, L).ReactionY!.Value.Newtons);
        Assert.Equal(0d, At(solution, 0).ReactionX!.Value.Newtons);
    }

    [Fact]
    public void SpringSupportsAreAxiallyRigidWithExactZeroAxialResults()
    {
        BeamSolution solution = Solve(Beam([Spring(0, 1e5), Spring(L, 3e5)], [Point(1, -2000), Couple(0.5, 300)]));
        Assert.All(solution.Nodes, node => Assert.Equal(0, node.AxialDisplacement.Meters));
        Assert.True(double.IsPositiveInfinity(1 / At(solution, 0).ReactionX!.Value.Newtons));
        Assert.True(double.IsPositiveInfinity(1 / At(solution, L).ReactionX!.Value.Newtons));
    }

    [Theory]
    [InlineData(1e4, 2e4)]
    [InlineData(5e6, 5e6)]
    [InlineData(3e9, 1e3)]
    public void MirroringTheBeamMirrorsReactionsAndDeflectionsOfTheSpringPair(double kLeft, double kRight)
    {
        BeamSolution left = Solve(Beam([Spring(0, kLeft), Spring(L, kRight), SupportAt(0.8, SupportType.Pinned)],
            [Point(0.3, -1000), Point(1.4, 400), Uniform(1.1, 1.9, -250)]));
        BeamSolution right = Solve(Beam([Spring(L, kLeft), Spring(0, kRight), SupportAt(L - 0.8, SupportType.Pinned)],
            [Point(L - 0.3, -1000), Point(L - 1.4, 400), Uniform(L - 1.9, L - 1.1, -250)]));
        foreach (double x in new[] { 0, L / 4, 0.8, 1.25, L })
        {
            BeamSectionResult a = left.EvaluateAt(M(x), EvaluationSide.Right);
            BeamSectionResult b = right.EvaluateAt(M(L - x), EvaluationSide.Left);
            Close(a.TransverseDisplacement.Meters, b.TransverseDisplacement.Meters, 1e-13);
            Close(a.RotationRadians, -b.RotationRadians, 1e-13);
            Close(a.BendingMoment.NewtonMeters, b.BendingMoment.NewtonMeters, 1e-7);
        }
        Close(At(left, 0).ReactionY!.Value.Newtons, At(right, L).ReactionY!.Value.Newtons, 1e-7);
        Close(At(left, L).ReactionY!.Value.Newtons, At(right, 0).ReactionY!.Value.Newtons, 1e-7);
        Close(At(left, 0.8).ReactionY!.Value.Newtons, At(right, L - 0.8).ReactionY!.Value.Newtons, 1e-7);
    }

    [Fact]
    public void ResultsAreLinearInTheLoadsOfASpringBeam()
    {
        Support[] supports = [Spring(0, 3e5), Spring(L / 2, 4e4), SupportAt(L, SupportType.Roller)];
        BeamLoad[] first = [Point(0.5, -700)];
        BeamLoad[] second = [Uniform(0.6, 1.8, -330), Couple(1.2, 90)];
        BeamSolution a = Solve(Beam(supports, first));
        BeamSolution b = Solve(Beam(supports, second));
        BeamSolution both = Solve(Beam(supports, [.. first, .. second]));
        foreach (BeamNodeResult node in both.Nodes)
        {
            foreach (EvaluationSide side in new[] { EvaluationSide.Left, EvaluationSide.Right })
            {
                BeamSectionResult sum = both.EvaluateAt(node.Position, side);
                BeamSectionResult x = a.EvaluateAt(node.Position, side), y = b.EvaluateAt(node.Position, side);
                Close(x.TransverseDisplacement.Meters + y.TransverseDisplacement.Meters, sum.TransverseDisplacement.Meters, 1e-13);
                Close(x.RotationRadians + y.RotationRadians, sum.RotationRadians, 1e-13);
                Close(x.ShearForce.Newtons + y.ShearForce.Newtons, sum.ShearForce.Newtons, 1e-7);
                Close(x.BendingMoment.NewtonMeters + y.BendingMoment.NewtonMeters, sum.BendingMoment.NewtonMeters, 1e-7);
            }
        }
        foreach (double x in new[] { 0, L / 2, L })
            Close(At(a, x).ReactionY!.Value.Newtons + At(b, x).ReactionY!.Value.Newtons,
                At(both, x).ReactionY!.Value.Newtons, 1e-6);
    }

    [Fact]
    public void SpringForceEqualsMinusStiffnessTimesDisplacementAndBalancesTheLoad()
    {
        const double k1 = 2e5, k2 = 7e4;
        BeamSolution solution = Solve(Beam([Spring(0.2, k1), Spring(1.5, k2), SupportAt(L, SupportType.Roller)],
            [Point(0.9, -1200), Uniform(0, L, -150), Couple(1.1, 250)]));
        Close(-k1 * At(solution, 0.2).TransverseDisplacement.Meters, At(solution, 0.2).ReactionY!.Value.Newtons, 1e-6);
        Close(-k2 * At(solution, 1.5).TransverseDisplacement.Meters, At(solution, 1.5).ReactionY!.Value.Newtons, 1e-6);
        double reactions = solution.Nodes.Sum(n => n.ReactionY?.Newtons ?? 0);
        Close(1200 + 150 * L, reactions, 1e-6);
    }

    [Fact]
    public void AVeryStiffSpringIsASolvedModelWhoseDistanceToTheRigidSupportIsTheClosedFormValue()
    {
        // Not a penalty: the stiff spring is a solved model; its relative distance to the rigid result is 1 / (1 + k f).
        BeamModel rigid = Beam([SupportAt(0, SupportType.Pinned), SupportAt(L, SupportType.Roller),
            SupportAt(L / 2, SupportType.Roller)], [Point(0.5, -1000)]);
        BeamModel spring = Beam([SupportAt(0, SupportType.Pinned), SupportAt(L, SupportType.Roller),
            Spring(L / 2, 1e14)], [Point(0.5, -1000)]);
        BeamSolution a = Solve(rigid), b = Solve(spring);
        double rigidForce = At(a, L / 2).ReactionY!.Value.Newtons, springForce = At(b, L / 2).ReactionY!.Value.Newtons;
        double f = Math.Pow(L, 3) / (48 * EI);
        Close(rigidForce / (1 + 1e14 * f), rigidForce - springForce, 1e-6);
    }
}
