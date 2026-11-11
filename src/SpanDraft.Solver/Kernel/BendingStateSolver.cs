using System.Collections.Immutable;
using static SpanDraft.Solver.Kernel.Binary64;

namespace SpanDraft.Solver.Kernel;

internal static class BendingStateSolver
{
    internal static VerifiedBendingState Solve(BendingSystem system)
    {
        int n = system.Dimension, full = system.ReducedColumns.Length;
        KernelFailureException.Require(n >= 0 && (long)n * n == system.A.Length &&
            system.K.Length == system.A.Length && system.B.Length == n && system.BB.Length == n &&
            system.FreeComponents.Length == n && full == (long)system.Stations.Length * 4);
        for (int i = 0; i < n; i++)
        {
            FreeBendingComponent c = system.FreeComponents[i];
            KernelFailureException.Require(c.StationIndex >= 0 && c.StationIndex < system.Stations.Length &&
                (uint)c.Component < 4 && system.ReducedColumns[4 * c.StationIndex + (int)c.Component] == i);
        }
        if (system.IsUnloaded)
        {
            ImmutableArray<double> reducedZero = new double[n].ToImmutableArray();
            ImmutableArray<double> fullZero = new double[full].ToImmutableArray();
            return new(system, true, true, [], [], [], [], [], [], reducedZero, fullZero, 0,
                [], [], [], reducedZero, reducedZero, fullZero, fullZero, [], null, 0, 0, 0);
        }

        EquilibratedSystem scaled = EquilibratedSystem.Create(n, system.A, system.K, system.B, system.BB);
        var lu = new DenseLu(n, scaled.S);
        var (y, iterations) = Primary(scaled, lu);
        double[] x = UnscaleState(system, scaled.CS, y);
        var (residual, rounding, d) = ResidualAndD(scaled, y);
        double[] weights = Weights(system, x, scaled.CS);
        BendingVerification.Result verification = BendingVerification.Verify(scaled, lu, d, weights);
        var (bounds, scaleError) = UnscaleBounds(system, scaled.CS, y, x, verification.Proof.Bound);

        return new(system, false, true, scaled.RS.ToImmutableArray(), scaled.CS.ToImmutableArray(),
            scaled.S.ToImmutableArray(), scaled.DS.ToImmutableArray(), scaled.SB.ToImmutableArray(),
            scaled.DSB.ToImmutableArray(), y.ToImmutableArray(), x.ToImmutableArray(), iterations,
            residual.ToImmutableArray(), rounding.ToImmutableArray(), d.ToImmutableArray(),
            verification.RawBound.ToImmutableArray(), verification.Proof.Bound.ToImmutableArray(),
            bounds.ToImmutableArray(), scaleError.ToImmutableArray(), verification.Proof.Mus,
            verification.Proof.Mu, verification.AdjointSolves, verification.AdjointIterations,
            verification.MaxAdjointIterations);
    }

    internal static (double[] Y, int Iterations) Primary(EquilibratedSystem scaled, DenseLu lu)
    {
        int n = scaled.Dimension;
        double[] y = lu.Solve(scaled.SB);
        FlushStored(y);
        double previous = double.PositiveInfinity;
        int iterations = 0;
        for (int step = 0; step < 5; step++)
        {
            double[] dy = lu.Solve(scaled.CandidateResidual(y, scaled.SB, transpose: false));
            for (int i = 0; i < n; i++)
                y[i] += dy[i];
            FlushStored(y);
            iterations++;
            double nd = 0, ny = 0;
            for (int i = 0; i < n; i++)
            {
                nd = Math.Max(nd, Abs(dy[i]));
                ny = Math.Max(ny, Abs(y[i]));
            }
            if (StopPrimary(nd, ny, previous))
                break;
            previous = nd;
        }
        foreach (double value in y)
            KernelFailureException.RequireFinite(value);
        return (y, iterations);
    }

    internal static bool StopPrimary(double nd, double ny, double previous) =>
        IsNaN(nd) || nd <= 2 * U * ny || nd > 0.5 * previous;

