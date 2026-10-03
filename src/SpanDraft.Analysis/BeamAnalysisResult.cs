using SpanDraft.Core.Beams;
using SpanDraft.Engineering;
using SpanDraft.Solver;

namespace SpanDraft.Analysis;

/// <summary>A completed solution and its engineering assessment, without duplicated indicators.</summary>
public sealed class BeamAnalysisResult
{
    internal BeamAnalysisResult(BeamSolution solution, BeamEngineeringResult engineering)
    {
        ArgumentNullException.ThrowIfNull(solution);
        ArgumentNullException.ThrowIfNull(engineering);
        Solution = solution;
        Engineering = engineering;
    }

    public BeamSolution Solution { get; }
    public BeamEngineeringResult Engineering { get; }
    public BeamModel Beam => Solution.Beam;
}
