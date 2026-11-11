using System.Runtime.CompilerServices;
using static SpanDraft.Solver.Kernel.Binary64;

namespace SpanDraft.Solver.Kernel;

internal static class CertifiedResidual
{
    internal static (double Value, double Error) Evaluate(double rhs, IEnumerable<(double A, double B)> products)
    {
        KernelFailureException.RequireFinite(rhs);
        double acc = MagnitudeBits(rhs) < MagnitudeBits(Lambda) ? 0 : rhs;
        double error = IsSubnormal(rhs) ? Lambda : 0;
        foreach (var (a, b) in products)
        {
            if (IsZero(a) || IsZero(b))
                continue;
            KernelFailureException.RequireFinite(a);
            KernelFailureException.RequireFinite(b);
            KernelFailureException.Require(IsNormal(a) && IsNormal(b));
            double p = Product(a, b);
            error = Directed.Add(error, LocalRounding(p), upper: true);
            KernelFailureException.RequireFinite(error);
            if (MagnitudeBits(p) < MagnitudeBits(Lambda))
                p = 0;
            if (IsZero(p))
                continue;
            acc = Difference(acc, p);
            error = Directed.Add(error, LocalRounding(acc), upper: true);
            KernelFailureException.RequireFinite(error);
            if (MagnitudeBits(acc) < MagnitudeBits(Lambda))
                acc = 0;
        }
        return (acc, error);
    }

    internal static double LocalRounding(double result)
    {
        KernelFailureException.RequireFinite(result);
        return MagnitudeBits(result) <= MagnitudeBits(Lambda) ? Lambda :
            Directed.Multiply(U, Abs(result), upper: true);
    }

    // Separate calls materialize the two certified roundings; the JIT cannot fuse them.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static double Product(double a, double b) => a * b;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static double Difference(double a, double b) => a - b;
}
