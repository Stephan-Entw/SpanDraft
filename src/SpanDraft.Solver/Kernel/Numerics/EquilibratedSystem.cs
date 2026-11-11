using static SpanDraft.Solver.Kernel.Binary64;

namespace SpanDraft.Solver.Kernel;

internal sealed class EquilibratedSystem
{
    internal int Dimension { get; }
    internal int[] RS { get; }
    internal int[] CS { get; }
    internal double[] S { get; }
    internal double[] DS { get; }
    internal double[] SB { get; }
    internal double[] DSB { get; }
    internal int[][] Rows { get; }
    internal int[][] Columns { get; }

    internal EquilibratedSystem(int n, int[] rs, int[] cs, double[] s, double[] ds, double[] sb, double[] dsb)
    {
        KernelFailureException.Require(n >= 0 && (long)n * n == s.Length && ds.Length == s.Length &&
            rs.Length == n && cs.Length == n && sb.Length == n && dsb.Length == n);
        Dimension = n;
        RS = rs;
        CS = cs;
        S = s;
        DS = ds;
        SB = sb;
        DSB = dsb;
        Rows = new int[n][];
        Columns = new int[n][];
        for (int i = 0; i < n; i++)
        {
            int row = i;
            Rows[i] = Enumerable.Range(0, n).Where(j => !IsZero(s[row * n + j])).ToArray();
            Columns[i] = Enumerable.Range(0, n).Where(j => !IsZero(s[j * n + row])).ToArray();
        }
    }

    internal static EquilibratedSystem Create(int n, IReadOnlyList<double> a, IReadOnlyList<long> k,
        IReadOnlyList<double> b, IReadOnlyList<double> bb)
    {
        KernelFailureException.Require(n >= 0 && (long)n * n == a.Count && k.Count == a.Count && b.Count == n && bb.Count == n);
        int[] rs = new int[n], cs = new int[n];
        try
        {
            checked
            {
                for (int pass = 0; pass < 8; pass++)
                {
                    for (int i = 0; i < n; i++)
                    {
                        int row = i;
                        rs[i] -= Correction(Enumerable.Range(0, n).Select(j => (a[row * n + j], rs[row] + cs[j])));
                    }
                    for (int j = 0; j < n; j++)
                    {
                        int column = j;
                        cs[j] -= Correction(Enumerable.Range(0, n).Select(i => (a[i * n + column], rs[i] + cs[column])));
                    }
                }
                var s = new double[a.Count];
                var ds = new double[a.Count];
                var sb = new double[n];
                var dsb = new double[n];
                for (int i = 0; i < n; i++)
                {
                    for (int j = 0; j < n; j++)
                    {
                        int index = i * n + j;
                        s[index] = ScaleByPowerOfTwo(a[index], rs[i] + cs[j]);
                        if (!IsZero(a[index]) && (!IsFinite(s[index]) || !(Abs(s[index]) > Lambda)))
                            throw new NumericalRangeException("A nonzero equilibrated matrix entry is outside its range.");
                        ds[index] = RoundCounted.ComputedBound(k[index], s[index]);
                        KernelFailureException.RequireFinite(ds[index]);
                    }
                    sb[i] = ScaleByPowerOfTwo(b[i], rs[i]);
                    if (!IsFinite(sb[i]))
                        throw new NumericalRangeException("The equilibrated RHS is outside its range.");
                    double loss = !IsZero(b[i]) && MagnitudeBits(sb[i]) <= MagnitudeBits(Lambda) ? Lambda : 0;
                    dsb[i] = Directed.Add(Directed.DirectedScale(bb[i], rs[i], upper: true), loss, upper: true);
                    KernelFailureException.RequireFinite(dsb[i]);
                }
                return new(n, rs, cs, s, ds, sb, dsb);
            }
        }
        catch (OverflowException)
        {
            throw new NumericalRangeException("The Ruiz exponents exceed the integer range.");
        }
    }

    internal static int Correction(IEnumerable<(double Value, int Exponent)> entries)
    {
        int maxExponent = int.MinValue;
        double maxMantissa = 0;
        bool found = false;
        foreach (var (value, exponent) in entries)
        {
            if (IsZero(value))
                continue;
            KernelFailureException.RequireFinite(value);
            int binaryExponent = PowerOfTwoExponent(Abs(value));
            int scaledExponent = checked(binaryExponent + exponent);
            double mantissa = ScaleByPowerOfTwo(Abs(value), -binaryExponent - 1);
            if (!found || scaledExponent > maxExponent || scaledExponent == maxExponent && mantissa > maxMantissa)
                (maxExponent, maxMantissa) = (scaledExponent, mantissa);
            found = true;
        }
        if (!found)
            return 0;
        // Arithmetic shift is Python's floor division, including negative odd exponents.
        int half = maxExponent >> 1;
        return half + ((maxExponent & 1) != 0 && (maxMantissa != 0.5 || (half & 1) != 0) ? 1 : 0);
    }

    internal double[] CandidateResidual(double[] value, double[] rhs, bool transpose)
    {
        int n = Dimension;
        KernelFailureException.Require(value.Length == n && rhs.Length == n);
        var residual = new double[n];
        for (int i = 0; i < n; i++)
        {
            double sum = 0;
            foreach (int j in transpose ? Columns[i] : Rows[i])
                sum += S[transpose ? j * n + i : i * n + j] * value[j];
            residual[i] = rhs[i] - sum;
        }
        return residual;
    }
}
