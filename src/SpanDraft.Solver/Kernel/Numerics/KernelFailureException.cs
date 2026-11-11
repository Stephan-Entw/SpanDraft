using System.Collections.Immutable;

namespace SpanDraft.Solver.Kernel;

internal enum KernelFailureCause { ZeroPivot, NotVerified, NumericalRange, NonFinite, InternalInconsistency, Gate }

internal sealed class KernelFailureException(KernelFailureCause cause, string message,
    ImmutableArray<double> testedMus = default) : ArithmeticException(message)
{
    internal KernelFailureCause Cause { get; } = cause;
    internal ImmutableArray<double> TestedMus { get; } = testedMus.IsDefault ? [] : testedMus;

    internal static void RequireFinite(double value)
    {
        if (!Binary64.IsFinite(value))
            throw new KernelFailureException(KernelFailureCause.NonFinite, "A numerical candidate or residual is not finite.");
    }

    internal static void Require(bool condition)
    {
        if (!condition)
            throw new KernelFailureException(KernelFailureCause.InternalInconsistency, "Inconsistent internal numerical dimensions or operands.");
    }
}
