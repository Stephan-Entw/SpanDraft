using System.Collections.Immutable;
using SpanDraft.Solver.Kernel;
using Xunit;
using static SpanDraft.Tests.SolverBendingTestSupport;
using static SpanDraft.Tests.SolverExactNumber;
using static SpanDraft.Tests.SolverExactLinearAlgebra;

namespace SpanDraft.Tests;

public sealed class SolverBendingFunctionalTests
{
    [Fact]
    public void FullStateFunctionalEnclosesExactRationalSystemAndReusesPrimaryLu()
    {
        BendingSystem system = SolverKernel.BuildSystem(Model(forces: [new(2, -7)], lines: [new(0.25, 1.75, 3)]));
        VerifiedBendingState state = BendingStateSolver.Solve(system, out BendingSolveContext? context);
        Assert.NotNull(context);
        var (a, b) = SolverBendingSystemTests.CheckExactMechanics(Model(forces: [new(2, -7)], lines: [new(0.25, 1.75, 3)]), system);
        SolverExactNumber[] exact = Solve(a, b);
        (int Index, double Coefficient)[] coefficients = [(2, 3), (3, -2), (4, 5), (5, -7), (0, 1e100)];
        SolverExactNumber error = Integer(0);
        foreach (var (k, c) in coefficients)
        {
            int i = system.ReducedColumns[k];
            error += From(c) * (From(state.FullState[k]) - (i < 0 ? Integer(0) : exact[i]));
        }
        DenseLu original = context.Lu;
        int before = original.TransposeSolveCalls, primary = original.SolveCalls;
        double bound = context.Functional(state, coefficients);
        Bounded(error, bound);
        Assert.Same(original, context.Lu);
        Assert.Equal(primary, original.SolveCalls);
        Assert.Equal(state.PrimaryIterations + 1, primary);
        Assert.Equal(1 + context.FunctionalIterations, original.TransposeSolveCalls - before);
        Assert.Equal(1, context.FunctionalSolves);
        Assert.Equal(4, state.TestedMus.Length);
        Assert.Equal(1 + state.PrimaryIterations, original.SolveCalls);

        int solves = original.TransposeSolveCalls;
        Assert.Equal(0, context.Functional(state, [(0, 7), (1, 11)]));
        Assert.Equal(0, context.Functional(state, [(2, 0)]));
        Assert.Equal(solves, original.TransposeSolveCalls);
    }

    [Fact]
    public void StateScaleErrorAndCoefficientScaleLossAreSeparateDirectedBudgets()
    {
        var (state, context) = Scalar(cs: -1023, bound: 3);
        state = state with { StateScaleError = new double[] { 0, 0, Binary64.Lambda, 0, 0, 0, 0, 0 }.ToImmutableArray() };
        double b = context.Functional(state, [(2, 1)]);
        Assert.True(b >= 4 * Binary64.Lambda);
        // The coefficient remains subnormal; its possible loss is against the y-state bound.
        Assert.True(context.FunctionalSolves > 0);
        foreach (int exponent in new[] { -1022, -1075 })
        {
            (state, context) = Scalar(cs: exponent, bound: 3);
            b = context.Functional(state, [(2, 1)]);
            Assert.True(b >= 3 * Binary64.Lambda);
            Assert.Equal(exponent == -1075 ? 0 : 1, context.FunctionalSolves);
        }
        (state, context) = Scalar();
        state = state with { StateScaleError = new double[] { Binary64.Lambda, 0, 0, 0, 0, 0, 0, 0 }.ToImmutableArray() };
        Assert.True(context.Functional(state, [(0, 2)]) >= 2 * Binary64.Lambda);
        Assert.Equal(0, context.FunctionalSolves);
    }

    [Fact]
    public void FunctionalIncludesAdjointDefectAgainstVerifiedBoundWithoutNewContraction()
    {
        var (state, _) = Scalar(bound: 2);
        // Fixed approximate inverse: solving 2 rather than stored S=3 leaves a visible defect
        // even after all five mandatory/permitted refinement steps. No LU mutation option.
        var scaled = new EquilibratedSystem(1, [0], [0], [3], [0], [1], [0]);
        var context = new BendingSolveContext(scaled, new DenseLu(1, [2]));
        double b = context.Functional(state, [(2, 1)]);
        Assert.True(b >= 23d / 64);
        Assert.Equal(5, context.FunctionalIterations);
        Assert.Equal(6, context.Lu.TransposeSolveCalls);
        Assert.Equal(0, context.Lu.SolveCalls);
    }

