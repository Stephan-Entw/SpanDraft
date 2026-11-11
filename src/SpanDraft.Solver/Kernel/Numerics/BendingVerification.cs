using System.Collections.Immutable;
using static SpanDraft.Solver.Kernel.Binary64;

namespace SpanDraft.Solver.Kernel;

internal static class BendingVerification
{
    private static readonly double Delta = ScaleByPowerOfTwo(1, -200);

    internal sealed record AdjointState(double[] Z, double[] ResidualBound, int Iterations);
    internal sealed record Contraction(double[] Bound, double Mu, ImmutableArray<double> Mus, double[] V, double[] MV);
    internal sealed record Result(double[] RawBound, Contraction Proof, int AdjointSolves,
        int AdjointIterations, int MaxAdjointIterations);

    internal static double UpperDot(IEnumerable<(double A, double B)> products)
    {
        double sum = 0;
        foreach (var (a, b) in products)
            sum = Directed.Add(sum, Directed.Multiply(Abs(a), Abs(b), upper: true), upper: true);
        return sum;
    }

    internal static double UpperDot(double[] a, double[] b) => UpperDot(a.Zip(b, (x, y) => (x, y)));

    internal static AdjointState Adjoint(EquilibratedSystem scaled, DenseLu lu, double[] d, int component)
    {
        int n = scaled.Dimension;
        var rhs = new double[n];
        rhs[component] = 1;
        double[] z = lu.SolveTranspose(rhs);
        BendingStateSolver.FlushStored(z);
        double betaPrevious = UpperDot(z, d);
        int iterations = 0;
        for (int step = 0; step < 5; step++)
        {
            double[] dz = lu.SolveTranspose(scaled.CandidateResidual(z, rhs, transpose: true));
            for (int i = 0; i < n; i++)
                z[i] += dz[i];
            BendingStateSolver.FlushStored(z);
            iterations++;
            double beta = UpperDot(z, d);
            if (Abs(beta - betaPrevious) <= beta / 16)
                break;
            betaPrevious = beta;
        }
        foreach (double value in z)
            KernelFailureException.RequireFinite(value);
        var residualBound = new double[n];
        for (int j = 0; j < n; j++)
        {
            int column = j;
            var (r, dr) = CertifiedResidual.Evaluate(rhs[j],
                scaled.Columns[j].Select(i => (scaled.S[i * n + column], z[i])));
            residualBound[j] = Directed.Add(Abs(r), dr, upper: true);
            KernelFailureException.RequireFinite(residualBound[j]);
        }
        return new(z, residualBound, iterations);
    }

    internal static Result Verify(EquilibratedSystem scaled, DenseLu lu, double[] d, double[] weights)
    {
        int n = scaled.Dimension;
        var z = new double[n][];
        var sb = new double[n][];
        var raw = new double[n];
        int steps = 0, maxSteps = 0;
        for (int i = 0; i < n; i++)
        {
            AdjointState adjoint = Adjoint(scaled, lu, d, i);
            z[i] = adjoint.Z;
            sb[i] = adjoint.ResidualBound;
            raw[i] = UpperDot(z[i], d);
            steps += adjoint.Iterations;
            maxSteps = Math.Max(maxSteps, adjoint.Iterations);
        }
        return new(raw, VerifyContraction(scaled, z, sb, raw, weights), n, steps, maxSteps);
    }

    internal static double[] GS(EquilibratedSystem scaled, double[] v)
    {
        int n = scaled.Dimension;
        var gsv = new double[n];
        for (int k = 0; k < n; k++)
        {
            int row = k;
            gsv[k] = UpperDot(scaled.Rows[k].Select(j => (scaled.DS[row * n + j], v[j])));
        }
        return gsv;
    }

    internal static double[] MV(EquilibratedSystem scaled, double[][] z, double[][] sb, double[] v)
    {
        double[] gsv = GS(scaled, v);
        return Enumerable.Range(0, scaled.Dimension).Select(i =>
            Directed.Add(UpperDot(sb[i], v), UpperDot(z[i], gsv), upper: true)).ToArray();
    }

    internal static Contraction VerifyContraction(EquilibratedSystem scaled, double[][] z, double[][] sb,
        double[] raw, double[] weights)
    {
        int n = scaled.Dimension;
        KernelFailureException.Require(raw.Length == n && weights.Length == n && z.Length == n && sb.Length == n &&
            z.All(row => row.Length == n) && sb.All(row => row.Length == n));
        double[] v = Enumerable.Range(0, n).Select(j =>
            Directed.Add(raw[j], Directed.Multiply(Delta, weights[j], upper: true), upper: true)).ToArray();
        var mus = ImmutableArray.CreateBuilder<double>();
        double[]? selectedV = null, selectedMV = null;
        double selectedMu = 0;
        for (int iteration = 0; iteration < 4; iteration++)
        {
            double[] mv = MV(scaled, z, sb, v);
            if (!v.All(w => IsPositive(w) && IsNormal(w)) || !mv.All(IsFinite))
                break;
            double mu = 0;
            for (int i = 0; i < n; i++)
                mu = Math.Max(mu, Directed.Divide(mv[i], v[i], upper: true));
            mus.Add(mu);
            if (mu < 1)
                (selectedMu, selectedV, selectedMV) = (mu, v, mv);
            v = Enumerable.Range(0, n).Select(j => Directed.Add(
                Directed.Add(raw[j], mv[j], upper: true),
                Directed.Multiply(Delta, weights[j], upper: true), upper: true)).ToArray();
        }
        if (selectedV is null || selectedMV is null)
            throw new KernelFailureException(KernelFailureCause.NotVerified,
                "No valid weight candidate proves a contraction.", mus.ToImmutable());
        double denominator = Directed.Subtract(1, selectedMu, upper: false);
        var bound = new double[n];
        for (int i = 0; i < n; i++)
        {
            bound[i] = Directed.Add(raw[i], Directed.Divide(selectedMV[i], denominator, upper: true), upper: true);
            if (!IsFinite(bound[i]))
                throw new NumericalRangeException("The verified state bound is outside its range.");
        }
        return new(bound, selectedMu, mus.ToImmutable(), selectedV, selectedMV);
    }
}
