using System.Collections.Immutable;
using static SpanDraft.Solver.Kernel.Binary64;

namespace SpanDraft.Solver.Kernel;

internal sealed class BendingSolveContext(EquilibratedSystem scaled, DenseLu lu)
{
    internal EquilibratedSystem Scaled { get; } = scaled;
    internal DenseLu Lu { get; } = lu;
    internal int FunctionalSolves { get; private set; }
    internal int FunctionalIterations { get; private set; }

    internal double Functional(VerifiedBendingState state, IReadOnlyList<(int Index, double Coefficient)> coefficients)
    {
        if (!state.IsVerified)
            throw new KernelFailureException(KernelFailureCause.NotVerified, "The state has not been verified.");
        var rhs = new double[Scaled.Dimension];
        double scaling = 0;
        foreach (var (k, c) in coefficients)
        {
            KernelFailureException.RequireFinite(c);
            scaling = Directed.Add(scaling, Directed.Multiply(Abs(c), state.StateScaleError[k], true), true);
            int i = state.System.ReducedColumns[k];
            if (i < 0) continue;
            rhs[i] = ScaleByPowerOfTwo(c, Scaled.CS[i]);
            if (!IsZero(c) && MagnitudeBits(rhs[i]) <= MagnitudeBits(Lambda))
                scaling = Directed.Add(scaling, Directed.Multiply(Lambda, state.VerifiedStateBound[i], true), true);
        }
        if (!rhs.All(IsFinite))
            throw new NumericalRangeException("The functional scaling is outside its range.");
        if (rhs.All(IsZero)) return scaling;

        // The state contraction is already proved; only this adjoint's defect needs enclosing.
        BendingVerification.AdjointState adjoint = BendingVerification.Adjoint(Scaled, Lu, state.D.ToArray(), rhs);
        FunctionalSolves++;
        FunctionalIterations += adjoint.Iterations;
        double[] bound = state.VerifiedStateBound.ToArray();
        double result = BendingVerification.UpperDot(adjoint.Z, state.D.ToArray());
        result = Directed.Add(result, scaling, true);
        result = Directed.Add(result, BendingVerification.UpperDot(adjoint.ResidualBound, bound), true);
        result = Directed.Add(result, BendingVerification.UpperDot(adjoint.Z, BendingVerification.GS(Scaled, bound)), true);
        KernelFailureException.RequireFinite(result);
        return result;
    }
}

internal readonly record struct NormalizedReaction(double Position, RestrainedDirections Direction,
    double Value, double Bound)
{
    internal int Component => Direction == RestrainedDirections.W ? 3 : 2;
}
internal readonly record struct BendingReaction(double Position, RestrainedDirections Direction, VerifiedOutput Output);

internal static class BendingReactions
{
    internal static ImmutableArray<NormalizedReaction> Calculate(VerifiedBendingState state, BendingSolveContext? context)
    {
        BendingSystem system = state.System;
        var result = ImmutableArray.CreateBuilder<NormalizedReaction>();
        for (int i = 0; i < system.Stations.Length; i++)
            foreach (RestrainedDirections direction in new[] { RestrainedDirections.U, RestrainedDirections.W, RestrainedDirections.Theta })
            {
                if (!system.Stations[i].Directions.HasFlag(direction)) continue;
                if (state.IsUnloaded || direction == RestrainedDirections.U)
                {
                    result.Add(new(system.Stations[i].Position, direction, 0, 0));
                    continue;
                }
                KernelFailureException.Require(context is not null);
                int q = direction == RestrainedDirections.W ? 3 : 2;
                double sign = q == 3 ? 1 : -1;
                var coefficients = new List<(int Index, RoundCounted Coefficient)>();
                if (i < system.Stations.Length - 1)
                    coefficients.Add((4 * i + q, new(sign)));
                Bounded constant = default;
                if (i > 0)
                {
                    int previous = 4 * (i - 1);
                    coefficients.Add((previous + q, new(-sign)));
                    if (q == 2)
                    {
                        RoundCounted a = RoundCounted.Distance(system.Stations[i].Position,
                            system.Stations[i - 1].Position, system.EL);
                        coefficients.Add((previous + 3, sign == 1 ? -a : a));
                    }
                    Bounded load = system.Loads.Segments[i - 1].Integral(system.Stations[i].Position, 4 - q);
                    constant = new(-sign * load.Value, load.Error);
                }
                Bounded estimate = default;
                double coefficientError = 0;
                foreach (var (k, c) in coefficients)
                {
                    estimate += new Bounded(c.Value) * new Bounded(state.FullState[k]);
                    coefficientError = Directed.Add(coefficientError, Directed.Multiply(c.Bound(),
                        Directed.Add(Abs(state.FullState[k]), state.FullStateBound[k], true), true), true);
                }
                StationBendingLoads stationLoad = system.Loads.Stations[i];
                estimate = estimate + constant - (q == 3 ? stationLoad.Force : stationLoad.Moment);
                double bound = Directed.Add(Directed.Add(estimate.Error, context!.Functional(state,
                    coefficients.Select(c => (c.Index, c.Coefficient.Value)).ToArray()), true), coefficientError, true);
                KernelFailureException.RequireFinite(estimate.Value);
                KernelFailureException.RequireFinite(bound);
                result.Add(new(system.Stations[i].Position, direction, estimate.Value, bound));
            }
        return result.ToImmutable();
    }
}