    [Fact]
    public void FunctionalIncludesMatrixDataDefectAgainstVerifiedBound()
    {
        var (state, context) = Scalar(ds: 0.25, bound: 2);
        double b = context.Functional(state, [(2, 1)]);
        Assert.True(b >= 1.5); // |z|d = 1, |z|dS E = 1/2.
        Bounded(Integer(4) / Integer(3), b); // Exact operator 3/4, exact RHS 1, stored state 0.
    }

    [Fact]
    public void AdditionalFunctionalDoesNotAttemptToReproveTheStateContraction()
    {
        var (state, context) = Scalar(ds: 2, bound: 2);
        // This context cannot establish a new contraction; the supplied state bound is
        // already verified. A functional only budgets the adjoint defect against it.
        Assert.True(context.Functional(state, [(2, 1)]) >= 5);
        Assert.Equal(1, context.FunctionalSolves);
    }

    [Fact]
    public void FunctionalScalingOverflowAndUnexpectedNonfiniteHaveDifferentCauses()
    {
        var (state, context) = Scalar(cs: 1023);
        Assert.Equal(KernelFailureCause.NumericalRange,
            Assert.Throws<NumericalRangeException>(() => context.Functional(state, [(2, 4)])).Cause);
        Assert.Equal(KernelFailureCause.NonFinite,
            Assert.Throws<KernelFailureException>(() => context.Functional(state, [(2, double.NaN)])).Cause);
        Assert.Equal(KernelFailureCause.NotVerified,
            Assert.Throws<KernelFailureException>(() => context.Functional(state with { IsVerified = false }, [(2, 1)])).Cause);
    }

    [Fact]
    public void EveryReactionAdjointUsesSameLuAndIsSharperThanUncorrelatedFieldBounds()
    {
        // Antisymmetry makes the middle reaction exactly zero while both adjacent shears
        // grow as 1/g. Their state errors are correlated, not independent.
        double g = Math.ScaleB(1, -30);
        var input = Model(restraints: [new(1 - g, RestrainedDirections.U | RestrainedDirections.W),
            new(1, RestrainedDirections.W), new(1 + g, RestrainedDirections.W)], forces: [new(0, -1), new(2, 1)]);
        BendingSystem system = SolverKernel.BuildSystem(input);
        VerifiedBendingState state = BendingStateSolver.Solve(system, out BendingSolveContext? context);
        Assert.NotNull(context);
        DenseLu original = context.Lu;
        int before = original.TransposeSolveCalls;
        var reactions = BendingReactions.Calculate(state, context);
        Assert.Same(original, context.Lu);
        Assert.Equal(context.FunctionalSolves + context.FunctionalIterations, original.TransposeSolveCalls - before);
        Assert.Equal(3, context.FunctionalSolves);
        BendingFields fields = BendingFields.From(state);
        double x = 1;
        NormalizedBendingField l = fields.EvaluateNormalized(x, FieldSide.Left), r = fields.EvaluateNormalized(x, FieldSide.Right);
        NormalizedReaction reaction = Assert.Single(reactions, r => r.Position == x && r.Direction == RestrainedDirections.W);
        Assert.True(reaction.Bound < l.Bounds[3] + r.Bounds[3],
            $"Functional {reaction.Bound:R}, separate {l.Bounds[3] + r.Bounds[3]:R}");
        Bounded(From(reaction.Value), reaction.Bound);
    }

    private static (VerifiedBendingState State, BendingSolveContext Context) Scalar(int cs = 0, double ds = 0,
        double bound = 2)
    {
        BendingSystem system = SolverKernel.BuildSystem(Model(length: 1, forces: [new(1, 1)]));
        VerifiedBendingState state = BendingStateSolver.Solve(system);
        system = system with { Dimension = 1, FreeComponents = [new(0, BendingComponent.M)],
            ReducedColumns = [-1, -1, 0, -1, -1, -1, -1, -1], A = [1], K = [0], B = [1], BB = [0] };
        state = state with { System = system, CS = [cs], D = [1], VerifiedStateBound = [bound], FullState = [0, 0, 0, 0, 0, 0, 0, 0],
            StateScaleError = [0, 0, 0, 0, 0, 0, 0, 0], FullStateBound = [0, 0, 0, 0, 0, 0, 0, 0] };
        var scaled = new EquilibratedSystem(1, [0], [cs], [1], [ds], [1], [0]);
        return (state, new(scaled, new DenseLu(1, [1])));
    }
}
