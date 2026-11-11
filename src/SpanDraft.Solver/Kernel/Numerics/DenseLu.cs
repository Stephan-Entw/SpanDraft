using static SpanDraft.Solver.Kernel.Binary64;

namespace SpanDraft.Solver.Kernel;

internal sealed class DenseLu
{
    private readonly int n;
    private readonly double[] lu;
    private readonly int[] piv;

    internal DenseLu(int dimension, IReadOnlyList<double> matrix)
    {
        KernelFailureException.Require(dimension >= 0 && (long)dimension * dimension == matrix.Count);
        n = dimension;
        lu = matrix.ToArray();
        piv = Enumerable.Range(0, n).ToArray();
        foreach (double value in lu)
            KernelFailureException.RequireFinite(value);
        for (int k = 0; k < n; k++)
        {
            int p = k;
            for (int i = k + 1; i < n; i++)
                if (Abs(lu[i * n + k]) > Abs(lu[p * n + k]))
                    p = i;
            double pivot = lu[p * n + k];
            if (IsZero(pivot))
                throw new KernelFailureException(KernelFailureCause.ZeroPivot, "GEPP encountered a zero pivot.");
            if (Abs(pivot) <= Lambda)
                throw new NumericalRangeException("A nonzero GEPP pivot is at or below the normal boundary.");
            for (int j = 0; j < n; j++)
                (lu[k * n + j], lu[p * n + j]) = (lu[p * n + j], lu[k * n + j]);
            (piv[k], piv[p]) = (piv[p], piv[k]);
            for (int i = k + 1; i < n; i++)
            {
                double factor = lu[i * n + k] / lu[k * n + k];
                KernelFailureException.RequireFinite(factor);
                lu[i * n + k] = factor;
                if (IsZero(factor))
                    continue;
                for (int j = k + 1; j < n; j++)
                {
                    lu[i * n + j] -= factor * lu[k * n + j];
                    KernelFailureException.RequireFinite(lu[i * n + j]);
                }
            }
        }
    }

    internal double[] Solve(double[] rhs)
    {
        KernelFailureException.Require(rhs.Length == n);
        var z = new double[n];
        for (int i = 0; i < n; i++)
        {
            KernelFailureException.RequireFinite(rhs[piv[i]]);
            z[i] = rhs[piv[i]];
        }
        for (int i = 0; i < n; i++)
        {
            double sum = 0;
            for (int j = 0; j < i; j++)
                sum += lu[i * n + j] * z[j];
            z[i] -= sum;
            KernelFailureException.RequireFinite(z[i]);
        }
        for (int i = n - 1; i >= 0; i--)
        {
            double sum = 0;
            for (int j = i + 1; j < n; j++)
                sum += lu[i * n + j] * z[j];
            z[i] = (z[i] - sum) / lu[i * n + i];
            KernelFailureException.RequireFinite(z[i]);
        }
        return z;
    }

    internal double[] SolveTranspose(double[] rhs)
    {
        KernelFailureException.Require(rhs.Length == n);
        var w = rhs.ToArray();
        foreach (double value in w)
            KernelFailureException.RequireFinite(value);
        for (int i = 0; i < n; i++)
        {
            double sum = 0;
            for (int j = 0; j < i; j++)
                sum += lu[j * n + i] * w[j];
            w[i] = (w[i] - sum) / lu[i * n + i];
            KernelFailureException.RequireFinite(w[i]);
        }
        for (int i = n - 1; i >= 0; i--)
        {
            double sum = 0;
            for (int j = i + 1; j < n; j++)
                sum += lu[j * n + i] * w[j];
            w[i] -= sum;
            KernelFailureException.RequireFinite(w[i]);
        }
        var z = new double[n];
        for (int i = 0; i < n; i++)
            z[piv[i]] = w[i];
        return z;
    }
}
