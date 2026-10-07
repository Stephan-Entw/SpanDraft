using SpanDraft.Analysis;
using SpanDraft.Core.Validation;
using SpanDraft.Desktop.Resources;

namespace SpanDraft.Desktop.State;

public enum AnalysisPresentationKind
{
    Success, MissingSupports, IncompleteModel, UnstableModel, IllConditionedSystem, NumericalFailure
}

/// <summary>Presentation classification and the original successful result; no copied engineering data.</summary>
public sealed record AnalysisPresentationState(AnalysisPresentationKind Kind, BeamAnalysisResult? Result)
{
    public static AnalysisPresentationState FromOutcome(BeamAnalysisOutcome outcome)
    {
        if (outcome.Result is { } result) return new(AnalysisPresentationKind.Success, result);
        var failure = outcome.Failure!;
        var kind = failure.Code switch
        {
            BeamAnalysisFailureCode.InvalidModel when failure.ValidationErrors.Any(
                error => error.Code == ValidationErrorCode.MissingSupports) => AnalysisPresentationKind.MissingSupports,
            BeamAnalysisFailureCode.InvalidModel => AnalysisPresentationKind.IncompleteModel,
            BeamAnalysisFailureCode.UnstableModel => AnalysisPresentationKind.UnstableModel,
            BeamAnalysisFailureCode.IllConditionedSystem => AnalysisPresentationKind.IllConditionedSystem,
            BeamAnalysisFailureCode.NumericalFailure => AnalysisPresentationKind.NumericalFailure,
            _ => throw new ArgumentOutOfRangeException(nameof(outcome))
        };
        return new(kind, null);
    }

    public bool IsSuccess => Result is not null;
    public string StatusText => Kind switch
    {
        AnalysisPresentationKind.Success => Strings.CalculationComplete,
        AnalysisPresentationKind.MissingSupports => Strings.CalculationUnavailable + " · " + Strings.SupportsMissing,
        AnalysisPresentationKind.UnstableModel => Strings.CalculationUnavailable + " · " + Strings.InsufficientSupport,
        AnalysisPresentationKind.IncompleteModel => Strings.CalculationUnavailable + " · " + Strings.IncompleteModel,
        _ => Strings.CalculationNotPossible
    };

    public string Displacement => Result is { } r
        ? UiNumbers.Indicator(r.Engineering.TransverseDisplacementMagnitude.Meters * 1000) + " mm" : "";
    public string Moment => Result is { } r
        ? UiNumbers.Indicator(r.Engineering.BendingMomentMagnitude.NewtonMeters) + " Nm" : "";
    public string Stress => Result is { } r
        ? UiNumbers.Indicator(r.Engineering.MaximumBendingStress.Megapascals) + " MPa" : "";
    public string SafetyFactor => Result is { } r ? UiNumbers.Indicator(r.Engineering.SafetyFactor) : "";
}
