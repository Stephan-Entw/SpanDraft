using SpanDraft.Solver.Kernel;
using Xunit;
using static SpanDraft.Solver.Kernel.RestrainedDirections;
using static SpanDraft.Tests.SolverBendingTestSupport;
using static SpanDraft.Tests.SolverExactNumber;
using static SpanDraft.Tests.SolverExactLinearAlgebra;
using static SpanDraft.Tests.SolverOutputOracle;

namespace SpanDraft.Tests;

/// <summary>
/// A spring reaction has two exact evaluations: the jump V+ - V- - P and the spring law -k w+. The kernel encloses
/// both, checks their identity and delivers the one with the smaller verified bound.
/// </summary>
public sealed class SolverSpringReactionTests
{
    public static TheoryData<string> Cases => new()
    {
        "single-mid", "end-pair", "soft-pair", "stiff-mid", "loaded-on-spring", "next-to-rigid", "spring-trio",
        "near-rigid-pair", "stiff-between-near-rigid"
    };

    private static MechanicalInput Case(string name) => name switch
    {
        "single-mid" => Model(ei: 1000, restraints: [new(0, U | W), new(2, W)], elastic: [new(1, 5e3, 0, false)],
            forces: [new(0.7, -30)]),
        "end-pair" => Model(ei: 1000, restraints: [], elastic: [new(0, 4e3, 0, false), new(2, 9e3, 0, false)],
            forces: [new(1, -3)], lines: [new(0, 2, -5)]),
        "soft-pair" => Model(ei: 1000, restraints: [], elastic: [new(0, 1e-3, 0, false), new(2, 4e-3, 0, false)],
            forces: [new(1, -3)]),
        "stiff-mid" => Model(ei: 1000, restraints: [new(0, U | W), new(2, W)], elastic: [new(1, 5e13, 0, false)],
            forces: [new(0.5, -3)], lines: [new(0.25, 1.75, -2)]),
        "loaded-on-spring" => Model(ei: 1000, restraints: [new(0, U | W | Theta)], elastic: [new(1.5, 7e2, 0, false)],
            forces: [new(1.5, -3), new(1.5, 1)], moments: [new(1.5, 4)]),
        "next-to-rigid" => Model(ei: 1000, restraints: [new(1, U | W)],
            elastic: [new(Math.BitIncrement(1), 6e4, 0, false)], forces: [new(2, -3)]),
        "spring-trio" => Model(ei: 1000, restraints: [], elastic: [new(0, 5e2, 0, false), new(1, 5e5, 0, false),
            new(2, 5e8, 0, false)], lines: [new(0, 2, -3)], forces: [new(0.3, 2)]),
        "near-rigid-pair" => Model(ei: 1000, restraints: [new(1, U | W), new(1 + Math.ScaleB(1, -20), W)],
            elastic: [new(0.2, 3e3, 0, false)], forces: [new(1.9, -3)]),
        "stiff-between-near-rigid" => Model(ei: 1000, restraints: [new(1, U | W), new(1 + Math.ScaleB(1, -20), W)],
            elastic: [new(1.5, 1e9, 0, false)], forces: [new(0.2, -3)], lines: [new(0, 2, 1)]),
        _ => throw new ArgumentOutOfRangeException(nameof(name))
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void JumpAndSpringLawEncloseTheExactReactionAndTheSmallerBoundIsDelivered(string name)
    {
        MechanicalInput input = Case(name);
        SolverOutputOracle oracle = FromCompatibility(input);
        BendingSystem system = SolverKernel.BuildSystem(input);
        VerifiedBendingState state = BendingStateSolver.Solve(system, out BendingSolveContext? context);
        var delivered = BendingReactions.Calculate(state, context);
        int springs = 0;
        for (int i = 0; i < system.Stations.Length; i++)
        {
            if (!system.Stations[i].ElasticW)
                continue;
            springs++;
            SolverExactNumber exact = Assert.Single(oracle.Reactions, r => r.X == system.Stations[i].Position && r.Direction == W).Value;
            int exponent = system.StateExponents[3];
            VerifiedOutput Si((double Value, double Bound) r) => BendingFields.ToSi(r.Value, r.Bound, exponent);
            var jump = BendingReactions.Jump(state, context!, i, W);
            var law = BendingReactions.TranslationalSpringLaw(state, i);
            Bounded((From(Si(jump).Value) - exact).Abs(), Si(jump).VerifiedOutputBound);
            Bounded((From(Si(law).Value) - exact).Abs(), Si(law).VerifiedOutputBound);

            NormalizedReaction reaction = Assert.Single(delivered, r => r.Position == system.Stations[i].Position && r.Direction == W);
            Assert.Equal(Math.Min(jump.Bound, law.Bound), reaction.Bound);
            // The jump wins a tie, so the law is only delivered when its bound is strictly smaller.
            Assert.Equal(law.Bound < jump.Bound ? law.Value : jump.Value, reaction.Value);
            Bounded((From(Si((reaction.Value, reaction.Bound)).Value) - exact).Abs(),
                Si((reaction.Value, reaction.Bound)).VerifiedOutputBound);
        }
        Assert.True(springs > 0);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ReactionIdentityHoldsAndEverySpringReactionEntersTheGate(string name)
    {
        MechanicalInput input = Case(name);
        BendingResult result = SolverKernel.Solve(input);
        int expected = result.Reactions.Count(r => r.Direction != U);
        Assert.Equal(expected, result.Quality.ReactionChecks);
        Assert.True(result.Quality.GatePassed);
    }

    [Fact]
    public void TheShearJumpEqualsTheSpringReactionAtTheSpringStation()
    {
        MechanicalInput input = Case("single-mid");
        BendingResult result = SolverKernel.Solve(input);
        BendingStation station = Assert.Single(result.Stations, s => s.Position == 1);
        Assert.True(station.Jumps.HasFlag(BendingJump.ReactionY));
        double jump = station.Right!.Bending[3].Value - station.Left!.Bending[3].Value;
        double reaction = Assert.Single(result.Reactions, r => r.Position == 1 && r.Direction == W).Output.Value;
        double bound = Assert.Single(result.Reactions, r => r.Position == 1 && r.Direction == W).Output.VerifiedOutputBound;
        Assert.True(Math.Abs(jump - reaction) <= bound + station.Right.Bending[3].VerifiedOutputBound +
            station.Left.Bending[3].VerifiedOutputBound);
    }

    [Fact]
    public void ChoosePrefersTheSmallerBoundAndTheJumpOnEquality()
    {
        Assert.Equal((1.0, 0.5), BendingReactions.Choose((1.0, 0.5), (1.1, 0.5)));
        Assert.Equal((1.1, 0.25), BendingReactions.Choose((1.0, 0.5), (1.1, 0.25)));
        Assert.Equal((1.0, 0.25), BendingReactions.Choose((1.0, 0.25), (1.1, 0.5)));
    }

    [Theory]
    [InlineData(1.0, 0.1, 1.5, 0.1)]
    [InlineData(-3.0, 0.0, 3.0, 1.0)]
    [InlineData(1e300, 1.0, -1e300, 1.0)]
    public void ChooseRejectsReactionsWhoseDifferenceExceedsTheSumOfTheirBounds(double j, double jb, double l, double lb)
    {
        KernelFailureException exception = Assert.Throws<KernelFailureException>(() =>
            BendingReactions.Choose((j, jb), (l, lb)));
        Assert.Equal(KernelFailureCause.InternalInconsistency, exception.Cause);
    }

    [Fact]
    public void ChooseAcceptsADifferenceExactlyAtTheSumOfTheBounds()
    {
        Assert.Equal((1.0, 0.5), BendingReactions.Choose((1.0, 0.5), (2.0, 0.5)));
    }
}
