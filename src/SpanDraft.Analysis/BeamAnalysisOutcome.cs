namespace SpanDraft.Analysis;

/// <summary>Exactly one completed result or one expected failure; never a partial result.</summary>
public sealed class BeamAnalysisOutcome
{
    private BeamAnalysisOutcome(BeamAnalysisResult? result, BeamAnalysisFailure? failure)
    {
        Result = result;
        Failure = failure;
    }

    internal static BeamAnalysisOutcome Success(BeamAnalysisResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new BeamAnalysisOutcome(result, null);
    }

    internal static BeamAnalysisOutcome FromFailure(BeamAnalysisFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return new BeamAnalysisOutcome(null, failure);
    }

    public bool IsSuccess => Result is not null;
    public BeamAnalysisResult? Result { get; }
    public BeamAnalysisFailure? Failure { get; }
}
