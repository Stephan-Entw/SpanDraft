using SpanDraft.Analysis;
using SpanDraft.Core.Validation;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Resources;

namespace SpanDraft.Desktop.State;

public enum AnalysisPresentationKind
{
    Success, MissingSupports, IncompleteModel, UnstableModel, IllConditionedSystem, NumericalFailure
}

/// <summary>Presentation classification and the original successful result; no copied engineering data.</summary>
public sealed record AnalysisPresentationState
{
    private readonly Lazy<ModelReferenceValues>? _references;

    public AnalysisPresentationState(AnalysisPresentationKind kind, BeamAnalysisResult? result,
        ResultPresentationOptions? options = null)
        : this(kind, result, options ?? ResultPresentationOptions.Default,
            result is null ? null : new Lazy<ModelReferenceValues>(() => ModelReferenceValues.FromAnalysis(result))) { }

    private AnalysisPresentationState(AnalysisPresentationKind kind, BeamAnalysisResult? result,
        ResultPresentationOptions options, Lazy<ModelReferenceValues>? references)
    {
        Kind = kind;
        Result = result;
        Options = options;
        _references = references;
    }

    public AnalysisPresentationKind Kind { get; }
    public BeamAnalysisResult? Result { get; }
    public ResultPresentationOptions Options { get; }
    public ModelReferenceValues? References => _references?.Value;

    public AnalysisPresentationState WithPresentation(ResultPresentationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return Options == options ? this : new(Kind, Result, options, _references);
    }

    public static AnalysisPresentationState FromOutcome(BeamAnalysisOutcome outcome, ResultPresentationOptions? options = null)
    {
        if (outcome.Result is { } result) return new(AnalysisPresentationKind.Success, result, options);
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
        return new(kind, null, options);
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
        ? Format(r.Engineering.TransverseDisplacementMagnitude.Meters, QuantityKind.TransverseDisplacement) : "";
    public string Moment => Result is { } r
        ? Format(r.Engineering.BendingMomentMagnitude.NewtonMeters, QuantityKind.Moment) : "";
    public string Stress => Result is { } r
        ? Format(r.Engineering.MaximumBendingStress.Pascals, QuantityKind.Stress) : "";
    public string SafetyFactor => Result is { } r ? Format(r.Engineering.SafetyFactor, QuantityKind.SafetyFactor) : "";

    private string Format(double siValue, QuantityKind kind) =>
        QuantityFormatter.Format(siValue, kind, Options.Profile, Options.Mode,
            kind == QuantityKind.SafetyFactor ? null : References);
}
