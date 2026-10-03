using SpanDraft.Core.Validation;
using SpanDraft.Solver;

namespace SpanDraft.Analysis;

public enum BeamAnalysisFailureCode
{
    InvalidModel,
    UnstableModel,
    IllConditionedSystem,
    NumericalFailure
}

/// <summary>An expected solver failure with the original structured model diagnostics.</summary>
public sealed class BeamAnalysisFailure
{
    internal BeamAnalysisFailure(BeamSolverException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Code = MapSolverErrorCode(exception.Code);
        ValidationErrors = exception.ValidationErrors;
        TechnicalMessage = exception.Message;
    }

    internal static BeamAnalysisFailureCode MapSolverErrorCode(SolverErrorCode code) => code switch
    {
        SolverErrorCode.InvalidModel => BeamAnalysisFailureCode.InvalidModel,
        SolverErrorCode.UnstableModel => BeamAnalysisFailureCode.UnstableModel,
        SolverErrorCode.IllConditionedSystem => BeamAnalysisFailureCode.IllConditionedSystem,
        SolverErrorCode.NumericalFailure => BeamAnalysisFailureCode.NumericalFailure,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown solver error code.")
    };

    public BeamAnalysisFailureCode Code { get; }
    public IReadOnlyList<ValidationError> ValidationErrors { get; }
    /// <summary>Original diagnostic message for logging, not a localized UI text.</summary>
    public string TechnicalMessage { get; }
}
