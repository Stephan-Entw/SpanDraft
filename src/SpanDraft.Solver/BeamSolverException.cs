using SpanDraft.Core.Validation;
using SpanDraft.Solver.Kernel;

namespace SpanDraft.Solver;

public enum SolverErrorCode
{
    InvalidModel,
    UnstableModel,
    IllConditionedSystem,
    NumericalFailure
}

/// <summary>A model or numerical failure; no partial solution is returned.</summary>
public sealed class BeamSolverException : Exception
{
    internal BeamSolverException(SolverErrorCode code, string message,
        IEnumerable<ValidationError>? validationErrors = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
        ValidationErrors = Array.AsReadOnly(validationErrors?.ToArray() ?? []);
    }

    public SolverErrorCode Code { get; }
    public IReadOnlyList<ValidationError> ValidationErrors { get; }

    /// <summary>
    /// Maps a structured kernel cause to its public code. The kernel exception stays the inner
    /// exception, so diagnostics keep the cause without widening the public contract.
    /// </summary>
    internal static BeamSolverException Rejected(KernelFailureCause cause, Exception kernelException) => cause switch
    {
        KernelFailureCause.ZeroPivot or KernelFailureCause.NotVerified or KernelFailureCause.Gate =>
            new(SolverErrorCode.IllConditionedSystem,
                "The stable, loaded model cannot be solved reliably within the numerical contract.",
                innerException: kernelException),
        KernelFailureCause.NumericalRange or KernelFailureCause.NonFinite or KernelFailureCause.InternalInconsistency =>
            new(SolverErrorCode.NumericalFailure,
                "The model cannot be evaluated reliably in double precision.", innerException: kernelException),
        _ => throw new ArgumentOutOfRangeException(nameof(cause), cause, "Unknown kernel failure cause.")
    };
}

internal static class NumericalGuard
{
    internal static double Finite(double value, string quantity)
    {
        if (!double.IsFinite(value))
            throw new BeamSolverException(SolverErrorCode.NumericalFailure,
                $"{quantity} is not finite; the model cannot be evaluated reliably in double precision.");
        return value;
    }

    internal static double Positive(double value, string quantity)
    {
        Finite(value, quantity);
        if (value <= 0)
            throw new BeamSolverException(SolverErrorCode.NumericalFailure,
                $"{quantity} must remain strictly positive in double precision.");
        return value;
    }

    internal static void AllFinite(IEnumerable<double> values, string quantity)
    {
        foreach (double value in values)
            Finite(value, quantity);
    }
}