    internal static void FlushStored(double[] values)
    {
        for (int i = 0; i < values.Length; i++)
            if (MagnitudeBits(values[i]) < MagnitudeBits(Lambda))
                values[i] = 0;
    }

    internal static (double[] Residual, double[] Rounding, double[] D) ResidualAndD(EquilibratedSystem scaled, double[] y)
    {
        int n = scaled.Dimension;
        var residual = new double[n];
        var rounding = new double[n];
        var d = new double[n];
        for (int i = 0; i < n; i++)
        {
            int row = i;
            (residual[i], rounding[i]) = CertifiedResidual.Evaluate(scaled.SB[i],
                scaled.Rows[i].Select(j => (scaled.S[row * n + j], y[j])));
            double data = Directed.Add(BendingVerification.UpperDot(scaled.Rows[i].Select(j =>
                (scaled.DS[row * n + j], y[j]))), scaled.DSB[i], upper: true);
            d[i] = Directed.Add(Directed.Add(Abs(residual[i]), rounding[i], upper: true), data, upper: true);
            KernelFailureException.RequireFinite(d[i]);
        }
        return (residual, rounding, d);
    }

    internal static double[] UnscaleState(BendingSystem system, int[] cs, double[] y)
    {
        var x = new double[system.ReducedColumns.Length];
        for (int i = 0; i < system.Dimension; i++)
        {
            FreeBendingComponent c = system.FreeComponents[i];
            double value = ScaleByPowerOfTwo(y[i], cs[i]);
            if (!IsFinite(value))
                throw new NumericalRangeException("The unscaled normalized state is outside its range.");
            x[4 * c.StationIndex + (int)c.Component] = value;
        }
        return x;
    }

    internal static double[] Weights(BendingSystem system, double[] x, int[] cs)
    {
        var maxima = new double[4];
        // The final station has no right side; weights() uses only the stored right-side states.
        for (int station = 0; station < system.Stations.Length - 1; station++)
            for (int q = 0; q < 4; q++)
            {
                double value = Abs(x[4 * station + q]);
                if (MagnitudeBits(value) > MagnitudeBits(maxima[q]))
                    maxima[q] = value;
            }
        double f = Math.Max(system.Excitation.Lower, Lambda);
        double sm = Math.Max(maxima[2], f);
        double sv = Math.Max(sm, f);
        double st = Math.Max(maxima[1], sm / system.NormalizedEI.Value);
        double sw = Math.Max(Math.Max(maxima[0], sm / system.NormalizedEI.Value), st);
        double[] sq = [sw, st, sm, sv];
        var d = new double[system.Dimension];
        try
        {
            for (int i = 0; i < d.Length; i++)
                d[i] = ScaleByPowerOfTwo(sq[(int)system.FreeComponents[i].Component], checked(-cs[i]));
        }
        catch (OverflowException)
        {
            throw new NumericalRangeException("The weight exponents exceed the integer range.");
        }
        return d;
    }

    internal static (double[] Bound, double[] ScaleError) UnscaleBounds(BendingSystem system, int[] cs,
        double[] y, double[] x, double[] stateBound)
    {
        var bound = new double[x.Length];
        var scaleError = new double[x.Length];
        for (int i = 0; i < system.Dimension; i++)
        {
            FreeBendingComponent c = system.FreeComponents[i];
            int k = 4 * c.StationIndex + (int)c.Component;
            double b = Directed.DirectedScale(stateBound[i], cs[i], upper: true);
            double loss = !IsZero(y[i]) && MagnitudeBits(x[k]) <= MagnitudeBits(Lambda) ? Lambda : 0;
            scaleError[k] = loss;
            bound[k] = IsZero(loss) ? b : Directed.Add(b, loss, upper: true);
            if (!IsFinite(bound[k]))
                throw new NumericalRangeException("The unscaled normalized state bound is outside its range.");
        }
        FlushStored(x);
        return (bound, scaleError);
    }
}
