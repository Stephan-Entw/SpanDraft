using System.Numerics;
using Xunit;
using static SpanDraft.Tests.SolverExactNumber;

namespace SpanDraft.Tests;

internal static class SolverExactLinearAlgebra
{
    internal static SolverExactNumber Reduce(SolverExactNumber value)
    {
        BigInteger gcd = BigInteger.GreatestCommonDivisor(value.Numerator, value.Denominator);
        return new(value.Numerator / gcd, value.Denominator / gcd);
    }

    // Rational elimination uses the first nonzero pivot, independently of floating-point GEPP.
    internal static SolverExactNumber[] Solve(SolverExactNumber[,] a, SolverExactNumber[] b)
    {
        int n = b.Length;
        var m = new SolverExactNumber[n, n + 1];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++) m[i, j] = Reduce(a[i, j]);
            m[i, n] = Reduce(b[i]);
        }
        for (int k = 0; k < n; k++)
        {
            int p = Enumerable.Range(k, n - k).First(i => !m[i, k].Numerator.IsZero);
            for (int j = k; j <= n; j++)
                (m[k, j], m[p, j]) = (m[p, j], m[k, j]);
            for (int i = k + 1; i < n; i++)
            {
                SolverExactNumber f = Reduce(m[i, k] / m[k, k]);
                for (int j = k; j <= n; j++)
                    m[i, j] = Reduce(m[i, j] - f * m[k, j]);
            }
        }
        var x = new SolverExactNumber[n];
        for (int i = n - 1; i >= 0; i--)
        {
            SolverExactNumber rhs = m[i, n];
            for (int j = i + 1; j < n; j++) rhs = Reduce(rhs - m[i, j] * x[j]);
            x[i] = Reduce(rhs / m[i, i]);
        }
        return x;
    }

    internal static SolverExactNumber[,] Matrix(double[] a, bool transpose = false)
    {
        int n = (int)Math.Sqrt(a.Length);
        Assert.Equal(n * n, a.Length);
        var matrix = new SolverExactNumber[n, n];
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++) matrix[i, j] = From(a[transpose ? j * n + i : i * n + j]);
        return matrix;
    }

    internal static void Bounded(SolverExactNumber value, double bound) =>
        Assert.True(value.Abs().CompareTo(From(bound)) <= 0, $"Exact error exceeds {bound:R}");

    internal static SolverExactNumber Dot(IEnumerable<(SolverExactNumber A, SolverExactNumber B)> products)
    {
        SolverExactNumber sum = Integer(0);
        foreach (var (a, b) in products) sum = Reduce(sum + a * b);
        return sum;
    }
}
